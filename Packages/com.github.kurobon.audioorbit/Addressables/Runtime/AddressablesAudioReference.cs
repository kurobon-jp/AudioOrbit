#if ENABLE_ADDRESSABLES
using System;

namespace AudioOrbit.Addressables
{
    [Serializable]
    public sealed class AddressablesAudioReference : IAudioReference
    {
        public AddressablesLibraryReference Library = new();
        public string Key;
        string IAudioReference.Key => Key;
        public IAudioLibraryReference LibraryReference => Library ?? throw new InvalidOperationException("Assign a Library reference.");
    }
}
#endif
