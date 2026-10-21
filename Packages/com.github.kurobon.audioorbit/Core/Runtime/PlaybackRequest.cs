using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    internal sealed class PlaybackRequest
    {
        public string Key { get; internal set; }
        internal AudioEngine Engine;
        internal Playback Playback;
        internal int ClipIndex { get; private set; } = -1;
        internal PlayOptions Options;
        internal LibraryHandle Library;
        internal AudioCategoryId Category;

        private readonly object completionGate = new();
        private TaskCompletionSource<PlaybackResult> completion;
        private Task<PlaybackResult> completionTask;
        private PlaybackResult completionResult;
        private bool completed;

        private CancellationTokenSource requestCancellation;
        private CancellationToken requestToken;
        private bool? loop;
        internal void NotifyStateChanged() => Engine?.NotifyPlaybackHandle(this, State);
        internal bool ResolveLoop(bool fallback) => loop ?? fallback;
        public bool Started { get; private set; }
        public bool IsCompleted => Volatile.Read(ref completed);
        public PlayRejectionReason RejectionReason { get; private set; }
        internal CancellationToken RequestToken => requestToken;

        public PlaybackState State => IsCompleted ? PlaybackState.Ended :
            Playback == null ? PlaybackState.Loading :
            Playback.Stopping ? PlaybackState.FadingOut :
            Playback.Paused ? PlaybackState.Paused : PlaybackState.Playing;

        internal PlaybackRequest(AudioEngine engine = null, Playback playback = null)
        {
            Engine = engine;
            Playback = playback;
        }

        // Only pending Library/PCM loads need a cancellation source. Synchronous playback does not.
        internal CancellationToken GetRequestToken()
        {
            if (IsCompleted) return new CancellationToken(true);
            if (requestCancellation == null)
            {
                requestCancellation = new CancellationTokenSource();
                requestToken = requestCancellation.Token;
            }
            return requestToken;
        }

        internal void Attach(Playback playback)
        {
            Playback = playback;
            ClipIndex = playback.ClipIndex;
            if (loop.HasValue) playback.Loop = loop.Value;
            Started = true;
            Engine?.RemovePending(this);
            NotifyStateChanged();
        }

        internal void AcceptStart(PlayStartResult result)
        {
            if (!result.Started && !IsCompleted) FinishBeforeStart(result.RejectionReason, null, result.Error);
        }

        internal void Fail(Exception error)
        {
            if (!IsCompleted) FinishBeforeStart(PlayRejectionReason.Failed, PlaybackEndReason.Failed, error);
        }

        internal void FinishBeforeStart(PlayRejectionReason reason, PlaybackEndReason? end, Exception error = null)
        {
            if (IsCompleted) return;
            RejectionReason = reason;
            Engine?.RemovePending(this);
            SetCompletion(new PlaybackResult(false, reason, end, error));
            Engine?.CompleteStopWaits(this);
            if (error != null)
            {
                Debug.LogException(error);
            }
            ReleaseRequest();
            NotifyStateChanged();
        }

        internal void Complete(PlaybackEndReason reason)
        {
            Playback = null;
            SetCompletion(new PlaybackResult(true, PlayRejectionReason.None, reason));
            Engine?.CompleteStopWaits(this);
            ReleaseRequest();
            NotifyStateChanged();
        }

        private void ReleaseRequest()
        {
            var engine = Engine;
            Engine = null;
            Options = default;
            Library = null;
            if (requestCancellation != null)
            {
                requestCancellation.Cancel();
                requestCancellation.Dispose();
                requestCancellation = null;
            }
            engine?.CompletePlaybackHandle(this);
        }

        private void SetCompletion(PlaybackResult result)
        {
            lock (completionGate)
            {
                if (completed) return;
                completionResult = result;
                Volatile.Write(ref completed, true);
                completion?.TrySetResult(result);
            }
        }

        private Task<PlaybackResult> CompletionTask()
        {
            lock (completionGate)
            {
                if (completionTask != null) return completionTask;
                if (completed) return completionTask = Task.FromResult(completionResult);
                completion = new TaskCompletionSource<PlaybackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                return completionTask = completion.Task;
            }
        }

        public void Stop(float fadeSeconds = 0)
        {
            AudioEngine.ValidateNonNegative(fadeSeconds, nameof(fadeSeconds));
            if (IsCompleted) return;
            Engine?.CheckThread();
            if (Playback != null) Engine.StopPlayback(Playback, fadeSeconds, PlaybackEndReason.Stopped);
            else FinishBeforeStart(PlayRejectionReason.None, PlaybackEndReason.Stopped);
        }

        /// <summary>Requests a stop. Cancellation cancels only waiting, not the stop itself.</summary>
        public Task<PlaybackResult> StopAsync(float fadeSeconds = 0, CancellationToken cancellationToken = default)
        {
            Stop(fadeSeconds);
            return WaitForCompletionAsync(cancellationToken);
        }

        public bool Loop
        {
            get => Playback != null ? Playback.Loop : loop ?? (Options.Loop.IsSpecified && Options.Loop.Value);
            set
            {
                if (IsCompleted) return;
                Engine?.CheckThread();
                loop = value;
                if (Playback != null) Playback.Loop = value;
            }
        }

        public bool Pause()
        {
            if (Playback == null) return false;
            Engine.CheckThread();
            if (Playback.Paused) return false;
            Playback.Paused = true;
            Playback.Source.Pause();
            NotifyStateChanged();
            return true;
        }

        public bool Resume()
        {
            if (Playback == null) return false;
            Engine.CheckThread();
            if (!Playback.Paused) return false;
            Playback.Paused = false;
            Playback.Source.UnPause();
            NotifyStateChanged();
            return true;
        }

        public bool SetVolume(float volume)
        {
            AudioEngine.ValidateNonNegative(volume, nameof(volume));
            if (Playback == null) return false;
            Engine.CheckThread();
            Playback.Volume = volume;
            Engine.ApplyVolume(Playback);
            return true;
        }

        public bool SetPitch(float pitch)
        {
            AudioEngine.ValidatePitch(pitch);
#if UNITY_6000_3_OR_NEWER
            if (Playback?.Generator != null && pitch < 0)
                throw new ArgumentOutOfRangeException(nameof(pitch), "Region playback requires positive pitch.");
#endif
            if (Playback == null) return false;
            Engine.CheckThread();
            Playback.Source.pitch = pitch;
            return true;
        }

        public bool SetPosition(Vector3 position)
        {
            if (Playback == null || Playback.External != null) return false;
            Engine.CheckThread();
            Playback.Target = null;
            Playback.Following = false;
            Playback.Source.transform.position = position;
            return true;
        }

        public bool AttachTo(Transform target, Vector3 offset = default)
        {
            if (Playback == null || Playback.External != null || target == null) return false;
            Engine.CheckThread();
            Playback.Target = target;
            Playback.Offset = offset;
            Playback.Following = true;
            Playback.Source.transform.position = target.TransformPoint(offset);
            return true;
        }

        /// <summary>Cancels only this wait. Use Stop to stop the playback.</summary>
        public Task<PlaybackResult> WaitForCompletionAsync(CancellationToken cancellationToken = default) =>
            AsyncWait.WithCancellation(CompletionTask(), cancellationToken);
    }
}
