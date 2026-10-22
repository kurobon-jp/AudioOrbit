#if UNITY_6000_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
namespace AudioOrbit.Tests
{
    public sealed class RegionPlaybackTests
    {
        private readonly List<UnityEngine.Object> objects = new();
        private AudioEngine engine;
        private AudioLibrary library;
        private AudioClip clip;
        private LoopRegionClip region;
        private string id = new("region");
        [SetUp] public void Setup()
        {
            var listener = new GameObject("Region test listener");
            listener.AddComponent<AudioListener>(); objects.Add(listener);
            var settings = ScriptableObject.CreateInstance<AudioEngineSettings>(); objects.Add(settings);
            settings.InitialPoolSize = 2; settings.MaxPoolSize = 2;
            clip = AudioClip.Create("region", 24000, 1, 48000, false); objects.Add(clip);
            var data = new float[clip.samples];
            for (int i = 0; i < data.Length; i++) data[i] = 0.1f;
            clip.SetData(data, 0);
            library = ScriptableObject.CreateInstance<AudioLibrary>(); objects.Add(library);
            region = ScriptableObject.CreateInstance<LoopRegionClip>(); objects.Add(region);
            region.Clip = clip; region.ReferenceSampleRate = clip.frequency; region.LoopStartFrame = 4800; region.LoopEndFrameExclusive = 14400;
            library.Entries.Add(new AudioDefinition { Key = id, Loop = true,
                Clips = new List<ClipDefinition> { new() { Generator = new IAudioGenerator.Serializable(region) } } });
            engine = new AudioEngine(settings); engine.Preload(library);
        }
        [TearDown] public void Cleanup()
        {
            AudioListener.pause = false;
            engine.Dispose();
            ControlContext.WaitForBuiltInQueueFlush();
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
        }
        private static IEnumerator Until(Func<bool> condition, float timeout = 3)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.True(condition(), "Region playback timed out");
        }
        private static int CachedPcmCount => ((System.Collections.IDictionary)typeof(AudioEngine).Assembly
            .GetType("AudioOrbit.LoopRegionPlayback").GetField("cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;
        private static int RuntimeCount => ((System.Collections.IDictionary)typeof(AudioEngine).Assembly
            .GetType("AudioOrbit.LoopRegionPlayback").GetField("instances", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;
        [UnityTest] public IEnumerator ReusablePlaybackCanStartANewRegionRun()
        {
            using var playback = engine.CreatePlayback(id);
            var first = playback.StartRunForTest(); Assert.True(first.Started);
            playback.Loop = false;
            yield return Until(() => first.IsCompleted);
            var second = playback.StartRunForTest(); Assert.True(second.Started); Assert.True(playback.Loop);
            first.Stop(); Assert.False(second.IsCompleted);
            playback.Stop();
            yield return Until(() => CachedPcmCount == 0);
        }
        [Test] public void RuntimePlaybackDoesNotCreateAScriptableObject()
        {
            var runtimeType = typeof(AudioEngine).Assembly.GetType("AudioOrbit.LoopRegionPlayback");
            Assert.False(typeof(UnityEngine.Object).IsAssignableFrom(runtimeType));
            Assert.IsNull(typeof(AudioEngine).Assembly.GetType("AudioOrbit.RegionAudioGenerator"));
        }
        [UnityTest] public IEnumerator AudioClipGeneratorUsesOrdinaryPlayback()
        {
            engine.Dispose();
            library.Entries[0].Clips[0].Generator = new IAudioGenerator.Serializable(clip);
            var settings = ScriptableObject.CreateInstance<AudioEngineSettings>(); objects.Add(settings);
            engine = new AudioEngine(settings); engine.Preload(library);
            var playback = engine.CreatePlayback(id).StartForTest(new PlayOptions { Loop = false });
            Assert.True(playback.Started, playback.ObserveStartAsync().Result.Error?.ToString());
            Assert.AreEqual(0, CachedPcmCount);
            Assert.True(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Any(source => source.clip == clip));
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
        }

        [UnityTest] public IEnumerator DefinitionCanBeUsedDirectlyByMultipleAudioSources()
        {
            var first = new GameObject("Direct region first"); objects.Add(first);
            var second = new GameObject("Direct region second"); objects.Add(second);
            var a = first.AddComponent<AudioSource>(); var b = second.AddComponent<AudioSource>();
            a.playOnAwake = false; b.playOnAwake = false;
            a.generator = region; b.generator = region;
            a.Play(); b.Play();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(1, CachedPcmCount);
            a.Stop();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(1, CachedPcmCount);
            b.Stop();
            yield return Until(() => CachedPcmCount == 0);
        }
        [UnityTest] public IEnumerator DirectReferenceLoopsThenFinishesTheOutro()
        {
            region.ReferenceSampleRate = clip.frequency * 2;
            region.LoopStartFrame *= 2;
            region.LoopEndFrameExclusive *= 2;
            var reference = new DirectAudioReference(new ClipDefinition
            {
                Generator = new IAudioGenerator.Serializable(region)
            });
            var playback = engine.CreatePlayback(reference).StartForTest(new PlayOptions { Loop = true });
            yield return Until(() => playback.Started || playback.IsCompleted);
            Assert.True(playback.Started);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.False(playback.IsCompleted);
            playback.Loop = false;
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            ControlContext.WaitForBuiltInQueueFlush();
            yield return Until(() => RuntimeCount == 0);
        }
        [UnityTest] public IEnumerator OneSourceLoopsThenFinishesTheOutro()
        {
            var playback = engine.CreatePlayback(id).StartForTest();
            yield return Until(() => playback.Started || playback.IsCompleted);
            Assert.True(playback.Started, playback.ObserveStartAsync().Result.Error?.ToString());
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.generator != null).ToArray();
            Assert.AreEqual(1, sources.Length); Assert.False(sources[0].loop);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.False(playback.IsCompleted); Assert.True(playback.Loop);
            playback.Loop = false;
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            ControlContext.WaitForBuiltInQueueFlush(); yield return Until(() => CachedPcmCount == 0);
        }
        [UnityTest] public IEnumerator LoopOffFromStartPlaysAllRegionsOnce()
        {
            var playback = engine.CreatePlayback(id).StartForTest(new PlayOptions { Loop = false });
            Assert.True(playback.Started, playback.ObserveStartAsync().Result.Error?.ToString());
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
        }
        [UnityTest] public IEnumerator TwoPlaybacksSharePcmButHaveIndependentLoopState()
        {
            var first = engine.CreatePlayback(id).StartForTest(); var second = engine.CreatePlayback(id).StartForTest();
            Assert.True(first.Started && second.Started, first.ObserveStartAsync().Result.Error?.ToString()); Assert.AreEqual(1, CachedPcmCount);
            Assert.AreEqual(2, RuntimeCount);
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(s => ReferenceEquals(s.generator, region)).ToArray();
            Assert.AreEqual(2, sources.Length);
            Assert.AreNotEqual(sources[0].generatorInstance, sources[1].generatorInstance);
            first.Loop = false;
            yield return Until(() => first.IsCompleted);
            Assert.False(second.IsCompleted); Assert.True(second.Loop);
            yield return Until(() => second.StopAsync().IsCompleted);
            ControlContext.WaitForBuiltInQueueFlush(); yield return Until(() => CachedPcmCount == 0);
            Assert.AreEqual(0, RuntimeCount);
        }
        [UnityTest] public IEnumerator SeekingOnePlaybackDoesNotMoveOrFinishTheOther()
        {
            var first = engine.CreatePlayback(id).StartForTest(); var second = engine.CreatePlayback(id).StartForTest();
            Assert.True(first.Started && second.Started);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var playback = typeof(PlaybackHandle).GetProperty("Playback", flags).GetValue(first);
            var runtime = playback.GetType().GetField("Generator", flags).GetValue(playback);
            Assert.True((bool)runtime.GetType().GetMethod("Seek", flags).Invoke(runtime, new object[] { 19200 }));
            yield return Until(() => first.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, first.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            Assert.False(second.IsCompleted); Assert.True(second.Loop);
            yield return Until(() => second.StopAsync().IsCompleted);
            yield return Until(() => CachedPcmCount == 0 && RuntimeCount == 0);
        }
        [UnityTest] public IEnumerator FadeStopAndPoolReuseDoNotLeaveAGeneratorAttached()
        {
            var playback = engine.CreatePlayback(id).StartForTest();
            playback.Stop(0.05f);
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Stopped, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                Assert.IsNull(source.generator);
            ControlContext.WaitForBuiltInQueueFlush(); yield return Until(() => CachedPcmCount == 0);
        }
        [UnityTest] public IEnumerator StereoResamplingAndPositivePitchFinishNormally()
        {
            engine.Dispose();
            var stereo = AudioClip.Create("stereo region", 22050, 2, 44100, false); objects.Add(stereo);
            var data = new float[44100];
            for (int i = 0; i < data.Length; i++) data[i] = i % 2 == 0 ? 0.1f : 0.2f;
            stereo.SetData(data, 0);
            region.Clip = stereo; region.ReferenceSampleRate = stereo.frequency;
            region.LoopStartFrame = 4410;
            region.LoopEndFrameExclusive = 13230;
            var settings = ScriptableObject.CreateInstance<AudioEngineSettings>(); objects.Add(settings);
            engine = new AudioEngine(settings); engine.Preload(library);
            var playback = engine.CreatePlayback(id).StartForTest(new PlayOptions { Loop = false, Pitch = 2 });
            Assert.True(playback.Started, playback.ObserveStartAsync().Result.Error?.ToString());
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            yield return Until(() => CachedPcmCount == 0);
        }
        [UnityTest] public IEnumerator PausingRegionPlaybackDefersCompletionUntilResume()
        {
            var playback = engine.CreatePlayback(id).StartForTest(new PlayOptions { Loop = false });
            playback.Pause();
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.False(playback.IsCompleted);
            playback.Resume();
            yield return Until(() => playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            yield return Until(() => CachedPcmCount == 0);
        }
        [UnityTest] public IEnumerator EngineDisposalReleasesGeneratorRuntimeAndPcm()
        {
            var playback = engine.CreatePlayback(id).StartForTest();
            yield return new WaitForSecondsRealtime(0.1f);
            engine.Dispose();
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, playback.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            yield return Until(() => CachedPcmCount == 0);
        }
        [Test] public void InvalidRegionFailsBeforePlaybackAndDoesNotPinPcm()
        {
            engine.Dispose(); region.LoopEndFrameExclusive = clip.samples + 1;
            var settings = ScriptableObject.CreateInstance<AudioEngineSettings>(); objects.Add(settings);
            engine = new AudioEngine(settings); engine.Preload(library);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Loop region must satisfy"));
            var playback = engine.CreatePlayback(id).StartForTest();
            Assert.True(playback.IsCompleted); Assert.False(playback.Started);
            Assert.IsInstanceOf<ArgumentException>(playback.WaitForCompletionAsync().GetAwaiter().GetResult().Error);
            Assert.AreEqual(0, CachedPcmCount);
        }
        [Test] public void CursorCrossesLoopBoundaryInsideTheSameBufferAndThenExits()
        {
            var type = typeof(AudioEngine).Assembly.GetType("AudioOrbit.RegionPlaybackCursor");
            var cursor = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { 9, 2, 5, true }, null);
            var read = type.GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic);
            var loop = type.GetField("Looping", BindingFlags.Instance | BindingFlags.NonPublic);
            var sequence = new List<int>();
            Action<int> render = capacity =>
            {
                while (capacity > 0)
                {
                    var arguments = new object[] { capacity, 0 };
                    var count = (int)read.Invoke(cursor, arguments);
                    if (count == 0) break;
                    sequence.AddRange(Enumerable.Range((int)arguments[1], count));
                    capacity -= count;
                }
            };
            render(8);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 2, 3, 4 }, sequence);
            loop.SetValue(cursor, false); render(20);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 2, 3, 4, 2, 3, 4, 5, 6, 7, 8 }, sequence);
            var eof = new object[] { 512, 0 }; Assert.AreEqual(0, read.Invoke(cursor, eof));
        }
    }
}

#endif
