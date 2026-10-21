using System;
using UnityEngine;

namespace AudioOrbit
{
    [Serializable]
    public sealed class DirectAudioReference : IAudioReference
    {
        public ClipDefinition Clip = new();
        public bool Loop;
        string IAudioReference.Key => null;
        public IAudioLibraryReference LibraryReference => null;
        public DirectAudioReference() { }
        public DirectAudioReference(AudioClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            Clip = new ClipDefinition { Clip = clip };
        }
        public DirectAudioReference(ClipDefinition clip)
        {
            Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        }
    }
}
