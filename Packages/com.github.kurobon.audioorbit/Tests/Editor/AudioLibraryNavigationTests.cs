using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioLibraryNavigationTests
    {
        private AudioLibrary library;
        private AudioLibraryWindow window;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            var prefix = Guid.NewGuid().ToString("N");
            for (var i = 0; i < 3; i++) library.Entries.Add(new AudioDefinition { Key = prefix + "/" + i, DisplayName = "Audio " + i });
            window = ScriptableObject.CreateInstance<AudioLibraryWindow>();
            typeof(AudioLibraryWindow).GetField("library", Private).SetValue(window, library);
            window.position = new Rect(100, 100, 1000, 700);
            window.Show();
        }
        [TearDown] public void Cleanup()
        {
            window.Close();
            UnityEngine.Object.DestroyImmediate(library);
        }
        private void Navigate(int index) => typeof(AudioLibraryWindow).GetMethod("NavigateToAudio", Private).Invoke(window, new object[] { index });
        private void AssertSelected(int index)
        {
            Assert.AreSame(library, typeof(AudioLibraryWindow).GetField("library", Private).GetValue(window), "Window Library changed");
            Assert.AreSame(library, ((SerializedObject)typeof(AudioLibraryWindow).GetField("serializedLibrary", Private).GetValue(window)).targetObject, "Details binding changed");
            Assert.AreEqual(index, typeof(AudioLibraryWindow).GetField("selected", Private).GetValue(window));
            var table = window.rootVisualElement.Q<MultiColumnListView>();
            Assert.AreEqual(index, table.selectedItem);
            var key = window.rootVisualElement.Q<ScrollView>("audioorbit-library-details").Query<TextField>().ToList().Single(f => f.label == "Key");
            Assert.AreEqual(library.Entries[index].Key, key.value);
        }

        [Test] public async Task LatestNavigationWinsAndUpdatesDetails()
        {
            await Task.Delay(50);
            Navigate(2); Navigate(0); Navigate(1);
            await Task.Delay(150);
            AssertSelected(1);
            Navigate(2); await Task.Delay(100);
            AssertSelected(2);
        }

        [Test] public async Task ImportFolderUpdatesWhenLibraryIsOpenedWithoutAFieldChange()
        {
            await Task.Delay(50);
            var button = window.rootVisualElement.Query<UnityEditor.UIElements.ToolbarButton>().ToList()
                .Single(item => item.text == "Import Folder");
            typeof(AudioLibraryWindow).GetField("library", Private).SetValue(window, null);
            typeof(AudioLibraryWindow).GetMethod("Refresh", Private).Invoke(window, null);
            Assert.False(button.enabledSelf);
            AudioLibraryWindow.ShowLibrary(library);
            Assert.True(button.enabledSelf);
        }

        [Test] public async Task RestoredListSelectionCannotOverrideRequestedSound()
        {
            await Task.Delay(50);
            Navigate(2); await Task.Delay(100);
            window.rootVisualElement.Q<MultiColumnListView>().SetSelection(0);
            await Task.Delay(50);
            AssertSelected(2);
        }

        [Test] public async Task UserSelectionCancelsPendingNavigation()
        {
            await Task.Delay(50);
            Navigate(2);
            var table = window.rootVisualElement.Q<MultiColumnListView>();
            using (var click = PointerDownEvent.GetPooled())
            {
                click.target = table;
                table.SendEvent(click);
            }
            table.SetSelection(1);
            await Task.Delay(150);
            AssertSelected(1);
        }
    }
}
