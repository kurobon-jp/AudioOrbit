#if UNITY_6000_3_OR_NEWER
using System;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class LoopRegionSampleRateTests
    {
        private LoopRegionClip region;
        private AudioClip clip;
        [SetUp] public void Setup()
        {
            clip = AudioClip.Create("24kHz", 48000, 1, 24000, false);
            region = ScriptableObject.CreateInstance<LoopRegionClip>();
            region.Clip = clip;
            region.ReferenceSampleRate = 48000;
            region.LoopStartFrame = 24000;
            region.LoopEndFrameExclusive = 96000;
        }
        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(region);
            UnityEngine.Object.DestroyImmediate(clip);
        }
        [Test] public void ScalesBothBoundariesAndPreservesAuthoredValues()
        {
            region.GetRuntimeLoopFrames(out var start, out var end);
            Assert.AreEqual(12000, start);
            Assert.AreEqual(clip.samples, end);
            Assert.AreEqual(24000, region.LoopStartFrame);
            Assert.AreEqual(96000, region.LoopEndFrameExclusive);
        }
        [Test] public void SameRatePreservesFramesExactly()
        {
            region.ReferenceSampleRate = 24000;
            region.LoopEndFrameExclusive = 48000;
            region.GetRuntimeLoopFrames(out var start, out var end);
            Assert.AreEqual(24000, start);
            Assert.AreEqual(48000, end);
        }
        [TestCase(0)] [TestCase(-1)] public void UnsetOrInvalidReferenceRateIsRejected(int rate)
        {
            region.ReferenceSampleRate = rate;
            Assert.Throws<ArgumentException>(() => region.GetRuntimeLoopFrames(out _, out _));
        }
        [Test] public void RejectsCollapsedAndOutOfBoundsRegions()
        {
            region.LoopStartFrame = 1; region.LoopEndFrameExclusive = 2;
            Assert.Throws<ArgumentException>(() => region.GetRuntimeLoopFrames(out _, out _));
            region.LoopStartFrame = 0; region.LoopEndFrameExclusive = 96002;
            Assert.Throws<ArgumentException>(() => region.GetRuntimeLoopFrames(out _, out _));
        }
    }
}
#endif
