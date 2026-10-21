using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        internal void Tick(float delta, bool allowSameFrameCompletion = false)
        {
            if (disposed) return;
            StopPending(h => h.Options.Mode == PlaybackMode.Attached && h.Options.Target == null, PlaybackEndReason.TargetLost);
            StopPending(h => !ReferenceEquals(h.Options.Owner, null) && h.Options.Owner == null, PlaybackEndReason.OwnerLost);
            for (var i = playbacks.Count - 1; i >= 0; i--)
            {
                var v = playbacks[i];
                if (v.Source == null)
                {
                    End(v, v.External != null ? PlaybackEndReason.TargetLost : PlaybackEndReason.Failed);
                    continue;
                }

                bool targetLost = v.Following && v.Target == null, ownerLost = v.HasOwner && v.Owner == null;
                if (!v.Stopping && (targetLost || ownerLost))
                {
                    var immediate = targetLost && v.TargetLost == TargetLostBehavior.Stop ||
                                    ownerLost && v.OwnerLost == OwnerLostBehavior.Stop;
                    var fade = targetLost && v.TargetLost == TargetLostBehavior.FadeOut ||
                               ownerLost && v.OwnerLost == OwnerLostBehavior.FadeOut;
                    if (targetLost)
                    {
                        v.Following = false;
                        v.Target = null;
                    }

                    if (ownerLost)
                    {
                        v.HasOwner = false;
                        v.Owner = null;
                    }

                    if (immediate || fade)
                    {
                        StopPlayback(v, immediate ? 0 : v.LossFadeOutSeconds,
                            targetLost ? PlaybackEndReason.TargetLost : PlaybackEndReason.OwnerLost);
                        if (v.Ended) continue;
                    }
                }

                if (v.Following) v.Source.transform.position = v.Target.TransformPoint(v.Offset);
                var listenerPaused = AudioListener.pause && !v.Source.ignoreListenerPause;
                if (v.Stopping)
                {
                    v.FadeElapsed += delta;
                    v.Fade = v.FadeStart * (1 - Mathf.Clamp01(v.FadeElapsed / v.FadeDuration));
                    ApplyVolume(v);
                    if (v.FadeElapsed >= v.FadeDuration)
                    {
                        End(v, v.StopReason);
                        continue;
                    }
                }
                else if (!v.Paused && !listenerPaused && v.FadeIn > 0)
                {
                    v.Fade = Mathf.Clamp01(v.Fade + delta / v.FadeIn);
                    ApplyVolume(v);
                }

                if (!v.Paused && !listenerPaused &&
                    v.Finished &&
                    (allowSameFrameCompletion || Time.frameCount > v.StartFrame))
                    End(v, v.Stopping ? v.StopReason : PlaybackEndReason.Completed);
            }
        }

        internal void StopPlayback(Playback v, float fade, PlaybackEndReason reason)
        {
            CheckThread();
            ValidateNonNegative(fade, nameof(fade));
            if (v.Ended) return;
            if (fade == 0)
            {
                End(v, reason);
                return;
            }

            if (v.Stopping) return;
            v.Stopping = true;
            v.StopReason = reason;
            v.FadeStart = v.Fade;
            v.FadeDuration = fade;
            v.FadeElapsed = 0;
            v.Handle.NotifyStateChanged();
        }

        private void End(Playback v, PlaybackEndReason reason)
        {
            if (v.Ended) return;
            v.Ended = true;
            playbacks.Remove(v);
            if (v.Source != null)
            {
                v.Source.Stop();
                if (v.External == null)
                {
                    ResetSource(v.Source);
                    pool.Push(v.Source);
                }
            }

            v.Lease?.Dispose();
#if UNITY_6000_3_OR_NEWER
            v.Generator?.Dispose();
            v.Generator = null;
#endif
            v.Lease = null;
            v.Owner = null;
            v.Target = null;
            v.Source = null;
            v.External = null;
            v.Library = null;
            v.Handle.Complete(reason);
        }

        private enum StopScope { All, Category, Owner, Library }

        private readonly struct StopFilter
        {
            private readonly StopScope scope;
            private readonly AudioCategoryId category;
            private readonly Object owner;
            private readonly LibraryHandle library;
            internal StopFilter(StopScope scope, AudioCategoryId category = default, Object owner = null, LibraryHandle library = null)
            { this.scope = scope; this.category = category; this.owner = owner; this.library = library; }
            internal bool Matches(Playback playback)
            {
                switch (scope)
                {
                    case StopScope.Category: return playback.Category == category;
                    case StopScope.Owner: return playback.Owner == owner;
                    case StopScope.Library: return playback.Library == library;
                    default: return true;
                }
            }
            internal bool Matches(PlaybackRequest handle)
            {
                switch (scope)
                {
                    case StopScope.Category: return handle.Category == category;
                    case StopScope.Owner: return handle.Options.Owner == owner;
                    case StopScope.Library: return handle.Library == library;
                    default: return true;
                }
            }
        }

        private readonly Stack<List<PlaybackRequest>> stopSnapshots = new();

        private sealed class StopWait
        {
            private readonly TaskCompletionSource<bool> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int Remaining;
            internal Task Task => completion.Task;
            internal void CompleteOne()
            {
                if (--Remaining == 0) completion.TrySetResult(true);
            }
        }

        private readonly Dictionary<PlaybackRequest, List<StopWait>> stopWaiters = new();
        private readonly Stack<List<StopWait>> stopWaiterLists = new();

        private void AddStopWait(PlaybackRequest handle, StopWait wait)
        {
            if (!stopWaiters.TryGetValue(handle, out var waits))
            {
                waits = stopWaiterLists.Count > 0 ? stopWaiterLists.Pop() : new List<StopWait>();
                stopWaiters.Add(handle, waits);
            }
            waits.Add(wait);
            wait.Remaining++;
        }

        internal void CompleteStopWaits(PlaybackRequest handle)
        {
            if (!stopWaiters.TryGetValue(handle, out var waits)) return;
            stopWaiters.Remove(handle);
            try
            {
                // A completion continuation may reenter the engine; keep this list private until it returns.
                foreach (var wait in waits) wait.CompleteOne();
            }
            finally
            {
                waits.Clear();
                stopWaiterLists.Push(waits);
            }
        }

        private Task StopGroup(StopFilter filter, float fade, bool wait, CancellationToken cancellationToken = default)
        {
            Check(); ValidateNonNegative(fade, nameof(fade));
            List<PlaybackRequest> captured = null;
            try
            {
                // Capture both sets before stopping anything: callbacks may start or stop other playbacks.
                foreach (var playback in playbacks)
                    if (filter.Matches(playback)) CaptureStop(ref captured, playback.Handle);
                foreach (var handle in pending)
                    if (filter.Matches(handle)) CaptureStop(ref captured, handle);
                if (captured == null) return Task.CompletedTask;
                foreach (var handle in captured) handle.Stop(fade);
                StopWait group = null;
                if (wait)
                    foreach (var handle in captured)
                        if (!handle.IsCompleted) AddStopWait(handle, group ??= new StopWait());
                Task completion = group == null ? Task.CompletedTask : group.Task;
                return AsyncWait.WithCancellation(completion, cancellationToken);
            }
            finally
            {
                if (captured != null)
                {
                    captured.Clear();
                    stopSnapshots.Push(captured);
                }
            }
        }

        private void CaptureStop(ref List<PlaybackRequest> captured, PlaybackRequest handle)
        {
            if (captured == null)
                captured = stopSnapshots.Count > 0 ? stopSnapshots.Pop() : new List<PlaybackRequest>();
            captured.Add(handle);
        }

        public void StopAll(float fadeSeconds = 0) => StopGroup(new StopFilter(StopScope.All), fadeSeconds, false);
        public void StopCategory(AudioCategoryId category, float fadeSeconds = 0) =>
            StopGroup(new StopFilter(StopScope.Category, category), fadeSeconds, false);
        public void StopOwner(Object owner, float fadeSeconds = 0) =>
            StopGroup(new StopFilter(StopScope.Owner, owner: owner), fadeSeconds, false);
        public void StopLibrary(LibraryHandle library, float fadeSeconds = 0)
        {
            Check(); ValidateLibrary(library);
            StopGroup(new StopFilter(StopScope.Library, library: library), fadeSeconds, false);
        }
        /// <summary>Stops current requests and playbacks. Cancellation cancels only waiting.</summary>
        public Task StopAllAsync(float fadeSeconds = 0, CancellationToken cancellationToken = default) =>
            StopGroup(new StopFilter(StopScope.All), fadeSeconds, true, cancellationToken);
        public Task StopCategoryAsync(AudioCategoryId category, float fadeSeconds = 0, CancellationToken cancellationToken = default) =>
            StopGroup(new StopFilter(StopScope.Category, category), fadeSeconds, true, cancellationToken);
        public Task StopOwnerAsync(Object owner, float fadeSeconds = 0, CancellationToken cancellationToken = default) =>
            StopGroup(new StopFilter(StopScope.Owner, owner: owner), fadeSeconds, true, cancellationToken);
        public Task StopLibraryAsync(LibraryHandle library, float fadeSeconds = 0, CancellationToken cancellationToken = default)
        {
            Check(); ValidateLibrary(library);
            return StopGroup(new StopFilter(StopScope.Library, library: library), fadeSeconds, true, cancellationToken);
        }
        public void SetMasterVolume(float volume)
        {
            Check();
            ValidateNonNegative(volume, nameof(volume));
            master = volume;
            foreach (var v in playbacks) ApplyVolume(v);
        }

        private CategoryState GetCategory(AudioCategoryId id)
        {
            Check();
            if (!categories.TryGetValue(id, out var c)) throw new KeyNotFoundException("Unknown category.");
            return c;
        }

        public void SetCategoryVolume(AudioCategoryId category, float volume)
        {
            ValidateNonNegative(volume, nameof(volume));
            GetCategory(category).Volume = volume;
            foreach (var v in playbacks)
                if (v.Category == category)
                    ApplyVolume(v);
        }

        public void SetCategoryMuted(AudioCategoryId category, bool muted)
        {
            GetCategory(category).Muted = muted;
            foreach (var v in playbacks)
                if (v.Category == category)
                    ApplyVolume(v);
        }

        public void SetCategoryIgnoreListenerPause(AudioCategoryId category, bool ignore)
        {
            GetCategory(category).Ignore = ignore;
            foreach (var v in playbacks)
                if (v.Category == category && v.Source != null)
                    v.Source.ignoreListenerPause = ignore;
        }

        public bool GetCategoryIgnoreListenerPause(AudioCategoryId category) => GetCategory(category).Ignore;

        public void GetPlaybackInfo(List<PlaybackInfo> destination)
        {
            Check();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            foreach (var v in playbacks)
                destination.Add(new PlaybackInfo(v.Key, v.Category, v.Owner, v.Source.transform.position, v.Handle.State,
                    v.Paused, AudioListener.pause && !v.Source.ignoreListenerPause, v.Source.ignoreListenerPause));
        }

        public ExternalSourceHandle RegisterExternalSource(AudioSource source, ExternalSourceOptions options = default)
        {
            Check();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(options.Key)) options.Key = string.Empty;
            foreach (var h in external)
                if (h.Source == source)
                    throw new ArgumentException("Source is already registered.");
            if (source.playOnAwake) throw new ArgumentException("External source must disable playOnAwake.");
            if (source.clip == null) throw new ArgumentException("External source has no clip.");
            options.Limits = CopyLimits(options.Limits);
            ValidateProfile(options.DefaultProfile);
            RegisterCategory(options.Category);
            var handle = new ExternalSourceHandle(this, source, options);
            source.Stop();
            external.Add(handle);
            return handle;
        }

        private PlayStartResult StartExternal(ExternalSourceHandle source, PlaybackRequest request)
        {
            Check();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Engine != this || source.Source == null) return Reject(PlayRejectionReason.TargetLost);
            var o = new PlayOptions
            {
                Owner = source.Options.Owner, Priority = source.Options.Priority
            };
            ValidateOptions(o);
            var d = new Definition
            {
                Key = source.Options.Key, ExplicitLimitKey = source.LimitKey, Category = RegisterCategory(source.Options.Category),
                Volume = source.Snapshot.Volume,
                Loop = source.Snapshot.Loop, Limits = source.Options.Limits,
                Profile = source.Options.DefaultProfile != null
                    ? new SpatialSettings(source.Options.DefaultProfile)
                    : null
            };
            return Start(d, -1, new ClipDefinition { Pitch = source.Snapshot.Pitch }, source.Snapshot.Clip, null, o, source, request);
        }

        internal void UnregisterExternal(ExternalSourceHandle h)
        {
            CheckThread();
            for (var i = playbacks.Count - 1; i >= 0; i--)
                if (playbacks[i].External == h)
                    End(playbacks[i], PlaybackEndReason.Stopped);
            h.Snapshot.Restore(h.Source);
            external.Remove(h);
            if (string.IsNullOrEmpty(h.Options.Key))
            {
                cooldown.Remove(h.LimitKey);
                var owners = new List<(object, int)>();
                foreach (var key in ownerCooldown.Keys)
                    if (ReferenceEquals(key.Item1, h.LimitKey)) owners.Add(key);
                foreach (var key in owners) ownerCooldown.Remove(key);
            }
            h.Source = null;
            h.Engine = null;
            h.Options = default;
            h.Snapshot = null;
        }
    }
}
