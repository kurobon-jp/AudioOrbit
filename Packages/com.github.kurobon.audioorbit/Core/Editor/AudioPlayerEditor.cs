using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomEditor(typeof(AudioPlayer), true), CanEditMultipleObjects]
    public sealed class AudioPlayerEditor : UnityEditor.Editor
    {
        internal static Type[] ReferenceTypes => AudioReferenceDrawer.ReferenceTypes;

        internal static void SetReferenceType(SerializedObject serialized, Type type)
        {
            if (type != null && !ReferenceTypes.Contains(type))
                throw new ArgumentException("Select a serializable IAudioReference class with a public parameterless constructor.", nameof(type));
            Undo.SetCurrentGroupName("Change Audio Reference Type");
            foreach (var target in serialized.targetObjects)
            {
                using var individual = new SerializedObject(target);
                individual.FindProperty("reference").managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
                individual.ApplyModifiedProperties();
            }
            serialized.Update();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var property = serializedObject.GetIterator();
            if (property.NextVisible(true)) do
            {
                if (property.propertyPath == "reference")
                {
                    root.Add(new PropertyField(serializedObject.FindProperty("reference")));
                    continue;
                }
                if (property.propertyPath == "engineKey")
                {
                    root.Add(EngineKeyEditor(property.Copy()));
                    continue;
                }
                var field = new PropertyField(property.Copy());
                if (property.propertyPath == "m_Script") field.SetEnabled(false);
                root.Add(field);
            } while (property.NextVisible(false));
            return root;
        }

        private VisualElement EngineKeyEditor(SerializedProperty key)
        {
            var root = new VisualElement();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var picker = new DropdownField("Engine") { name = "engine-key" };
            picker.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            picker.style.flexGrow = 1;
            var add = new Button(() =>
            {
                var context = PlayerSettings.GetPreloadedAssets().OfType<OrbitContext>().Single();
                var newKey = AddEngine(context);
                serializedObject.FindProperty(key.propertyPath).stringValue = newKey;
                serializedObject.ApplyModifiedProperties();
                Selection.activeObject = context;
                EditorGUIUtility.PingObject(context);
            }) { text = "+", name = "add-engine", tooltip = "Add an Engine to the preloaded OrbitContext and open its settings." };
            add.style.flexShrink = 0;
            var warning = new HelpBox("", HelpBoxMessageType.Error);
            row.Add(picker); row.Add(add);
            root.Add(row); root.Add(warning);
            void Refresh()
            {
                if (serializedObject.targetObject == null) return;
                var current = serializedObject.FindProperty(key.propertyPath);
                var contexts = PlayerSettings.GetPreloadedAssets().OfType<OrbitContext>().ToArray();
                var choices = contexts.Length == 1 ? contexts[0].Definitions.Where(definition => definition != null &&
                    !string.IsNullOrWhiteSpace(definition.Key)).Select(definition => definition.Key).Distinct(StringComparer.Ordinal).ToList()
                    : new System.Collections.Generic.List<string>();
                var valid = choices.Contains(current.stringValue);
                if (!valid) choices.Insert(0, current.stringValue);
                picker.choices = choices;
                picker.SetValueWithoutNotify(current.stringValue);
                picker.showMixedValue = current.hasMultipleDifferentValues;
                picker.SetEnabled(contexts.Length == 1);
                add.SetEnabled(contexts.Length == 1 && !EditorApplication.isPlayingOrWillChangePlaymode);
                warning.text = contexts.Length != 1 ? "Assign exactly one OrbitContext in Preloaded Assets."
                    : "Engine key '" + current.stringValue + "' is not defined in the preloaded OrbitContext.";
                warning.style.display = contexts.Length == 1 && valid ? DisplayStyle.None : DisplayStyle.Flex;
            }
            picker.RegisterValueChangedCallback(evt =>
            {
                var current = serializedObject.FindProperty(key.propertyPath);
                current.stringValue = evt.newValue;
                serializedObject.ApplyModifiedProperties();
                Refresh();
            });
            root.TrackPropertyValue(key, _ => Refresh());
            root.schedule.Execute(Refresh).Every(500);
            Refresh(); return root;
        }

        internal static string AddEngine(OrbitContext context)
        {
            using var data = new SerializedObject(context);
            var engines = data.FindProperty("definitions");
            var keys = context.Definitions.Where(item => item != null).Select(item => item.Key).ToArray();
            var key = "NewEngine";
            for (var suffix = 2; keys.Contains(key, StringComparer.Ordinal); suffix++) key = "NewEngine" + suffix;
            var index = engines.arraySize;
            engines.InsertArrayElementAtIndex(index);
            var definition = engines.GetArrayElementAtIndex(index);
            definition.FindPropertyRelative("Key").stringValue = key;
            definition.FindPropertyRelative("Settings").objectReferenceValue = null;
            definition.FindPropertyRelative("Libraries").arraySize = 0;
            data.ApplyModifiedProperties();
            return key;
        }

    }
}
