using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioOrbit.Tests
{
    public sealed class OrbitContextTests
    {
        private readonly List<UnityEngine.Object> objects = new();
        private OrbitContext context;
        private AudioEngineSettings settings;
        private AudioLibrary library;
        private static void Set(object obj, string field, object value) => obj.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
        private T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); objects.Add(asset); return asset; }
        [SetUp] public void Setup()
        {
            typeof(OrbitContext).GetMethod("ResetEngines", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            settings = Asset<AudioEngineSettings>(); settings.InitialPoolSize = 0;
            var clip = AudioClip.Create("context test", 44100, 1, 44100, false); objects.Add(clip);
            library = Asset<AudioLibrary>();
            library.Entries.Add(new AudioDefinition { Key = "context_test", Loop = true,
                Clips = new List<ClipDefinition> { new() { Clip = clip } } });
            context = Asset<OrbitContext>(); Set(context, "definitions", new[] { new EngineDefinition { Settings = settings, Libraries = new[] { library } } });
            typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, context);
        }
        [TearDown] public void Cleanup()
        {
            if (context != null) UnityEngine.Object.DestroyImmediate(context);
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
        }
        [Test] public void EnginesAreLazyAndKeysAreOrdinal()
        {
            Assert.False(OrbitContext.TryGet(OrbitContext.DefaultKey, out _));
            var engine = OrbitContext.Get();
            Assert.AreSame(engine, OrbitContext.Get("Default"));
            Assert.Throws<KeyNotFoundException>(() => OrbitContext.Get("default"));
            Assert.True(OrbitContext.TryGet("Default", out var found)); Assert.AreSame(engine, found);
        }
        [Test] public void EnginesHaveIndependentPlaybackAndStopRanges()
        {
            Set(context, "definitions", new[] { new EngineDefinition { Key = "SE", Settings = settings, Libraries = new[] { library } }, new EngineDefinition { Key = "BGM", Settings = settings, Libraries = new[] { library } } });
            var first = OrbitContext.Get("SE"); var second = OrbitContext.Get("BGM");
            var a = first.CreatePlayback("context_test").StartForTest(); var b = second.CreatePlayback("context_test").StartForTest();
            Assert.True(a.Started); Assert.True(b.Started);
            first.StopAll(); Assert.True(a.IsCompleted); Assert.False(b.IsCompleted);
        }
        [TestCase(null)] [TestCase("")] [TestCase(" ")]
        public void EmptyKeysAreRejected(string key) => Assert.Throws<ArgumentException>(() => OrbitContext.Get(key));
        [Test] public void UnknownKeyDoesNotCreateAnEngine()
        {
            Assert.Throws<KeyNotFoundException>(() => OrbitContext.Get("Unknown"));
            Assert.False(OrbitContext.TryGet("Unknown", out _));
        }
        [Test] public void RegisterInstanceDoesNotOverwriteAnotherContext()
        {
            var engine = OrbitContext.Get(); var playback = engine.CreatePlayback("context_test").StartForTest();
#if !UNITY_EDITOR
            LogAssert.Expect(LogType.Error, "Another OrbitContext is already registered. Only one Context can be active.");
#endif
            var other = Asset<OrbitContext>();
            LogAssert.Expect(LogType.Error, "Another OrbitContext is already registered. Only one Context can be active.");
            typeof(OrbitContext).GetMethod("RegisterInstance", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(other, null);
            Assert.AreSame(engine, OrbitContext.Get()); Assert.False(playback.IsCompleted);
            UnityEngine.Object.DestroyImmediate(other);
            Assert.AreSame(engine, OrbitContext.Get()); Assert.False(playback.IsCompleted);
        }
#if UNITY_EDITOR
        [Test] public void LoadingAnUnregisteredContextDoesNotReplaceTheActiveContext()
        {
            var engine = OrbitContext.Get();
            var other = Asset<OrbitContext>();
            Assert.AreSame(engine, OrbitContext.Get());
            UnityEngine.Object.DestroyImmediate(other);
            Assert.AreSame(engine, OrbitContext.Get());
        }
        [Test] public void OnEnableRegistersOnlyTheSinglePreloadedContext()
        {
            var previous = UnityEditor.PlayerSettings.GetPreloadedAssets();
            var folder = "Assets/AudioOrbitContextRegistration_" + Guid.NewGuid().ToString("N");
            UnityEditor.AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            try
            {
                var first = Asset<OrbitContext>(); var second = Asset<OrbitContext>();
                UnityEditor.AssetDatabase.CreateAsset(first, folder + "/First.asset");
                UnityEditor.AssetDatabase.CreateAsset(second, folder + "/Second.asset");
                var onEnable = typeof(OrbitContext).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
                var singleton = typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
                UnityEditor.PlayerSettings.SetPreloadedAssets(new UnityEngine.Object[] { first });
                onEnable.Invoke(first, null);
                Assert.AreSame(first, singleton.GetValue(null));
                onEnable.Invoke(second, null);
                Assert.AreSame(first, singleton.GetValue(null));
                UnityEditor.PlayerSettings.SetPreloadedAssets(new UnityEngine.Object[] { first, second });
                LogAssert.Expect(LogType.Error, "Assign only one OrbitContext in Preloaded Assets.");
                onEnable.Invoke(second, null);
                Assert.AreSame(first, singleton.GetValue(null));
                UnityEditor.PlayerSettings.SetPreloadedAssets(new UnityEngine.Object[] { second });
                onEnable.Invoke(second, null);
                Assert.AreSame(second, singleton.GetValue(null));
            }
            finally
            {
                UnityEditor.PlayerSettings.SetPreloadedAssets(previous);
                UnityEditor.AssetDatabase.DeleteAsset(folder);
                typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, context);
            }
        }
