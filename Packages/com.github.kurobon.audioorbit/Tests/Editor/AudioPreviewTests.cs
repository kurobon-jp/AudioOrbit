using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.Audio;
#endif
using UnityEngine.TestTools;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioPreviewTests
    {
        private AudioPreview preview;
        private AudioClip clip;
#if UNITY_6000_3_OR_NEWER
        private LoopRegionClip region;
#endif
        private AudioDefinition sound;
        private int sceneCount;
        private PlaybackHandle Handle => (PlaybackHandle)typeof(AudioPreview)
            .GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
        private AudioSource Source
        {
            get
            {
                var playback = typeof(PlaybackHandle).GetProperty("Playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Handle);
                return (AudioSource)playback.GetType().GetField("Source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(playback);
            }
        }
        [SetUp] public void Setup()
        {
            sceneCount = EditorSceneManager.previewSceneCount;
            clip = AudioClip.Create("Preview test", 24000, 1, 48000, false);
            sound = new AudioDefinition { Key = "preview/test", Volume = 0.5f, Loop = true, Clips = new List<ClipDefinition>
            {
                new() { Clip = clip, Volume = 0.2f },
                new() { Clip = clip, Volume = 0.6f, Pitch = 1.2f }
            } };
            preview = new AudioPreview();
        }
        [TearDown] public void Cleanup()
        {
            preview.Dispose();
#if UNITY_6000_3_OR_NEWER
            ControlContext.WaitForBuiltInQueueFlush();
            if (region != null) Object.DestroyImmediate(region);
#endif
            Object.DestroyImmediate(clip);
            Assert.AreEqual(sceneCount, EditorSceneManager.previewSceneCount);
        }
        [Test] public void SequenceAndExplicitPreviewDistinguishClipsUsingTheSameClip()
        {
            preview.Play(sound); Assert.AreEqual(0, preview.ClipIndex); Assert.AreEqual(0.1f, Source.volume, 0.0001f);
            preview.Play(sound, 0); Assert.AreEqual(0, preview.ClipIndex);
            preview.Play(sound); Assert.AreEqual(1, preview.ClipIndex); Assert.AreEqual(0.3f, Source.volume, 0.0001f);
            Assert.AreEqual(1.2f, Source.pitch, 0.0001f); Assert.True(Source.loop);
            preview.Play(sound); Assert.AreEqual(0, preview.ClipIndex);
        }
        [Test] public void PauseResumeAndStopControlTheCurrentPreview()
        {
            preview.Play(sound); preview.Pause(); Assert.AreEqual(PlaybackState.Paused, preview.State);
            preview.Resume(); Assert.AreEqual(PlaybackState.Playing, preview.State);
            preview.Stop(); Assert.AreEqual(PlaybackState.Ended, preview.State);
        }
#if UNITY_6000_3_OR_NEWER
        [UnityTest] public IEnumerator RegionPreviewRendersTheOutroInEditMode()
        {
            region = ScriptableObject.CreateInstance<LoopRegionClip>();
            region.Clip = clip; region.ReferenceSampleRate = clip.frequency; region.LoopStartFrame = 2400; region.LoopEndFrameExclusive = 7200;
            sound.Clips[0].Generator = new IAudioGenerator.Serializable(region);
            preview.Play(sound, 0); Assert.True(Handle.Started, preview.Error);
            Handle.Loop = false;
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (!Handle.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.True(Handle.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, Handle.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            Assert.AreEqual(0, preview.ClipIndex);
        }
#endif
        [Test] public void OrdinaryClipCanSeekWhilePausedWithoutChangingTheClip()
        {
            preview.Play(sound, 1); preview.Pause();
            Assert.True(preview.Seek(0.75f));
            Assert.AreEqual(0.375f, preview.PositionSeconds, 0.001f);
            Assert.AreEqual(1, preview.ClipIndex); Assert.AreEqual(PlaybackState.Paused, preview.State);
            Assert.True(preview.Seek(0)); Assert.AreEqual(0, preview.PositionSeconds);
            preview.Resume(); Assert.AreEqual(PlaybackState.Playing, preview.State);
            Assert.True(sound.Loop);
        }
#if UNITY_6000_3_OR_NEWER
        [UnityTest] public IEnumerator RegionSeekWhilePausedResumesAtTheOutroAndCompletes()
        {
            region = ScriptableObject.CreateInstance<LoopRegionClip>();
            region.Clip = clip; region.ReferenceSampleRate = clip.frequency; region.LoopStartFrame = 4800; region.LoopEndFrameExclusive = 14400;
            sound.Clips[0].Generator = new IAudioGenerator.Serializable(region);
            preview.Play(sound, 0); preview.Pause();
            Assert.True(preview.Seek(0.5f)); Assert.AreEqual(0.25f, preview.PositionSeconds, 0.001f);
            Assert.True(preview.Seek(0.8f)); Assert.AreEqual(0.4f, preview.PositionSeconds, 0.001f);
            Assert.AreEqual(PlaybackState.Paused, preview.State);
            Handle.Loop = false; preview.Resume();
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (!Handle.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.True(Handle.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Completed, Handle.WaitForCompletionAsync().GetAwaiter().GetResult().EndReason);
            Assert.AreEqual(preview.Duration, preview.PositionSeconds, 0.001f);
        }
#endif
        [Test] public void LiveParametersPreservePlaybackAndPausedPosition()
        {
            preview.Play(sound, 0); preview.Pause(); preview.Seek(0.4f);
            var handle = Handle; var source = Source; var position = Source.timeSamples;
            sound.Volume = 0.8f; sound.Clips[0].Volume = 0.5f;
            sound.Clips[0].Pitch = 1.5f; sound.Loop = false;
            preview.ApplyChanges(sound);
            Assert.AreSame(handle, Handle); Assert.AreSame(source, Source);
            Assert.AreEqual(position, Source.timeSamples);
            Assert.AreEqual(PlaybackState.Paused, preview.State);
            Assert.AreEqual(0.4f, Source.volume, 0.0001f);
            Assert.AreEqual(1.5f, Source.pitch); Assert.False(Handle.Loop);
            Assert.IsNull(preview.Error);
        }
        [Test] public void LiveCategoryAndRandomRangesUpdateWithoutRepeatedRerolls()
        {
            var category = ScriptableObject.CreateInstance<AudioCategory>();
            try
            {
                sound.Category = category;
                preview.Play(sound, 0); preview.Pause();
                category.Volume = 0.5f;
                sound.Clips[0].Volume = 1; sound.Clips[0].VolumeRandomRange = 0.1f;
                preview.ApplyChanges(sound);
                Assert.That(Source.volume, Is.InRange(0.225f, 0.25f));
                var volume = Source.volume;
                sound.DisplayName = "Renamed during playback";
                preview.ApplyChanges(sound);
                Assert.AreEqual(volume, Source.volume);
                category.Muted = true; preview.ApplyChanges(sound);
                Assert.AreEqual(0, Source.volume);
                category.Muted = false; preview.ApplyChanges(sound);
                Assert.AreEqual(volume, Source.volume);
            }
            finally { Object.DestroyImmediate(category); }
        }
        [Test] public void ReplacingClipPreservesPausedStateAndRelativePosition()
        {
            var replacement = AudioClip.Create("Replacement", 48000, 1, 48000, false);
            try
            {
                preview.Play(sound, 0); preview.Pause(); preview.Seek(0.4f);
                var previous = Handle;
                sound.Clips[0].Clip = replacement;
                preview.ApplyChanges(sound);
                Assert.AreNotSame(previous, Handle);
                Assert.AreSame(replacement, Source.clip);
                Assert.AreEqual(PlaybackState.Paused, preview.State);
                Assert.AreEqual(0.4f, preview.PositionSeconds, 0.001f);
                Assert.IsNull(preview.Error);
            }
            finally { Object.DestroyImmediate(replacement); }
        }
#if UNITY_6000_3_OR_NEWER
        [Test] public void LiveRegionLoopChangesAndRegionEditsRemainIndependent()
        {
            region = ScriptableObject.CreateInstance<LoopRegionClip>();
            region.Clip = clip; region.ReferenceSampleRate = clip.frequency; region.LoopStartFrame = 4800; region.LoopEndFrameExclusive = 14400;
            sound.Clips[0].Generator = new IAudioGenerator.Serializable(region);
            preview.Play(sound, 0); preview.Pause(); var handle = Handle;
            sound.Loop = false; preview.ApplyChanges(sound);
            Assert.AreSame(handle, Handle); Assert.False(Handle.Loop);
            region.LoopEndFrameExclusive = 19200; preview.ApplyChanges(sound);
            Assert.AreNotSame(handle, Handle); Assert.False(Handle.Loop);
            Assert.AreEqual(PlaybackState.Paused, preview.State);
            Assert.IsNull(preview.Error);
        }
#endif
        [Test] public void InvalidLivePitchCanBeCorrectedWithoutStoppingPreview()
        {
            preview.Play(sound, 0); preview.Pause(); var handle = Handle;
            sound.Clips[0].Pitch = 0; preview.ApplyChanges(sound);
            Assert.IsNotNull(preview.Error); Assert.AreSame(handle, Handle);
            sound.Clips[0].Pitch = 1.2f; preview.ApplyChanges(sound);
            Assert.IsNull(preview.Error); Assert.AreEqual(1.2f, Source.pitch);
        }
        [TestCase(1f, 0.1f, 0.9f, 1f)]
        [TestCase(0f, 0.1f, 0f, 0.1f)]
        [TestCase(0.8f, 0.1f, 0.7f, 0.9f)]
        [TestCase(0.8f, 0f, 0.8f, 0.8f)]
        public void RandomVolumeSamplesWithinNormalizedBounds(float volume, float range, float minimum, float maximum)
        {
            var randomState = Random.state;
            try
            {
                Random.InitState(1234);
                sound.Volume = 0.4f;
                sound.Clips[0].Volume = volume;
                sound.Clips[0].VolumeRandomRange = range;
                float total = 0;
                for (int i = 0; i < 128; i++)
                {
                    preview.Play(sound, 0);
                    var sampled = Source.volume / sound.Volume;
                    Assert.That(sampled, Is.InRange(minimum - 0.00001f, maximum + 0.00001f));
                    total += sampled;
                }
                Assert.AreEqual((minimum + maximum) / 2, total / 128, 0.01f);
            }
            finally { Random.state = randomState; }
        }
        [UnityTest] public IEnumerator SoundCooldownExpiresUsingEditorTime()
        {
            sound.Limits.CooldownSeconds = 0.05f;
            preview.Play(sound); Assert.True(Handle.Started);
            preview.Play(sound); Assert.AreEqual(PlayRejectionReason.Cooldown, Handle.RejectionReason);
            var deadline = EditorApplication.timeSinceStartup + 0.15;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
            preview.Play(sound); Assert.True(Handle.Started, preview.Error);
        }
    }
}
