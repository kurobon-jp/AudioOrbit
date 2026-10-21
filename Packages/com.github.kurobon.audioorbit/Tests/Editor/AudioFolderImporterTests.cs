using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioFolderImporterTests
    {
        private string folder;
        private AudioLibrary library, other;
        private AudioClip first, nested;
        [SetUp] public void Setup()
        {
            var name = "AudioOrbitFolderTests_" + Guid.NewGuid().ToString("N");
            folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            AssetDatabase.CreateFolder(folder, "Audio");
            AssetDatabase.CreateFolder(folder + "/Audio", "zNested");
            first = AudioClip.Create("same", 100, 1, 44100, false);
            nested = AudioClip.Create("same", 100, 1, 44100, false);
            AssetDatabase.CreateAsset(first, folder + "/Audio/same.asset");
            AssetDatabase.CreateAsset(nested, folder + "/Audio/zNested/same.asset");
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            other = ScriptableObject.CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(library, folder + "/" + name + ".asset");
            AssetDatabase.CreateAsset(other, folder + "/Other.asset");
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(folder);

        [Test] public void FolderScanHonorsRecursionAndRejectsInvalidFolders()
        {
            CollectionAssert.AreEqual(new[] { first }, AudioFolderImporter.FindClips(folder + "/Audio", false));
            CollectionAssert.AreEqual(new[] { first, nested }, AudioFolderImporter.FindClips(folder + "/Audio", true));
            Assert.Throws<ArgumentException>(() => AudioFolderImporter.FindClips(folder + "/Missing", true));
        }

        [Test] public void BatchImportSkipsExistingClipsAndAssignsGloballyUniqueKeys()
        {
            var basis = "same";
            other.Entries.Add(new AudioDefinition { Key = basis });
            var category = ScriptableObject.CreateInstance<AudioCategory>();
            try
            {
                var result = AudioFolderImporter.Import(library, new[] { first, nested, first }, category);
                Assert.AreEqual(2, result.Added); Assert.AreEqual(1, result.Skipped);
                Assert.AreEqual(basis + "_2", library.Entries[0].Key);
                Assert.AreEqual(basis + "_3", library.Entries[1].Key);
                Assert.True(library.Entries.All(s => s.DisplayName == "same" && s.Category == category && s.Clips.Count == 1));
                AudioLibraryEditor.InitializeAddedEntries(library, 0);
                Assert.AreEqual(basis + "_2", library.Entries[0].Key);
                Assert.AreEqual(basis + "_3", library.Entries[1].Key);
                Assert.True(library.Entries.All(s => s.DisplayName == "same" && s.Category == category));
                Assert.IsEmpty(AudioKeyAuthoring.Errors(library));
                var repeated = AudioFolderImporter.Import(library, new[] { first, nested }, null);
                Assert.AreEqual(0, repeated.Added); Assert.AreEqual(2, repeated.Skipped);
            }
            finally { UnityEngine.Object.DestroyImmediate(category); }
        }

        [Test] public void ImportCanBeUndoneInOneStep()
        {
            Undo.IncrementCurrentGroup();
            AudioFolderImporter.Import(library, new[] { first, nested }, null);
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(2, library.Entries.Count);
            Undo.PerformUndo();
            Assert.AreEqual(0, library.Entries.Count);
            Undo.PerformRedo();
            Assert.AreEqual(2, library.Entries.Count);
        }

        [Test] public async Task ImportWindowAddsFolderContentsAndReportsSkippedClips()
        {
            var window = ScriptableObject.CreateInstance<AudioFolderImportWindow>();
            try
            {
                typeof(AudioFolderImportWindow).GetField("library", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, library);
                window.Show(); await Task.Delay(100);
                var root = window.rootVisualElement;
                var field = root.Query<ObjectField>().ToList().Single(f => f.label == "Folder");
                field.value = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder + "/Audio");
                var button = root.Query<Button>().ToList().Single(b => b.text == "Import");
                Assert.True(button.enabledSelf);
                var invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
                invoke.Invoke(button.clickable, new object[] { null });
                Assert.AreEqual(2, library.Entries.Count);
                invoke.Invoke(button.clickable, new object[] { null });
                Assert.AreEqual(2, library.Entries.Count);
                Assert.True(root.Query<Label>().ToList().Any(l => l.text.Contains("Skipped 2")));
            }
            finally { window.Close(); }
        }
    }
}
