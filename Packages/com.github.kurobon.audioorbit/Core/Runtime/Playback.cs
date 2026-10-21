using System;
using Object = UnityEngine.Object;
using UnityEngine;
using UnityEngine.Audio;
namespace AudioOrbit
{
    internal sealed class Playback
    {
        internal string Key;
        internal object LimitKey;
        internal int ClipIndex;
        internal LibraryHandle Library;
        internal AudioSource Source;
        internal IDisposable Lease;
        internal ExternalSourceHandle External;
        internal PlaybackRequest Handle;
#if UNITY_6000_3_OR_NEWER
        internal LoopRegionPlayback Generator;
#endif
        internal bool Finished
        {
            get
            {
#if UNITY_6000_3_OR_NEWER
                if (Generator != null) return Generator.Finished;
#endif
                return !Source.loop && !Source.isPlaying;
            }
        }
        internal bool Loop
        {
            get
            {
#if UNITY_6000_3_OR_NEWER
                if (Generator != null) return Generator.Loop;
#endif
                return Source.loop;
            }
            set
            {
#if UNITY_6000_3_OR_NEWER
                if (Generator != null) { Generator.SetLoop(value); return; }
#endif
                Source.loop = value;
            }
        }
        internal AudioCategoryId Category;
        internal Object Owner;
        internal bool HasOwner;
        internal Transform Target;
        internal bool Following;
        internal Vector3 Offset;
        internal TargetLostBehavior TargetLost;
        internal OwnerLostBehavior OwnerLost;
        internal float LossFadeOutSeconds;
        internal bool Paused, Stopping, Ended;
        internal float Volume, BaseVolume, Fade = 1, FadeStart = 1, FadeElapsed, FadeDuration, FadeIn;
        internal int Priority;
        internal double Started;
        internal int StartFrame;
        internal PlaybackEndReason StopReason;
    }
    internal sealed class SourceSnapshot
    {
        private readonly AudioClip clip;
        internal AudioClip Clip => clip;
        internal float Volume => volume;
        internal float Pitch => pitch;
        internal bool Loop => loop;
        private readonly float volume, pitch, blend, min, max, spread, doppler;
        private readonly bool loop, ignore, awake;
        private readonly int priority;
        private readonly AudioMixerGroup mixer;
        private readonly AudioRolloffMode rolloff;
        private readonly AnimationCurve curve;
        internal SourceSnapshot(AudioSource s)
        {
            clip=s.clip; volume=s.volume; pitch=s.pitch; blend=s.spatialBlend; min=s.minDistance; max=s.maxDistance;
            spread=s.spread; doppler=s.dopplerLevel; loop=s.loop; ignore=s.ignoreListenerPause;
            awake=s.playOnAwake; priority=s.priority; mixer=s.outputAudioMixerGroup; rolloff=s.rolloffMode;
            curve=s.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        }
        internal void Restore(AudioSource s)
        {
            if(s==null) return;
            s.Stop(); s.clip=clip; s.volume=volume; s.pitch=pitch; s.spatialBlend=blend;
            s.minDistance=min; s.maxDistance=max; s.spread=spread; s.dopplerLevel=doppler; s.loop=loop;
            s.ignoreListenerPause=ignore; s.playOnAwake=awake; s.priority=priority; s.outputAudioMixerGroup=mixer;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff,curve); s.rolloffMode=rolloff;
        }
    }
}



