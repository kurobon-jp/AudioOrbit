using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioOrbit.Tests
{
    public sealed class AudioLibraryLoadingTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private AudioEngine engine;
        private AudioLibrary library;
        private AudioDefinition sound;
        private CountingLibraryLoader loader;
        private sealed class TestAudioReference : IAudioReference, IAudioLibraryReference
        {
            public IAudioLibraryReference LibraryReference => this;
            public string Key;
            string IAudioReference.Key => Key;
            public string LibraryIdentity { get; set; }
            public Func<IAudioLibraryLoader> Factory;
            public IAudioLibraryLoader CreateLibraryLoader() => Factory();
        }
        private sealed class CountingLibraryLoader : IAudioLibraryLoader, IDisposable
        {
            internal IReadOnlyList<AudioLibrary> Libraries;
            internal int Active;
            internal int Calls;
            internal bool Disposed;
            internal TaskCompletionSource<IAudioLibraryLease> Pending;
            private sealed class Lease : IAudioLibraryLease
            {
                private CountingLibraryLoader owner;
                public IReadOnlyList<AudioLibrary> Libraries { get; private set; }
                internal Lease(CountingLibraryLoader owner)
                { this.owner = owner; Libraries = owner.Libraries; owner.Active++; }
                public void Dispose()
                { if (owner == null) return; owner.Active--; owner = null; Libraries = null; }
            }
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default)
            {
                Calls++;
                if (Disposed) throw new ObjectDisposedException(nameof(CountingLibraryLoader));
                return Pending?.Task ?? Task.FromResult<IAudioLibraryLease>(new Lease(this));
            }
            internal void Deliver() => Pending.SetResult(new Lease(this));
            public void Dispose() { Disposed = true; }
        }
        private T Asset<T>() where T : ScriptableObject
        { var result = ScriptableObject.CreateInstance<T>(); objects.Add(result); return result; }
        [SetUp] public void Setup()
        {
            var settings = Asset<AudioEngineSettings>();
            settings.InitialPoolSize = 2; settings.MaxPoolSize = 2; settings.MaxPlaybacks = 2;
            var clip = AudioClip.Create("library test", 44100, 1, 44100, false); objects.Add(clip);
            sound = new AudioDefinition { Key = "library sound", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            library = Asset<AudioLibrary>(); library.Entries.Add(sound);
            loader = new CountingLibraryLoader { Libraries = new[] { library } };
            engine = new AudioEngine(settings);
        }
        [TearDown] public void Cleanup()
        {
            engine.Dispose(); Assert.AreEqual(0, loader.Active);
            foreach (var asset in objects) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            objects.Clear();
        }

        private TestAudioReference Reference(string identity = "test/library") =>
            new TestAudioReference { LibraryIdentity = identity, Key = sound.Key, Factory = () => loader };

        [Test] public async Task DirectLibraryPreloadIsIdempotentAndReleaseRemovesItsKeys()
        {
            engine.Preload(library);
            engine.Preload(library);
            using var playback = engine.CreatePlayback(sound.Key);
            playback.Play(); Assert.True(playback.Started); playback.Stop();
            engine.ReleaseLibrary(library);
            engine.ReleaseLibrary(library);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + sound.Key));
            playback.Play();
            Assert.IsInstanceOf<KeyNotFoundException>((await playback.WaitForCompletionAsync()).Error);
            engine.Preload(library);
            playback.Play(); Assert.True(playback.Started);
        }

        [Test] public void DirectLibraryPreloadFailureCanBeCorrectedAndRetried()
        {
            engine.Preload(library);
            var conflicting = Asset<AudioLibrary>();
            var entry = new AudioDefinition { Key = sound.Key, Loop = true,
                Clips = new List<ClipDefinition> { sound.Clips[0] } };
            conflicting.Entries.Add(entry);
            Assert.Throws<ArgumentException>(() => engine.Preload(conflicting));
            entry.Key = "corrected";
            engine.Preload(conflicting);
            using var original = engine.CreatePlayback(sound.Key);
            using var corrected = engine.CreatePlayback(entry.Key);
            original.Play(); corrected.Play();
            Assert.True(original.Started); Assert.True(corrected.Started);
            original.Stop(); corrected.Stop();
            engine.ReleaseLibrary(conflicting);
            original.Play(); Assert.True(original.Started);
        }

        [Test] public async Task DirectAndLoaderCachesShareLibraryAndRetainActivePlayback()
        {
            engine.Preload(library);
            await engine.PreloadAsync(loader);
            engine.ReleaseLibrary(library);
            using var playback = engine.CreatePlayback(sound.Key);
            playback.Play(); Assert.True(playback.Started);
            engine.ReleaseLibrary(loader);
            Assert.False(playback.IsCompleted);
            Assert.AreEqual(1, loader.Active);
            engine.StopLibrary(library);
            Assert.True(playback.IsCompleted);
            Assert.AreEqual(0, loader.Active);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + sound.Key));
            playback.Play();
            Assert.IsInstanceOf<KeyNotFoundException>((await playback.WaitForCompletionAsync()).Error);
        }

        [Test] public async Task MissingKeyResultIsIndependentOfLibraryLoadHistory()
        {
            using var playback = engine.CreatePlayback(sound.Key);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + sound.Key));
            playback.Play();
            var before = await playback.WaitForCompletionAsync();
            await engine.PreloadAsync(loader);
            playback.Play(); Assert.True(playback.Started); playback.Stop();
            engine.ReleaseLibrary(loader);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + sound.Key));
            playback.Play();
            var after = await playback.WaitForCompletionAsync();
            Assert.False(before.Started); Assert.False(after.Started);
            Assert.AreEqual(PlayRejectionReason.Failed, before.RejectionReason);
            Assert.AreEqual(before.RejectionReason, after.RejectionReason);
            Assert.AreEqual(before.EndReason, after.EndReason);
            Assert.IsInstanceOf<KeyNotFoundException>(before.Error);
            Assert.AreEqual(before.Error.GetType(), after.Error.GetType());
            Assert.AreEqual(before.Error.Message, after.Error.Message);
            await engine.PreloadAsync(loader);
            playback.Play(); Assert.True(playback.Started);
        }

        [Test] public async Task SequentialPlaybackKeepsCacheUntilExplicitRelease()
        {
            var reference = Reference();
            for (int i = 0; i < 3; i++)
            {
                using var playback = engine.CreatePlayback(reference);
                playback.Play();
                Assert.True((await playback.ObserveStartAsync()).Started);
                playback.Stop();
                Assert.AreEqual(1, loader.Active);
            }
            Assert.AreEqual(1, loader.Calls);
            Assert.True(loader.Disposed);
            engine.ReleaseLibrary(reference);
            Assert.AreEqual(0, loader.Active);
        }

        [Test] public async Task NaturalCompletionKeepsLibraryReadyForTheNextPlay()
        {
            var reference = Reference();
            using var playback = engine.CreatePlayback(reference);
            playback.Play(new PlayOptions { Loop = false, Pitch = 3 });
            var completion = await playback.WaitForCompletionAsync();
            Assert.AreEqual(PlaybackEndReason.Completed, completion.EndReason);
            Assert.AreEqual(1, loader.Active);
            playback.Play();
            Assert.True(playback.Started); Assert.AreEqual(1, loader.Calls);
            playback.Stop();
        }

        [Test] public async Task FailedLoaderLoadIsEvictedEvenAfterItsWaitIsCanceled()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            using var canceled = new CancellationTokenSource();
            var wait = engine.PreloadAsync(loader, canceled.Token);
            canceled.Cancel();
            try { await wait; Assert.Fail("Expected cancellation"); } catch (OperationCanceledException) { }
            loader.Pending.SetException(new InvalidOperationException("load failed"));
            await Task.Delay(10);
            loader.Pending = null;
            await engine.PreloadAsync(loader);
            Assert.AreEqual(2, loader.Calls); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task PreloadIsIdempotentAndIgnoresAudioKey()
        {
            var reference = Reference(); reference.Key = "missing";
            await engine.PreloadAsync(reference);
            reference.Factory = () => throw new Exception("Already cached");
            await engine.PreloadAsync(reference);
            reference.Key = sound.Key;
            var playback = engine.CreatePlayback(reference).StartForTest();
            Assert.True(playback.Started); playback.Stop();
            Assert.AreEqual(1, loader.Calls); Assert.AreEqual(1, loader.Active);
            engine.ReleaseLibrary(reference); engine.ReleaseLibrary(reference);
            Assert.AreEqual(0, loader.Active);
        }

        [Test] public async Task ExplicitReleaseKeepsActivePlaybackUntilItsLastUserStops()
        {
            var reference = Reference();
            await engine.PreloadAsync(reference);
            var first = engine.CreatePlayback(reference).StartForTest();
            var second = engine.CreatePlayback(reference).StartForTest();
            engine.ReleaseLibrary(reference);
            Assert.AreEqual(PlaybackState.Playing, first.State);
            first.Stop(); Assert.AreEqual(1, loader.Active);
            second.Stop(); Assert.AreEqual(0, loader.Active);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + sound.Key));
            Assert.AreEqual(PlayRejectionReason.Failed,
                engine.CreatePlayback(sound.Key).StartForTest().RejectionReason);
        }

        [Test] public async Task ReleasedReferenceLoadsAgainWithANewLoader()
        {
            var reference = Reference();
            await engine.PreloadAsync(reference);
            engine.ReleaseLibrary(reference);
            Assert.AreEqual(0, loader.Active);
            loader = new CountingLibraryLoader { Libraries = new[] { library } };
            var playback = engine.CreatePlayback(reference).StartForTest();
            Assert.True((await playback.ObserveStartAsync()).Started);
            playback.Stop(); Assert.AreEqual(1, loader.Calls); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task LoaderPreloadIsCachedAndCanBeExplicitlyReleased()
        {
            await engine.PreloadAsync(loader); await engine.PreloadAsync(loader);
            Assert.AreEqual(1, loader.Calls);
            var playback = engine.CreatePlayback(sound.Key).StartForTest();
            engine.ReleaseLibrary(loader);
            Assert.AreEqual(1, loader.Active); Assert.AreEqual(PlaybackState.Playing, playback.State);
            playback.Stop(); Assert.AreEqual(0, loader.Active);
            await engine.PreloadAsync(loader);
            Assert.AreEqual(2, loader.Calls);
        }

        [Test] public async Task PreloadAndConcurrentPlaybackSharePendingLoad()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var reference = Reference();
            var preload = engine.PreloadAsync(reference);
            var first = engine.CreatePlayback(reference).StartForTest();
            var second = engine.CreatePlayback(reference).StartForTest();
            first.Stop(); loader.Deliver(); await preload;
            Assert.True((await second.ObserveStartAsync()).Started);
            Assert.False(first.Started); Assert.AreEqual(1, loader.Calls);
            second.Stop(); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task ConcurrentLoaderPreloadsShareOneBackendCall()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var first = engine.PreloadAsync(loader); var second = engine.PreloadAsync(loader);
            Assert.AreEqual(1, loader.Calls); loader.Deliver();
            await Task.WhenAll(first, second);
            Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task CancelingPreloadWaitDoesNotDiscardSharedCache()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var reference = Reference();
            using var canceled = new CancellationTokenSource();
            var first = engine.PreloadAsync(reference, canceled.Token);
            var second = engine.PreloadAsync(reference);
            canceled.Cancel();
            try { await first; Assert.Fail("Expected cancellation"); } catch (OperationCanceledException) { }
            loader.Deliver(); await second;
            Assert.AreEqual(1, loader.Calls); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task StoppingPendingPlaybackLeavesLoadCached()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var reference = Reference();
            var playback = engine.CreatePlayback(reference).StartForTest();
            engine.StopAll(); loader.Deliver();
            await engine.PreloadAsync(reference);
            Assert.False(playback.Started); Assert.True(playback.IsCompleted);
            Assert.AreEqual(1, loader.Active);
            var next = engine.CreatePlayback(reference).StartForTest();
            Assert.True(next.Started); Assert.AreEqual(1, loader.Calls);
            next.Stop();
        }

        [Test] public async Task ReleaseWhileLoadingReclaimsLateAssetsAndAllowsRetry()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var oldLoader = loader;
            var reference = Reference();
            var preload = engine.PreloadAsync(reference);
            engine.ReleaseLibrary(reference);
            loader = new CountingLibraryLoader { Libraries = new[] { library } };
            await engine.PreloadAsync(reference);
            oldLoader.Deliver();
            try { await preload; Assert.Fail("Released load must not register"); } catch (OperationCanceledException) { }
            Assert.AreEqual(0, oldLoader.Active); Assert.True(oldLoader.Disposed);
            Assert.AreEqual(1, loader.Active);
            var playback = engine.CreatePlayback(reference).StartForTest();
            Assert.True(playback.Started); playback.Stop();
        }

        [Test] public async Task EngineDisposalReclaimsLateLoad()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var preload = engine.PreloadAsync(Reference());
            engine.Dispose(); loader.Deliver();
            try { await preload; Assert.Fail("Disposed engine must not register"); }
            catch (Exception error) { Assert.True(error is ObjectDisposedException || error is OperationCanceledException); }
            for (int i = 0; i < 20 && loader.Active != 0; i++) await Task.Delay(10);
            Assert.AreEqual(0, loader.Active); Assert.True(loader.Disposed);
        }

        [Test] public async Task EngineDisposalReleasesCacheAndPlayback()
        {
            var reference = Reference();
            await engine.PreloadAsync(reference);
            var playback = engine.CreatePlayback(reference).StartForTest();
            engine.Dispose();
            Assert.AreEqual(0, loader.Active);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await playback.WaitForCompletionAsync()).EndReason);
        }

        [Test] public async Task FailedReferenceLoadCanRetry()
        {
            loader.Pending = new TaskCompletionSource<IAudioLibraryLease>();
            var reference = Reference();
            var preload = engine.PreloadAsync(reference);
            loader.Pending.SetException(new InvalidOperationException("load failed"));
            try { await preload; Assert.Fail("Expected load failure"); } catch (InvalidOperationException) { }
            loader = new CountingLibraryLoader { Libraries = new[] { library } };
            await engine.PreloadAsync(reference);
            Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task MissingKeyDoesNotEvictCachedLibraryOrOtherPlayback()
        {
            var reference = Reference();
            await engine.PreloadAsync(reference);
            var valid = engine.CreatePlayback(reference).StartForTest();
            reference.Key = "missing";
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Audio key 'missing' is not present"));
            var failed = engine.CreatePlayback(reference).StartForTest();
            Assert.IsInstanceOf<KeyNotFoundException>((await failed.WaitForCompletionAsync()).Error);
            Assert.AreEqual(PlaybackState.Playing, valid.State);
            valid.Stop(); Assert.AreEqual(1, loader.Active); Assert.AreEqual(1, loader.Calls);
        }

        [TestCase("empty")]
        [TestCase("pitch")]
        [TestCase("weight")]
        [TestCase("clip")]
        [TestCase("volume")]
        public async Task InvalidEntryDoesNotBlockLibraryOrEvictItsCache(string field)
        {
            var invalid = new AudioDefinition { Key = "unfinished" };
            if (field != "empty") invalid.Clips.Add(new ClipDefinition { Clip = sound.Clips[0].Clip });
            switch (field)
            {
                case "pitch": invalid.Clips[0].Pitch = 0; break;
                case "weight": invalid.Clips[0].Weight = 0; break;
                case "clip": invalid.Clips[0].Clip = null; break;
                case "volume": invalid.Volume = -1; break;
            }
            library.Entries.Add(invalid);
            var reference = Reference(); await engine.PreloadAsync(reference);
            var valid = engine.CreatePlayback(reference).StartForTest(); Assert.True(valid.Started);
            reference.Key = invalid.Key;
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Exception:"));
            var rejected = engine.CreatePlayback(reference).StartForTest();
            Assert.NotNull((await rejected.WaitForCompletionAsync()).Error);
            valid.Stop(); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task DifferentReferencesDoNotResolveEachOthersKeys()
        {
            var otherLibrary = Asset<AudioLibrary>();
            otherLibrary.Entries.Add(new AudioDefinition { Key = "other", Clips = new List<ClipDefinition>
                { new ClipDefinition { Clip = sound.Clips[0].Clip } } });
            var otherLoader = new CountingLibraryLoader { Libraries = new[] { otherLibrary } };
            var first = Reference();
            var second = new TestAudioReference { LibraryIdentity = "other", Key = sound.Key, Factory = () => otherLoader };
            await engine.PreloadAsync(first); await engine.PreloadAsync(second);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Audio key 'library sound' is not present"));
            var rejected = engine.CreatePlayback(second).StartForTest();
            Assert.IsInstanceOf<KeyNotFoundException>((await rejected.WaitForCompletionAsync()).Error);
            engine.ReleaseLibrary(second); Assert.AreEqual(0, otherLoader.Active); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task DuplicateKeysRejectOnlyTheConflictingLoad()
        {
            var otherLibrary = Asset<AudioLibrary>();
            otherLibrary.Entries.Add(new AudioDefinition { Key = sound.Key });
            var otherLoader = new CountingLibraryLoader { Libraries = new[] { otherLibrary } };
            await engine.PreloadAsync(loader);
            try { await engine.PreloadAsync(otherLoader); Assert.Fail("Duplicate key must fail"); } catch (ArgumentException) { }
            Assert.AreEqual(0, otherLoader.Active); Assert.AreEqual(1, loader.Active);
        }

        [Test] public async Task OverlappingLoadersKeepIndependentCacheOwnership()
        {
            var other = new CountingLibraryLoader { Libraries = new[] { library } };
            await engine.PreloadAsync(loader); await engine.PreloadAsync(other);
            engine.ReleaseLibrary(loader); Assert.AreEqual(0, loader.Active); Assert.AreEqual(1, other.Active);
            var playback = engine.CreatePlayback(sound.Key).StartForTest(); Assert.True(playback.Started);
            engine.ReleaseLibrary(other); Assert.AreEqual(PlaybackState.Playing, playback.State);
            playback.Stop(); Assert.AreEqual(0, other.Active);
        }

        [Test] public async Task OverlappingReferenceSessionsSurviveReleaseOfTheirOriginalOwner()
        {
            var first = Reference("first");
            var otherLoader = new CountingLibraryLoader { Libraries = new[] { library } };
            var second = new TestAudioReference { LibraryIdentity = "second", Key = sound.Key, Factory = () => otherLoader };
            await engine.PreloadAsync(first); await engine.PreloadAsync(second);
            var active = engine.CreatePlayback(first).StartForTest();
            engine.ReleaseLibrary(first); Assert.AreEqual(1, loader.Active);
            await engine.PreloadAsync(second);
            active.Stop(); Assert.AreEqual(0, loader.Active); Assert.AreEqual(1, otherLoader.Active);
            var next = engine.CreatePlayback(second).StartForTest(); Assert.True(next.Started);
            engine.ReleaseLibrary(second); Assert.AreEqual(PlaybackState.Playing, next.State);
            next.Stop(); Assert.AreEqual(0, otherLoader.Active);
        }

        [Test] public async Task DuplicateLibraryRollsBackOwnershipAndCanBeCorrected()
        {
            loader.Libraries = new[] { library, library };
            try { await engine.PreloadAsync(loader); Assert.Fail("Duplicate Library must fail"); } catch (ArgumentException) { }
            Assert.AreEqual(0, loader.Active);
            loader.Libraries = new[] { library };
            await engine.PreloadAsync(loader); Assert.AreEqual(1, loader.Active);
            engine.ReleaseLibrary(loader); Assert.AreEqual(0, loader.Active);
        }

        [TestCase(ClipSelectionMode.Sequence)]
        [TestCase(ClipSelectionMode.Shuffle)]
        [TestCase(ClipSelectionMode.AvoidImmediateRepeat)]
        public async Task SelectionHistorySurvivesExplicitRelease(ClipSelectionMode mode)
        {
            sound.SelectionMode = mode;
            for (int i = 0; i < 2; i++) sound.Clips.Add(new ClipDefinition { Clip = sound.Clips[0].Clip });
            var seen = new HashSet<int>(); var previous = -1;
            for (int i = 0; i < 9; i++)
            {
                await engine.PreloadAsync(loader);
                var playback = engine.CreatePlayback(sound.Key).StartForTest();
                Assert.True(playback.Started);
                if (mode == ClipSelectionMode.Sequence) Assert.AreEqual(i % 3, playback.ClipIndex);
                if (mode == ClipSelectionMode.Shuffle)
                { Assert.True(seen.Add(playback.ClipIndex)); if (seen.Count == 3) seen.Clear(); }
                if (mode == ClipSelectionMode.AvoidImmediateRepeat) Assert.AreNotEqual(previous, playback.ClipIndex);
                previous = playback.ClipIndex;
                playback.Stop(); engine.ReleaseLibrary(loader); Assert.AreEqual(0, loader.Active);
            }
        }
    }
}
