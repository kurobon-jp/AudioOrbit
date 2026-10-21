#if ENABLE_ADDRESSABLES
using UnityEditor;
using UnityEngine.UIElements;

namespace AudioOrbit.Addressables.Editor
{
    [CustomPropertyDrawer(typeof(AddressablesLibraryReference))]
    public sealed class AddressablesLibraryReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) =>
            AddressablesAudioReferenceDrawer.CreateGUI(property, true);
    }
}
#endif
