using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AudioOrbit.Editor.Tests")]

namespace AudioOrbit.Editor
{
    internal sealed class AudioPreview : IDisposable
    {
        private AudioEngine engine;
        private AudioLibrary snapshot;
        private AudioEngineSettings settings;
        private Scene scene;
        private string id;
        private string definition;
        private PlaybackHandle playback;
        private AudioDefinition liveSound;
        private string liveDefinition, liveResource;
        private float volumeRoll, pitchRoll;
        private float volumeRange, pitchRange;
        private bool updating;
        private double lastUpdate;
        private bool reportedStart;
        private PlaybackState observedState = PlaybackState.Ended;
        private string observedError;
        internal int ClipIndex { get; private set; } = -1;
        internal PlaybackState State => playback?.State ?? PlaybackState.Ended;
        internal string Error { get; private set; }
        internal float Duration { get; private set; }
        internal float PositionSeconds { get; private set; }
        internal bool CanSeek => playback?.Playback != null &&
            (
#if UNITY_6000_3_OR_NEWER
                playback.Playback.Generator != null ||
#endif
                playback.Playback.Source?.clip != null);
        internal event Action Changed;

        internal AudioPreview()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }
        internal void Play(AudioDefinition sound, int? clipDefinition = null)
        {
            updating = true;
            Stop();
            Error = null;
            ClipIndex = -1;
            Duration = PositionSeconds = 0;
            reportedStart = false;
            try
            {
                var json = JsonUtility.ToJson(sound);
                if (engine == null || id != sound.Key || definition != json)
                {
                    ReleaseEngine();
                    scene = EditorSceneManager.NewPreviewScene();
                    var listener = new GameObject("AudioOrbit Preview Listener") { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(listener, scene);
                    listener.AddComponent<AudioListener>();
                    settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
                    settings.hideFlags = HideFlags.HideAndDontSave;
                    settings.InitialPoolSize = settings.MaxPoolSize = settings.MaxPlaybacks = 1;
                    snapshot = ScriptableObject.CreateInstance<AudioLibrary>();
                    snapshot.hideFlags = HideFlags.HideAndDontSave;
                    snapshot.Entries.Add(sound);
                    engine = new AudioEngine(settings);
                    engine.ConfigureEditorPreview(scene);
                    engine.RegisterLibrary(snapshot);
                    playback = engine.CreatePlayback(sound.Key);
                    id = sound.Key;
                    definition = json;
                }
                var options = PlayOptions.TwoD();
                if (clipDefinition.HasValue) options.ClipIndex = clipDefinition.Value;
                lastUpdate = EditorApplication.timeSinceStartup;
                playback.Play(options);
                Observe();
                liveSound = sound;
                liveDefinition = Fingerprint(sound);
                liveResource = ResourceFingerprint(sound);
                var active = playback.Playback;
                if (active != null && ClipIndex >= 0)
                {
                    var clip = sound.Clips[ClipIndex];
                    var lower = Mathf.Clamp01(clip.Volume - clip.VolumeRandomRange);
                    var upper = Mathf.Clamp01(clip.Volume + clip.VolumeRandomRange);
                    volumeRoll = sound.Volume > 0 && upper > lower
                        ? Mathf.InverseLerp(lower, upper, active.BaseVolume / sound.Volume) : 0.5f;
                    pitchRoll = clip.PitchRandomRange > 0
                        ? Mathf.InverseLerp(clip.Pitch - clip.PitchRandomRange, clip.Pitch + clip.PitchRandomRange, active.Source.pitch)
                        : 0.5f;
                    volumeRange = clip.VolumeRandomRange; pitchRange = clip.PitchRandomRange;
                }
            }
            catch (Exception exception) { Error = exception.Message; Changed?.Invoke(); }
            finally { updating = false; }
        }
        internal void Stop() { CapturePosition(); if (playback != null) playback.Stop(); Changed?.Invoke(); }
        internal bool Seek(float fraction)
        {
            if (!CanSeek || float.IsNaN(fraction) || float.IsInfinity(fraction)) return false;
            fraction = Mathf.Clamp01(fraction);
            var active = playback.Playback;
#if UNITY_6000_3_OR_NEWER
            if (active.Generator != null)
            {
                if (!active.Generator.Seek(Mathf.RoundToInt(fraction * (active.Generator.Frames - 1)))) return false;
            }
            else
#endif
                active.Source.timeSamples = Mathf.RoundToInt(fraction * (active.Source.clip.samples - 1));
            CapturePosition(); Changed?.Invoke(); return true;
        }
        private void CapturePosition()
        {
            var active = playback?.Playback;
#if UNITY_6000_3_OR_NEWER
            if (active?.Generator != null)
            {
                Duration = (float)active.Generator.Frames / active.Generator.SampleRate;
                PositionSeconds = (float)active.Generator.PositionFrames / active.Generator.SampleRate;
            }
            else
#endif
            if (active?.Source?.clip != null)
            {
                Duration = active.Source.clip.length;
                PositionSeconds = (float)active.Source.timeSamples / active.Source.clip.frequency;
            }
        }
        internal void Pause() { playback?.Pause(); Changed?.Invoke(); }
        internal void Resume() { playback?.Resume(); Changed?.Invoke(); }
        private string Fingerprint(AudioDefinition sound) => JsonUtility.ToJson(sound) +
            (sound.Category != null ? JsonUtility.ToJson(sound.Category) : "") +
            (sound.SpatialProfile != null ? JsonUtility.ToJson(sound.SpatialProfile) : "") + ResourceFingerprint(sound);
        private string ResourceFingerprint(AudioDefinition sound)
        {
            if (sound.Clips == null || ClipIndex < 0 || ClipIndex >= sound.Clips.Count) return "";
            var asset = sound.Clips[ClipIndex]?.Resource;
            if (asset == null) return "";
#if UNITY_6000_3_OR_NEWER
            if (asset is LoopRegionClip region)
                return $"{asset.GetInstanceID()}/{(region.Clip != null ? region.Clip.GetInstanceID() : 0)}/{region.ReferenceSampleRate}/{region.LoopStartFrame}/{region.LoopEndFrameExclusive}";
#endif
            return asset.GetInstanceID().ToString();
        }
        internal void ApplyChanges(AudioDefinition sound)
        {
            liveSound = sound;
            if (updating || sound == null || playback?.Playback == null) return;
            var fingerprint = Fingerprint(sound);
            if (liveDefinition == fingerprint) return;
            liveDefinition = fingerprint;
            updating = true;
            try
            {
                var resource = ResourceFingerprint(sound);
                if (resource != liveResource)
                {
                    var paused = State == PlaybackState.Paused;
                    var fraction = Duration > 0 ? PositionSeconds / Duration : 0;
                    var index = ClipIndex;
                    Play(sound, index);
                    if (paused) Pause();
                    if (fraction > 0) Seek(fraction);
                    return;
                }
                if (sound.Clips == null || ClipIndex < 0 || ClipIndex >= sound.Clips.Count) return;
                var clip = sound.Clips[ClipIndex];
                AudioEngine.ValidateNonNegative(clip.VolumeRandomRange, "VolumeRandomRange");
                AudioEngine.ValidateNonNegative(clip.PitchRandomRange, "PitchRandomRange");
                if (volumeRange != clip.VolumeRandomRange) volumeRoll = UnityEngine.Random.value;
                if (pitchRange != clip.PitchRandomRange) pitchRoll = UnityEngine.Random.value;
                volumeRange = clip.VolumeRandomRange; pitchRange = clip.PitchRandomRange;
                var volume = Mathf.Lerp(Mathf.Clamp01(clip.Volume - clip.VolumeRandomRange),
                    Mathf.Clamp01(clip.Volume + clip.VolumeRandomRange), volumeRoll);
                var pitch = Mathf.Lerp(clip.Pitch - clip.PitchRandomRange, clip.Pitch + clip.PitchRandomRange, pitchRoll);
                engine.UpdateEditorPreview(playback.Request, sound, volume, pitch);
                Error = null;
                Changed?.Invoke();
            }
            catch (Exception exception) { Error = exception.Message; Changed?.Invoke(); }
            finally { updating = false; }
        }
        private void Update()
        {
            if (engine == null || playback == null) return;
            ApplyChanges(liveSound);
            if (engine == null || playback == null) return;
            var now = EditorApplication.timeSinceStartup;
            CapturePosition();
            engine.Tick((float)(now - lastUpdate), true);
            lastUpdate = now;
            Observe();
        }
        private void Observe()
        {
            if (playback == null) return;
            CapturePosition();
            var changed = false;
            if (!reportedStart && playback.Started)
            {
                ClipIndex = playback.ClipIndex;
                reportedStart = true;
                changed = true;
            }
            if (playback.IsCompleted)
            {
                var result = playback.WaitForCompletionAsync().GetAwaiter().GetResult();
                if (result.EndReason == PlaybackEndReason.Completed) PositionSeconds = Duration;
                if (!result.Started) Error = result.Error?.Message ?? result.RejectionReason.ToString();
            }
            if (changed || State != observedState || Error != observedError)
            {
                observedState = State;
                observedError = Error;
                Changed?.Invoke();
            }
        }
        private void OnPlayModeChanged(PlayModeStateChange state) => ReleaseEngine();
        private void ReleaseEngine()
        {
            playback?.Dispose();
            engine?.Dispose(); engine = null; playback = null;
            if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
        }
        public void Dispose()
        {
            EditorApplication.update -= Update;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            ReleaseEngine();
        }
    }
}
