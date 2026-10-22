using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    /// <summary>A reusable handle for one audio entry. Waits remain bound to the run on which they started.</summary>
    public sealed class PlaybackHandle : IDisposable
    {
        private readonly AudioEngine engine;
        private readonly string key;
        private IAudioReference reference;
        private ExternalSourceHandle external;
        private readonly SynchronizationContext context = SynchronizationContext.Current;
        private bool disposed, starting;
        private PlaybackState notified = PlaybackState.Idle;
        private Action<PlaybackHandle, PlaybackState> stateChanged;
        private Delegate[] listeners;
        internal Playback ReusablePlayback { get; } = new Playback();

        internal PlaybackRequest Request { get; private set; }
        internal AudioEngine Engine => Request?.Engine;
        internal Playback Playback => Request?.Playback;
        internal int ClipIndex => Request?.ClipIndex ?? -1;
        public string Key => Request?.Key ?? key ?? reference?.Key;
        public bool Started => Request?.Started ?? false;
        public PlayRejectionReason RejectionReason => Request?.RejectionReason ?? PlayRejectionReason.None;
        public PlaybackState State => Request?.State ?? PlaybackState.Idle;
        public bool IsDisposed => disposed;
        public bool IsCompleted => Request == null || Request.IsCompleted;

        public event Action<PlaybackHandle, PlaybackState> StateChanged
        {
            add
            {
                Check();
                stateChanged += value;
                listeners = stateChanged?.GetInvocationList();
            }
            remove
            {
                engine.CheckThread();
                stateChanged -= value;
                listeners = stateChanged?.GetInvocationList();
            }
        }

        internal PlaybackHandle(AudioEngine engine, string key, IAudioReference reference,
            ExternalSourceHandle external = null)
        {
            this.engine = engine;
            this.key = key;
            this.reference = reference;
            this.external = external;
        }

        private void Check()
        {
            engine.CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(PlaybackHandle));
        }

        /// <summary>Starts a new run, immediately stopping any current playback or pending load.</summary>
        public void Play(PlayOptions options = default)
        {
            Check();
            if (starting)
                throw new InvalidOperationException("Play cannot be called recursively while starting playback.");
            starting = true;
            try
            {
                Request?.Stop();
                Check();
                engine.PlayReusable(this, key, reference, external, options);
            }
            finally
            {
                starting = false;
            }
        }

        /// <summary>Starts playback and waits for completion. Cancellation stops only this run.</summary>
        public Task<PlaybackResult> PlayAsync(PlayOptions options = default, CancellationToken cancellationToken = default)
        {
            Check();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<PlaybackResult>(cancellationToken);
            Play(options);
            return WaitForRunAsync(Request, cancellationToken);
        }

        public Task<PlaybackResult> PlayAsync(CancellationToken token) => PlayAsync(default, token);

        private static async Task<PlaybackResult> WaitForRunAsync(PlaybackRequest request, CancellationToken token)
        {
            try { return await request.WaitForCompletionAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                request.Stop();
                throw;
            }
        }

        internal void Begin(PlaybackRequest handle)
        {
            Request = handle;
            notified = PlaybackState.Idle;
            Notify(handle, PlaybackState.Loading);
        }

        internal void Notify(PlaybackRequest handle, PlaybackState state)
        {
            if (disposed || Request != handle || notified == state) return;
            notified = state;
            if (listeners == null) return;
            var notification = Notification.Rent(this, handle, state, listeners);
            if (context == null) Notification.Dispatch(notification);
            else
                try
                {
                    context.Post(Notification.Callback, notification);
                }
                catch
                {
                    notification.Release();
                    throw;
                }
        }

        internal void End(PlaybackRequest handle)
        {
            if (Request != handle) return;
            var playback = ReusablePlayback;
            playback.Handle = null;
            playback.Key = null;
            playback.LimitKey = null;
            playback.Library = null;
            playback.Source = null;
            playback.Lease = null;
            playback.External = null;
            playback.Owner = null;
            playback.Target = null;
#if UNITY_6000_3_OR_NEWER
            playback.Generator = null;
#endif
            playback.Ended = true;
            Notify(handle, PlaybackState.Ended);
        }

        private sealed class Notification
        {
            private static readonly Stack<Notification> pool = new();
            internal static readonly SendOrPostCallback Callback = Dispatch;
            private PlaybackHandle owner;
            private PlaybackRequest handle;
            private PlaybackState state;
            private Delegate[] listeners;

            internal static Notification Rent(PlaybackHandle owner, PlaybackRequest handle, PlaybackState state,
                Delegate[] listeners)
            {
                var item = pool.Count > 0 ? pool.Pop() : new Notification();
                item.owner = owner;
                item.handle = handle;
                item.state = state;
                item.listeners = listeners;
                return item;
            }

            internal static void Dispatch(object value)
            {
                var item = (Notification)value;
                try
                {
                    if (item.owner.disposed || item.owner.Request != item.handle) return;
                    foreach (Action<PlaybackHandle, PlaybackState> listener in item.listeners)
                    {
                        if (item.owner.disposed || item.owner.Request != item.handle) break;
                        try
                        {
                            listener(item.owner, item.state);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error);
                        }
                    }
                }
                finally
                {
                    item.Release();
                }
            }

            internal void Release()
            {
                owner = null;
                handle = null;
                listeners = null;
                if (pool.Count < 256) pool.Push(this);
            }
        }

        private PlaybackRequest RequireRequest() =>
            Request ?? throw new InvalidOperationException("Call Play before waiting or changing a run.");

        public Task<PlaybackResult> WaitForCompletionAsync(CancellationToken cancellationToken = default) =>
            RequireRequest().WaitForCompletionAsync(cancellationToken);

        public void Stop(float fadeSeconds = 0)
        {
            Check();
            AudioEngine.ValidateNonNegative(fadeSeconds, nameof(fadeSeconds));
            Request?.Stop(fadeSeconds);
        }

        public Task<PlaybackResult> StopAsync(float fadeSeconds = 0, CancellationToken cancellationToken = default)
        {
            Check();
            return RequireRequest().StopAsync(fadeSeconds, cancellationToken);
        }

        public bool Pause()
        {
            Check();
            return Request?.Pause() ?? false;
        }

        public bool Resume()
        {
            Check();
            return Request?.Resume() ?? false;
        }

        public bool SetVolume(float volume)
        {
            Check();
            return RequireRequest().SetVolume(volume);
        }

        public bool SetPitch(float pitch)
        {
            Check();
            return RequireRequest().SetPitch(pitch);
        }

        public bool SetPosition(Vector3 position)
        {
            Check();
            return RequireRequest().SetPosition(position);
        }

        public bool AttachTo(Transform target, Vector3 offset = default)
        {
            Check();
            return RequireRequest().AttachTo(target, offset);
        }

        public bool Loop
        {
            get => Request?.Loop ?? false;
            set
            {
                Check();
                RequireRequest().Loop = value;
            }
        }

        public void Dispose()
        {
            engine.CheckThread();
            if (disposed) return;
            disposed = true;
            stateChanged = null;
            listeners = null;
            Request?.Stop();
            external?.Dispose();
            reference = null;
            external = null;
            ReusablePlayback.Handle = null;
            ReusablePlayback.LimitKey = null;
            ReusablePlayback.Key = null;
        }
    }
}