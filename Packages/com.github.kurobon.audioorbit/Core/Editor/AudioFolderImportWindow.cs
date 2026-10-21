using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    public sealed class AudioFolderImportWindow : EditorWindow
    {
        [SerializeField] private AudioLibrary library;
        private Action imported;

        internal static void ShowFor(AudioLibrary library, Action imported)
        {
            if (library == null) return;
            var window = CreateInstance<AudioFolderImportWindow>();
            window.library = library;
            window.imported = imported;
            window.titleContent = new GUIContent("Import Audio Folder");
            window.minSize = new Vector2(420, 235);
            window.ShowUtility();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.audioorbit.core/Editor/AudioLibraryStyles.uss");
            if (styles != null) root.styleSheets.Add(styles);
            root.AddToClassList("audioorbit-import");
            var target = new ObjectField("Library") { objectType = typeof(AudioLibrary), value = library };
            target.SetEnabled(false); root.Add(target);
            var folder = new ObjectField("Folder") { objectType = typeof(DefaultAsset), allowSceneObjects = false };
            root.Add(folder);
            var recursive = new Toggle("Include Subfolders") { value = true };
            root.Add(recursive);
            var category = new ObjectField("Category")
            {
                objectType = typeof(AudioCategory), allowSceneObjects = false,
                value = library?.Defaults.Category
            };
            root.Add(category);
            root.Add(new HelpBox("Choose a Project folder. Each AudioClip becomes one Audio. Existing clips are skipped. Changes support Undo; use Save in Library Manager to save the Library.", HelpBoxMessageType.Info));
            var status = new Label();
            root.Add(status);
            var import = new Button { text = "Import" };
            root.Add(import);
            AudioClip[] clips = Array.Empty<AudioClip>();
            void Refresh()
            {
                var path = AssetDatabase.GetAssetPath(folder.value);
                var valid = library != null && AssetDatabase.IsValidFolder(path);
                clips = valid ? AudioFolderImporter.FindClips(path, recursive.value) : Array.Empty<AudioClip>();
                status.text = !valid ? "Select a project folder." : clips.Length + " AudioClips found.";
                import.SetEnabled(valid && clips.Length > 0);
            }
            folder.RegisterValueChangedCallback(_ => Refresh());
            recursive.RegisterValueChangedCallback(_ => Refresh());
            import.clicked += () =>
            {
                Refresh();
                if (!import.enabledSelf) return;
                var result = AudioFolderImporter.Import(library, clips, category.value as AudioCategory);
                imported?.Invoke();
                status.text = $"Imported {result.Added} Audio Entries. Skipped {result.Skipped} existing clips.";
            };
            var selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(selected)) folder.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<DefaultAsset>(selected));
            Refresh();
        }
    }
}
