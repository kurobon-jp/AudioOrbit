using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(ResourcesAudioReference))]
    public sealed class ResourcesAudioReferenceDrawer : PropertyDrawer
    {
        internal static string ResourcesPath(string assetPath)
        {
            const string marker = "/Resources/";
            var index = assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            var relative = assetPath.Substring(index + marker.Length);
            var extension = relative.LastIndexOf('.');
            return extension < 0 ? relative : relative.Substring(0, extension);
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property) => CreateGUI(property, false);

        internal static VisualElement CreateGUI(SerializedProperty property, bool libraryOnly)
        {
            var data = property.serializedObject;
            var binding = new ReferencePropertyBinding(property);
            SerializedProperty path = null, key = null;
            var root = new VisualElement();
            root.Add(new Label(AudioLibraryReferenceDrawer.DisplayName(property)) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            if (libraryOnly) AudioLibraryReferenceDrawer.AddTypePicker(root, property);
            else AudioReferenceDrawer.AddTypePicker(root, property);

            var libraryField = new ObjectField("Library") { objectType = typeof(AudioLibrary), allowSceneObjects = false };
            root.Add(libraryField);
            // root.Add(new PropertyField(path, "Library Path"));
            var warning = new HelpBox("Select an AudioLibrary inside a Resources folder.", HelpBoxMessageType.Error);
            root.Add(warning);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var popup = new PopupField<string>("Audio", new List<string> { "None" }, 0) { style = { flexGrow = 1 } };
            row.Add(popup);
            var open = new Button { text = "Open", tooltip = "Open the selected Audio in the Library Manager." };
            row.Add(open);
            if (!libraryOnly) root.Add(row);
            AudioLibrary library = null;
            var keys = new List<string>();

            bool ResolveProperties()
            {
                if (!binding.TryGet(out var current)) { root.SetEnabled(false); return false; }
                var location = libraryOnly ? current : current.FindPropertyRelative("Library");
                path = location?.FindPropertyRelative("LibraryPath");
                key = libraryOnly ? null : current.FindPropertyRelative("Key");
                return path != null && (libraryOnly || key != null);
            }

            bool Refresh()
            {
                if (!ResolveProperties()) return false;
                var libraryPath = path.stringValue;
                library = AssetDatabase.FindAssets("t:AudioLibrary").Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => string.Equals(ResourcesPath(p), libraryPath, StringComparison.OrdinalIgnoreCase))
                    .Select(AssetDatabase.LoadAssetAtPath<AudioLibrary>).FirstOrDefault();
                libraryField.SetValueWithoutNotify(library);
                warning.style.display = library == null && !string.IsNullOrEmpty(libraryPath) ? DisplayStyle.Flex : DisplayStyle.None;
                if (libraryOnly) return true;
                keys.Clear(); keys.Add(string.Empty);
                var labels = new List<string> { "None" };
                if (library != null)
                    foreach (var sound in library.Entries.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)))
                    {
                        var label = (sound.DisplayName + " (" + sound.Key + ")").Replace('/', '／');
                        while (labels.Contains(label)) label += " *";
                        labels.Add(label); keys.Add(sound.Key);
                    }
                var index = keys.IndexOf(key.stringValue);
                if (index < 0)
                {
                    index = keys.Count; keys.Add(key.stringValue);
                    labels.Add(("Missing Audio (" + key.stringValue + ")").Replace('/', '／'));
                }
                popup.choices = labels;
                popup.SetValueWithoutNotify(labels[index]);
                popup.SetEnabled(library != null);
                open.SetEnabled(library != null && library.Entries.Any(s => s != null && s.Key == key.stringValue));
                return true;
            }

            libraryField.RegisterValueChangedCallback(e =>
            {
                if (!ResolveProperties()) return;
                var selected = e.newValue as AudioLibrary;
                var selectedPath = selected == null ? string.Empty : ResourcesPath(AssetDatabase.GetAssetPath(selected));
                if (selectedPath == null)
                {
                    libraryField.SetValueWithoutNotify(library);
                    warning.style.display = DisplayStyle.Flex;
                    return;
                }
                path.stringValue = selectedPath;
                data.ApplyModifiedProperties();
                Refresh();
            });
            popup.RegisterValueChangedCallback(e =>
            {
                if (!ResolveProperties()) return;
                var index = popup.choices.IndexOf(e.newValue);
                if (index < 0) return;
                key.stringValue = keys[index];
                data.ApplyModifiedProperties();
                Refresh();
            });
            open.clicked += () =>
            {
                if (!Refresh() || library == null) return;
                var index = library.Entries.FindIndex(s => s != null && s.Key == key.stringValue);
                if (index >= 0) AudioLibraryWindow.ShowAudio(library, index);
            };
            ReferencePropertyBinding.Track(root, () => Refresh());
            root.RegisterCallback<FocusInEvent>(_ => Refresh());
            Refresh();
            return root;
        }
    }
}
