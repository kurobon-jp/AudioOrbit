using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(AudioKeyAttribute))]
    public sealed class AudioKeyDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var field = new TextField(property.displayName) { isDelayed = true };
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            var warning = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            root.Add(field); root.Add(warning);
            var serialized = property.serializedObject;
            var path = property.propertyPath;

            void ShowError(string error)
            {
                warning.text = error ?? string.Empty;
                warning.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex;
            }

            string Validate(SerializedProperty value, string key)
            {
                if (serialized.isEditingMultipleObjects) return "Edit Audio Keys one Library at a time.";
                return AudioKeyAuthoring.Conflict(serialized.targetObject as AudioLibrary, AudioKeyAuthoring.Index(value), key);
            }

            void Refresh()
            {
                var value = serialized.FindProperty(path);
                if (value == null) return;
                field.SetValueWithoutNotify(value.stringValue);
                field.showMixedValue = value.hasMultipleDifferentValues;
                ShowError(Validate(value, value.stringValue));
            }

            field.RegisterValueChangedCallback(e =>
            {
                serialized.UpdateIfRequiredOrScript();
                var value = serialized.FindProperty(path);
                if (value == null) return;
                var error = Validate(value, e.newValue);
                if (error != null)
                {
                    field.SetValueWithoutNotify(value.stringValue);
                    ShowError(error);
                    return;
                }
                value.stringValue = e.newValue;
                serialized.ApplyModifiedProperties();
                ShowError(null);
                using var changed = SerializedPropertyChangeEvent.GetPooled(value);
                changed.target = root;
                root.SendEvent(changed);
            });
            root.TrackPropertyValue(property, _ => Refresh());
            root.RegisterCallback<FocusInEvent>(_ => Refresh());
            Refresh();
            return root;
        }
    }
}
