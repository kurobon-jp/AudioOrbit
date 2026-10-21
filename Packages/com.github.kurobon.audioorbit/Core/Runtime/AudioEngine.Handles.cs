using System;
using System.Collections.Generic;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private readonly Dictionary<PlaybackRequest, PlaybackHandle> reusableRequests = new();

        public PlaybackHandle CreatePlayback(string key)
        {
            Check();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Invalid Audio key.", nameof(key));
            return new PlaybackHandle(this, key, null);
        }

        public PlaybackHandle CreatePlayback(IAudioReference reference)
        {
            Check();
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            return new PlaybackHandle(this, null, reference);
        }

        public PlaybackHandle CreatePlayback(ExternalSourceHandle source)
        {
            Check();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Engine != this) throw new ArgumentException("Register the source with this engine.", nameof(source));
            return new PlaybackHandle(this, null, null, source);
        }

        internal PlaybackRequest PlayReusable(PlaybackHandle owner, string key, IAudioReference reference, ExternalSourceHandle externalSource, PlayOptions options)
        {
            Check();
            var library = reference?.LibraryReference;
            if (reference != null) key = reference.Key;
            if (externalSource == null && !(reference is DirectAudioReference) && string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Invalid Audio key.", nameof(key));
            var handle = CreateRequest(key, externalSource == null ? options : new PlayOptions { Owner = externalSource.Options.Owner });
            reusableRequests.Add(handle, owner);
            try
            {
                owner.Begin(handle);
                if (handle.IsCompleted) return handle;
                if (externalSource != null) PlayExternalRequest(handle, externalSource);
                else if (reference is DirectAudioReference direct) StartDirectRequest(direct, handle, options);
                else if (library != null)
                    RunRequest(handle, LoadReferencePlayback(library, library.LibraryIdentity, key, handle, options));
                else RunRequest(handle, LoadPlayback(key, handle, options, handle.RequestToken));
            }
            catch (Exception error) { FailRequest(handle, error); }
            return handle;
        }

        private Playback GetReusablePlayback(PlaybackRequest handle) =>
            handle != null && reusableRequests.TryGetValue(handle, out var owner) ? owner.ReusablePlayback : null;

        internal void NotifyPlaybackHandle(PlaybackRequest handle, PlaybackState state)
        {
            if (reusableRequests.TryGetValue(handle, out var owner)) owner.Notify(handle, state);
        }

        internal void CompletePlaybackHandle(PlaybackRequest handle)
        {
            if (!reusableRequests.TryGetValue(handle, out var owner)) return;
            reusableRequests.Remove(handle);
            owner.End(handle);
        }
    }
}
