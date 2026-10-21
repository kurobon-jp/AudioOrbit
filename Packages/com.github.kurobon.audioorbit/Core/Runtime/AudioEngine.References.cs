using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private readonly Dictionary<string, AudioLibrary> referencedLibraries = new(StringComparer.Ordinal);
        private sealed class ReferenceLoad
        {
            internal Task Task;
            internal IAudioLibraryLoader Loader;
            internal LibrarySession Session;
            internal bool Released;
        }
        private readonly Dictionary<string, ReferenceLoad> referenceLoads = new(StringComparer.Ordinal);

        private ValueTask<PlayStartResult> LoadReferencePlayback(IAudioLibraryReference reference, string identity,
            string key, PlaybackRequest handle, PlayOptions options)
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Library identity is empty.", nameof(reference));
            if (!referencedLibraries.ContainsKey(identity))
                return new ValueTask<PlayStartResult>(LoadReferencePlaybackAsync(reference, identity, key, handle, options));
            // The engine cache pins the Library; LoadPlayback takes its own hold before yielding.
            return LoadPlayback(ResolveReferenceAudio(identity, key), handle, options, handle.RequestToken);
        }

        private async Task<PlayStartResult> LoadReferencePlaybackAsync(IAudioLibraryReference reference, string identity,
            string key, PlaybackRequest handle, PlayOptions options)
        {
            using (await AcquireReferencedLibrary(reference, identity, handle.GetRequestToken()))
                return await LoadPlayback(ResolveReferenceAudio(identity, key), handle, options, handle.RequestToken);
        }

        private Definition ResolveReferenceAudio(string identity, string key)
        {
            var library = referencedLibraries[identity];
            var shared = sharedLibraries[library];
            if (!definitions.TryGetValue(key, out var sound) || sound.Library != shared.Handle)
                throw new KeyNotFoundException($"Audio key '{key}' is not present in Library '{library.name}'.");
            return sound;
        }

        /// <summary>Loads the entire referenced Library into the engine cache. No Audio key is required.</summary>
        public async Task PreloadAsync(IAudioLibraryReference reference,
            CancellationToken cancellationToken = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            using (await AcquireReferencedLibrary(reference, reference.LibraryIdentity, cancellationToken)) { }
        }

        /// <summary>Releases the referenced Library cache.</summary>
        public void ReleaseLibrary(IAudioLibraryReference reference)
        {
            Check();
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (referenceLoads.TryGetValue(reference.LibraryIdentity, out var loading))
                ReleaseLibrary(loading.Loader);
        }

        private async Task<SessionReference> AcquireReferencedLibrary(IAudioLibraryReference reference, string identity,
            CancellationToken token)
        {
            Check(); token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Library identity is empty.", nameof(reference));
            if (!referenceLoads.TryGetValue(identity, out var loading))
            {
                loading = new ReferenceLoad { Loader = reference.CreateLibraryLoader() };
                if (loading.Loader == null) throw new InvalidOperationException("Audio reference returned a null Library loader.");
                referenceLoads.Add(identity, loading);
                loading.Task = LoadReferenceLibrary(identity, loading);
            }
            await AsyncWait.WithCancellation(loading.Task, token);
            Check(); token.ThrowIfCancellationRequested();
            if (loading.Released) throw new OperationCanceledException("Library was released while loading.");
            return AcquireLoadedReferencedLibrary(identity, token);
        }

        private SessionReference AcquireLoadedReferencedLibrary(string identity, CancellationToken token)
        {
            Check(); token.ThrowIfCancellationRequested();
            var session = referenceLoads[identity].Session;
            if (session == null || session.Released)
                throw new InvalidOperationException("Referenced Library has no active session.");
            session.Users++;
            return new SessionReference(this, session);
        }

        private async Task LoadReferenceLibrary(string identity, ReferenceLoad loading)
        {
            using (loading.Loader)
            {
                try
                {
                    using var acquired = await AcquireLibraries(loading.Loader, lifetime.Token);
                    if (acquired.Session.Shared.Count != 1)
                        throw new InvalidOperationException("A Library reference must resolve to exactly one AudioLibrary.");
                    Check();
                    if (loading.Released) throw new OperationCanceledException("Library was released while loading.");
                    loading.Session = acquired.Session;
                    referencedLibraries[identity] = acquired.Session.Shared[0].Asset;
                }
                catch
                {
                    if (!disposed && !loading.Released) ReleaseLibrary(loading.Loader);
                    throw;
                }
            }
        }

        private void ForgetLoaderReferences(IAudioLibraryLoader loader)
        {
            var keys = new List<string>();
            foreach (var pair in referenceLoads)
                if (ReferenceEquals(pair.Value.Loader, loader))
                {
                    pair.Value.Released = true;
                    keys.Add(pair.Key);
                }
            foreach (var key in keys)
            {
                referenceLoads.Remove(key);
                referencedLibraries.Remove(key);
            }
        }

        private void ForgetLibraryReferences(AudioLibrary library)
        {
            var keys = new List<string>();
            foreach (var pair in referencedLibraries) if (pair.Value == library) keys.Add(pair.Key);
            foreach (var key in keys) referencedLibraries.Remove(key);
        }
    }
}
