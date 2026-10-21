using System;

namespace AudioOrbit
{
    [Serializable]
    public sealed class ResourcesAudioReference : IAudioReference
    {
        public ResourcesLibraryReference Library = new();
        public string Key;
        string IAudioReference.Key => Key;
        public IAudioLibraryReference LibraryReference => Library ?? throw new InvalidOperationException("Assign a Library reference.");
    }
}
