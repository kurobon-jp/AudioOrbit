using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioKeyAuthoringTests
    {
        private string folder, prefix;
        private AudioLibrary first, second;
        [SetUp] public void Setup()
        {
            prefix = "key_test_" + Guid.NewGuid().ToString("N");
            folder = "Assets/" + prefix;
            AssetDatabase.CreateFolder("Assets", prefix);
            first = ScriptableObject.CreateInstance<AudioLibrary>();
            second = ScriptableObject.CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(first, folder + "/First.asset");
            AssetDatabase.CreateAsset(second, folder + "/Second.asset");
            first.Entries.Add(new AudioDefinition { Key = prefix + "/one", DisplayName = "First" });
            first.Entries.Add(new AudioDefinition { Key = prefix + "/two", DisplayName = "Second" });
            second.Entries.Add(new AudioDefinition { Key = prefix + "/other" });
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(folder);

        [Test] public void ConflictsIncludeOtherLibrariesAndExcludeOnlyTheEditedEntry()
        {
            Assert.IsNull(AudioKeyAuthoring.Conflict(first, 0, first.Entries[0].Key));
            Assert.IsNotNull(AudioKeyAuthoring.Conflict(first, 0, first.Entries[1].Key));
            StringAssert.Contains("Second.asset", AudioKeyAuthoring.Conflict(first, 0, second.Entries[0].Key));
            Assert.IsNull(AudioKeyAuthoring.Conflict(first, 0, first.Entries[1].Key.ToUpperInvariant()));
            Assert.IsNotNull(AudioKeyAuthoring.Conflict(first, 0, " "));
        }

        [Test] public async Task KeyFieldRejectsDuplicateWithoutChangingOtherProperties()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                using var serialized = new SerializedObject(first);
                var key = serialized.FindProperty("definitions").GetArrayElementAtIndex(0).FindPropertyRelative("Key");
                var root = new AudioKeyDrawer().CreatePropertyGUI(key);
                window.Show(); window.rootVisualElement.Add(root); root.Bind(serialized);
                await Task.Delay(50);
                var input = root.Q<TextField>();
                var original = first.Entries[0].Key;
                input.value = second.Entries[0].Key;
                Assert.AreEqual(original, first.Entries[0].Key);
                Assert.AreEqual(original, input.value);
                Assert.AreEqual(DisplayStyle.Flex, root.Q<HelpBox>().style.display.value);
                Assert.AreEqual("First", first.Entries[0].DisplayName);
                input.value = prefix + "/new";
                Assert.AreEqual(prefix + "/new", first.Entries[0].Key);
                Assert.AreEqual(DisplayStyle.None, root.Q<HelpBox>().style.display.value);
            }
            finally { window.Close(); }
        }

        [Test] public void NewSoundsAvoidKeysUsedByOtherLibraries()
        {
            var initial = first.Entries.Count;
            first.Entries.Add(new AudioDefinition());
            second.Entries[0].Key = "new_audio";
            AudioLibraryEditor.InitializeAddedEntries(first, initial);
            Assert.AreNotEqual("new_audio", first.Entries.Last().Key);
            Assert.IsNull(AudioKeyAuthoring.Conflict(first, first.Entries.Count - 1, first.Entries.Last().Key));
        }

        [Test] public async Task DuplicateAssignsUniqueKeyAndPreservesDisplayName()
        {
            var window = ScriptableObject.CreateInstance<AudioLibraryWindow>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(AudioLibraryWindow).GetField("library", flags).SetValue(window, first);
                window.Show();
                await Task.Delay(50);
                typeof(AudioLibraryWindow).GetField("selected", flags).SetValue(window, 0);
                typeof(AudioLibraryWindow).GetMethod("Duplicate", flags).Invoke(window, null);
                Assert.AreEqual(3, first.Entries.Count);
                Assert.AreNotEqual(first.Entries[0].Key, first.Entries[1].Key);
                Assert.AreEqual(first.Entries[0].DisplayName, first.Entries[1].DisplayName);
                Assert.IsEmpty(AudioKeyAuthoring.Errors(first));
            }
            finally { window.Close(); }
        }

        [Test] public void SaveGuardRejectsExistingConflictsAndLeavesOtherFilesAllowed()
        {
            first.Entries[0].Key = second.Entries[0].Key;
            var path = AssetDatabase.GetAssetPath(first);
            LogAssert.Expect(LogType.Error, new Regex("Cannot save AudioLibrary:.*", RegexOptions.Singleline));
            var save = typeof(AudioKeySaveGuard).GetMethod("OnWillSaveAssets", BindingFlags.Static | BindingFlags.NonPublic);
            var allowed = (string[])save.Invoke(null, new object[] { new[] { path, folder + "/Scene.unity" } });
            CollectionAssert.AreEqual(new[] { folder + "/Scene.unity" }, allowed);
            first.Entries[0].Key = prefix + "/fixed";
            Assert.IsEmpty(AudioKeyAuthoring.Errors(first));
        }
    }
}
