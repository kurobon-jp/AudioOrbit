#if ENABLE_ADDRESSABLES
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Addressables.Editor
{
    [CustomPropertyDrawer(typeof(AddressablesAudioReference))]
    public sealed class AddressablesAudioReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => CreateGUI(property, false);

        internal static VisualElement CreateGUI(SerializedProperty property, bool libraryOnly)
        {
            var root = new VisualElement();
            root.Add(new Label(AudioOrbit.Editor.AudioLibraryReferenceDrawer.DisplayName(property)) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            if (libraryOnly) AudioOrbit.Editor.AudioLibraryReferenceDrawer.AddTypePicker(root, property);
            else AudioOrbit.Editor.AudioReferenceDrawer.AddTypePicker(root, property);

            var data = property.serializedObject;
            var binding = new AudioOrbit.Editor.ReferencePropertyBinding(property);
            SerializedProperty library = null, key = null, libraryGuid = null;
            var libraryField = new ObjectField("Library")
            {
                objectType = typeof(AudioLibrary),
                allowSceneObjects = false,
                tooltip = "Select an AudioLibrary registered in Addressables."
            };
            root.Add(libraryField);
            var validation = new HelpBox(string.Empty, HelpBoxMessageType.Error) { name = "addressable-library-validation" };
            validation.style.display = DisplayStyle.None;
            root.Add(validation);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var choices = new List<string>();
            var keys = new List<string>();
            var popup = new PopupField<string>("Audio", new List<string> { "None" }, 0);
            popup.style.flexGrow = 1;
            row.Add(popup);
            var open = new Button { text = "Open", tooltip = "Open the selected Audio in the Library Manager." };
            row.Add(open);
            if (!libraryOnly) root.Add(row);
            AudioLibrary selectedLibrary = null;
            var rejectedAssignment = false;

            bool IsAddressable(string guid)
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                return !string.IsNullOrEmpty(guid) && settings != null && settings.FindAssetEntry(guid, true) != null;
            }

            bool ResolveProperties()
            {
                if (!binding.TryGet(out var current)) { root.SetEnabled(false); return false; }
                var location = libraryOnly ? current : current.FindPropertyRelative("Library");
                library = location?.FindPropertyRelative("Asset");
                libraryGuid = library?.FindPropertyRelative("m_AssetGUID");
                key = libraryOnly ? null : current.FindPropertyRelative("Key");
                return libraryGuid != null && (libraryOnly || key != null);
            }

            bool Refresh()
            {
                if (!ResolveProperties()) return false;
                selectedLibrary = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetDatabase.GUIDToAssetPath(
                    libraryGuid.stringValue));
                libraryField.SetValueWithoutNotify(selectedLibrary);
                var addressable = selectedLibrary != null && IsAddressable(libraryGuid.stringValue);
                var invalid = !string.IsNullOrEmpty(libraryGuid.stringValue) && !addressable;
                validation.text = rejectedAssignment
                    ? "Only Addressable AudioLibraries can be assigned. Register the asset in Addressables first."
                    : "The selected Library is not Addressable. Register it in Addressables or select another Library.";
                validation.style.display = invalid || rejectedAssignment ? DisplayStyle.Flex : DisplayStyle.None;
                if (libraryOnly) return true;
                choices.Clear();
                keys.Clear();
                choices.Add("None");
                keys.Add(string.Empty);
                if (addressable)
                    foreach (var sound in selectedLibrary.Entries.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)))
                    {
                        var text = (sound.DisplayName + " (" + sound.Key + ")").Replace('/', '／');
                        while (choices.Contains(text)) text += " *";
                        choices.Add(text);
                        keys.Add(sound.Key);
                    }

                var index = keys.IndexOf(key.stringValue);
                if (index < 0)
                {
                    index = keys.Count;
                    choices.Add(("Missing Audio (" + key.stringValue + ")").Replace('/', '／'));
                    keys.Add(key.stringValue);
                }

                popup.choices = new List<string>(choices);
                popup.SetValueWithoutNotify(choices[index]);
                popup.SetEnabled(addressable);
                open.SetEnabled(addressable &&
                                selectedLibrary.Entries.Any(s => s != null && s.Key == key.stringValue));
                return true;
            }

            libraryField.RegisterValueChangedCallback(e =>
            {
                if (!ResolveProperties()) return;
                var asset = e.newValue as AudioLibrary;
                var guid = asset == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
                if (asset != null && !IsAddressable(guid))
                {
                    rejectedAssignment = true;
                    Refresh();
                    e.StopImmediatePropagation();
                    return;
                }
                rejectedAssignment = false;
                libraryGuid.stringValue = guid;
                var subObjectName = library.FindPropertyRelative("m_SubObjectName");
                if (subObjectName != null)
                {
                    subObjectName.stringValue = string.Empty;
                }

                var subObjectType = library.FindPropertyRelative("m_SubObjectType");
                if (subObjectType != null)
                {
                    subObjectType.stringValue = string.Empty;
                }

                var subObjectGuid = library.FindPropertyRelative("m_SubObjectGUID");
                if (subObjectGuid != null)
                {
                    subObjectGuid.stringValue = string.Empty;
                }

                data.ApplyModifiedProperties();
                Refresh();
            });
            popup.RegisterValueChangedCallback(e =>
            {
                if (!ResolveProperties()) return;
                var index = choices.IndexOf(e.newValue);
                if (index < 0) return;
                key.stringValue = keys[index];
                data.ApplyModifiedProperties();
                Refresh();
            });
            open.clicked += () =>
            {
                if (!Refresh() || selectedLibrary == null) return;
                var index = selectedLibrary.Entries.FindIndex(s => s != null && s.Key == key.stringValue);
                if (index >= 0) AudioOrbit.Editor.AudioLibraryWindow.ShowAudio(selectedLibrary, index);
            };
            void OnAddressablesChanged(AddressableAssetSettings settings, AddressableAssetSettings.ModificationEvent change, object data) => Refresh();
            root.RegisterCallback<AttachToPanelEvent>(_ => AddressableAssetSettings.OnModificationGlobal += OnAddressablesChanged);
            root.RegisterCallback<DetachFromPanelEvent>(_ => AddressableAssetSettings.OnModificationGlobal -= OnAddressablesChanged);
            AudioOrbit.Editor.ReferencePropertyBinding.Track(root, data, () => Refresh());
            root.RegisterCallback<FocusInEvent>(_ => Refresh());
            Refresh();
            return root;
        }
    }
}
#endif
