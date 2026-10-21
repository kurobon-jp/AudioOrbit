using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.Audio;
#endif

namespace AudioOrbit.Tests.Editor
{
    public sealed class ClipResourceSerializationTests
    {
        private string folder;
        private AudioClip clip;
        [SetUp] public void Setup()
        {
            folder = "Assets/AudioOrbitClipSerialization_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var paths = AssetDatabase.FindAssets("t:AudioClip");
            Assert.IsNotEmpty(paths);
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(paths[0]));
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(folder);

        [Test] public void AudioClipUsesAVersionIndependentSerializedReference()
        {
            var library = CreateLibrary();
            var yaml = File.ReadAllText(AssetDatabase.GetAssetPath(library));
            StringAssert.Contains("resource: {fileID:", yaml);
            var copy = JsonUtility.FromJson<ClipDefinition>(JsonUtility.ToJson(library.Entries[0].Clips[0]));
            Assert.AreSame(clip, copy.Clip);
            Assert.True(copy.HasSupportedResource);
        }
#if UNITY_6000_3_OR_NEWER
        [Test] public void RegionReferenceSurvivesSerializationAndClipAssignmentClearsIt()
        {
            var region = ScriptableObject.CreateInstance<LoopRegionClip>();
            region.Clip = clip;
            AssetDatabase.CreateAsset(region, folder + "/Region.asset");
            var definition = new ClipDefinition { Generator = new IAudioGenerator.Serializable(region) };
            var copy = JsonUtility.FromJson<ClipDefinition>(JsonUtility.ToJson(definition));
            Assert.AreSame(region, copy.Generator.definition);
            Assert.AreSame(clip, copy.Clip);
            copy.Clip = clip;
            Assert.AreSame(clip, copy.Generator.definition);
        }
#endif
        private AudioLibrary CreateLibrary()
        {
            var library = ScriptableObject.CreateInstance<AudioLibrary>();
            library.Entries.Add(new AudioDefinition
            {
                Key = Guid.NewGuid().ToString("N"),
                Clips = new System.Collections.Generic.List<ClipDefinition> { new ClipDefinition { Clip = clip } }
            });
            AssetDatabase.CreateAsset(library, folder + "/Library.asset");
            AssetDatabase.SaveAssetIfDirty(library);
            return library;
        }
    }
}

