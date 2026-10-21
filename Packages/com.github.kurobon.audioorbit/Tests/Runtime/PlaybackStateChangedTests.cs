using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioOrbit.Tests
{
    public sealed class PlaybackStateChangedTests
    {
        private AudioEngine engine;
        private AudioEngineSettings settings;
        private AudioClip clip;
        private AudioLibrary library;
        private sealed class Reference : IAudioReference, IAudioLibraryReference, IAudioLibraryLoader
        {
            public IAudioLibraryReference LibraryReference => this;
            public string Key;
            string IAudioReference.Key => Key;
            public string LibraryIdentity => "state_test";
            public Reference() { Key = "state_test"; }
            public readonly TaskCompletionSource<IAudioLibraryLease> Pending = new();
            public IAudioLibraryLoader CreateLibraryLoader() => this;
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default) => Pending.Task;
            public void Dispose() { }
        }
        private sealed class Lease : IAudioLibraryLease
        {
            public IReadOnlyList<AudioLibrary> Libraries { get; }
            public Lease(AudioLibrary library) { Libraries = new[] { library }; }
            public void Dispose() { }
        }
        [SetUp] public void Setup()
        {
            settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
            settings.InitialPoolSize = 0;
            clip = AudioClip.Create("state test", 44100, 1, 44100, false);
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            var sound = new AudioDefinition { Key = "state_test", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound);
            engine = new AudioEngine(settings);
        }
        [TearDown] public void Cleanup()
        {
            engine.Dispose();
            UnityEngine.Object.DestroyImmediate(library);
            UnityEngine.Object.DestroyImmediate(clip);
            UnityEngine.Object.DestroyImmediate(settings);
        }
        [Test] public async Task TransitionsAreOrderedDistinctAndOnTheMainThread()
        {
            var handle = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
            var thread = Thread.CurrentThread.ManagedThreadId;
            var states = new List<PlaybackState>();
            handle.StateChanged += (sender, state) =>
            {
                Assert.AreSame(handle, sender);
                Assert.AreEqual(thread, Thread.CurrentThread.ManagedThreadId);
                if (state == PlaybackState.Ended)
                {
                    Assert.True(sender.IsCompleted);
                    Assert.AreEqual(PlaybackEndReason.Stopped, sender.WaitForCompletionAsync().Result.EndReason);
                }
                states.Add(state);
            };
            handle.Pause(); handle.Pause(); handle.Resume(); handle.Resume();
            handle.Stop(.1f); handle.Stop(.2f);
            engine.Tick(.2f);
            await Task.Delay(100);
            CollectionAssert.AreEqual(new[] { PlaybackState.Paused, PlaybackState.Playing, PlaybackState.FadingOut, PlaybackState.Ended }, states);
        }
        [Test] public async Task AsynchronousLoadReportsPlayingAndNaturalCompletion()
        {
            var reference = new Reference();
            var handle = engine.CreatePlayback(reference).StartForTest();
            var states = new List<PlaybackState>();
            handle.StateChanged += (_, state) => states.Add(state);
            reference.Pending.SetResult(new Lease(library));
            await Task.Delay(100);
            Assert.True(handle.Started);
            // Exercise the same completion path used when the audio source reaches EOF.
            handle.Loop = false;
            handle.Playback.Source.Stop();
            engine.Tick(0, true);
            await Task.Delay(100);
            CollectionAssert.AreEqual(new[] { PlaybackState.Playing, PlaybackState.Ended }, states);
            Assert.AreEqual(PlaybackEndReason.Completed, handle.WaitForCompletionAsync().Result.EndReason);
        }
        [Test] public async Task StoppedLoadingRequestDoesNotNotifyPlayingAfterLateLoad()
        {
            var reference = new Reference();
            var handle = engine.CreatePlayback(reference).StartForTest();
            var states = new List<PlaybackState>();
            handle.StateChanged += (_, state) => states.Add(state);
            handle.Stop();
            reference.Pending.SetResult(new Lease(library));
            await Task.Delay(100);
            CollectionAssert.AreEqual(new[] { PlaybackState.Ended }, states);
            Assert.False(handle.Started);
        }
        [Test] public async Task FailureIsAvailableDuringEndedNotification()
        {
            var reference = new Reference();
            var handle = engine.CreatePlayback(reference).StartForTest();
            PlaybackResult? result = null;
            handle.StateChanged += (sender, state) => { if (state == PlaybackState.Ended) result = sender.WaitForCompletionAsync().Result; };
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: state load failed"));
            reference.Pending.SetException(new InvalidOperationException("state load failed"));
            await Task.Delay(100);
            Assert.True(result.HasValue);
            Assert.AreEqual(PlayRejectionReason.Failed, result.Value.RejectionReason);
            Assert.AreEqual("state load failed", result.Value.Error.Message);
        }
        [Test] public async Task SubscriptionChangesPreserveQueuedListenerSnapshots()
        {
            var handle = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
            var first = new List<PlaybackState>();
            var second = new List<PlaybackState>();
            Action<PlaybackHandle, PlaybackState> firstListener = (_, state) => first.Add(state);
            Action<PlaybackHandle, PlaybackState> secondListener = (_, state) => second.Add(state);
            handle.StateChanged += firstListener;
            handle.Pause();
            handle.StateChanged -= firstListener;
            handle.StateChanged += secondListener;
            handle.Resume(); handle.Stop();
            await Task.Delay(100);
            CollectionAssert.AreEqual(new[] { PlaybackState.Paused }, first);
            CollectionAssert.AreEqual(new[] { PlaybackState.Playing, PlaybackState.Ended }, second);
        }

        [Test] public async Task QueuedBurstPreservesEachHandleAndTransition()
        {
            var states = new Dictionary<PlaybackHandle, List<PlaybackState>>();
            for (var i = 0; i < 128; i++)
            {
                var handle = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
                states.Add(handle, new List<PlaybackState>());
                handle.StateChanged += (sender, state) => states[sender].Add(state);
                handle.Pause(); handle.Resume(); handle.Stop();
            }
            await Task.Delay(150);
            foreach (var pair in states)
                CollectionAssert.AreEqual(new[] { PlaybackState.Paused, PlaybackState.Playing, PlaybackState.Ended }, pair.Value);
        }

        [Test] public async Task SubscriberExceptionsDoNotInterruptOtherSubscribers()
        {
            var handle = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
            var notified = false;
            handle.StateChanged += (_, state) => { if (state == PlaybackState.Paused) throw new InvalidOperationException("state callback failed"); };
            handle.StateChanged += (_, state) => { if (state == PlaybackState.Paused) notified = true; };
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: state callback failed"));
            handle.Pause();
            await Task.Delay(100);
            Assert.True(notified);
        }

        [Test] public async Task CallbackCanStopAllWithoutInterruptingOtherNotifications()
        {
            var first = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
            var second = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });
            var ended = 0;
            first.StateChanged += (_, state) => { if (state == PlaybackState.Paused) engine.StopAll(); if (state == PlaybackState.Ended) ended++; };
            second.StateChanged += (_, state) => { if (state == PlaybackState.Ended) ended++; };
            first.Pause();
            await Task.Delay(150);
            Assert.True(first.IsCompleted && second.IsCompleted);
            Assert.AreEqual(2, ended);
        }
    }
}
