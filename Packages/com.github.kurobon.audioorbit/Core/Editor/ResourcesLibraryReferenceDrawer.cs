using UnityEditor;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(ResourcesLibraryReference))]
    public sealed class ResourcesLibraryReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) =>
            ResourcesAudioReferenceDrawer.CreateGUI(property, true);
    }
}
