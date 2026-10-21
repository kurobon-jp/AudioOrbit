using System.Collections;
using System.Reflection;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class WaveformEnvelopeTests
    {
        [Test] public void ShortPeaksAndStereoChannelsSurviveDownsampling()
        {
            var envelope = new WaveformEnvelope(8, 2, 1);
            var pcm = new float[16]; pcm[4] = 1; pcm[5] = -0.8f; pcm[12] = -0.6f; pcm[13] = 0.9f;
            envelope.Add(pcm, 0, 8); envelope.Columns(2, out var min, out var max);
            CollectionAssert.AreEqual(new[] { 0f, -0.8f, -0.6f, 0f }, min);
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f, 0.9f }, max);
        }
        [Test] public void ChunkBoundariesAndPartialFinalBufferDoNotLoseOrInventPeaks()
        {
            var envelope = new WaveformEnvelope(7, 1, 4);
            envelope.Add(new[] { 0f, 0.1f, 0f }, 0, 3);
            envelope.Add(new[] { 0.8f, -0.7f, 0f, 0.2f, 1f }, 3, 4);
            envelope.Columns(1, out var min, out var max);
            Assert.AreEqual(-0.7f, min[0]); Assert.AreEqual(0.8f, max[0]);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(300)]
        public void ResizingPreservesThePeak(int width)
        {
            var envelope = new WaveformEnvelope(128, 1, 8);
            var pcm = new float[128]; pcm[63] = 0.9f; pcm[64] = -1;
            envelope.Add(pcm, 0, 128); envelope.Columns(width, out var min, out var max);
            Assert.AreEqual(width, min.Length); Assert.AreEqual(width, max.Length);
            Assert.Contains(-1f, min); Assert.Contains(0.9f, max);
            foreach (var value in min) Assert.False(float.IsInfinity(value) || float.IsNaN(value));
        }
        [Test] public void ImporterPeakPairsKeepChannelOrderAndPolarity()
        {
            var envelope = WaveformEnvelope.FromImporter(100, 2, new[] { 0.6f, -0.2f, 0.9f, -0.4f, 0.3f, -0.8f, 0.7f, -0.1f });
            envelope.Columns(2, out var min, out var max);
            CollectionAssert.AreEqual(new[] { -0.2f, -0.4f, -0.8f, -0.1f }, min);
            CollectionAssert.AreEqual(new[] { 0.6f, 0.9f, 0.3f, 0.7f }, max);
        }
        [UnityTest] public IEnumerator PlaybackPositionUpdatesWithoutSeekBarOrFeedback()
        {
            var waveform = new AudioWaveform();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.Show(); window.rootVisualElement.Add(waveform);
                yield return null;
                int requests = 0;
                waveform.SeekRequested += _ => requests++;
                waveform.SetPlaybackPosition(0.25f, 1, true);
                var slider = waveform.Q<UnityEngine.UIElements.Slider>("audioorbit-seek");
                Assert.IsNull(slider); Assert.AreEqual(0, requests);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                Assert.AreEqual(0.25f, typeof(AudioWaveform).GetField("playhead", flags).GetValue(waveform));
                waveform.SetPlaybackPosition(0.75f, 1, false);
                Assert.AreEqual(0.75f, typeof(AudioWaveform).GetField("playhead", flags).GetValue(waveform));
                Assert.AreEqual(0, requests);
            }
            finally { window.Close(); waveform.Dispose(); }
        }
        [UnityTest] public IEnumerator ClipPcmIsAnalyzedInChunksAndReused()
        {
            var clip = AudioClip.Create("Waveform chunk test", 33001, 2, 48000, false);
            var pcm = new float[66002]; pcm[66000] = 1; pcm[66001] = -0.5f; clip.SetData(pcm, 0);
            var waveform = new AudioWaveform();
            try
            {
                waveform.SetClip(clip);
                var type = typeof(AudioWaveform); var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var deadline = EditorApplication.timeSinceStartup + 3;
                while ((bool)type.GetField("analyzing", flags).GetValue(waveform) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.False((bool)type.GetField("analyzing", flags).GetValue(waveform));
                var data = (WaveformEnvelope)type.GetField("envelope", flags).GetValue(waveform);
                Assert.NotNull(data); data.Columns(1, out var min, out var max);
                CollectionAssert.AreEqual(new[] { 0f, -0.5f }, min); CollectionAssert.AreEqual(new[] { 1f, 0f }, max);
                waveform.SetClip(null); waveform.SetClip(clip);
                Assert.AreSame(data, type.GetField("envelope", flags).GetValue(waveform));
                Assert.False((bool)type.GetField("analyzing", flags).GetValue(waveform));
            }
            finally { waveform.Dispose(); Object.DestroyImmediate(clip); }
        }
    }
}
