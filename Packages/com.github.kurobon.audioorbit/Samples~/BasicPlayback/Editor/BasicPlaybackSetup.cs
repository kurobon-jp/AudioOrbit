#if ENABLE_ADDRESSABLES
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AudioOrbit.Samples.BasicPlayback.Editor
{
    public sealed class BasicPlaybackSetup : ScriptableObject
    {
        [MenuItem("Tools/AudioOrbit Samples/Rebuild Basic Playback Demo")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var helper = CreateInstance<BasicPlaybackSetup>();
            var script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(helper)); DestroyImmediate(helper);
            var sample = Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
            var folder = sample + "/Demo";
            var scenePath = folder + "/BasicPlayback.unity";
            var existing = SceneManager.GetSceneByPath(scenePath);
            if (existing.IsValid() && existing.isLoaded && existing.isDirty)
                throw new InvalidOperationException("Save the open BasicPlayback scene before rebuilding the sample.");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(sample, "Demo");
            var addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var settings = GetOrCreate<AudioEngineSettings>(folder, "EngineSettings");
            settings.InitialPoolSize = 4; settings.MaxPoolSize = settings.MaxPlaybacks = 16;
            var kinds = new[] { "SE", "VOICE", "BGM" };
            var names = new[] { new[] { "battle_charge", "battle_cutin", "battle_enemy" },
                new[] { "1100101_1", "1100101_2", "1100101_3" }, new[] { "BGMIngame", "IntroLoopSample" } };
            var libraries = new AudioLibrary[3]; var sounds = new AudioDefinition[3][];
            for (int k = 0; k < 3; k++)
            {
                libraries[k] = GetOrCreate<AudioLibrary>(folder, k == 1 ? "VoiceLibrary" : kinds[k] + "Library");
                sounds[k] = names[k].Select(n => EnsureSound(libraries[k], sample + "/" + kinds[k] + "/" + n + (k < 2 ? ".mp3" : ".wav"), kinds[k])).ToArray();
                var group = addressables.FindGroup("AudioOrbit Samples " + kinds[k]) ?? addressables.CreateGroup("AudioOrbit Samples " + kinds[k], false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var entry = addressables.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(libraries[k])), group);
                entry.address = "audioorbit/samples/" + kinds[k].ToLowerInvariant() + "/library";
                EditorUtility.SetDirty(libraries[k]);
            }
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(addressables);
            var original = SceneManager.GetActiveScene(); var keepOpen = existing.IsValid() && existing.isLoaded;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (keepOpen) EditorSceneManager.CloseScene(existing, true);
                var root = new GameObject("AudioOrbit Basic Playback");
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.transform.SetParent(root.transform);
                camera.tag = "MainCamera"; camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
                camera.GetComponent<Camera>().backgroundColor = new Color(0.055f, 0.07f, 0.1f);
                BasicPlaybackUiSetup.Build(root, settings, libraries, sounds);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/BasicPlayback.prefab");
                DestroyImmediate(root); PrefabUtility.InstantiatePrefab(prefab, scene);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                if (!keepOpen) EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
            AssetDatabase.SaveAssetIfDirty(settings);
            foreach (var library in libraries) AssetDatabase.SaveAssetIfDirty(library);
            AssetDatabase.SaveAssetIfDirty(addressables);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            Debug.Log("Basic Playback demo rebuilt: " + scenePath);
        }
        private static AudioDefinition EnsureSound(AudioLibrary library, string path, string kind)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new InvalidOperationException("Missing sample audio: " + path);
            var found = library.Entries.FirstOrDefault(s => s?.Clips != null && s.Clips.Count == 1 && s.Clips[0]?.Clip == clip);
            if (found != null) return found;
            var used = new HashSet<string>(AssetDatabase.FindAssets("t:AudioLibrary").Select(AssetDatabase.GUIDToAssetPath)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AudioLibrary>().SelectMany(l => l.Entries).Where(s => s != null).Select(s => s.Key));
            var basis = ("sample_" + kind.ToLowerInvariant() + "_" + clip.name).Replace('/', '_');
            var key = basis; for (int i = 2; used.Contains(key); i++) key = basis + "_" + i;
            var sound = new AudioDefinition { Key = key, DisplayName = clip.name, Loop = kind == "BGM", Volume = kind == "BGM" ? 0.35f : 0.8f };
            sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound); return sound;
        }
        private static T GetOrCreate<T>(string folder, string name) where T : ScriptableObject
        {
            var path = folder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset;
            asset = CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}

#endif