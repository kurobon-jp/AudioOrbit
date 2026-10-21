using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class DirectAudioReferenceTests
    {
        private AudioEngine engine;
        private AudioEngineSettings settings;
        private AudioClip clip;

        [SetUp]
        public void Setup()
        {
            settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
            settings.InitialPoolSize = 0;
            settings.MaxPoolSize = 2;
            settings.MaxPlaybacks = 2;
            clip = AudioClip.Create("Direct test", 44100, 1, 44100, false);
            engine = new AudioEngine(settings);
        }

        [TearDown]
        public void Cleanup()
        {
            engine.Dispose();
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(settings);
        }

        private int Count(string name) => ((ICollection)typeof(AudioEngine)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine)).Count;

        [Test]
        public void SameReferencePlaysIndependentlyWithoutRegisteringALibrary()
        {
            var reference = new DirectAudioReference(clip);
            var options = PlayOptions.TwoD();
            options.Loop = true;
            var first = engine.CreatePlayback(reference).StartForTest(options);
            var second = engine.CreatePlayback(reference).StartForTest(options);
            Assert.True(first.Started);
            Assert.True(second.Started);
            first.Stop();
            Assert.AreEqual(PlaybackState.Playing, second.State);
            Assert.AreEqual(0, Count("definitions"));
            Assert.AreEqual(0, Count("librarySessions"));
            Assert.AreEqual(0, Count("referencedLibraries"));
            Assert.AreEqual(0, Count("cooldown"));
            Assert.AreEqual(0, Count("ownerCooldown"));
            second.Stop();
        }

        [Test]
        public void DirectPlaybackHonorsGlobalLimits()
        {
            settings.MaxPlaybacks = 1;
            var first = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest();
            var second = engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest();
            Assert.True(first.Started);
            Assert.AreEqual(PlayRejectionReason.PlaybackLimit, second.RejectionReason);
        }

        [Test]
        public async Task ClipSettingsAreSnapshottedAndStopCanBeAwaited()
        {
            var definition = new ClipDefinition { Clip = clip, Volume = .3f, Pitch = 1.5f, Weight = 0 };
            var reference = new DirectAudioReference(definition);
            var first = engine.CreatePlayback(reference).StartForTest();
            Assert.True(first.Started);
            definition.Volume = .7f;
            definition.Pitch = .8f;
            var second = engine.CreatePlayback(reference).StartForTest();
            Assert.True(second.Started);
            var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            Assert.True(System.Array.Exists(sources, source => source.clip == clip && Mathf.Approximately(source.volume, .3f) && Mathf.Approximately(source.pitch, 1.5f)));
            Assert.True(System.Array.Exists(sources, source => source.clip == clip && Mathf.Approximately(source.volume, .7f) && Mathf.Approximately(source.pitch, .8f)));
            await first.StopAsync();
            Assert.True(first.IsCompleted);
            Assert.AreEqual(PlaybackState.Playing, second.State);
        }
    }
}
