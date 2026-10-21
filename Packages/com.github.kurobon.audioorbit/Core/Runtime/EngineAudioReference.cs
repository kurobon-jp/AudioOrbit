using System;
using UnityEngine;

namespace AudioOrbit
{
    /// <summary>References an Audio key already registered with the playback engine.</summary>
    [Serializable]
    public sealed class EngineAudioReference : IAudioReference
    {
        [Tooltip("Key registered with the selected Engine. The Library must be registered before playback.")]
        public string Key;
        string IAudioReference.Key => Key;
        public IAudioLibraryReference LibraryReference => null;

        public EngineAudioReference() { }
        public EngineAudioReference(string key) => Key = key;
    }
}
