using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private sealed class DirectLibraryLoader : IAudioLibraryLoader
        {
            private sealed class Lease : IAudioLibraryLease
            {
                public IReadOnlyList<AudioLibrary> Libraries { get; private set; }
                internal Lease(AudioLibrary library) => Libraries = new[] { library };
                public void Dispose() => Libraries = null;
            }

            private readonly AudioLibrary library;
            internal DirectLibraryLoader(AudioLibrary library) => this.library = library;
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(CreateLease());
            }
            internal IAudioLibraryLease CreateLease() => new Lease(library);
            public void Dispose() { }
        }

        private readonly Dictionary<AudioLibrary, DirectLibraryLoader> directLibraryLoaders = new();

        /// <summary>Caches an already loaded Library. The caller retains ownership of the asset.</summary>
        public void Preload(AudioLibrary library)
        {
            Check();
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (directLibraryLoaders.ContainsKey(library)) return;
            var loader = new DirectLibraryLoader(library);
            var cached = RegisterLoadedLibraries(loader, loader.CreateLease());
            libraryLoads.Add(loader, Task.FromResult(cached));
            directLibraryLoaders.Add(library, loader);
        }

        /// <summary>Releases a directly supplied Library cache. Active playback retains its resources.</summary>
        public void ReleaseLibrary(AudioLibrary library)
        {
            Check();
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (!directLibraryLoaders.TryGetValue(library, out var loader)) return;
            directLibraryLoaders.Remove(library);
            ReleaseLibrary(loader);
        }

        private sealed class SelectionHistory
        {
            internal int Last, Position;
            internal int[] Shuffle;
        }
        private sealed class LibrarySession
        {
            internal IAudioLibraryLoader Loader;
            internal IAudioLibraryLease Lease;
            internal readonly List<LibraryHandle> Handles = new();
            internal readonly List<SharedLibrary> Shared = new();
            internal readonly HashSet<string> Keys = new();
            internal int Users;
            internal bool Released;
        }
        private sealed class SharedLibrary
        {
            internal AudioLibrary Asset;
            internal LibraryHandle Handle;
            internal readonly HashSet<LibrarySession> Sessions = new();
        }
        private sealed class SessionReference : IDisposable
        {
            private AudioEngine engine;
            internal LibrarySession Session { get; }
            internal SessionReference(AudioEngine engine, LibrarySession session)
            { this.engine = engine; Session = session; }
            public void Dispose()
            {
                if (engine == null) return;
                engine.CheckThread();
                if (!Session.Released && --Session.Users == 0) engine.ReleaseLibrarySession(Session);
                engine = null;
            }
        }
        private readonly Dictionary<IAudioLibraryLoader, LibrarySession> librarySessions = new();
        private readonly Dictionary<AudioLibrary, SharedLibrary> sharedLibraries = new();
        // History belongs to a live loader; it must not keep a discarded loader (or its assets) alive.
        private readonly ConditionalWeakTable<IAudioLibraryLoader, Dictionary<string, SelectionHistory>> librarySelections =
            new();

        private readonly Dictionary<IAudioLibraryLoader, Task<SessionReference>> libraryLoads = new();

        private async Task<SessionReference> AcquireLibraries(IAudioLibraryLoader loader, CancellationToken token)
        {
            Check();
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            token.ThrowIfCancellationRequested();
            if (!libraryLoads.TryGetValue(loader, out var operation))
            {
                operation = AcquireLibrariesCore(loader, lifetime.Token);
                libraryLoads.Add(loader, operation);
                _ = EvictFailedLibraryLoad(loader, operation);
            }
            SessionReference cached;
            try { cached = await AsyncWait.WithCancellation(operation, token); }
            catch
            {
                if ((operation.IsFaulted || operation.IsCanceled) &&
                    libraryLoads.TryGetValue(loader, out var current) && ReferenceEquals(current, operation))
                    libraryLoads.Remove(loader);
                throw;
            }
            Check(); token.ThrowIfCancellationRequested();
            if (!libraryLoads.TryGetValue(loader, out var active) || !ReferenceEquals(active, operation))
                throw new OperationCanceledException("Library was released while loading.");
            cached.Session.Users++;
            return new SessionReference(this, cached.Session);
        }

        private async Task EvictFailedLibraryLoad(IAudioLibraryLoader loader, Task<SessionReference> operation)
        {
            try { await operation; }
            catch
            {
                if (libraryLoads.TryGetValue(loader, out var current) && ReferenceEquals(current, operation))
                    libraryLoads.Remove(loader);
            }
        }

        /// <summary>Releases the engine cache. Active playback retains its resources until it ends.</summary>
        public void ReleaseLibrary(IAudioLibraryLoader loader)
        {
            Check();
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            ForgetLoaderReferences(loader);
            if (!libraryLoads.Remove(loader, out var operation)) return;
            if (operation.IsCompletedSuccessfully) operation.Result.Dispose();
            else _ = ReleaseCachedLoad(operation);
        }

        private static async Task ReleaseCachedLoad(Task<SessionReference> operation)
        {
            try { (await operation).Dispose(); }
            catch
            {
                // ignored
            }
        }

        private async Task<SessionReference> AcquireLibrariesCore(IAudioLibraryLoader libraryLoader, CancellationToken token)
        {
            Check();
            if (libraryLoader == null) throw new ArgumentNullException(nameof(libraryLoader));
            token.ThrowIfCancellationRequested();
            IAudioLibraryLease lease = null;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var operation = libraryLoader.LoadAsync(linked.Token);
                if (operation == null) throw new InvalidOperationException("Library loader returned a null Task.");
                try { lease = await AsyncWait.WithCancellation(operation, linked.Token); }
                catch
                {
                    _ = ReclaimLibraries(operation);
                    if (disposed) throw new ObjectDisposedException(nameof(AudioEngine));
                    token.ThrowIfCancellationRequested();
                    throw;
                }
            }
            try { Check(); token.ThrowIfCancellationRequested(); }
            catch { lease?.Dispose(); throw; }
            return RegisterLoadedLibraries(libraryLoader, lease);
        }

        private SessionReference RegisterLoadedLibraries(IAudioLibraryLoader libraryLoader, IAudioLibraryLease lease)
        {
            try
            {
                Check();
                if (lease?.Libraries == null || lease.Libraries.Count == 0)
                    throw new InvalidOperationException("Library loader returned no libraries.");
                if (librarySessions.TryGetValue(libraryLoader, out var existing))
                {
                    existing.Users++;
                    return new SessionReference(this, existing);
                }
                var session = new LibrarySession { Loader = libraryLoader, Lease = lease, Users = 1 };
                try
                {
                    foreach (var library in lease.Libraries)
                    {
                        if (library == null) throw new ArgumentException("Null library.");
                        var fresh = !sharedLibraries.TryGetValue(library, out var shared);
                        if (fresh)
                        {
                            shared = new SharedLibrary { Asset = library, Handle = RegisterLibrary(library) };
                            sharedLibraries.Add(library, shared);
                        }
                        if (!shared.Sessions.Add(session)) throw new ArgumentException("Duplicate Library in loaded libraries.");
                        session.Shared.Add(shared);
                        var handle = shared.Handle;
                        session.Handles.Add(handle);
                        foreach (var id in handle.Keys)
                        {
                            if (!session.Keys.Add(id)) throw new ArgumentException("Duplicate Audio key in loaded libraries.");
                            if (fresh) definitions[id].Session = session;
                            if (fresh && librarySelections.TryGetValue(libraryLoader, out var history) && history.TryGetValue(id, out var state))
                            {
                                var definition = definitions[id];
                                definition.LastClip = state.Last;
                                definition.Shuffle = state.Shuffle;
                                definition.ShufflePosition = state.Position;
                            }
                        }
                    }
                    librarySessions.Add(libraryLoader, session);
                    lease = null;
                    return new SessionReference(this, session);
                }
                catch
                {
                    foreach (var shared in session.Shared) ReleaseSharedLibrary(shared, session);
                    throw;
                }
            }
            finally { lease?.Dispose(); }
        }

        private static async Task ReclaimLibraries(Task<IAudioLibraryLease> operation)
        {
            try { (await operation)?.Dispose(); }
            catch
            {
                // ignored
            }
        }

        private void ReleaseLibrarySession(LibrarySession session)
        {
            if (session.Released) return;
            session.Released = true;
            librarySessions.Remove(session.Loader);
            if (!disposed)
            {
                var history = librarySelections.GetOrCreateValue(session.Loader);
                history.Clear();
                foreach (var id in session.Keys)
                    if (definitions.TryGetValue(id, out var definition))
                        history[id] = new SelectionHistory { Last = definition.LastClip,
                            Shuffle = definition.Shuffle, Position = definition.ShufflePosition };
            }
            foreach (var shared in session.Shared) ReleaseSharedLibrary(shared, session);
            session.Lease.Dispose();
            session.Lease = null;
            session.Shared.Clear(); session.Handles.Clear(); session.Keys.Clear();
        }

        private void ReleaseSharedLibrary(SharedLibrary shared, LibrarySession released)
        {
            shared.Sessions.Remove(released);
            if (shared.Sessions.Count == 0)
            {
                sharedLibraries.Remove(shared.Asset);
                ForgetLibraryReferences(shared.Asset);
                if (!disposed) UnregisterLibrary(shared.Handle);
                else shared.Handle.Registered = false;
                return;
            }
            // Owners are indexed by Library; choose one replacement for all its definitions.
            LibrarySession replacement = null;
            foreach (var owner in shared.Sessions) { replacement = owner; break; }
            foreach (var id in shared.Handle.Keys)
                if (definitions.TryGetValue(id, out var definition) && ReferenceEquals(definition.Session, released))
                    definition.Session = replacement;
        }

        /// <summary>Loads libraries into the engine cache. Cancellation stops only this wait.</summary>
        public async Task PreloadAsync(IAudioLibraryLoader libraryLoader,
            CancellationToken cancellationToken = default)
        {
            using (await AcquireLibraries(libraryLoader, cancellationToken)) { }
        }
    }
}
