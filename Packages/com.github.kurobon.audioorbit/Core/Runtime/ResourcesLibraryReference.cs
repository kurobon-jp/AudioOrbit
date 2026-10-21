using System;

namespace AudioOrbit
{
    [Serializable]
    public sealed class ResourcesLibraryReference : IAudioLibraryReference
    {
        public string LibraryPath;
        public string LibraryIdentity => string.IsNullOrWhiteSpace(LibraryPath)
            ? string.Empty : "resources:" + LibraryPath.ToLowerInvariant();
        public IAudioLibraryLoader CreateLibraryLoader() => ResourcesAudioLibraryLoader.FromPath(LibraryPath);
    }
}
