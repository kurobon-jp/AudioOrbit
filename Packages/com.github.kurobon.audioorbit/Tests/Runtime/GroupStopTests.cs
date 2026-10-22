using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class GroupStopTests
    {
        private readonly List<UnityEngine.Object> objects = new();
        private AudioEngine engine;
        private AudioCategory firstCategory, secondCategory;
        private AudioLibrary firstLibrary, secondLibrary;
        private AudioLibrary firstRegistration;
        private AudioClip clip;
        private T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); objects.Add(asset); return asset; }
        [SetUp] public void Setup()
        {
            var settings = Asset<AudioEngineSettings>();
            settings.InitialPoolSize = 4; settings.MaxPoolSize = 8; settings.MaxPlaybacks = 8;
            firstCategory = Asset<AudioCategory>(); firstCategory.Id = new AudioCategoryId("first");
            secondCategory = Asset<AudioCategory>(); secondCategory.Id = new AudioCategoryId("second");
            clip = AudioClip.Create("group stop", 44100, 1, 44100, false); objects.Add(clip);
            firstLibrary = Library("first", firstCategory); secondLibrary = Library("second", secondCategory);
            engine = new AudioEngine(settings);
            firstRegistration = firstLibrary; engine.Preload(firstLibrary); engine.Preload(secondLibrary);
        }
        private AudioLibrary Library(string key, AudioCategory category)
        {
            var library = Asset<AudioLibrary>();
            var sound = new AudioDefinition { Key = key, Category = category, Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound); return library;
        }
        [TearDown] public void Cleanup()
        { engine?.Dispose(); foreach (var asset in objects) if (asset != null) UnityEngine.Object.DestroyImmediate(asset); objects.Clear(); }
        private Task Stop(int scope, bool wait, float fade)
        {
            if (wait)
            {
                switch (scope)
                {
                    case 1: return engine.StopCategoryAsync(firstCategory.Id, fade);
                    case 2: return engine.StopOwnerAsync(firstCategory, fade);
                    case 3: return engine.StopLibraryAsync(firstRegistration, fade);
                    default: return engine.StopAllAsync(fade);
                }
            }
            switch (scope)
            {
                case 1: engine.StopCategory(firstCategory.Id, fade); break;
                case 2: engine.StopOwner(firstCategory, fade); break;
                case 3: engine.StopLibrary(firstRegistration, fade); break;
                default: engine.StopAll(fade); break;
            }
            return Task.CompletedTask;
        }
        [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)] [TestCase(3, false)]
        [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)] [TestCase(3, true)]
        public async Task GroupFadeStopsOnlyItsSnapshot(int scope, bool wait)
        {
            var first = engine.CreatePlayback("first").StartForTest(new PlayOptions { Owner = firstCategory });
            var second = engine.CreatePlayback("second").StartForTest(new PlayOptions { Owner = secondCategory });
            var stopping = Stop(scope, wait, 1);
            Assert.AreEqual(PlaybackState.FadingOut, first.State);
            Assert.AreEqual(scope == 0 ? PlaybackState.FadingOut : PlaybackState.Playing, second.State);
            Assert.AreEqual(!wait, stopping.IsCompleted);
            var later = engine.CreatePlayback("first").StartForTest(new PlayOptions { Owner = firstCategory });
            engine.Tick(2);
            await stopping;
            Assert.True(first.IsCompleted); Assert.AreEqual(scope == 0, second.IsCompleted);
            Assert.False(later.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await first.WaitForCompletionAsync()).EndReason);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public async Task ImmediateAsyncStopCompletesWithCanceledToken(int scope)
        {
            var playback = engine.CreatePlayback("first").StartForTest(new PlayOptions { Owner = firstCategory });
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Task operation;
            switch (scope)
            {
                case 1: operation = engine.StopCategoryAsync(firstCategory.Id, 0, canceled.Token); break;
                case 2: operation = engine.StopOwnerAsync(firstCategory, 0, canceled.Token); break;
                case 3: operation = engine.StopLibraryAsync(firstRegistration, 0, canceled.Token); break;
                default: operation = engine.StopAllAsync(0, canceled.Token); break;
            }
            Assert.True(operation.IsCompleted); await operation; Assert.True(playback.IsCompleted);
        }
        [Test] public async Task CancelingBatchWaitLeavesAllFadesRunning()
        {
            var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
            using var cancellation = new CancellationTokenSource();
            var stopping = engine.StopAllAsync(1, cancellation.Token); cancellation.Cancel();
            try { await stopping; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.AreEqual(PlaybackState.FadingOut, first.State); Assert.AreEqual(PlaybackState.FadingOut, second.State);
            engine.Tick(2); Assert.True(first.IsCompleted); Assert.True(second.IsCompleted);
        }
        [Test] public void InvalidFadeDoesNotStopAnyPlayback()
        {
            var first = engine.CreatePlayback("first").StartForTest();
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.StopAll(-1));
            Assert.False(first.IsCompleted); Assert.AreEqual(PlaybackState.Playing, first.State);
        }
        [Test] public async Task OverlappingBatchesCompleteWithTheirOwnLastPlayback()
        {
            var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
            var category = engine.StopCategoryAsync(firstCategory.Id, 10);
            var all = engine.StopAllAsync(10);
            first.Stop();
            Assert.True(category.IsCompleted); Assert.False(all.IsCompleted);
            await category;
            second.Stop(); Assert.True(all.IsCompleted); await all;
            var later = engine.CreatePlayback("first").StartForTest();
            var next = engine.StopAllAsync(10); Assert.False(next.IsCompleted);
            later.Stop(); await next;
        }
        [Test] public async Task CancelingOneBatchDoesNotCancelAnotherBatch()
        {
            var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
            using var cancellation = new CancellationTokenSource();
            var canceled = engine.StopAllAsync(10, cancellation.Token);
            var other = engine.StopAllAsync(10);
            cancellation.Cancel();
            try { await canceled; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.False(other.IsCompleted);
            first.Stop(); Assert.False(other.IsCompleted);
            second.Stop(); await other;
        }
        [Test] public async Task EngineDisposalCompletesAllOverlappingBatches()
        {
            var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
            var all = engine.StopAllAsync(10);
            var category = engine.StopCategoryAsync(firstCategory.Id, 10);
            engine.Dispose();
            await Task.WhenAll(all, category);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await first.WaitForCompletionAsync()).EndReason);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await second.WaitForCompletionAsync()).EndReason);
        }
        [Test] public async Task IndividualCompletionAndBatchWaitRemainIndependent()
        {
            var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
            var individual = first.WaitForCompletionAsync();
            var all = engine.StopAllAsync(10);
            first.Stop(); Assert.True(individual.IsCompleted); Assert.False(all.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await individual).EndReason);
            second.Stop(); await all;
        }
        [Test] public async Task FadingNotificationCanCreateAnOverlappingBatch()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
                Task nested = null;
                first.StateChanged += (_, state) => { if (state == PlaybackState.FadingOut) nested = engine.StopAllAsync(10); };
                var outer = engine.StopAllAsync(10);
                Assert.NotNull(nested); Assert.False(outer.IsCompleted); Assert.False(nested.IsCompleted);
                first.Stop(); Assert.False(outer.IsCompleted); Assert.False(nested.IsCompleted);
                second.Stop(); await Task.WhenAll(outer, nested);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        [Test] public async Task ReentrantImmediateStopDoesNotLeaveTheOuterBatchWaiting()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest();
                first.StateChanged += (_, state) => { if (state == PlaybackState.FadingOut) engine.StopAll(); };
                var stopping = engine.StopAllAsync(10);
                Assert.True(stopping.IsCompleted); await stopping;
                Assert.True(first.IsCompleted); Assert.True(second.IsCompleted);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        private sealed class ImmediateContext : SynchronizationContext
        {
            private readonly SynchronizationContext fallback = Current;
            private readonly int thread = Thread.CurrentThread.ManagedThreadId;
            public override void Post(SendOrPostCallback callback, object state)
            { if (Thread.CurrentThread.ManagedThreadId == thread) callback(state); else fallback.Post(callback, state); }
        }
        [Test] public void ReentrantStopsAndNewPlaybackDoNotChangeOuterSnapshot()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                var first = engine.CreatePlayback("first").StartForTest(); var second = engine.CreatePlayback("second").StartForTest(new PlayOptions { Owner = secondCategory });
                PlaybackHandle later = null;
                first.StateChanged += (_, state) =>
                {
                    if (state != PlaybackState.Ended) return;
                    engine.StopOwner(secondCategory);
                    later = engine.CreatePlayback("first").StartForTest();
                };
                engine.StopAll();
                Assert.True(first.IsCompleted); Assert.True(second.IsCompleted);
                Assert.NotNull(later); Assert.True(later.Started); Assert.False(later.IsCompleted);
                engine.StopAll(); Assert.True(later.IsCompleted);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        private sealed class PendingReference : IAudioReference, IAudioLibraryReference, IAudioLibraryLoader
        {
            public IAudioLibraryReference LibraryReference => this;
            public string Key;
            string IAudioReference.Key => Key;
            internal readonly TaskCompletionSource<IAudioLibraryLease> Pending = new();
            internal int Active;
            public PendingReference() { Key = "pending"; }
            public string LibraryIdentity => "group-stop-pending";
            public IAudioLibraryLoader CreateLibraryLoader() => this;
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken token = default) => Pending.Task;
            public void Dispose() { }
            private sealed class Lease : IAudioLibraryLease
            {
                private PendingReference owner;
                public IReadOnlyList<AudioLibrary> Libraries { get; }
                internal Lease(PendingReference owner, AudioLibrary library)
                { this.owner = owner; Libraries = new[] { library }; owner.Active++; }
                public void Dispose() { if (owner == null) return; owner.Active--; owner = null; }
            }
            internal void Deliver(AudioLibrary library) => Pending.SetResult(new Lease(this, library));
        }
        [TestCase(false)] [TestCase(true)]
        public async Task StopsLoadingAndActiveRequestsButExcludesRequestsCreatedByCallback(bool wait)
        {
            var library = Library("pending", firstCategory);
            var reference = new PendingReference();
            var pending = engine.CreatePlayback(reference).StartForTest();
            var previous = SynchronizationContext.Current;
            PlaybackHandle later = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                var active = engine.CreatePlayback("first").StartForTest();
                active.StateChanged += (_, state) => { if (state == PlaybackState.Ended) later = engine.CreatePlayback(reference).StartForTest(); };
                await Stop(0, wait, 0);
                Assert.True(active.IsCompleted); Assert.True(pending.IsCompleted); Assert.False(pending.Started);
                Assert.NotNull(later); Assert.False(later.IsCompleted);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            reference.Deliver(library);
            Assert.True((await later.ObserveStartAsync()).Started);
            later.Stop();
            Assert.AreEqual(1, reference.Active, "Playback completion must retain the engine cache.");
            engine.ReleaseLibrary((IAudioLibraryReference)reference);
            Assert.AreEqual(0, reference.Active);
        }
    }
}

