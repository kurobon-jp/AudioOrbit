using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(IAudioReference), true)]
    public sealed class AudioReferenceDrawer : PropertyDrawer
    {
        internal static Type[] ReferenceTypes => AudioLibraryReferenceDrawer.GetReferenceTypes(typeof(IAudioReference));

        public override VisualElement CreatePropertyGUI(SerializedProperty property) =>
            AudioLibraryReferenceDrawer.CreateGUI(property, typeof(IAudioReference));

        public static void AddTypePicker(VisualElement root, SerializedProperty property) =>
            AudioLibraryReferenceDrawer.AddTypePicker(root, property, typeof(IAudioReference));
    }
}
