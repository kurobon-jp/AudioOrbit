using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioPlayerEditorTests
    {
        private string folder;
        private GameObject owner;
        private AudioPlayer player;
        [SetUp] public void Setup()
        {
            folder = "Assets/AudioOrbitAudioPlayerTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            owner = new GameObject("Audio Player"); owner.SetActive(false);
            player = owner.AddComponent<AudioPlayer>();
        }
        [TearDown] public void Cleanup()
        {
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            AssetDatabase.DeleteAsset(folder);
        }
        [Test] public void PickerDiscoversSerializableBackendsWithoutCoreDependingOnAddressables()
        {
            var types = AudioPlayerEditor.ReferenceTypes;
            Assert.Contains(typeof(ResourcesAudioReference), types);
            Assert.Contains(typeof(DirectAudioReference), types);
            Assert.Contains(typeof(EngineAudioReference), types);
            Assert.True(types.All(type => typeof(IAudioReference).IsAssignableFrom(type) && type.IsSerializable && !type.IsAbstract));
            var addressables = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("AudioOrbit.Addressables.AddressablesAudioReference")).FirstOrDefault(type => type != null);
            if (addressables != null) Assert.Contains(addressables, types);
        }
        [Test] public async Task InspectorTypePickerCreatesAndClearsReference()
        {
            var editor = (AudioPlayerEditor)UnityEditor.Editor.CreateEditor(player);
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                var root = editor.CreateInspectorGUI();
                window.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                window.Show();
                await Task.Delay(100);
                var picker = root.Q<DropdownField>("reference-type");
                Assert.NotNull(picker);
                Assert.AreEqual("None", picker.value);
                picker.value = UnityEditor.ObjectNames.NicifyVariableName(nameof(ResourcesAudioReference));
                using var serialized = new SerializedObject(player);
                Assert.IsInstanceOf<ResourcesAudioReference>(serialized.FindProperty("reference").managedReferenceValue);
                var previousPicker = picker;
                var deadline = EditorApplication.timeSinceStartup + 3;
                do
                {
                    await Task.Delay(20);
                    picker = root.Q<DropdownField>("reference-type");
                } while ((picker == null || picker == previousPicker || picker.panel == null) &&
                         EditorApplication.timeSinceStartup < deadline);
                Assert.NotNull(picker);
                Assert.AreNotSame(previousPicker, picker, "Type changes must rebuild the reference UI.");
                picker.value = "None";
                serialized.Update(); Assert.Null(serialized.FindProperty("reference").managedReferenceValue);
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(editor); }
        }
        [Test] public async Task AddressablesLibraryFieldRejectsUnregisteredAssetsAndTracksRegistrationChanges()
        {
            Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            var referenceType = FindType("AudioOrbit.Addressables.AddressablesAudioReference");
            var drawerType = FindType("AudioOrbit.Addressables.Editor.AddressablesAudioReferenceDrawer");
            var settingsType = FindType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            if (referenceType == null || drawerType == null || settingsType == null) Assert.Ignore("Addressables extension not installed.");
            var settings = settingsType.GetProperty("Settings").GetValue(null);
            if (settings == null) Assert.Ignore("No Addressables settings configured.");
            var group = settings.GetType().GetProperty("DefaultGroup").GetValue(settings);
            Assert.NotNull(group);
            var create = settings.GetType().GetMethod("CreateOrMoveEntry", new[] { typeof(string), group.GetType(), typeof(bool), typeof(bool) });
            var remove = settings.GetType().GetMethod("RemoveAssetEntry", new[] { typeof(string), typeof(bool) });
            var find = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string), typeof(bool) });
            var library = ScriptableObject.CreateInstance<AudioLibrary>(); library.Entries.Add(new AudioDefinition { Key = "test_key" });
            AssetDatabase.CreateAsset(library, folder + "/Library.asset");
            var guid = AssetDatabase.AssetPathToGUID(folder + "/Library.asset");
            AssetDatabase.CreateFolder(folder, "AddressableFolder");
            var nested = ScriptableObject.CreateInstance<AudioLibrary>(); nested.Entries.Add(new AudioDefinition { Key = "nested_key" });
            AssetDatabase.CreateAsset(nested, folder + "/AddressableFolder/Nested.asset");
            var folderGuid = AssetDatabase.AssetPathToGUID(folder + "/AddressableFolder");
            var nestedGuid = AssetDatabase.AssetPathToGUID(folder + "/AddressableFolder/Nested.asset");
            using var data = new SerializedObject(player);
            var property = data.FindProperty("reference");
            property.managedReferenceValue = Activator.CreateInstance(referenceType);
            property.FindPropertyRelative("Key").stringValue = "test_key";
            data.ApplyModifiedPropertiesWithoutUndo();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                var root = ((PropertyDrawer)Activator.CreateInstance(drawerType)).CreatePropertyGUI(property);
                window.rootVisualElement.Add(root); root.Bind(data); window.Show(); await Task.Delay(100);
                var field = root.Query<ObjectField>().Where(f => f.label == "Library").First();
                var popup = root.Q<PopupField<string>>();
                var warning = root.Q<HelpBox>("addressable-library-validation");
                string SelectedGuid() { data.Update(); return property.FindPropertyRelative("Library").FindPropertyRelative("Asset").FindPropertyRelative("m_AssetGUID").stringValue; }
                field.value = library;
                Assert.IsEmpty(SelectedGuid()); Assert.Null(field.value);
                Assert.AreEqual(DisplayStyle.Flex, warning.style.display.value);
                Assert.Null(find.Invoke(settings, new object[] { guid, true }), "Rejected assignments must not auto-register assets.");
                create.Invoke(settings, new object[] { guid, group, false, true });
                field.value = library;
                Assert.AreEqual(guid, SelectedGuid()); Assert.True(popup.enabledSelf);
                field.value = nested;
                Assert.AreEqual(guid, SelectedGuid()); Assert.AreSame(library, field.value);
                remove.Invoke(settings, new object[] { guid, true });
                Assert.False(popup.enabledSelf); Assert.AreEqual(DisplayStyle.Flex, warning.style.display.value);
                create.Invoke(settings, new object[] { guid, group, false, true });
                Assert.True(popup.enabledSelf);
                field.value = null;
                Assert.IsEmpty(SelectedGuid()); Assert.False(popup.enabledSelf);
                create.Invoke(settings, new object[] { folderGuid, group, false, true });
                field.value = nested;
                Assert.AreEqual(nestedGuid, SelectedGuid()); Assert.True(popup.enabledSelf, "A Library included by an Addressable folder must be accepted.");
            }
            finally
            {
                window.Close();
                remove.Invoke(settings, new object[] { guid, true });
                remove.Invoke(settings, new object[] { folderGuid, true });
            }
        }

        [Test] public void EveryAvailableBackendSurvivesPrefabSerialization()
        {
            foreach (var type in AudioPlayerEditor.ReferenceTypes)
            {
                using var serialized = new SerializedObject(player);
                AudioPlayerEditor.SetReferenceType(serialized, type);
                var reference = serialized.FindProperty("reference").managedReferenceValue;
                var key = type.GetField("Key"); if (key == null) continue;
                serialized.FindProperty("reference").FindPropertyRelative("Key").stringValue = "test_key";
                var libraryPath = serialized.FindProperty("reference").FindPropertyRelative("Library")?.FindPropertyRelative("LibraryPath");
                if (libraryPath != null) libraryPath.stringValue = "Test/Library";
                serialized.FindProperty("engineKey").stringValue = "BGM";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var path = folder + "/" + type.Name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(owner, path);
                var loaded = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    using var copy = new SerializedObject(loaded.GetComponent<AudioPlayer>());
                    var restored = copy.FindProperty("reference").managedReferenceValue;
                    Assert.AreEqual(type, restored.GetType());
                    Assert.AreEqual("test_key", key.GetValue(restored));
                    Assert.AreEqual("BGM", copy.FindProperty("engineKey").stringValue);
                    if (type == typeof(ResourcesAudioReference)) Assert.AreEqual("Test/Library", ((ResourcesAudioReference)restored).Library.LibraryPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
            }
        }
        [Test] public void DirectReferencePreservesClipSettingsInPrefab()
        {
            using var serialized = new SerializedObject(player);
            AudioPlayerEditor.SetReferenceType(serialized, typeof(DirectAudioReference));
            var reference = (DirectAudioReference)serialized.FindProperty("reference").managedReferenceValue;
            reference.Clip.Volume = .4f;
            reference.Clip.Pitch = 1.2f;
            reference.Clip.VolumeRandomRange = .15f;
            serialized.FindProperty("reference").managedReferenceValue = reference;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var path = folder + "/Direct.prefab";
            PrefabUtility.SaveAsPrefabAsset(owner, path);
            var loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                using var copy = new SerializedObject(loaded.GetComponent<AudioPlayer>());
                var restored = (DirectAudioReference)copy.FindProperty("reference").managedReferenceValue;
                Assert.AreEqual(.4f, restored.Clip.Volume);
                Assert.AreEqual(1.2f, restored.Clip.Pitch);
                Assert.AreEqual(.15f, restored.Clip.VolumeRandomRange);
                var drawer = new DirectAudioReferenceDrawer();
                var root = drawer.CreatePropertyGUI(copy.FindProperty("reference"));
                Assert.NotNull(root);
                Assert.IsNull(root.Query<PropertyField>().ToList().FirstOrDefault(field => field.bindingPath.EndsWith("Weight")));
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }
        [Test] public void MultiSelectionCreatesIndependentReferencesAndSupportsUndo()
        {
            var other = new GameObject("Other Player"); other.SetActive(false);
            try
            {
                var second = other.AddComponent<AudioPlayer>();
                using var serialized = new SerializedObject(new UnityEngine.Object[] { player, second });
                Undo.IncrementCurrentGroup();
                AudioPlayerEditor.SetReferenceType(serialized, typeof(ResourcesAudioReference));
                using var firstData = new SerializedObject(player);
                using var secondData = new SerializedObject(second);
                Assert.AreNotSame(firstData.FindProperty("reference").managedReferenceValue, secondData.FindProperty("reference").managedReferenceValue);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                firstData.Update(); secondData.Update();
                Assert.Null(firstData.FindProperty("reference").managedReferenceValue);
                Assert.Null(secondData.FindProperty("reference").managedReferenceValue);
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }
        [Test] public void EngineKeyPickerUsesPreloadedDefinitionsAndPreservesUnknownKeys()
        {
            var previous = PlayerSettings.GetPreloadedAssets();
            var context = ScriptableObject.CreateInstance<OrbitContext>();
            AssetDatabase.CreateAsset(context, folder + "/Context.asset");
            UnityEditor.Editor editor = null;
            try
            {
                using (var data = new SerializedObject(context))
                {
                    var definitions = data.FindProperty("definitions"); definitions.arraySize = 2;
                    definitions.GetArrayElementAtIndex(0).FindPropertyRelative("Key").stringValue = "Default";
                    definitions.GetArrayElementAtIndex(1).FindPropertyRelative("Key").stringValue = "BGM";
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                PlayerSettings.SetPreloadedAssets(previous.Where(item => !(item is OrbitContext)).Concat(new UnityEngine.Object[] { context }).ToArray());
                editor = UnityEditor.Editor.CreateEditor(player);
                var root = ((AudioPlayerEditor)editor).CreateInspectorGUI();
                var picker = root.Q<DropdownField>("engine-key");
                CollectionAssert.AreEqual(new[] { "Default", "BGM" }, picker.choices);
                using var serialized = new SerializedObject(player);
                serialized.FindProperty("engineKey").stringValue = "Unknown"; serialized.ApplyModifiedPropertiesWithoutUndo();
                editor.serializedObject.Update();
                root = ((AudioPlayerEditor)editor).CreateInspectorGUI();
                Assert.AreEqual("Unknown", root.Q<DropdownField>("engine-key").value);
                Assert.AreEqual("Unknown", serialized.FindProperty("engineKey").stringValue);
                Assert.True(root.Query<HelpBox>().ToList().Any(box => box.text.Contains("not defined")));
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(previous);
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            }
        }
        [Test] public void ContextInspectorDisplaysDuplicateAndEmptyKeys()
        {
            var context = ScriptableObject.CreateInstance<OrbitContext>();
            var editor = (OrbitContextEditor)UnityEditor.Editor.CreateEditor(context);
            try
            {
                var definitions = editor.serializedObject.FindProperty("definitions"); definitions.arraySize = 2;
                for (int i = 0; i < 2; i++) definitions.GetArrayElementAtIndex(i).FindPropertyRelative("Key").stringValue = "Default";
                editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                StringAssert.Contains("Duplicate", editor.CreateInspectorGUI().Q<HelpBox>().text);
                definitions.GetArrayElementAtIndex(1).FindPropertyRelative("Key").stringValue = "";
                editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                StringAssert.Contains("empty", editor.CreateInspectorGUI().Q<HelpBox>().text);
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(context); }
        }
    }
}
