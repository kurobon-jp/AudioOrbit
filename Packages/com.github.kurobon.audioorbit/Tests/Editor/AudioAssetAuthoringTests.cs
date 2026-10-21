using System;
using System.Collections.Generic;
using System.Linq;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioAssetAuthoringTests
    {
        private string folder;
        [SetUp] public void Setup()
        {
            folder = "Assets/AudioOrbitAuthoringTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
        }
        [TearDown] public void Cleanup() { AssetDatabase.DeleteAsset(folder); }
        private T Create<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, folder + "/" + name + ".asset");
            return asset;
        }
#if UNITY_6000_3_OR_NEWER
        [Test] public void GeneratorPickerOnlyOffersGeneratorAssets()
        {
            var region = Create<LoopRegionClip>("Picker Region");
            Create<AudioLibrary>("Not a Generator");
            using (var context = AudioGeneratorPicker.CreateContext())
            {
                var results = UnityEditor.Search.SearchService.Request(context, UnityEditor.Search.SearchFlags.Synchronous);
                try
                {
                    var assets = results.Select(item => item.data as UnityEngine.Object).ToArray();
                    Assert.True(assets.Contains(region));
                    Assert.True(assets.Any(asset => asset is AudioClip));
                    Assert.True(assets.All(asset => asset is UnityEngine.Audio.IAudioGenerator));
                }
                finally { (results as IDisposable)?.Dispose(); }
            }
        }
#endif
        [Test] public void InlineClipReferenceSurvivesAssetRename()
        {
            var sourcePath = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:AudioClip")[0]);
            var path = folder + "/Tone" + System.IO.Path.GetExtension(sourcePath);
            Assert.True(AssetDatabase.CopyAsset(sourcePath, path));
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var library = Create<AudioLibrary>("Library");
            var sound = new AudioDefinition { Key = Guid.NewGuid().ToString("N") };
            library.Entries.Add(sound);
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            Assert.IsEmpty(AssetDatabase.RenameAsset(path, "Renamed"));
            Assert.AreSame(clip, sound.Clips[0].Clip);
            AssetDatabase.SaveAssets();
            Assert.AreEqual(1, AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(library)).Length);
        }
        [Test] public void NewDefinitionHasEditableDefaults()
        {
            Assert.AreEqual("new_audio", new AudioDefinition().Key);
            Assert.AreEqual("New Audio", new AudioDefinition().DisplayName);
        }
        [Test] public void AddedSoundsReceiveUniqueDefaultsWithoutChangingExistingEntries()
        {
            var library = Create<AudioLibrary>("Library");
            library.Entries.Add(new AudioDefinition { Key = "custom/existing", DisplayName = "Existing" });
            library.Entries.Add(new AudioDefinition { Key = "new_audio" });
            library.Entries.Add(new AudioDefinition { Key = default, DisplayName = null });
            library.Entries.Add(new AudioDefinition { Key = default, DisplayName = null });
            AudioLibraryEditor.InitializeAddedEntries(library, 2);
            Assert.AreEqual("new_audio_2", library.Entries[2].Key);
            Assert.AreEqual("new_audio_3", library.Entries[3].Key);
            Assert.AreEqual("New Audio", library.Entries[2].DisplayName);
            Assert.AreEqual("New Audio", library.Entries[3].DisplayName);
            Assert.AreEqual("custom/existing", library.Entries[0].Key);
            Assert.AreEqual("Existing", library.Entries[0].DisplayName);
        }
        [Test] public void DuplicatedLibraryPreservesUserDefinedIds()
        {
            var library = Create<AudioLibrary>("Library");
            library.Entries.Add(new AudioDefinition { Key = "se/charge" });
            AudioIdentityPostprocessor.EnsureIdentity(library);
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            var path = folder + "/Copy.asset";
            Assert.True(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(library), path));
            var copy = AssetDatabase.LoadAssetAtPath<AudioLibrary>(path);
            AudioIdentityPostprocessor.EnsureIdentity(copy);
            Assert.AreEqual("se/charge", copy.Entries[0].Key);
        }
        [Test] public void IdentityPostprocessorDoesNotReplaceEmptyOrDuplicateSoundIds()
        {
            var library = Create<AudioLibrary>("Library");
            library.Entries.Add(new AudioDefinition { Key = default });
            library.Entries.Add(new AudioDefinition { Key = "se/charge" });
            library.Entries.Add(new AudioDefinition { Key = "se/charge" });
            AudioIdentityPostprocessor.EnsureIdentity(library);
            Assert.True(string.IsNullOrWhiteSpace(library.Entries[0].Key));
            Assert.AreEqual(library.Entries[1].Key, library.Entries[2].Key);
        }

    }
}
