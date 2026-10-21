using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class LibraryReferenceTestHost : ScriptableObject
    {
        [SerializeReference] public IAudioLibraryReference Reference;
        [SerializeReference] public IAudioLibraryReference[] References = new IAudioLibraryReference[2];
    }

    public sealed class AudioLibraryReferenceDrawerTests
    {
        private LibraryReferenceTestHost host;
        private EditorWindow window;
        private SerializedObject data;
        [SetUp] public void Setup()
        {
            host = ScriptableObject.CreateInstance<LibraryReferenceTestHost>();
            window = ScriptableObject.CreateInstance<EditorWindow>();
            window.Show();
        }
        [TearDown] public void Cleanup()
        {
            window.rootVisualElement.Unbind();
            window.Close();
            data?.Dispose();
            Object.DestroyImmediate(host);
        }
        [Test] public async Task NullReferenceCanSelectBackendEditAndClear()
        {
            data = new SerializedObject(host);
            window.rootVisualElement.Add(new PropertyField(data.FindProperty("Reference")));
            window.rootVisualElement.Bind(data);
            await Task.Delay(150);
            var picker = window.rootVisualElement.Q<DropdownField>("library-reference-type");
            Assert.NotNull(picker);
            picker.value = ObjectNames.NicifyVariableName(nameof(ResourcesLibraryReference));
            await Task.Delay(150);
            data.Update();
            Assert.IsInstanceOf<ResourcesLibraryReference>(host.Reference);
            Assert.Null(window.rootVisualElement.Query<PopupField<string>>().Where(field => field.label == "Audio").First(), "A Library reference must not display Audio selection.");
            Assert.NotNull(window.rootVisualElement.Query<ObjectField>().Where(field => field.label == "Library").First());
            picker = window.rootVisualElement.Q<DropdownField>("library-reference-type");
            Assert.NotNull(picker);
            picker.value = "None";
            await Task.Delay(150);
            data.Update(); Assert.Null(host.Reference);
        }
        [Test] public async Task ArrayElementsAreIndependentAndExcludeDirectReferences()
        {
            CollectionAssert.DoesNotContain(AudioLibraryReferenceDrawer.ReferenceTypes, typeof(DirectAudioReference));
            data = new SerializedObject(host);
            for (var i = 0; i < 2; i++)
                window.rootVisualElement.Add(new PropertyField(data.FindProperty("References").GetArrayElementAtIndex(i)));
            window.rootVisualElement.Bind(data);
            await Task.Delay(150);
            var pickers = window.rootVisualElement.Query<DropdownField>("library-reference-type").ToList();
            Assert.AreEqual(2, pickers.Count);
            pickers[1].value = ObjectNames.NicifyVariableName(nameof(ResourcesLibraryReference));
            await Task.Delay(150);
            Assert.Null(host.References[0]);
            Assert.IsInstanceOf<ResourcesLibraryReference>(host.References[1]);
        }
    }
}
