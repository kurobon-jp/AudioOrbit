using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    [CustomEditor(typeof(AudioLibrary)), CanEditMultipleObjects]
    public sealed class AudioLibraryEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            foreach (var asset in targets) AudioIdentityPostprocessor.EnsureIdentity(asset);
            var root = new VisualElement();
            if (targets.Length == 1)
                root.Add(new Button(() => AudioLibraryWindow.ShowLibrary((AudioLibrary)target))
                    { text = "Open Library Manager" });
            root.Add(new HelpBox(
#if UNITY_6000_3_OR_NEWER
                "Edit Audio Entries and assign an AudioClip or LoopRegionClip to each Clip. Load and register the Library to play its clips directly.",
#else
                "Edit Audio Entries and assign an AudioClip to each Clip. Load and register the Library to play its clips directly.",
#endif
                HelpBoxMessageType.Info));
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            var counts = new System.Collections.Generic.Dictionary<AudioLibrary, int>();
            foreach (AudioLibrary library in targets) counts[library] = library.Entries.Count;
            root.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                foreach (AudioLibrary library in targets)
                    try
                    {
                        InitializeAddedEntries(library, counts[library]);
                        counts[library] = library.Entries.Count;
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogError(e.Message, library);
                    }
            });
            return root;
        }

        internal static void InitializeAddedEntries(AudioLibrary library, int previousCount)
        {
            if (library.Entries.Count <= previousCount) return;
            Undo.RecordObject(library, "Initialize New Audio");
            for (int i = previousCount; i < library.Entries.Count; i++)
            {
                var sound = library.Entries[i] ?? (library.Entries[i] = new AudioDefinition());
                if (sound.Clips != null && sound.Clips.Count > 0 ||
                    !string.IsNullOrWhiteSpace(sound.Key) && sound.Key != "new_audio" ||
                    !string.IsNullOrWhiteSpace(sound.DisplayName) && sound.DisplayName != "New Audio") continue;
                library.Defaults.ApplyTo(sound);
                var id = AudioKeyAuthoring.Unique(library, "new_audio", sound);
                sound.Key = id;
                sound.DisplayName = "New Audio";
            }

            EditorUtility.SetDirty(library);
        }
    }
}
