using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class AudioPlayerTests
    {
        private readonly List<UnityEngine.Object> objects = new();
        private OrbitContext context;
        private AudioLibrary library;
        private Reference reference;
        private readonly List<Loader> loaders = new();
        private sealed class Reference : IAudioReference, IAudioLibraryReference
        {
            public IAudioLibraryReference LibraryReference => this;
            public string Key;
            string IAudioReference.Key => Key;
            public string LibraryIdentity => "player_test_library";
            public Reference() { Key = "player_test"; }
            public Func<IAudioLibraryLoader> Factory;
            public IAudioLibraryLoader CreateLibraryLoader() => Factory();
        }
        private sealed class Loader : IAudioLibraryLoader
        {
            public AudioLibrary Library;
            public TaskCompletionSource<IAudioLibraryLease> Pending;
            public int Active;
            public bool Disposed;
            private sealed class Lease : IAudioLibraryLease
            {
                private Loader owner;
                public IReadOnlyList<AudioLibrary> Libraries { get; }
                public Lease(Loader loader) { owner = loader; loader.Active++; Libraries = new[] { loader.Library }; }
                public void Dispose() { if (owner == null) return; owner.Active--; owner = null; }
            }
            public Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default) =>
                Pending?.Task ?? Task.FromResult<IAudioLibraryLease>(new Lease(this));
            public void Deliver() => Pending.SetResult(new Lease(this));
            public void Dispose() => Disposed = true;
        }
        private static void Set(object obj, string field, object value)
        {
            for (var type = obj.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property == null) continue;
                property.SetValue(obj, value); return;
            }
            throw new MissingFieldException(field);
        }
        private T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); objects.Add(asset); return asset; }
        [SetUp] public void Setup()
        {
            var settings = Asset<AudioEngineSettings>(); settings.InitialPoolSize = 0;
            var clip = AudioClip.Create("player test", 44100, 1, 44100, false); objects.Add(clip);
            library = Asset<AudioLibrary>(); var sound = new AudioDefinition { Key = "player_test", Loop = true };
            sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound);
            context = Asset<OrbitContext>(); Set(context, "definitions", new[] { new EngineDefinition { Settings = settings } });
            typeof(OrbitContext).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, context);
            reference = new Reference { Factory = () => { var loader = new Loader { Library = library }; loaders.Add(loader); return loader; } };
        }
        [TearDown] public void Cleanup()
        {
            foreach (var obj in objects) if (obj is GameObject && obj != null) UnityEngine.Object.DestroyImmediate(obj);
            foreach (var obj in objects) if (!(obj is GameObject) && obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear(); loaders.Clear();
        }
        private AudioPlayer Player(bool enable = true)
        {
            var go = new GameObject("player"); objects.Add(go); go.SetActive(false);
            var player = go.AddComponent<AudioPlayer>(); Set(player, "reference", reference); Set(player, "engineKey", OrbitContext.DefaultKey);
            go.SetActive(enable); return player;
        }
        private static async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!condition()) { if (DateTime.UtcNow > deadline) Assert.Fail("Player operation timed out"); await Task.Delay(16); }
        }
        [Test] public void EngineReferenceUsesTheSelectedEngineWithoutALoader()
        {
            var engine = OrbitContext.Get();
            engine.RegisterLibrary(library);
            var player = Player(false);
            Set(player, "reference", new EngineAudioReference("player_test"));
            player.gameObject.SetActive(true);
            player.Play();
            Assert.True(player.Playback.Started);
            Assert.AreSame(engine, player.Playback.Engine);
            Assert.AreEqual(0, loaders.Count);
        }

        [Test] public void PlayersAutomaticallyStartAndShareOneEngineAndLibraryLoad()
        {
            var first = Player(); var second = Player();
            Assert.True(first.Playback.Started); Assert.True(second.Playback.Started);
            Assert.AreSame(OrbitContext.Get(), first.Playback.Engine); Assert.AreSame(OrbitContext.Get(), second.Playback.Engine);
            Assert.AreEqual(1, loaders.Count);
        }
        [Test] public void DisablingPlayerStopsOnlyItsOwnPlaybackAndReenablingStartsAgain()
        {
            var first = Player(); var second = Player(); var old = first.Playback.Request;
            var reusable = first.Playback;
            first.enabled = false;
            Assert.True(old.IsCompleted); Assert.False(second.Playback.IsCompleted); Assert.True(OrbitContext.TryGet(OrbitContext.DefaultKey, out _));
            first.enabled = true;
            Assert.AreSame(reusable, first.Playback); Assert.AreNotSame(old, first.Playback.Request); Assert.True(first.Playback.Started); Assert.False(second.Playback.IsCompleted);
        }
        [Test] public void ExplicitPlayRestartsWithoutOverlappingItsPreviousPlayback()
        {
            var player = Player(); var old = player.Playback.Request; player.Play(); var next = player.Playback;
            Assert.True(old.IsCompleted); Assert.True(next.Started); Assert.AreSame(next, player.Playback);
        }
        [Test] public void DestroyingPlayerDisposesItsReusablePlayback()
        {
            var player = Player(); var reusable = player.Playback; var run = player.Playback;
            UnityEngine.Object.DestroyImmediate(player.gameObject);
            Assert.True(reusable.IsDisposed); Assert.True(run.IsCompleted);
        }

        [Test] public async Task DisablingPlayerDuringLoadingStopsButKeepsEngineCache()
        {
            var loader = new Loader { Library = library, Pending = new TaskCompletionSource<IAudioLibraryLease>() };
            loaders.Add(loader); reference.Factory = () => loader;
            var player = Player(); var pending = player.Playback;
            Assert.False(pending.Started); player.enabled = false;
            Assert.True(pending.IsCompleted); loader.Deliver(); await Until(() => loader.Active == 1 && loader.Disposed);
            Assert.False(pending.Started);
            OrbitContext.Get().ReleaseLibrary(reference); Assert.AreEqual(0, loader.Active);
        }
        [Test] public async Task DestroyingContextDuringLoadingEndsRequestAndDoesNotReviveIt()
        {
            var loader = new Loader { Library = library, Pending = new TaskCompletionSource<IAudioLibraryLease>() };
            loaders.Add(loader); reference.Factory = () => loader;
            var player = Player(); var pending = player.Playback;
            UnityEngine.Object.DestroyImmediate(context);
            Assert.True(pending.IsCompleted); Assert.AreEqual(PlaybackEndReason.EngineDisposed, (await pending.WaitForCompletionAsync()).EndReason);
            loader.Deliver(); await Until(() => loader.Active == 0 && loader.Disposed); player.enabled = false;
            Assert.False(pending.Started);
        }
        [Test] public void DestroyingContextEndsBothPlayers()
        {
            var first = Player(); var second = Player();
            Assert.False(first.Playback.IsCompleted); Assert.False(second.Playback.IsCompleted);
            UnityEngine.Object.DestroyImmediate(context);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, first.Playback.WaitForCompletionAsync().Result.EndReason);
            Assert.AreEqual(PlaybackEndReason.EngineDisposed, second.Playback.WaitForCompletionAsync().Result.EndReason);
            first.enabled = false; second.enabled = false;
        }
        [Test] public async Task CancelingFadeStopWaitDoesNotCancelTheStop()
        {
            var player = Player(); using var token = new CancellationTokenSource();
            var stop = player.StopAsync(0.05f, token.Token); token.Cancel();
            try { await stop; Assert.Fail("Expected cancellation"); } catch (OperationCanceledException) { }
            await Until(() => player.Playback.IsCompleted);
            Assert.AreEqual(PlaybackEndReason.Stopped, (await player.Playback.WaitForCompletionAsync()).EndReason);
        }
        [Test] public void AttachedPlaybackFollowsThePlayerTransform()
        {
            var player = Player(false); Set(player, "mode", PlaybackMode.Attached); Set(player, "localOffset", Vector3.up);
            player.gameObject.SetActive(true); player.transform.position = Vector3.right * 3;
            OrbitContext.Get().Tick(0);
            Assert.AreEqual(player.transform.TransformPoint(Vector3.up), player.Playback.Playback.Source.transform.position);
        }
    }
}
