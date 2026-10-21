using System;
using System.Threading;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class ResourcesReferenceTestHost : ScriptableObject
    {
        public ResourcesAudioReference[] References = { new(), new() };
    }

    public sealed class ResourcesAudioReferenceTests
    {
        private string folder, path;
        private AudioLibrary library;
        private AudioEngineSettings settings;

        [SetUp] public void Setup()
        {
            var name = "AudioOrbitResourcesTests_" + Guid.NewGuid().ToString("N");
            folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            AssetDatabase.CreateFolder(folder, "Resources");
            AssetDatabase.CreateFolder(folder + "/Resources", name);
            path = name + "/Library";
            var clip = AudioClip.Create("Resources Test", 4410, 1, 44100, false);
            AssetDatabase.CreateAsset(clip, folder + "/Clip.asset");
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            var sound = new AudioDefinition { Key = "test/play", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip });
            library.Entries.Add(sound);
            AssetDatabase.CreateAsset(library, folder + "/Resources/" + path + ".asset");
            settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
            settings.InitialPoolSize = settings.MaxPoolSize = settings.MaxPlaybacks = 2;
        }

        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(settings);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test] public async Task LibraryReferencePreloadsWithoutAnAudioKey()
        {
            Assert.False(typeof(IAudioReference).IsAssignableFrom(typeof(IAudioLibraryReference)));
            Assert.Null(typeof(IAudioLibraryReference).GetProperty("Key"));
            Assert.Null(typeof(ResourcesLibraryReference).GetField("Key"));
            var location = new ResourcesLibraryReference { LibraryPath = path };
            using var engine = new AudioEngine(settings);
            await engine.PreloadAsync(location);
            using var first = engine.CreatePlayback(new ResourcesAudioReference { Library = location, Key = "test/play" });
            using var second = engine.CreatePlayback(new ResourcesAudioReference { Library = location, Key = "test/play" });
            first.Play(); second.Play();
            Assert.True(first.Started); Assert.True(second.Started);
            engine.ReleaseLibrary(location);
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }

        [Test] public async Task ReferenceLoadsPlaysAndPreloadsWithoutUsingKey()
        {
            var reference = new ResourcesAudioReference { Library = new ResourcesLibraryReference { LibraryPath = path }, Key = "missing" };
            using var engine = new AudioEngine(settings);
            await engine.PreloadAsync(reference.Library);
            reference.Key = "test/play";
            var playback = engine.CreatePlayback(reference).StartForTest();
            Assert.True((await playback.ObserveStartAsync()).Started);
            engine.ReleaseLibrary(reference.Library);
            Assert.AreEqual(PlaybackState.Playing, playback.State);
            await playback.StopAsync();
        }

        [Test] public async Task AutomaticLoadAndIndependentEnginesRetainSharedResource()
        {
            var reference = new ResourcesAudioReference { Library = new ResourcesLibraryReference { LibraryPath = path }, Key = "test/play" };
            using var first = new AudioEngine(settings);
            using var second = new AudioEngine(settings);
            var playback = first.CreatePlayback(reference).StartForTest();
            Assert.True((await playback.ObserveStartAsync()).Started);
            await second.PreloadAsync(reference.Library);
            await playback.StopAsync(); first.Dispose();
            Assert.IsTrue(library != null);
            var other = second.CreatePlayback(reference).StartForTest();
            Assert.True((await other.ObserveStartAsync()).Started);
            await other.StopAsync();
        }

        [Test] public async Task MissingResourceReportsPathAndCanceledRequestDoesNotLoad()
        {
            using var engine = new AudioEngine(settings);
            var reference = new ResourcesAudioReference { Library = new ResourcesLibraryReference { LibraryPath = path + "Missing" }, Key = "test/play" };
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("No AudioLibrary found at Resources path:"));
            var result = await engine.CreatePlayback(reference).StartForTest().ObserveStartAsync();
            Assert.False(result.Started);
            StringAssert.Contains(reference.Library.LibraryPath, result.Error.Message);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try
            {
                await engine.PreloadAsync(reference.Library, canceled.Token);
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
        }

        [Test] public async Task LoaderDisposalKeepsLeaseAndRejectsFurtherLoads()
        {
            var loader = ResourcesAudioLibraryLoader.FromPath(path);
            var lease = await loader.LoadAsync(); loader.Dispose();
            Assert.AreSame(library, lease.Libraries[0]);
            try
            {
                await loader.LoadAsync();
                Assert.Fail("Expected the disposed loader to reject loading.");
            }
            catch (ObjectDisposedException) { }
            lease.Dispose(); lease.Dispose();
            Assert.IsNull(lease.Libraries);
            Assert.IsTrue(library != null);
        }

        [Test] public void EmptyPathsAreRejectedAndResourceIdentitiesIgnoreCase()
        {
            Assert.Throws<ArgumentException>(() => ResourcesAudioLibraryLoader.FromPath(" "));
            Assert.AreEqual(new ResourcesLibraryReference { LibraryPath = path }.LibraryIdentity,
                new ResourcesLibraryReference { LibraryPath = path.ToUpperInvariant() }.LibraryIdentity);
            Assert.AreEqual(path, ResourcesAudioReferenceDrawer.ResourcesPath(AssetDatabase.GetAssetPath(library)));
            Assert.IsNull(ResourcesAudioReferenceDrawer.ResourcesPath(folder + "/NotInResources.asset"));
        }

        [Test] public async Task ArrayInspectorSavesEachLibraryAndKeyIndependently()
        {
            var host = ScriptableObject.CreateInstance<ResourcesReferenceTestHost>();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                using var serialized = new SerializedObject(host);
                var array = serialized.FindProperty("References");
                var drawer = new ResourcesAudioReferenceDrawer();
                var first = drawer.CreatePropertyGUI(array.GetArrayElementAtIndex(0));
                var second = drawer.CreatePropertyGUI(array.GetArrayElementAtIndex(1));
                window.Show(); window.rootVisualElement.Add(first); window.rootVisualElement.Add(second);
                first.Bind(serialized); second.Bind(serialized);
                await Task.Delay(50);
                second.Q<ObjectField>().value = library;
                var popup = second.Q<PopupField<string>>();
                Assert.True(popup.enabledSelf, "Audio selection should be enabled for the second array element.");
                Assert.AreEqual(2, popup.choices.Count);
                popup.value = popup.choices[1];
                Assert.AreEqual(path, host.References[1].Library.LibraryPath);
                Assert.AreEqual("test/play", host.References[1].Key);
                Assert.IsTrue(string.IsNullOrEmpty(host.References[0].Library.LibraryPath), "The first array element must remain unchanged.");
                Assert.IsFalse(popup.value.Contains("/"));
            }
            finally { window.Close(); UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