#endif
        [Test] public void RuntimeDoesNotSearchPreloadedAssetsWhenInstanceIsMissing()
        {
            if (!Application.isPlaying) Assert.Ignore("Play Mode registration test.");
            var field = typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
            field.SetValue(null, null);
            try { Assert.Throws<InvalidOperationException>(() => OrbitContext.Get()); }
            finally { field.SetValue(null, context); }
        }
        [Test] public void DuplicateAndEmptyDefinitionKeysAreRejectedAndCanBeCorrected()
        {
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings }, new EngineDefinition { Settings = settings } });
            Assert.Throws<ArgumentException>(() => OrbitContext.Get());
            StringAssert.Contains("Duplicate", string.Join("\n", context.ValidationErrors()));
            Set(context, "definitions", new[] { new EngineDefinition { Key = " ", Settings = settings } });
            Assert.Throws<ArgumentException>(() => OrbitContext.Get());
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings } });
            Assert.NotNull(OrbitContext.Get());
        }
        [Test] public void ReplacingDefinitionsRevalidatesKeysEvenForAnExistingEngine()
        {
            var existing = OrbitContext.Get();
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings }, new EngineDefinition { Settings = settings } });
            Assert.Throws<ArgumentException>(() => OrbitContext.Get());
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings }, new EngineDefinition { Key = "Music", Settings = settings } });
            Assert.AreSame(existing, OrbitContext.Get());
            Assert.AreNotSame(existing, OrbitContext.Get("Music"));
        }

        [Test] public void InspectorValidationRefreshesChangedKeysWithoutReplacingExistingEngines()
        {
            var existing = OrbitContext.Get();
            void EditKey(string key)
            {
#if UNITY_EDITOR
                using var serialized = new UnityEditor.SerializedObject(context);
                serialized.FindProperty("definitions").GetArrayElementAtIndex(0).FindPropertyRelative("Key").stringValue = key;
                serialized.ApplyModifiedPropertiesWithoutUndo();
#else
                context.Definitions[0].Key = key;
                typeof(OrbitContext).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
#endif
            }
            EditKey("Renamed");
            Assert.Throws<KeyNotFoundException>(() => OrbitContext.Get());
            Assert.AreNotSame(existing, OrbitContext.Get("Renamed"));
            EditKey(OrbitContext.DefaultKey);
            Assert.AreSame(existing, OrbitContext.Get());
        }

        [Test] public void DefinitionsUseTheirOwnSettingsAndLibraries()
        {
            var limited = Asset<AudioEngineSettings>(); limited.InitialPoolSize = 0; limited.MaxPlaybacks = 1;
            var other = Asset<AudioLibrary>();
            other.Entries.Add(new AudioDefinition { Key = "other_sound", Loop = true, Clips = library.Entries[0].Clips });
            Set(context, "definitions", new[] {
                new EngineDefinition { Key = "SE", Settings = limited, Libraries = new[] { library } },
                new EngineDefinition { Key = "BGM", Settings = settings, Libraries = new[] { other } }
            });
            var se = OrbitContext.Get("SE"); var bgm = OrbitContext.Get("BGM");
            Assert.True(se.CreatePlayback("context_test").StartForTest().Started);
            Assert.AreEqual(PlayRejectionReason.PlaybackLimit, se.CreatePlayback("context_test").StartForTest().RejectionReason);
            Assert.True(bgm.CreatePlayback("other_sound").StartForTest().Started); Assert.True(bgm.CreatePlayback("other_sound").StartForTest().Started);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("KeyNotFoundException: Unknown Audio key: context_test"));
            Assert.False(bgm.CreatePlayback("context_test").StartForTest().Started);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("KeyNotFoundException: Unknown Audio key: other_sound"));
            Assert.False(se.CreatePlayback("other_sound").StartForTest().Started);
        }
        [Test] public void UnusedSettingsDoNotPreventAnotherEngineFromStarting()
        {
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings }, new EngineDefinition { Key = "Unconfigured" } });
            Assert.NotNull(OrbitContext.Get());
            Assert.Throws<InvalidOperationException>(() => OrbitContext.Get("Unconfigured"));
            Assert.False(OrbitContext.TryGet("Unconfigured", out _));
        }
        [Test] public void DestroyingContextEndsAllEngines()
        {
            Set(context, "definitions", new[] { new EngineDefinition { Key = "A", Settings = settings, Libraries = new[] { library } }, new EngineDefinition { Key = "B", Settings = settings, Libraries = new[] { library } } });
            var a = OrbitContext.Get("A").CreatePlayback("context_test").StartForTest(); var b = OrbitContext.Get("B").CreatePlayback("context_test").StartForTest();
            UnityEngine.Object.DestroyImmediate(context);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, a.WaitForCompletionAsync().Result.EndReason);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, b.WaitForCompletionAsync().Result.EndReason);
            Assert.False(OrbitContext.TryGet("A", out _));
        }
        [Test] public void FailedInitializationCanBeCorrected()
        {
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings, Libraries = new[] { library, library } } });
            Assert.Throws<ArgumentException>(() => OrbitContext.Get());
            Assert.False(OrbitContext.TryGet(OrbitContext.DefaultKey, out _));
            Set(context, "definitions", new[] { new EngineDefinition { Settings = settings, Libraries = new[] { library } } });
            Assert.True(OrbitContext.Get().CreatePlayback("context_test").StartForTest().Started);
        }
        [Test] public void ResetCompletesPlaybackAndAllowsFreshEngine()
        {
            var old = OrbitContext.Get(); var playback = old.CreatePlayback("context_test").StartForTest();
            typeof(OrbitContext).GetMethod("ResetEngines", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.True(playback.IsCompleted); Assert.False(OrbitContext.TryGet(OrbitContext.DefaultKey, out _));
            typeof(OrbitContext).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, context);
            Assert.AreNotSame(old, OrbitContext.Get());
        }
