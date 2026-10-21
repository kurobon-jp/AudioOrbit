#if ENABLE_ADDRESSABLES
using System;
using UnityEngine.AddressableAssets;

namespace AudioOrbit.Addressables
{
    [Serializable]
    public sealed class AddressablesLibraryReference : IAudioLibraryReference
    {
        public AssetReferenceT<AudioLibrary> Asset = new(string.Empty);
        public string LibraryIdentity => GetLibraryIdentity(Asset);
        public IAudioLibraryLoader CreateLibraryLoader()
        {
            if (Asset == null || !Asset.RuntimeKeyIsValid())
                throw new InvalidOperationException("Select an Addressable AudioLibrary.");
            return AddressablesAudioLibraryLoader.FromKey(Asset.RuntimeKey.ToString());
        }
        public static string GetLibraryIdentity(AssetReferenceT<AudioLibrary> library) =>
            library != null && library.RuntimeKeyIsValid() ? "addressables:" + library.RuntimeKey : string.Empty;
    }
}
#endif
