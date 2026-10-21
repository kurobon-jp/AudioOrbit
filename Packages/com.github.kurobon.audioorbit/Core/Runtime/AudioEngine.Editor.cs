#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private System.Func<double> editorTime;
        internal void UpdateEditorPreview(PlaybackRequest handle, AudioDefinition sound, float clipVolume, float pitch)
        {
            var active = handle.Playback;
            if (active == null) return;
            ValidateNonNegative(sound.Volume, "Volume");
            ValidateProfile(sound.SpatialProfile);
            ValidateNonNegative(sound.Category != null ? sound.Category.Volume : 1, "Category Volume");
            handle.SetPitch(pitch);
            var category = sound.Category;
            active.Category = category != null ? category.Id : default;
            categories[active.Category] = new CategoryState
            {
                Volume = category != null ? category.Volume : 1,
                Muted = category != null && category.Muted
            };
            active.BaseVolume = sound.Volume * clipVolume;
            active.Source.outputAudioMixerGroup = category != null ? category.MixerOutput : null;
            active.Source.ignoreListenerPause = category != null && category.IgnoreListenerPause;
            active.Source.priority = 255 - Mathf.Clamp(category != null ? category.Priority : 0, 0, 255);
            if (sound.SpatialProfile != null) ApplyProfile(active.Source, new SpatialSettings(sound.SpatialProfile));
            else
            {
                active.Source.minDistance = 1; active.Source.maxDistance = 500;
                active.Source.spread = 0; active.Source.dopplerLevel = 1;
                active.Source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            active.Source.spatialBlend = 0;
            handle.Loop = sound.Loop;
            ApplyVolume(active);
        }
        internal void ConfigureEditorPreview(Scene scene)
        {
            editorTime = () => UnityEditor.EditorApplication.timeSinceStartup;
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, scene);
        }
    }
}
#endif