#if UNITY_EDITOR
        [Test] public async Task EditModePreviewTicksFadesAndDoesNotDirtyTheScene()
        {
            if (Application.isPlaying) Assert.Ignore("Edit Mode preview test.");
            var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                Assert.False(scene.isDirty);
                var playback = OrbitContext.Get().CreatePlayback("context_test").StartForTest(); Assert.True(playback.Started);
                Assert.True(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(playback.Playback.Source.gameObject.scene));
                var stop = playback.StopAsync(0.05f);
                Assert.AreSame(stop, await Task.WhenAny(stop, Task.Delay(3000)));
                Assert.AreEqual(PlaybackEndReason.Stopped, (await stop).EndReason);
                Assert.False(scene.isDirty);
            }
            finally
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
        [Test] public async Task EditModePreviewCompletesNaturally()
        {
            if (Application.isPlaying) Assert.Ignore("Edit Mode preview test.");
            var options = PlayOptions.TwoD(); options.Loop = false;
            var playback = OrbitContext.Get().CreatePlayback("context_test").StartForTest(options);
            Assert.True(playback.Started);
            var completion = playback.WaitForCompletionAsync();
            Assert.AreSame(completion, await Task.WhenAny(completion, Task.Delay(3000)));
            Assert.AreEqual(PlaybackEndReason.Completed, (await completion).EndReason);
        }
#endif
    }
}
