using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.Audio;
#endif
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(ClipDefinition))]
    public sealed class ClipDefinitionDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
            => CreateClipGUI(property, true);

        internal static VisualElement CreateClipGUI(SerializedProperty property, bool showWeight)
        {
            var reference = property.FindPropertyRelative("resource");
            var pitch = property.FindPropertyRelative("Pitch");
            var volume = property.FindPropertyRelative("Volume");
            var weight = property.FindPropertyRelative("Weight");
            if (pitch.floatValue == 0)
            {
                if (volume.floatValue == 0 && weight.floatValue == 0)
                {
                    volume.floatValue = 1;
                    weight.floatValue = 1;
                }
                pitch.floatValue = 1;
                property.serializedObject.ApplyModifiedProperties();
            }
            var data = property.serializedObject;
            var binding = new ReferencePropertyBinding(property);
            var root = new VisualElement();
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.audioorbit.core/Editor/AudioLibraryStyles.uss");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("audioorbit-clip");
            var marker = property.propertyPath.LastIndexOf("Array.data[", System.StringComparison.Ordinal);
            var end = marker >= 0 ? property.propertyPath.IndexOf(']', marker) : -1;
            var index = marker >= 0 && end > marker
                ? int.Parse(property.propertyPath.Substring(marker + 11, end - marker - 11))
                : -1;
            root.userData = index;
            var heading = new VisualElement();
            heading.AddToClassList("audioorbit-clip-heading");
            heading.style.flexDirection = FlexDirection.Row;
            heading.style.display = DisplayStyle.None;
            var title = new Label("No Clip");
            title.style.flexGrow = 1;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.Add(title);
            var previewButton = new Button(() =>
            {
                using (var evt = ClipPreviewEvent.GetPooled())
                {
                    evt.Index = index;
                    evt.target = root;
                    root.SendEvent(evt);
                }
            }) { text = "▶ Preview Clip", tooltip = "Preview this Clip with the Audio settings applied." };
            previewButton.SetEnabled(false);
            heading.Add(previewButton);
            root.Add(heading);
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                for (var parent = root.parent; parent != null; parent = parent.parent)
                    if (parent.name == "audioorbit-library-details")
                    {
                        heading.style.display = DisplayStyle.Flex;
                        break;
                    }
            });

            var field = AudioGeneratorPicker.CreateField("Clip");
            field.AddToClassList("audioorbit-clip-resource-field");

            bool ResolveResource()
            {
                if (!binding.TryGet(out var current)) return false;
                reference = current.FindPropertyRelative("resource");
                return reference != null;
            }

            void Refresh()
            {
                if (!ResolveResource()) return;
                var asset = reference.objectReferenceValue;
                field.SetValueWithoutNotify(asset);
                var clip = asset as AudioClip;
#if UNITY_6000_3_OR_NEWER
                if (asset is LoopRegionClip region) clip = region.Clip;
#endif
                title.text = clip != null ? clip.name : asset != null ? asset.name : "No Clip";
                title.tooltip = title.text;
                var canPreview = asset is AudioClip;
#if UNITY_6000_3_OR_NEWER
                canPreview = asset is IAudioGenerator;
                if (asset is LoopRegionClip) canPreview = clip != null;
#endif
                previewButton.SetEnabled(canPreview);
                using (var evt = ClipChangedEvent.GetPooled())
                {
                    evt.target = root;
                    root.SendEvent(evt);
                }
            }

            field.RegisterValueChangedCallback(e =>
            {
                if (!ResolveResource()) return;
#if UNITY_6000_3_OR_NEWER
                if (e.newValue != null && !(e.newValue is IAudioGenerator))
#else
                if (e.newValue != null && !(e.newValue is AudioClip))
#endif
                {
                    Refresh();
                    return;
                }

                reference.objectReferenceValue = e.newValue;
                data.ApplyModifiedProperties();
                Refresh();
            });
            root.Add(field);
            foreach (var name in new[] { "Volume", "Pitch" })
                root.Add(new PropertyField(property.FindPropertyRelative(name)));
            var advanced = new Foldout
            {
                text = showWeight ? "Randomize & Weight" : "Randomize", value = false,
                viewDataKey = "clip-variation-" + property.propertyPath
            };
            advanced.AddToClassList("audioorbit-clip-advanced");
            advanced.Add(new PropertyField(property.FindPropertyRelative("VolumeRandomRange"), "Volume Range"));
            advanced.Add(new PropertyField(property.FindPropertyRelative("PitchRandomRange"), "Pitch Range"));
            if (showWeight) advanced.Add(new PropertyField(property.FindPropertyRelative("Weight")));
            root.Add(advanced);
            ReferencePropertyBinding.Track(root, Refresh);
            Refresh();
            return root;
        }
    }
}

