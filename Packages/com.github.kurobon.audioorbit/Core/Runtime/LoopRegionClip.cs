#if UNITY_6000_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Audio;
using Unity.IntegerTime;
using static UnityEngine.Audio.ProcessorInstance;

namespace AudioOrbit
{
    /// <summary>A reusable clip and its intro, loop and outro boundaries. Playback state is created per instance.</summary>
    [CreateAssetMenu(menuName = "AudioOrbit/Loop Region Clip")]
    public sealed class LoopRegionClip : ScriptableObject, IAudioGenerator
    {
        public AudioClip Clip;
        [Min(1), Tooltip("Sample rate used when authoring the loop frame positions. Keep this value when import settings change.")]
        public int ReferenceSampleRate;
        [Min(0)] public int LoopStartFrame;
        [Min(1), Tooltip("First frame after the loop region (exclusive).")]
        public int LoopEndFrameExclusive;
        public bool isFinite => true;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        public void GetRuntimeLoopFrames(out int start, out int endExclusive)
        {
            if (Clip == null) throw new ArgumentException("Loop region Clip is not assigned.");
            if (ReferenceSampleRate <= 0)
                throw new ArgumentException("Set the loop region Reference Sample Rate to the rate used to author its frame positions.");
            if (LoopStartFrame < 0 || LoopEndFrameExclusive <= LoopStartFrame)
                throw new ArgumentException("Loop region must satisfy 0 <= start < end.");
            var ratio = (double)Clip.frequency / ReferenceSampleRate;
            var startFrame = Math.Round(LoopStartFrame * ratio, MidpointRounding.AwayFromZero);
            var endFrame = Math.Round(LoopEndFrameExclusive * ratio, MidpointRounding.AwayFromZero);
            if (startFrame > int.MaxValue || endFrame > int.MaxValue)
                throw new ArgumentException("Converted loop region exceeds the supported frame range.");
            start = (int)startFrame;
            endExclusive = (int)endFrame;
            LoopRegionPlayback.Validate(Clip, start, endExclusive);
        }

        public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedConfiguration,
            CreationParameters creationParameters)
        {
            GetRuntimeLoopFrames(out var start, out var end);
            var runtime = LoopRegionPlayback.Create(Clip, start, end, true);
            try { return runtime.Allocate(context, nestedConfiguration, creationParameters); }
            catch { runtime.Dispose(); throw; }
        }
    }
}

#endif
