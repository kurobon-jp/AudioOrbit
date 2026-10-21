using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomPropertyDrawer(typeof(DirectAudioReference))]
    public sealed class DirectAudioReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            AudioReferenceDrawer.AddTypePicker(root, property);
            root.Add(ClipDefinitionDrawer.CreateClipGUI(property.FindPropertyRelative("Clip"), false));
            root.Add(new PropertyField(property.FindPropertyRelative("Loop")));
            return root;
        }
    }
}
