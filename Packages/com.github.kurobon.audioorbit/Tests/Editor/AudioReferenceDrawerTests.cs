using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioReferenceTestHost : ScriptableObject
    {
        [SerializeReference] public IAudioReference Reference;
        [SerializeReference] public IAudioReference[] References = new IAudioReference[2];
    }

    public sealed class AudioReferenceDrawerTests
    {
        private AudioReferenceTestHost host;
        private EditorWindow window;
        private SerializedObject data;
        [SetUp] public void Setup()
        {
            host = ScriptableObject.CreateInstance<AudioReferenceTestHost>();
            window = ScriptableObject.CreateInstance<EditorWindow>();
            window.position = new Rect(100, 100, 600, 650);
            window.Show();
        }
        [TearDown] public void Cleanup()
        {
            window.rootVisualElement.Unbind();
            window.Close();
            data?.Dispose();
            Object.DestroyImmediate(host);
        }

        [Test] public async Task ArrayElementsCanSelectSwitchAndClearPlaybackReferences()
        {
            data = new SerializedObject(host);
            var array = data.FindProperty("References"); array.isExpanded = true;
            window.rootVisualElement.Add(new PropertyField(array));
            window.rootVisualElement.Bind(data);
            await Task.Delay(200);
            var pickers = window.rootVisualElement.Query<DropdownField>("reference-type").ToList();
            Assert.AreEqual(2, pickers.Count);
            pickers[1].value = ObjectNames.NicifyVariableName(nameof(ResourcesAudioReference));
            await Task.Delay(200); data.Update();
            Assert.Null(host.References[0]);
            Assert.IsInstanceOf<ResourcesAudioReference>(host.References[1]);
            Assert.NotNull(window.rootVisualElement.Query<ObjectField>().Where(f => f.label == "Library").First());
            pickers = window.rootVisualElement.Query<DropdownField>("reference-type").ToList();
            Assert.AreEqual(2, pickers.Count, "Changing type must not lose the picker or add duplicates.");
            pickers[1].value = ObjectNames.NicifyVariableName(nameof(DirectAudioReference));
            await Task.Delay(200); data.Update();
            Assert.IsInstanceOf<DirectAudioReference>(host.References[1]);
            pickers = window.rootVisualElement.Query<DropdownField>("reference-type").ToList();
            Assert.AreEqual(2, pickers.Count);
            pickers[1].value = ObjectNames.NicifyVariableName(nameof(EngineAudioReference));
            await Task.Delay(200); data.Update();
            Assert.IsInstanceOf<EngineAudioReference>(host.References[1]);
            pickers = window.rootVisualElement.Query<DropdownField>("reference-type").ToList();
            pickers[1].value = "None";
            await Task.Delay(150); data.Update();
            Assert.Null(host.References[0]); Assert.Null(host.References[1]);
        }

        [Test] public async Task RemovedArrayElementDoesNotReadOrModifyTheShiftedReference()
        {
            host.References[0] = new ResourcesAudioReference { Library = new ResourcesLibraryReference { LibraryPath = "first" }, Key = "first" };
            host.References[1] = new ResourcesAudioReference { Library = new ResourcesLibraryReference { LibraryPath = "second" }, Key = "second" };
            data = new SerializedObject(host);
            var array = data.FindProperty("References");
            var root = new ResourcesAudioReferenceDrawer().CreatePropertyGUI(array.GetArrayElementAtIndex(0));
            window.rootVisualElement.Add(root);
            await Task.Delay(150);
            array.DeleteArrayElementAtIndex(0);
            data.ApplyModifiedProperties();
            await Task.Delay(150);
            using (var focus = FocusInEvent.GetPooled()) root.SendEvent(focus);
            Assert.False(root.enabledSelf);
            Assert.AreEqual("second", ((ResourcesAudioReference)host.References[0]).Library.LibraryPath);
            Assert.AreEqual("second", ((ResourcesAudioReference)host.References[0]).Key);
            array = data.FindProperty("References");
            array.ClearArray(); data.ApplyModifiedProperties();
            await Task.Delay(150);
            using (var focus = FocusInEvent.GetPooled()) root.SendEvent(focus);
            root.RemoveFromHierarchy();
        }

        [Test] public async Task ReplacedReferenceDoesNotLeaveAReadableOldDrawer()
        {
            host.Reference = new ResourcesAudioReference();
            data = new SerializedObject(host);
            var root = new ResourcesAudioReferenceDrawer().CreatePropertyGUI(data.FindProperty("Reference"));
            window.rootVisualElement.Add(root);
            await Task.Delay(150);
            data.FindProperty("Reference").managedReferenceValue = new DirectAudioReference();
            data.ApplyModifiedProperties();
            await Task.Delay(150);
            using (var focus = FocusInEvent.GetPooled()) root.SendEvent(focus);
            Assert.False(root.enabledSelf);
            Assert.IsInstanceOf<DirectAudioReference>(host.Reference);
            root.RemoveFromHierarchy();
        }

        [Test] public void DisposedSerializedObjectCannotResolveReference()
        {
            host.Reference = new ResourcesAudioReference();
            var disposed = new SerializedObject(host);
            var binding = new ReferencePropertyBinding(disposed.FindProperty("Reference"));
            disposed.Dispose();
            Assert.False(binding.TryGet(out _));
        }

        [Test] public async Task SingleInterfaceFieldSupportsTextKeysAndExcludesLibraryReferences()
        {
            CollectionAssert.Contains(AudioReferenceDrawer.ReferenceTypes, typeof(EngineAudioReference));
            CollectionAssert.DoesNotContain(AudioReferenceDrawer.ReferenceTypes, typeof(ResourcesLibraryReference));
            data = new SerializedObject(host);
            window.rootVisualElement.Add(new PropertyField(data.FindProperty("Reference")));
            window.rootVisualElement.Bind(data);
            await Task.Delay(150);
            window.rootVisualElement.Q<DropdownField>("reference-type").value = ObjectNames.NicifyVariableName(nameof(EngineAudioReference));
            await Task.Delay(150); data.Update();
            data.FindProperty("Reference").FindPropertyRelative("Key").stringValue = "registered_later";
            data.ApplyModifiedProperties();
            Assert.AreEqual("registered_later", ((EngineAudioReference)host.Reference).Key);
        }
    }
}
