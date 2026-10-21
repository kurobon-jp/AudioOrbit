using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(IAudioLibraryReference), true)]
    public sealed class AudioLibraryReferenceDrawer : PropertyDrawer
    {
        public static string DisplayName(SerializedProperty property)
        {
            var index = AudioKeyAuthoring.Index(property);
            return index >= 0 ? "Element " + index : property.displayName;
        }

        internal static Type[] ReferenceTypes => GetReferenceTypes(typeof(IAudioLibraryReference));

        internal static Type[] GetReferenceTypes(Type referenceType) => TypeCache.GetTypesDerivedFrom(referenceType)
            .Where(type => type.IsClass && type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters &&
                type.IsSerializable && !typeof(UnityEngine.Object).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

        public override VisualElement CreatePropertyGUI(SerializedProperty property) =>
            CreateGUI(property, typeof(IAudioLibraryReference));

        internal static VisualElement CreateGUI(SerializedProperty property, Type referenceType)
        {
            var root = new VisualElement();
            AddTypePicker(root, property, referenceType);
            if (property.hasMultipleDifferentValues)
                root.Add(new HelpBox("Select one object to edit the reference, or choose a type to replace all selected references.", HelpBoxMessageType.Info));
            else if (property.managedReferenceValue != null)
            {
                var child = property.Copy();
                var end = property.GetEndProperty();
                if (child.NextVisible(true))
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end)) break;
                        root.Add(new PropertyField(child.Copy()));
                    } while (child.NextVisible(false));
            }
            return root;
        }

        public static void AddTypePicker(VisualElement root, SerializedProperty property, Type referenceType = null)
        {
            referenceType ??= typeof(IAudioLibraryReference);
            if (property.propertyType != SerializedPropertyType.ManagedReference ||
                !property.managedReferenceFieldTypename.EndsWith(" " + referenceType.FullName, StringComparison.Ordinal)) return;
            var data = property.serializedObject;
            var propertyPath = property.propertyPath;
            var binding = new ReferencePropertyBinding(property);
            var types = GetReferenceTypes(referenceType);
            var names = types.Select(type => types.Count(other => other.Name == type.Name) > 1
                ? type.FullName : ObjectNames.NicifyVariableName(type.Name)).ToList();
            names.Insert(0, "None");
            var picker = new DropdownField("Reference Type", names, 0) { name = referenceType == typeof(IAudioReference) ? "reference-type" : "library-reference-type" };
            void Refresh()
            {
                if (!binding.TryGet(out var current)) { picker.SetEnabled(false); return; }
                picker.showMixedValue = current.hasMultipleDifferentValues;
                var type = current.hasMultipleDifferentValues ? null : current.managedReferenceValue?.GetType();
                var index = Array.IndexOf(types, type) + 1;
                picker.SetValueWithoutNotify(index >= 0 && index < names.Count ? names[index] : "None");
            }
            picker.RegisterValueChangedCallback(evt =>
            {
                if (!binding.TryGet(out _)) return;
                var index = names.IndexOf(evt.newValue);
                if (index < 0) return;
                Undo.SetCurrentGroupName(referenceType == typeof(IAudioReference) ? "Change Audio reference type" : "Change Library reference type");
                foreach (var target in data.targetObjects)
                {
                    using var individual = new SerializedObject(target);
                    var current = individual.FindProperty(propertyPath);
                    if (current == null) continue;
                    current.managedReferenceValue = index == 0 ? null : Activator.CreateInstance(types[index - 1]);
                    individual.ApplyModifiedProperties();
                }
                data.Update();
                var updated = data.FindProperty(propertyPath);
                if (updated != null) binding = new ReferencePropertyBinding(updated);
                Refresh();
            });
            root.Add(picker);
            ReferencePropertyBinding.Track(root, data, Refresh);
            Refresh();
        }
    }
}
