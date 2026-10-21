using System;
using UnityEngine;

namespace AudioOrbit
{
    [Serializable]
    public sealed class EngineDefinition
    {
        public string Key = OrbitContext.DefaultKey;
        public AudioEngineSettings Settings;
        public AudioLibrary[] Libraries = Array.Empty<AudioLibrary>();
    }
}
