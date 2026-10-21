#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AudioOrbit
{
    public sealed partial class OrbitContext
    {
        private bool IsPreloadedEditorContext()
        {
            var contexts = PlayerSettings.GetPreloadedAssets().OfType<OrbitContext>().ToArray();
            if (!contexts.Contains(this)) return false;
            if (contexts.Length == 1) return true;
            Debug.LogError("Assign only one OrbitContext in Preloaded Assets.", this);
            return false;
        }
        internal static void ResolveEditorContext()
        {
            var contexts = PlayerSettings.GetPreloadedAssets().OfType<OrbitContext>().ToArray();
            if (contexts.Length > 1) throw new InvalidOperationException("Assign only one OrbitContext in Preloaded Assets.");
            var selected = contexts.FirstOrDefault();
            if (instance == selected) return;
            if (instance != null) instance.ReleaseEngines();
            instance = selected;
        }
        [NonSerialized] private Scene previewScene;
        [NonSerialized] private double lastPreviewUpdate;
        private void ConfigureEditorEngine(AudioEngine engine)
        {
            if (!previewScene.IsValid())
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                var listener = new GameObject("AudioOrbit Context Preview Listener") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(listener, previewScene);
                listener.AddComponent<AudioListener>();
                lastPreviewUpdate = EditorApplication.timeSinceStartup;
                EditorApplication.update += UpdatePreview;
            }
            engine.ConfigureEditorPreview(previewScene);
        }
        private void UpdatePreview()
        {
            if (Application.isPlaying) return;
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - lastPreviewUpdate);
            lastPreviewUpdate = now;
            if (engines != null) foreach (var engine in engines.Values) engine.Tick(delta, true);
        }
        private void ReleaseEditorPreview()
        {
            EditorApplication.update -= UpdatePreview;
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            previewScene = default;
        }
        [InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ResetEngines;
            AssemblyReloadEvents.beforeAssemblyReload += ResetEngines;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) ResetEngines();
        }
    }
}
#endif
