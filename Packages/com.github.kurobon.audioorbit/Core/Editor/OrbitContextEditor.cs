using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomEditor(typeof(OrbitContext)), CanEditMultipleObjects]
    public sealed class OrbitContextEditor : UnityEditor.Editor
    {
        internal static void AssignContext(OrbitContext context)
        {
            if (context == null || !EditorUtility.IsPersistent(context))
                throw new System.ArgumentException("Select a saved OrbitContext asset.", nameof(context));
            PlayerSettings.SetPreloadedAssets(PlayerSettings.GetPreloadedAssets()
                .Where(asset => !(asset is OrbitContext)).Concat(new UnityEngine.Object[] { context }).ToArray());
            OrbitContext.ResolveEditorContext();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var script = new PropertyField(serializedObject.FindProperty("m_Script")); script.SetEnabled(false); root.Add(script);
            root.Add(new PropertyField(serializedObject.FindProperty("definitions"), "Engines"));
            var validation = new HelpBox("", HelpBoxMessageType.Error); root.Add(validation);
            var preloadRow = new VisualElement { name = "preload-assignment" };
            preloadRow.style.flexDirection = FlexDirection.Row;
            var preload = new HelpBox("Assign this OrbitContext in Project Settings > Player > Preloaded Assets. Use only one OrbitContext.", HelpBoxMessageType.Warning);
            preload.style.flexGrow = 1;
            preloadRow.Add(preload);
            var assign = new Button { text = "Assign", name = "assign-context", tooltip = "Use this Context in Preloaded Assets. Other preloaded asset types are preserved." };
            assign.style.alignSelf = Align.Center;
            assign.style.flexShrink = 0;
            preloadRow.Add(assign);
            root.Add(preloadRow);
            void Refresh()
            {
                if (target == null) return;
                var errors = targets.OfType<OrbitContext>().SelectMany(context => context.ValidationErrors()).Distinct().ToArray();
                validation.text = string.Join("\n", errors);
                validation.style.display = errors.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                var contexts = PlayerSettings.GetPreloadedAssets().OfType<OrbitContext>().ToArray();
                preloadRow.style.display = contexts.Length == 1 && targets.All(item => item == contexts[0]) ? DisplayStyle.None : DisplayStyle.Flex;
                assign.SetEnabled(targets.Length == 1 && EditorUtility.IsPersistent(target) && !EditorApplication.isPlayingOrWillChangePlaymode);
            }
            assign.clicked += () => { AssignContext((OrbitContext)target); Refresh(); };
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.schedule.Execute(Refresh).Every(500);
            Refresh(); return root;
        }
    }
}
