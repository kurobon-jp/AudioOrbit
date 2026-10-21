#if UNITY_6000_3_OR_NEWER
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomEditor(typeof(LoopRegionClip))]
    public sealed class LoopRegionClipEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            var warning = new HelpBox("", HelpBoxMessageType.Error);
            Button assign = null;
            assign = new Button(() =>
            {
                var region = (LoopRegionClip)target;
                if (region.Clip == null) return;
                Undo.RecordObject(region, "Set reference sample rate");
                region.ReferenceSampleRate = region.Clip.frequency;
                EditorUtility.SetDirty(region);
                serializedObject.Update();
                Refresh();
            }) { text = "Use Current Clip Sample Rate", tooltip = "Use only if the saved frame positions were authored at the current Clip sample rate." };
            root.Add(warning);
            root.Add(assign);
            void Refresh()
            {
                var region = (LoopRegionClip)target;
                assign.SetEnabled(region.Clip != null);
                try
                {
                    region.GetRuntimeLoopFrames(out var start, out var end);
                    warning.style.display = DisplayStyle.None;
                    assign.tooltip = $"Authored: {region.ReferenceSampleRate} Hz. Current: {region.Clip.frequency} Hz. Runtime loop: [{start}, {end}).";
                }
                catch (ArgumentException error)
                {
                    warning.text = error.Message + " For existing assets, confirm the original authoring sample rate before setting it.";
                    warning.style.display = DisplayStyle.Flex;
                }
            }
            root.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                var region = (LoopRegionClip)target;
                if (region.Clip != null && region.ReferenceSampleRate == 0 &&
                    region.LoopStartFrame == 0 && region.LoopEndFrameExclusive == 0)
                {
                    Undo.RecordObject(region, "Initialize reference sample rate");
                    region.ReferenceSampleRate = region.Clip.frequency;
                    EditorUtility.SetDirty(region);
                    serializedObject.Update();
                }
                Refresh();
            });
            Refresh();
            return root;
        }
    }
}
#endif
