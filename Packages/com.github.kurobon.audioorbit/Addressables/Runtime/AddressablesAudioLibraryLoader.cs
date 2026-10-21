#if ENABLE_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using AddressableApi = UnityEngine.AddressableAssets.Addressables;

namespace AudioOrbit.Addressables
{
    /// <summary>Caches libraries selected by an explicit address or label. Main-thread only.</summary>
    public sealed class AddressablesAudioLibraryLoader : IAudioLibraryLoader
    {
        private sealed class Entry
        {
            internal Task<IReadOnlyList<AudioLibrary>> Task;
            internal AsyncOperationHandle<IList<AudioLibrary>> Assets;
            internal bool HasAssets, Releasing;
            internal int Users;
        }

        private sealed class Lease : IAudioLibraryLease
        {
            private AddressablesAudioLibraryLoader loader;
            private readonly Entry entry;
            public IReadOnlyList<AudioLibrary> Libraries { get; private set; }

            internal Lease(AddressablesAudioLibraryLoader loader, Entry entry, IReadOnlyList<AudioLibrary> libraries)
            {
                this.loader = loader;
                this.entry = entry;
                Libraries = libraries;
            }

            public void Dispose()
            {
                if (loader == null) return;
                loader.Release(entry);
                loader = null;
                Libraries = null;
            }
        }

        private readonly string key;
        private Entry cached;
        private bool disposed;

        private AddressablesAudioLibraryLoader(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is empty.", nameof(key));
            this.key = key;
        }

        public static AddressablesAudioLibraryLoader FromKey(string key) => new(key);

        public async Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AddressablesAudioLibraryLoader));
            cancellationToken.ThrowIfCancellationRequested();
            if (cached == null)
            {
                cached = new Entry();
                cached.Task = LoadLibraries(cached);
            }

            var entry = cached;
            entry.Users++;
            _ = EvictFailure(entry);
            try
            {
                var libraries = await Wait(entry.Task, cancellationToken);
                if (disposed) throw new ObjectDisposedException(nameof(AddressablesAudioLibraryLoader));
                cancellationToken.ThrowIfCancellationRequested();
                return new Lease(this, entry, libraries);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Release(entry);
                throw;
            }
        }

        private async Task<IReadOnlyList<AudioLibrary>> LoadLibraries(Entry entry)
        {
            var locations = AddressableApi.LoadResourceLocationsAsync(key, typeof(AudioLibrary));
            try
            {
                var found = await locations.Task;
                if (locations.Status != AsyncOperationStatus.Succeeded || found == null || found.Count == 0)
                    throw new InvalidOperationException("No AudioLibrary found for Addressables key: " + key);
                entry.Assets = AddressableApi.LoadAssetsAsync<AudioLibrary>(found, null, true);
                entry.HasAssets = true;
                var libraries = await entry.Assets.Task;
                if (entry.Assets.Status != AsyncOperationStatus.Succeeded || libraries == null)
                    throw new InvalidOperationException("Library load failed: " + key, entry.Assets.OperationException);
                var ids = new HashSet<string>();
                foreach (var library in libraries)
                {
                    if (library == null) throw new InvalidOperationException("Loaded a null AudioLibrary.");
                    foreach (var sound in library.Entries)
                        if (sound == null || string.IsNullOrWhiteSpace(sound.Key) || !ids.Add(sound.Key))
                            throw new InvalidOperationException(
                                "Invalid or duplicate Audio key in libraries selected by: " + key);
                }

                return new List<AudioLibrary>(libraries);
            }
            finally
            {
                if (locations.IsValid()) AddressableApi.Release(locations);
            }
        }

        private static async Task<T> Wait<T>(Task<T> task, CancellationToken token)
        {
            if (task.IsCompleted) return await task;
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task) throw new OperationCanceledException(token);
                return await task;
            }
        }

        private void Release(Entry entry)
        {
            if (--entry.Users != 0) return;
            if (disposed || entry.Task.IsFaulted || entry.Task.IsCanceled) _ = ReleaseAssets(entry);
        }

        private async Task EvictFailure(Entry entry)
        {
            try
            {
                await entry.Task;
            }
            catch
            {
                if (entry.Users == 0) await ReleaseAssets(entry);
            }
        }

        private async Task ReleaseAssets(Entry entry)
        {
            if (entry.Releasing) return;
            entry.Releasing = true;
            if (ReferenceEquals(cached, entry)) cached = null;
            try
            {
                await entry.Task;
            }
            catch
            {
            }
            finally
            {
                if (entry.HasAssets && entry.Assets.IsValid()) AddressableApi.Release(entry.Assets);
                entry.Assets = default;
                entry.HasAssets = false;
                entry.Task = null;
            }
        }

        /// <summary>Closes the cache. Existing playback/preload leases keep their libraries alive.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (cached != null && cached.Users == 0) _ = ReleaseAssets(cached);
        }
    }
}
#endif