using System;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.Audio;
#endif

namespace AudioOrbit
{
    [Serializable]
    public sealed class ClipDefinition
    {
        [SerializeField] private UnityEngine.Object resource;
#if UNITY_6000_3_OR_NEWER
        public IAudioGenerator.Serializable Generator
        {
            get => new IAudioGenerator.Serializable(Resource as IAudioGenerator);
            set => resource = value.definition as UnityEngine.Object;
        }
#endif
        internal UnityEngine.Object Resource { get => resource; set => resource = value; }
        internal bool HasSupportedResource =>
#if UNITY_6000_3_OR_NEWER
            Resource is IAudioGenerator;
#else
            Resource is AudioClip;
#endif
        /// <summary>The underlying AudioClip, including the source of a loop region.</summary>
        public AudioClip Clip
        {
            get
            {
#if UNITY_6000_3_OR_NEWER
                if (Resource is LoopRegionClip region) return region.Clip;
#endif
                return Resource as AudioClip;
            }
            set => Resource = value;
        }
        [Range(0, 1)] public float Volume = 1;
        public float Pitch = 1;
        [Range(0, 1)] public float VolumeRandomRange;
        [Min(0)] public float PitchRandomRange;
        [Min(0)] public float Weight = 1;
    }
}


