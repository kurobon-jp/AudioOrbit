using System;
using System.Reflection;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioLibraryDropTests
    {
        private AudioLibrary library;
        private AudioLibraryWindow window;
        private AudioClip first, second;
        private UnityEngine.Object[] previousReferences;
        private string[] previousPaths;
        [SetUp] public void Setup()
        {
            previousReferences = DragAndDrop.objectReferences; previousPaths = DragAndDrop.paths;
            library = ScriptableObject.CreateInstance<AudioLibrary>(); library.name = "Drop Test " + Guid.NewGuid().ToString("N");
            library.Defaults.Volume = 0.4f;
            first = AudioClip.Create("First", 100, 1, 44100, false); second = AudioClip.Create("Second", 100, 1, 44100, false);
            window = ScriptableObject.CreateInstance<AudioLibraryWindow>();
            typeof(AudioLibraryWindow).GetField("library", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, library);
            window.position = new Rect(100, 100, 1000, 700); window.Show();
        }
        [TearDown] public void Cleanup()
        {
            DragAndDrop.objectReferences = previousReferences; DragAndDrop.paths = previousPaths;
            window.Close(); UnityEngine.Object.DestroyImmediate(library);
            UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
        }
        [TestCase(false)]
        [TestCase(true)]
        public async Task DropReachesImporterBeforeChildCollectionHandlers(bool existingRow)
        {
            if (existingRow)
            {
                library.Entries.Add(new AudioDefinition { Key = Guid.NewGuid().ToString("N"), DisplayName = "Existing" });
                typeof(AudioLibraryWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            }
            await Task.Delay(100);
            var table = window.rootVisualElement.Q<MultiColumnListView>();
            var target = new VisualElement();
            table.Q<ScrollView>().contentContainer.Add(target);
            target.RegisterCallback<DragUpdatedEvent>(e => e.StopImmediatePropagation());
            target.RegisterCallback<DragPerformEvent>(e => e.StopImmediatePropagation());
            DragAndDrop.objectReferences = new UnityEngine.Object[] { first, second, first };
            DragAndDrop.paths = Array.Empty<string>();
            using (var update = DragUpdatedEvent.GetPooled())
            { update.target = target; target.SendEvent(update); }
            Assert.AreEqual(DragAndDropVisualMode.Copy, DragAndDrop.visualMode);
            using (var drop = DragPerformEvent.GetPooled())
            { drop.target = target; target.SendEvent(drop); }
            Assert.AreEqual(existingRow ? 3 : 2, library.Entries.Count);
            var offset = existingRow ? 1 : 0;
            Assert.AreSame(first, library.Entries[offset].Clips[0].Clip);
            Assert.AreSame(second, library.Entries[offset + 1].Clips[0].Clip);
            Assert.AreEqual(0.4f, library.Entries[offset].Volume);
            Assert.IsFalse(library.Entries[offset].Key.Contains("/"));
        }

        [Test] public async Task ClipFieldDropIsNotInterceptedByTheAppendHandler()
        {
            library.Entries.Add(new AudioDefinition { Key = "replace_" + Guid.NewGuid().ToString("N") });
            library.Entries[0].Clips.Add(new ClipDefinition { Clip = first });
            await Task.Delay(100);
            typeof(AudioLibraryWindow).GetMethod("NavigateToAudio", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { 0 });
            VisualElement field = null;
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                field = window.rootVisualElement.Q(className: "audioorbit-clip-resource-field");
                if (field != null && field.panel != null) break;
                await Task.Delay(20);
            }
            Assert.NotNull(field, "The bound Clips list must create its Clip field before testing drop propagation.");
            var child = new VisualElement(); field.Add(child);
            var reached = false;
            child.RegisterCallback<DragPerformEvent>(e => { reached = true; e.StopImmediatePropagation(); });
            DragAndDrop.objectReferences = new UnityEngine.Object[] { second };
            using (var drop = DragPerformEvent.GetPooled())
            { drop.target = child; child.SendEvent(drop); }
            Assert.True(reached, "The Clip field must receive the drop for replacement.");
            Assert.AreEqual(1, library.Entries[0].Clips.Count);
        }

        [Test] public async Task DropOntoClipsAppendsToSelectedSoundAndSupportsUndo()
        {
            library.Entries.Add(new AudioDefinition { Key = "clips_drop_" + Guid.NewGuid().ToString("N") });
            typeof(AudioLibraryWindow).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, 0);
            typeof(AudioLibraryWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            await Task.Delay(100);
            typeof(AudioLibraryWindow).GetMethod("NavigateToAudio", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { 0 });
            var clips = window.rootVisualElement.Q("sound-clips");
            Assert.NotNull(clips);
            DragAndDrop.objectReferences = new UnityEngine.Object[] { first, second, first };
            DragAndDrop.paths = Array.Empty<string>();
            Undo.IncrementCurrentGroup();
            using (var update = DragUpdatedEvent.GetPooled())
            { update.target = clips; clips.SendEvent(update); }
            Assert.AreEqual(DragAndDropVisualMode.Copy, DragAndDrop.visualMode);
            using (var drop = DragPerformEvent.GetPooled())
            { drop.target = clips; clips.SendEvent(drop); }
            Assert.AreEqual(1, library.Entries.Count);
            Assert.AreEqual(2, library.Entries[0].Clips.Count);
            Assert.AreSame(first, library.Entries[0].Clips[0].Clip);
            Assert.AreSame(second, library.Entries[0].Clips[1].Clip);
            Assert.AreEqual(1f, library.Entries[0].Clips[0].Pitch);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.AreEqual(0, library.Entries[0].Clips.Count);
        }
    }
}
