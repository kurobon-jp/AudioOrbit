using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    /// <summary>Loads one Library by its extensionless Resources path. Main-thread only.</summary>
    public sealed class ResourcesAudioLibraryLoader : IAudioLibraryLoader
    {
        private sealed class Lease : IAudioLibraryLease
        {
            public IReadOnlyList<AudioLibrary> Libraries { get; private set; }
            internal Lease(AudioLibrary library) => Libraries = Array.AsReadOnly(new[] { library });
            public void Dispose() => Libraries = null;
        }

        private readonly string path;
        private Task<AudioLibrary> loading;
        private bool disposed;

        private ResourcesAudioLibraryLoader(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Resources Library path is empty.", nameof(path));
            this.path = path;
        }

        public static ResourcesAudioLibraryLoader FromPath(string path) => new(path);

        public async Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ResourcesAudioLibraryLoader));
            cancellationToken.ThrowIfCancellationRequested();
            if (loading == null || loading.IsFaulted || loading.IsCanceled) loading = LoadLibrary();
            var library = await AsyncWait.WithCancellation(loading, cancellationToken);
            if (disposed) throw new ObjectDisposedException(nameof(ResourcesAudioLibraryLoader));
            cancellationToken.ThrowIfCancellationRequested();
            return new Lease(library);
        }

        private async Task<AudioLibrary> LoadLibrary()
        {
            var request = Resources.LoadAsync<AudioLibrary>(path);
            var completed = new TaskCompletionSource<bool>();
            request.completed += _ => completed.TrySetResult(true);
            if (request.isDone) completed.TrySetResult(true);
            await completed.Task;
            if (request.asset is not AudioLibrary library)
                throw new InvalidOperationException("No AudioLibrary found at Resources path: " + path);
            return library;
        }

        /// <summary>Releases the cache. Existing leases retain their Library; Unity controls Resources unloading.</summary>
        public void Dispose()
        {
            disposed = true;
            loading = null;
        }
    }
}
