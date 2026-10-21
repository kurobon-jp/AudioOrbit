using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class PlaybackHandleTests
    {
        private AudioEngine engine;
        private AudioEngineSettings settings;
        private AudioLibrary library, pendingLibrary;
        private AudioClip clip;
        [SetUp] public void Setup()
        {
            settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
            settings.InitialPoolSize = 2; settings.MaxPoolSize = 4; settings.MaxPlaybacks = 4;
            clip = AudioClip.Create("reusable", 44100, 1, 44100, false);
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            var sound = new AudioDefinition { Key = "reusable", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound);
            engine = new AudioEngine(settings); engine.RegisterLibrary(library);
        }
        [TearDown] public void Cleanup()
        {
            engine.Dispose(); if (pendingLibrary != null) UnityEngine.Object.DestroyImmediate(pendingLibrary); UnityEngine.Object.DestroyImmediate(library);
            UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(settings);
        }
        [Test] public async Task EngineReferenceResolvesTextKeyOnEachPlay()
        {
            var reference = new EngineAudioReference("reusable");
            using var playback = engine.CreatePlayback(reference);
            Assert.AreEqual("reusable", playback.Key);
            playback.Play();
            Assert.True(playback.Started);
            Assert.AreSame(clip, playback.Playback.Source.clip);
            reference.Key = "registered_later";
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("Unknown Audio key: registered_later"));
            playback.Play();
            var missing = await playback.WaitForCompletionAsync();
            Assert.False(missing.Started);
            Assert.AreEqual(PlayRejectionReason.Failed, missing.RejectionReason);
            Assert.IsInstanceOf<KeyNotFoundException>(missing.Error);
            pendingLibrary = ScriptableObject.CreateInstance<AudioLibrary>();
            var definition = new AudioDefinition { Key = reference.Key, Loop = true };
            definition.Clips.Add(new ClipDefinition { Clip = clip });
            pendingLibrary.Entries.Add(definition);
            engine.RegisterLibrary(pendingLibrary);
            playback.Play();
            Assert.True(playback.Started);
            Assert.AreEqual(reference.Key, playback.Key);
        }

        [Test] public void PublicApiExposesOnlyTheReusableHandle()
        {
            Assert.AreEqual(typeof(void), typeof(PlaybackHandle).GetMethod(nameof(PlaybackHandle.Play)).ReturnType);
            Assert.IsNull(typeof(PlaybackHandle).GetProperty("Current"));
            Assert.IsNull(typeof(PlaybackHandle).Assembly.GetType("AudioOrbit.AudioPlayback"));
            Assert.False(typeof(PlaybackRequest).IsPublic);
        }

        [Test] public async Task NotificationsCanBeRegisteredBeforeSynchronousStart()
        {
            using var playback = engine.CreatePlayback("reusable");
            Assert.AreEqual(PlaybackState.Idle, playback.State);
            var states = new List<PlaybackState>();
            var thread = Thread.CurrentThread.ManagedThreadId;
            playback.StateChanged += (_, state) => { Assert.AreEqual(thread, Thread.CurrentThread.ManagedThreadId); states.Add(state); };
            var handle = playback.StartRunForTest(); Assert.True(handle.Started);
            playback.Pause(); playback.Resume(); playback.Stop();
            for (var i = 0; i < 50 && states.Count < 5; i++) await Task.Delay(10);
            CollectionAssert.AreEqual(new[] { PlaybackState.Loading, PlaybackState.Playing, PlaybackState.Paused, PlaybackState.Playing, PlaybackState.Ended }, states);
        }
        [Test] public async Task ReusePreservesOldHandlesAndCompletionWaits()
        {
            using var playback = engine.CreatePlayback("reusable");
            var first = playback.StartRunForTest(); var waiting = playback.WaitForCompletionAsync();
            playback.Stop();
            var second = playback.StartRunForTest();
            Assert.AreNotSame(first, second); Assert.True(second.Started);
            first.Stop(); Assert.False(first.SetVolume(0));
            Assert.AreEqual(PlaybackState.Playing, playback.State);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await waiting).EndReason);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await first.WaitForCompletionAsync()).EndReason);
            Assert.False(second.IsCompleted); playback.Stop();
        }
        [Test] public void ReuseResetsPauseFadeLoopAndPositionState()
        {
            using var playback = engine.CreatePlayback("reusable");
            playback.StartRunForTest(new PlayOptions { Loop = false, FadeInSeconds = 10, Volume = .2f });
            playback.Pause(); playback.Stop();
            var next = playback.StartRunForTest(); Assert.True(next.Started);
            Assert.AreEqual(PlaybackState.Playing, playback.State); Assert.True(playback.Loop);
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            Assert.True(Array.Exists(sources, source => source.clip == clip && source.isPlaying && Mathf.Approximately(source.volume, 1)));
        }
        [Test] public void IndependentPlayersCanPlayTheSameSound()
        {
            using var first = engine.CreatePlayback("reusable"); using var second = engine.CreatePlayback("reusable");
            first.Play(); second.Play(); first.Stop();
            Assert.AreEqual(PlaybackState.Playing, second.State);
        }
        [Test] public void ExternalSourceCanBeReplayedAndRestored()
        {
            var go = new GameObject("reusable external");
            try
            {
                var source = go.AddComponent<AudioSource>(); source.playOnAwake = false;
                source.clip = clip; source.loop = true; source.volume = .4f;
                using var registration = engine.RegisterExternalSource(source);
                using var playback = engine.CreatePlayback(registration);
                var first = playback.StartRunForTest(); Assert.True(first.Started); playback.Stop();
                var second = playback.StartRunForTest(); Assert.True(second.Started);
                first.Stop(); Assert.False(second.IsCompleted);
                playback.Dispose(); Assert.True(second.IsCompleted);
                registration.Dispose(); Assert.AreSame(clip, source.clip); Assert.AreEqual(.4f, source.volume);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task PlayRestartsActiveRun(bool paused, bool fading)
        {
            using var playback = engine.CreatePlayback("reusable");
            Assert.Throws<InvalidOperationException>(() => playback.WaitForCompletionAsync());
            var first = playback.StartRunForTest();
            var waiting = playback.WaitForCompletionAsync();
            if (paused) playback.Pause();
            if (fading) playback.Stop(1f);
            var second = playback.StartRunForTest(new PlayOptions { Volume = .4f });
            Assert.AreNotSame(first, second);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await waiting).EndReason);
            Assert.True(second.Started);
            Assert.AreEqual(PlaybackState.Playing, playback.State);
            Assert.AreEqual(.4f, playback.Playback.Source.volume, .001f);
            first.Stop();
            engine.Tick(2);
            Assert.False(second.IsCompleted);
        }
        [Test] public async Task CanceledWaitDoesNotStopRunOrPreventReuse()
        {
            using var playback = engine.CreatePlayback("reusable"); playback.StartRunForTest();
            using var cancel = new CancellationTokenSource();
            var waiting = playback.WaitForCompletionAsync(cancel.Token); cancel.Cancel();
            try { await waiting; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.AreEqual(PlaybackState.Playing, playback.State);
            playback.Stop(); Assert.True(playback.StartRunForTest().Started);
        }
        [Test] public async Task BatchStopAndEngineDisposalCompleteTheCurrentRun()
        {
            using var playback = engine.CreatePlayback("reusable"); playback.StartRunForTest();
            var stopping = engine.StopAllAsync(1); engine.Tick(2); await stopping;
            playback.StartRunForTest(); var waiting = playback.WaitForCompletionAsync(); engine.Dispose();
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await waiting).EndReason);
            Assert.Throws<ObjectDisposedException>(() => playback.StartRunForTest());
        }
        private sealed class PumpContext : SynchronizationContext
        {
            private readonly Queue<(SendOrPostCallback, object)> queue = new();
            public override void Post(SendOrPostCallback callback, object state) => queue.Enqueue((callback, state));
            internal void Drain() { while (queue.Count > 0) { var item = queue.Dequeue(); item.Item1(item.Item2); } }
        }
        [Test] public void QueuedOldNotificationsDoNotDescribeANewRun()
        {
            var previous = SynchronizationContext.Current; var pump = new PumpContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(pump);
                using var playback = engine.CreatePlayback("reusable");
                var states = new List<PlaybackState>(); playback.StateChanged += (_, state) => states.Add(state);
                playback.StartRunForTest(); playback.Stop(); playback.StartRunForTest(); pump.Drain();
                CollectionAssert.AreEqual(new[] { PlaybackState.Loading, PlaybackState.Playing }, states);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        [Test] public void DisposeStopsItsRunAndDiscardsQueuedNotifications()
        {
            var previous = SynchronizationContext.Current; var pump = new PumpContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(pump);
                var playback = engine.CreatePlayback("reusable"); var notifications = 0;
                playback.StateChanged += (_, _) => notifications++;
                var handle = playback.StartRunForTest(); playback.Dispose(); playback.Dispose(); pump.Drain();
                Assert.True(handle.IsCompleted); Assert.AreEqual(0, notifications);
                Assert.Throws<ObjectDisposedException>(() => playback.StartRunForTest());
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        [Test] public async Task DirectReferenceCanBeReplayed()
        {
            using var playback = engine.CreatePlayback(new DirectAudioReference(clip));
            for (var i = 0; i < 3; i++)
            {
                var handle = playback.StartRunForTest(new PlayOptions { Loop = true }); Assert.True(handle.Started);
                Assert.AreEqual(PlaybackEndReason.Stopped, (await playback.StopAsync()).EndReason);
            }
        }
        [Test] public async Task FailedRunCanBeCorrectedAndReplayed()
        {
            var definition = new ClipDefinition { Clip = clip, Pitch = 0 };
            using var playback = engine.CreatePlayback(new DirectAudioReference(definition));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("[Pp]itch"));
            var failed = playback.StartRunForTest();
            Assert.True(failed.IsCompleted); Assert.False(failed.Started);
            Assert.IsInstanceOf<ArgumentOutOfRangeException>((await failed.WaitForCompletionAsync()).Error);
            definition.Pitch = 1;
            Assert.True(playback.StartRunForTest().Started); Assert.AreEqual(PlaybackState.Playing, playback.State);
        }
        private sealed class ImmediateContext : SynchronizationContext
        { public override void Post(SendOrPostCallback callback, object state) => callback(state); }
        [Test] public void EndNotificationCanStartTheNextRun()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                using var playback = engine.CreatePlayback("reusable");
                var replayed = false;
                playback.StateChanged += (_, state) => {
                    if (state == PlaybackState.Ended && !replayed) { replayed = true; playback.StartRunForTest(); }
                };
                var first = playback.StartRunForTest(); playback.Stop();
                Assert.True(replayed); Assert.True(first.IsCompleted);
                Assert.AreNotSame(first, playback.Request); Assert.AreEqual(PlaybackState.Playing, playback.State);
                first.Stop(); Assert.AreEqual(PlaybackState.Playing, playback.State);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        [Test] public async Task PlayAsyncCancellationStopsItsRunOnTheMainThread()
        {
            using var playback = engine.CreatePlayback("reusable");
            using var cancel = new CancellationTokenSource();
            var playing = playback.PlayAsync(new PlayOptions { Volume = .3f }, cancel.Token);
            var request = playback.Request;
            Assert.True(request.Started);
            Assert.AreEqual(.3f, playback.Playback.Source.volume, .001f);
            await Task.Run(() => cancel.Cancel());
            try { await playing; Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException error) { Assert.AreEqual(cancel.Token, error.CancellationToken); }
            Assert.True(playing.IsCanceled);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await request.WaitForCompletionAsync()).EndReason);
        }

        [Test] public async Task PlayAsyncAlreadyCanceledDoesNotReplaceCurrentRun()
        {
            using var playback = engine.CreatePlayback("reusable");
            playback.Play(); var first = playback.Request;
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            var playing = playback.PlayAsync(cancel.Token);
            try { await playing; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.AreSame(first, playback.Request);
            Assert.False(first.IsCompleted);
        }

        [Test] public async Task OldPlayAsyncCancellationDoesNotStopNewRun()
        {
            using var playback = engine.CreatePlayback("reusable");
            using var cancel = new CancellationTokenSource();
            var first = playback.PlayAsync(cancel.Token);
            cancel.Cancel();
            playback.Play(); var second = playback.Request;
            try { await first; } catch (OperationCanceledException) { }
            Assert.False(second.IsCompleted);
            Assert.AreEqual(PlaybackState.Playing, playback.State);
        }

        [Test] public async Task PlayAsyncWithoutTokenReturnsTheCompletionResult()
        {
            using var playback = engine.CreatePlayback("reusable");
            var playing = playback.PlayAsync();
            playback.Stop();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await playing).EndReason);
        }

        [Test] public async Task PlayAsyncCancellationDuringLoadingCannotStartPlaybackLater()
        {
            var reference = new PendingReference();
            pendingLibrary = ScriptableObject.CreateInstance<AudioLibrary>();
            var definition = new AudioDefinition { Key = "pending", Loop = true };
            definition.Clips.Add(new ClipDefinition { Clip = clip }); pendingLibrary.Entries.Add(definition);
            using var playback = engine.CreatePlayback(reference);
            using var cancel = new CancellationTokenSource();
            var playing = playback.PlayAsync(cancel.Token); var first = playback.Request;
            cancel.Cancel();
            try { await playing; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert.False(first.Started);
            Assert.True(first.IsCompleted);
            reference.Deliver(pendingLibrary);
            await engine.PreloadAsync((IAudioLibraryReference)reference);
            Assert.False(first.Started);
            Assert.AreEqual(1, reference.Active, "Cancellation must retain the shared library cache.");
            playback.Play();
            Assert.True(playback.Started);
        }

        private sealed class PendingReference : IAudioReference, IAudioLibraryReference, IAudioLibraryLoader
        {
            public IAudioLibraryReference LibraryReference => this;
            public string Key;
            string IAudioReference.Key => Key;
            internal readonly TaskCompletionSource<IAudioLibraryLease> Pending = new();
            internal int Active;
            public PendingReference() { Key = "pending"; }
            public string LibraryIdentity => "audio-playback-pending";
            public IAudioLibraryLoader CreateLibraryLoader() => this;
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken token = default) => Pending.Task;
            public void Dispose() { }
            private sealed class Lease : IAudioLibraryLease
            {
                private PendingReference owner;
                public IReadOnlyList<AudioLibrary> Libraries { get; }
                internal Lease(PendingReference owner, AudioLibrary library) { this.owner = owner; Libraries = new[] { library }; owner.Active++; }
                public void Dispose() { if (owner == null) return; owner.Active--; owner = null; }
            }
            internal void Deliver(AudioLibrary library) => Pending.SetResult(new Lease(this, library));
        }
        [Test] public async Task LateLoadCannotReviveThePreviousRun()
        {
            var reference = new PendingReference(); var sound = new AudioDefinition { Key = "pending", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            pendingLibrary = ScriptableObject.CreateInstance<AudioLibrary>(); pendingLibrary.Entries.Add(sound);
            using var playback = engine.CreatePlayback(reference);
            var first = playback.StartRunForTest(); Assert.AreEqual(PlaybackState.Loading, playback.State);
            var second = playback.StartRunForTest(); reference.Deliver(pendingLibrary);
            await second.ObserveStartAsync(); Assert.True(second.Started); Assert.False(first.Started);
            Assert.AreEqual(PlaybackState.Playing, playback.State); playback.Stop();
            Assert.AreEqual(1, reference.Active, "Playback completion must retain the engine cache.");
            engine.ReleaseLibrary((IAudioLibraryReference)reference);
            Assert.AreEqual(0, reference.Active);
        }
    }
}
