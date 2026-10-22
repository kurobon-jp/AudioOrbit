using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioOrbit.Tests
{
    public sealed class AudioEngineTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private AudioEngine engine;
        private AudioClip clip;
        private string id = "sound";
        private AudioCategory category;
        private AudioLibrary library;
        private AudioDefinition sound;
        private AudioEngineSettings settings;
        private Task<PlaybackTestStart> Start(string soundId, PlayOptions options = default) =>
            engine.CreatePlayback(soundId).StartForTest(options).ObserveStartAsync();

        private T Asset<T>() where T : ScriptableObject
        {
            var a = ScriptableObject.CreateInstance<T>();
            objects.Add(a);
            return a;
        }

        [SetUp]
        public void Setup()
        {
            settings = Asset<AudioEngineSettings>();
            settings.InitialPoolSize = 1;
            settings.MaxPoolSize = 2;
            settings.MaxPlaybacks = 2;
            category = Asset<AudioCategory>();
            category.Id = new AudioCategoryId("SE");
            clip = AudioClip.Create("test", 44100, 1, 44100, false); objects.Add(clip);
            sound = new AudioDefinition();
            sound.Key = id;
            sound.Category = category;
            sound.Loop = true;
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            var lib = Asset<AudioLibrary>();
            typeof(AudioLibrary).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(lib, new List<AudioDefinition> { sound });
            engine = new AudioEngine(settings);
            library = lib; engine.Preload(lib);
        }

        [TearDown]
        public void Cleanup()
        {
            AudioListener.pause = false;
            engine?.Dispose();
            foreach (var o in objects)
                if (o != null)
                    UnityEngine.Object.DestroyImmediate(o);
            objects.Clear();
        }

        [Test]
        public async Task ReferencedLibraryPlaysWithoutLoaderAndSnapshotsClips()
        {
            engine.Dispose();
            engine = new AudioEngine(settings);
            var lib = Asset<AudioLibrary>();
            sound.Clips.Clear();
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            lib.Entries.Add(sound);
            library = lib; engine.Preload(lib);
            sound.Clips[0].Clip = null;
            var synchronous = engine.CreatePlayback(id).StartForTest();
            Assert.True(synchronous.Started);
            synchronous.Stop();
            {
                var asynchronous = await Start(id);
                Assert.True(asynchronous.Started);
                engine.ReleaseLibrary(library);
                Assert.AreEqual(PlaybackState.Playing, asynchronous.Handle.State);
                asynchronous.Handle.Stop();
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + System.Text.RegularExpressions.Regex.Escape(id)));
                Assert.AreEqual(PlayRejectionReason.Failed, engine.CreatePlayback(id).StartForTest().RejectionReason);
            }

        }

        private sealed class FixedLibraryLoader : IAudioLibraryLoader
        {
            private sealed class Lease : IAudioLibraryLease
            {
                public IReadOnlyList<AudioLibrary> Libraries { get; private set; }

                internal Lease(AudioLibrary library)
                {
                    Libraries = new[] { library };
                }

                public void Dispose()
                {
                    Libraries = null;
                }
            }

            private readonly AudioLibrary library;

            internal FixedLibraryLoader(AudioLibrary library)
            {
                this.library = library;
            }

            public Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IAudioLibraryLease>(new Lease(library));

            public void Dispose()
            {
            }
        }

        [Test]
        public async Task LibraryPreloadAndPlaybackHaveIndependentLifetimes()
        {
            engine.ReleaseLibrary(library);
            var lib = Asset<AudioLibrary>();
            lib.Entries.Add(sound);
            var libraryLoader = new FixedLibraryLoader(lib);
            await engine.PreloadAsync(libraryLoader);
            var playback = engine.CreatePlayback(id).StartForTest();
            Assert.True(playback.Started);
            engine.ReleaseLibrary(libraryLoader);
            playback.Stop();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + System.Text.RegularExpressions.Regex.Escape(id)));
            Assert.AreEqual(PlayRejectionReason.Failed, engine.CreatePlayback(id).StartForTest().RejectionReason);
        }

        [Test]
        public async Task PlaybackResultSurvivesCompletion()
        {
            var handle = engine.CreatePlayback(id).StartForTest();
            handle.Stop();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await handle.WaitForCompletionAsync()).EndReason);
            Assert.False(handle.SetVolume(0));
            handle.Stop(); Assert.True(handle.IsCompleted);
        }

        [Test]
        public async Task CancelOneWaitDoesNotStopOtherWaitOrPlayback()
        {
            var handle = (await Start(id)).Handle;
            var cts = new CancellationTokenSource();
            var a = handle.WaitForCompletionAsync(cts.Token);
            var b = handle.WaitForCompletionAsync();
            cts.Cancel();
            try
            {
                await a;
                Assert.Fail("Expected cancellation");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.False(b.IsCompleted);
            Assert.AreEqual(PlaybackState.Playing, handle.State);
            handle.Stop();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await b).EndReason);
            cts.Dispose();
        }

        [Test]
        public async Task PlayReturnsHandleAndStopAsyncWaitsForCompletion()
        {
            var playback = engine.CreatePlayback(id).StartForTest();
            Assert.True((await playback.ObserveStartAsync()).Started);
            playback.Stop(); Assert.True(playback.IsCompleted);
            var result = await playback.WaitForCompletionAsync();
            Assert.True(result.Started);
            Assert.AreEqual(PlaybackEndReason.Stopped, result.EndReason);
            Assert.AreEqual(result.EndReason, (await playback.StopAsync()).EndReason);
        }

        [Test] public async Task CancelingStopAsyncWaitDoesNotCancelTheFade()
        {
            var playback = engine.CreatePlayback(id).StartForTest();
            using var cancellation = new CancellationTokenSource();
            var stopping = playback.StopAsync(0.5f, cancellation.Token);
            cancellation.Cancel();
            try { await stopping; Assert.Fail("Expected wait cancellation."); }
            catch (OperationCanceledException) { }
            Assert.AreEqual(PlaybackState.FadingOut, playback.State);
            engine.Tick(0.6f);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await playback.WaitForCompletionAsync()).EndReason);
        }





        [Test]
        public async Task StopAfterStartStopsItsOwnPlayback()
        {
            var other = engine.CreatePlayback(id).StartForTest();
            var playback = engine.CreatePlayback(id).StartForTest();
            Assert.True(playback.Started);
            await playback.StopAsync();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await playback.WaitForCompletionAsync()).EndReason);
            Assert.AreEqual(PlaybackState.Playing, other.State);
            await other.StopAsync();
        }



        [UnityTest]
        public IEnumerator FadeStopCompletesWhileExplicitlyAndListenerPaused()
        {
            var play = Start(id);
            yield return Await(play);
            var h = play.Result.Handle;
            h.Pause();
            AudioListener.pause = true;
            var stop = h.StopAsync(0.04f);
            Assert.False(stop.IsCompleted);
            yield return Await(stop);
            Assert.AreEqual(PlaybackEndReason.Stopped, stop.Result.EndReason);
        }

        [UnityTest]
        public IEnumerator CancelStopWaitLeavesFadeRunning()
        {
            var play = Start(id);
            yield return Await(play);
            var cts = new CancellationTokenSource();
            var stop = engine.StopAllAsync(0.05f, cts.Token);
            cts.Cancel();
            yield return AwaitCanceled(stop);
            var completion = play.Result.Handle.WaitForCompletionAsync();
            yield return Await(completion);
            Assert.AreEqual(PlaybackEndReason.Stopped, completion.Result.EndReason);
            cts.Dispose();
        }



        [Test]
        public async Task ReleaseDoesNotCutExistingPlayback()
        {
            var handle = (await Start(id)).Handle;
            engine.ReleaseLibrary(library);
            Assert.AreEqual(PlaybackState.Playing, handle.State);
            handle.Stop();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Unknown Audio key: " + System.Text.RegularExpressions.Regex.Escape(id)));
            Assert.AreEqual(PlayRejectionReason.Failed, engine.CreatePlayback(id).StartForTest().RejectionReason);
        }

        [Test]
        public async Task VoiceRejectionDoesNotStopExistingPlayback()
        {
            var a = (await Start(id)).Handle;
            var b = (await Start(id)).Handle;
            var rejected = await Start(id);
            Assert.AreEqual(PlayRejectionReason.PlaybackLimit, rejected.RejectionReason);
            Assert.AreEqual(PlaybackState.Playing, a.State);
            Assert.AreEqual(PlaybackState.Playing, b.State);
            var active = new List<PlaybackInfo>(); engine.GetPlaybackInfo(active); Assert.AreEqual(2, active.Count);
        }

        [Test]
        public async Task DisposeCompletesHandlesAndReleasesClip()
        {
            var h = (await Start(id)).Handle;
            engine.Dispose();
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await h.WaitForCompletionAsync()).EndReason);
        }

        private ExternalSourceHandle ExternalForKeyTest(string key, PlaybackLimitSettings limits)
        {
            var go = new GameObject("external key test"); objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.clip = clip; source.loop = true;
            return engine.RegisterExternalSource(source, new ExternalSourceOptions
                { Key = key, Category = category, Owner = category, Limits = limits });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("shared/external")]
        public async Task ExternalKeysSharePlaybackLimitsOnlyWhenSpecified(string key)
        {
            var limits = new PlaybackLimitSettings { MaxPlaybacks = 1, MaxPlaybacksPerOwner = 1 };
            using var first = ExternalForKeyTest(key, limits);
            using var second = ExternalForKeyTest(key, limits);
            var a = engine.CreatePlayback(first).StartForTest();
            var b = engine.CreatePlayback(second).StartForTest();
            Assert.True(a.Started);
            Assert.AreEqual(string.IsNullOrWhiteSpace(key), b.Started);
            if (!b.Started) Assert.AreEqual(PlayRejectionReason.PlaybackLimit, b.RejectionReason);
            await a.StopAsync(); await b.StopAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalKeysShareCooldownOnlyWhenSpecified(bool shared)
        {
            var key = shared ? "shared/cooldown" : null;
            var limits = new PlaybackLimitSettings { CooldownSeconds = 60, OwnerCooldownSeconds = 60 };
            using var first = ExternalForKeyTest(key, limits);
            using var second = ExternalForKeyTest(key, limits);
            var a = engine.CreatePlayback(first).StartForTest(); Assert.True(a.Started); await a.StopAsync();
            var repeated = engine.CreatePlayback(first).StartForTest();
            Assert.AreEqual(PlayRejectionReason.Cooldown, repeated.RejectionReason);
            var b = engine.CreatePlayback(second).StartForTest();
            Assert.AreEqual(!shared, b.Started);
            if (!b.Started) Assert.AreEqual(PlayRejectionReason.Cooldown, b.RejectionReason);
            await b.StopAsync();
        }

        [Test]
        public async Task ExternalRestartCannotBeControlledByOldHandleAndRestoresSource()
        {
            var go = new GameObject("external");
            objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.volume = 0.4f;
            source.pitch = 1.25f;
            source.loop = true;
            var registration = engine.RegisterExternalSource(source, new ExternalSourceOptions
            {
                Key = "ext",
                Category = category, RepeatBehavior = ExternalRepeatBehavior.Restart
            });
            var old = engine.CreatePlayback(registration).StartForTest();
            Assert.True(old.Started);
            Assert.AreEqual(0.4f, source.volume, 0.001f);
            Assert.AreEqual(1.25f, source.pitch, 0.001f);
            Assert.True(source.loop);
            var next = engine.CreatePlayback(registration).StartForTest();
            Assert.True(next.Started, next.ObserveStartAsync().Result.Error?.ToString());
            Assert.AreEqual(PlaybackEndReason.Stopped, (await old.WaitForCompletionAsync()).EndReason);
            old.Stop(); Assert.True(old.IsCompleted);
            Assert.False(next.SetPosition(Vector3.one));
            registration.Dispose();
            Assert.AreEqual(0.4f, source.volume, 0.001f);
            Assert.AreEqual(clip, source.clip);
        }

        [UnityTest]
        public IEnumerator BatchStopExcludesLaterPlayback()
        {
            var a = Start(id);
            yield return Await(a);
            var stop = engine.StopAllAsync(0.04f);
            var b = Start(id);
            yield return Await(b);
            yield return Await(stop);
            Assert.AreEqual(PlaybackState.Playing, b.Result.Handle.State);
        }

        [UnityTest]
        public IEnumerator NaturalCompletionAndListenerPauseStayDistinct()
        {
            var options = new PlayOptions { Loop = false };
            var p = Start(id, options);
            yield return Await(p);
            AudioListener.pause = true;
            var completion = p.Result.Handle.WaitForCompletionAsync();
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.False(completion.IsCompleted);
            p.Result.Handle.Pause();
            AudioListener.pause = false;
            yield return null;
            Assert.False(completion.IsCompleted);
            p.Result.Handle.Resume();
            yield return Await(completion);
            Assert.AreEqual(PlaybackEndReason.Completed, completion.Result.EndReason);
        }

        [UnityTest]
        public IEnumerator ListenerExemptCategoryCompletesDuringPause()
        {
            engine.SetCategoryIgnoreListenerPause(category.Id, true);
            AudioListener.pause = true;
            var p = Start(id, new PlayOptions { Loop = false, Pitch = 3 });
            yield return Await(p);
            var completion = p.Result.Handle.WaitForCompletionAsync();
            yield return Await(completion);
            Assert.AreEqual(PlaybackEndReason.Completed, completion.Result.EndReason);
        }

        [UnityTest]
        public IEnumerator AttachedOffsetTracksTransformAndLostTargetStops()
        {
            var go = new GameObject("target");
            objects.Add(go);
            go.transform.position = Vector3.one;
            go.transform.rotation = Quaternion.Euler(0, 90, 0);
            go.transform.localScale = Vector3.one * 2;
            var o = PlayOptions.Attached(go.transform, Vector3.forward);
            o.TargetLostBehavior = TargetLostBehavior.Stop;
            var p = Start(id, o);
            yield return Await(p);
            go.transform.position = Vector3.one * 3;
            yield return null;
            yield return null;
            var info = new List<PlaybackInfo>();
            engine.GetPlaybackInfo(info);
            Assert.Less(Vector3.Distance(go.transform.TransformPoint(Vector3.forward), info[0].Position), 0.001f);
            UnityEngine.Object.Destroy(go);
            yield return null;
            yield return null;
            var completion = p.Result.Handle.WaitForCompletionAsync();
            yield return Await(completion);
            Assert.AreEqual(PlaybackEndReason.TargetLost, completion.Result.EndReason);
        }

        [UnityTest]
        public IEnumerator StoppingAwaitedPlaybackStopsOnlyItsPlayback()
        {
            var other = Start(id);
            yield return Await(other);
            var request = engine.CreatePlayback(id).StartForTest();
            var wait = request.WaitForCompletionAsync();
            yield return null;
            request.Stop();
            yield return Await(wait);
            Assert.AreEqual(PlaybackEndReason.Stopped, wait.Result.EndReason);
            Assert.AreEqual(PlaybackState.Playing, other.Result.Handle.State);
            var info = new List<PlaybackInfo>();
            engine.GetPlaybackInfo(info);
            Assert.AreEqual(1, info.Count);
        }



        [UnityTest]
        public IEnumerator SceneUnloadDoesNotStopPlaybackWithoutLifetimeDependency()
        {
            var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("AudioOrbitTestScene");
            var owner = new GameObject("scene-owner");
            objects.Add(owner);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, scene);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            PlaybackHandle unowned;
            PlaybackHandle owned;
            try
            {
                unowned = engine.CreatePlayback(id);
                unowned.Play();
                owned = engine.CreatePlayback(id);
                owned.Play(new PlayOptions { Owner = owner, OwnerLostBehavior = OwnerLostBehavior.Continue });
            }
            finally
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            }
            yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            yield return null;
            Assert.AreEqual(PlaybackState.Playing, unowned.State);
            Assert.AreEqual(PlaybackState.Playing, owned.State);
            owned.Stop();
            Assert.AreEqual(PlaybackState.Playing, unowned.State);
            Assert.AreEqual(PlaybackEndReason.Stopped, awaitResult(owned).EndReason);
            unowned.Stop();
        }

        private static PlaybackResult awaitResult(PlaybackHandle handle)
        {
            var result = handle.WaitForCompletionAsync();
            Assert.True(result.IsCompleted);
            return result.Result;
        }

        [Test]
        public async Task ReusingPooledSourceDoesNotRetainSpatialSettingsOrOldHandle()
        {
            var a = (await Start(id, PlayOptions.At(Vector3.one))).Handle;
            a.Stop();
            var b = (await Start(id)).Handle;
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            AudioSource playing = null;
            foreach (var source in sources)
                if (source.clip == clip && source.isPlaying)
                    playing = source;
            Assert.NotNull(playing);
            Assert.AreEqual(0, playing.spatialBlend);
            Assert.AreEqual(Vector3.zero, playing.transform.position);
            a.Stop(); Assert.True(a.IsCompleted);
            Assert.AreEqual(PlaybackState.Playing, b.State);
        }

        [Test]
        public async Task RejectedCombinedLimitsDoNotCommitPlannedVictims()
        {
            engine.Dispose();
            settings.MaxPlaybacks = 2;
            settings.LimitBehavior = PlaybackLimitBehavior.StopOldest;
            category.MaxPlaybacks = 1;
            category.LimitBehavior = PlaybackLimitBehavior.RejectNew;
            engine = new AudioEngine(settings);
            var lib = Asset<AudioLibrary>();
            typeof(AudioLibrary).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(lib, new List<AudioDefinition> { sound });
            library = lib; engine.Preload(lib);
            var otherCategory = Asset<AudioCategory>();
            otherCategory.Id = new AudioCategoryId("other");
            var otherSound = new AudioDefinition();
            otherSound.Key = "other-sound";
            otherSound.Category = otherCategory;
            otherSound.Loop = true;
            otherSound.Clips.Add(new ClipDefinition { Clip = clip });
            var otherLib = Asset<AudioLibrary>();
            typeof(AudioLibrary).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(otherLib, new List<AudioDefinition> { otherSound });
            engine.Preload(otherLib);
            var oldest = (await Start(otherSound.Key)).Handle;
            var existing = (await Start(id)).Handle;
            var rejected = await Start(id);
            Assert.AreEqual(PlayRejectionReason.PlaybackLimit, rejected.RejectionReason);
            Assert.AreEqual(PlaybackState.Playing, oldest.State);
            Assert.AreEqual(PlaybackState.Playing, existing.State);
        }

        [Test] public void NewAudioDefinitionDefaultsToSequence()
        {
            Assert.AreEqual(ClipSelectionMode.Sequence, new AudioDefinition().SelectionMode);
        }

        [TestCase(ClipSelectionMode.Shuffle)]
        [TestCase(ClipSelectionMode.AvoidImmediateRepeat)]
        [TestCase(ClipSelectionMode.Sequence)]
        public async Task ClipSelectionHonorsConfiguredMode(ClipSelectionMode mode)
        {
            engine.Dispose();
            sound.SelectionMode = mode;
            sound.Clips.Clear();
            for (int i = 1; i <= 3; i++) sound.Clips.Add(new ClipDefinition { Clip = clip, Volume = i * 0.1f });
            var lib = Asset<AudioLibrary>();
            typeof(AudioLibrary).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(lib, new List<AudioDefinition> { sound });
            engine = new AudioEngine(settings);
            library = lib; engine.Preload(lib);
            var seen = new HashSet<int>();
            int previous = -1;
            for (int i = 0; i < 9; i++)
            {
                var h = (await Start(id)).Handle;
                var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                AudioSource playing = null;
                foreach (var s in sources)
                    if (s.clip == clip && s.isPlaying)
                        playing = s;
                Assert.NotNull(playing);
                int selected = Mathf.RoundToInt(playing.volume * 10);
                if (mode == ClipSelectionMode.AvoidImmediateRepeat) Assert.AreNotEqual(previous, selected);
                else if (mode == ClipSelectionMode.Sequence) Assert.AreEqual(i % 3 + 1, selected);
                else
                {
                    Assert.True(seen.Add(selected));
                    if (i % 3 == 2)
                    {
                        Assert.AreEqual(3, seen.Count);
                        seen.Clear();
                    }
                }

                previous = selected;
                h.Stop();
            }
        }

        [Test]
        public async Task SequenceSkipsZeroWeightsAndExplicitIndexDoesNotAdvanceIt()
        {
            engine.Dispose();
            sound.SelectionMode = ClipSelectionMode.Sequence;
            sound.Clips.Clear();
            sound.Clips.Add(new ClipDefinition { Clip = clip, Volume = 0.1f, Weight = 1 });
            sound.Clips.Add(new ClipDefinition { Clip = clip, Volume = 0.2f, Weight = 0 });
            sound.Clips.Add(new ClipDefinition { Clip = clip, Volume = 0.3f, Weight = 10 });
            var lib = Asset<AudioLibrary>(); lib.Entries.Add(sound);
            engine = new AudioEngine(settings); engine.Preload(lib);
            async Task Verify(float volume, PlayOptions options = default)
            {
                var playback = await Start(id, options);
                Assert.True(playback.Started);
                var source = Array.Find(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None),
                    s => s.clip == clip && s.isPlaying);
                Assert.NotNull(source); Assert.AreEqual(volume, source.volume, 0.0001f);
                await playback.Handle.StopAsync();
            }
            await Verify(0.1f);
            await Verify(0.2f, new PlayOptions { ClipIndex = 1 });
            await Verify(0.3f);
            await Verify(0.1f);
        }

        [Test] public void ShuffleCyclesExcludeZeroWeightClipsAndExplicitSelectionDoesNotConsumeACycle()
        {
            engine.ReleaseLibrary(library);
            sound.SelectionMode = ClipSelectionMode.Shuffle;
            sound.Clips.Clear();
            for (var i = 0; i < 4; i++) sound.Clips.Add(new ClipDefinition { Clip = clip, Weight = i == 1 ? 0 : 1 });
            var lib = Asset<AudioLibrary>(); lib.Entries.Add(sound); library = lib; engine.Preload(lib);
            using var playback = engine.CreatePlayback(id);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var seen = new HashSet<int>();
                for (var pick = 0; pick < 3; pick++)
                {
                    playback.Play();
                    Assert.True(playback.Started); Assert.AreNotEqual(1, playback.ClipIndex);
                    Assert.True(seen.Add(playback.ClipIndex), "Shuffle repeated within a cycle.");
                    playback.Stop();
                    playback.Play(new PlayOptions { ClipIndex = 1 });
                    Assert.True(playback.Started); Assert.AreEqual(1, playback.ClipIndex); playback.Stop();
                }
                CollectionAssert.AreEquivalent(new[] { 0, 2, 3 }, seen);
            }
        }

        [TestCase(ClipSelectionMode.Sequence)]
        [TestCase(ClipSelectionMode.Shuffle)]
        [TestCase(ClipSelectionMode.WeightedRandom)]
        [TestCase(ClipSelectionMode.AvoidImmediateRepeat)]
        public void SelectedClipIndexMatchesThePlayedSettingsForSharedAudioClips(ClipSelectionMode mode)
        {
            engine.ReleaseLibrary(library); sound.SelectionMode = mode; sound.Clips.Clear();
            for (var i = 0; i < 16; i++) sound.Clips.Add(new ClipDefinition { Clip = clip, Volume = (i + 1) / 20f });
            var lib = Asset<AudioLibrary>(); lib.Entries.Add(sound); library = lib; engine.Preload(lib);
            using var playback = engine.CreatePlayback(id);
            for (var i = 0; i < 40; i++)
            {
                playback.Play(); Assert.True(playback.Started);
                Assert.That(playback.ClipIndex, Is.InRange(0, 15));
                Assert.AreEqual(sound.Clips[playback.ClipIndex].Volume, playback.Playback.Source.volume, 0.0001f);
                playback.Stop();
            }
        }

        private static IEnumerator Await(Task task)
        {
            float end = Time.realtimeSinceStartup + 3;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.True(task.IsCompleted, "Task timed out");
            if (task.IsFaulted) throw task.Exception;
            Assert.False(task.IsCanceled);
        }

        private static IEnumerator AwaitCanceled(Task task)
        {
            float end = Time.realtimeSinceStartup + 3;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.True(task.IsCanceled, "Expected canceled Task");
        }
    }
}
