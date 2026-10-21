using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    internal static class AudioIdSelector
    {
        internal sealed class Choice
        {
            internal string Name;
            internal string Id;
        }

        internal static VisualElement Create(SerializedProperty property)
        {
            var serialized = property.serializedObject;
            var path = property.FindPropertyRelative("value").propertyPath;
            var popup = new PopupField<string>("", new List<string> { "None" }, 0)
            {
                name = property.propertyPath,
                tooltip = "Select an AudioCategory from the project."
            };
            popup.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            var ids = new List<string>();
            Action refresh = () =>
            {
                serialized.UpdateIfRequiredOrScript();
                var value = serialized.FindProperty(path);
                if (value == null) return;
                var choices = Categories();
                var names = new List<string> { "None" };
                ids.Clear();
                ids.Add(string.Empty);
                foreach (var choice in choices)
                {
                    var name = choice.Name;
                    for (int suffix = 2; names.Contains(name); suffix++) name = choice.Name + " (" + suffix + ")";
                    names.Add(name);
                    ids.Add(choice.Id);
                }

                var current = value.stringValue;
                var index = ids.IndexOf(current);
                if (index < 0)
                {
                    names.Add("Missing Category");
                    ids.Add(current);
                    index = ids.Count - 1;
                }

                popup.choices = names;
                popup.SetValueWithoutNotify(names[index]);
                popup.showMixedValue = value.hasMultipleDifferentValues;
            };
            refresh();
            popup.RegisterCallback<FocusInEvent>(_ => refresh());
            popup.RegisterValueChangedCallback(evt =>
            {
                var index = popup.choices.IndexOf(evt.newValue);
                if (index < 0 || index >= ids.Count) return;
                serialized.Update();
                var value = serialized.FindProperty(path);
                if (value == null) return;
                value.stringValue = ids[index];
                serialized.ApplyModifiedProperties();
                popup.showMixedValue = false;
            });
            popup.TrackPropertyValue(property.FindPropertyRelative("value"), _ => refresh());
            return popup;
        }

        private static List<Choice> Categories()
        {
            return AssetDatabase.FindAssets("t:AudioCategory").Concat(AssetDatabase.FindAssets("t:AudioLibrary"))
                .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AudioCategory>()
                .Where(c => c.Id.IsValid).Distinct()
                .Select(c => new Choice { Name = c.name, Id = c.Id.Value })
                .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        }
    }

    [CustomPropertyDrawer(typeof(AudioCategoryId))]
    public sealed class AudioCategoryIdDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) =>
            AudioIdSelector.Create(property);
    }
}