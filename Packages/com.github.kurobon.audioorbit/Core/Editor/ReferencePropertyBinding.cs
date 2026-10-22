using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    public sealed class ReferencePropertyBinding
    {
        private readonly SerializedObject data;
        private readonly string path, type;
        private readonly long? referenceId;

        public ReferencePropertyBinding(SerializedProperty property)
        {
            data = property.serializedObject;
            path = property.propertyPath;
            type = property.type;
            if (property.propertyType == SerializedPropertyType.ManagedReference && !property.hasMultipleDifferentValues)
                referenceId = property.managedReferenceId;
        }

        public bool TryGet(out SerializedProperty property)
        {
            property = null;
            try
            {
                if (data.targetObject == null) return false;
                property = data.FindProperty(path);
                return property != null && property.type == type &&
                    (!referenceId.HasValue || (property.propertyType == SerializedPropertyType.ManagedReference &&
                        !property.hasMultipleDifferentValues && property.managedReferenceId == referenceId.Value));
            }
            catch (ObjectDisposedException) { return false; }
            catch (NullReferenceException) { return false; }
        }

        public static void Track(VisualElement root, SerializedObject data, Action refresh)
        {
            root.TrackSerializedObjectValue(data, _ => refresh());
        }
    }
}
