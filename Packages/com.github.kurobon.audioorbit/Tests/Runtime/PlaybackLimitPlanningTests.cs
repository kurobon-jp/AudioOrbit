using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public sealed class PlaybackLimitPlanningTests
    {
        private readonly List<UnityEngine.Object> objects = new();
        private AudioEngine engine;
        private AudioEngineSettings settings;
        private AudioCategory category;
        private AudioLibrary library;
        private double now;
        private T Asset<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }
        [SetUp] public void Setup()
        {
            settings = Asset<AudioEngineSettings>(); settings.InitialPoolSize = 4; settings.MaxPoolSize = 4; settings.MaxPlaybacks = 4;
            category = Asset<AudioCategory>(); category.Id = new AudioCategoryId("limit_test");
            var clip = AudioClip.Create("limit_test", 44100, 1, 44100, false); objects.Add(clip);
            library = Asset<AudioLibrary>();
            foreach (var key in new[] { "a", "b" })
            {
                var sound = new AudioDefinition { Key = key, Category = category, Loop = true };
                sound.Clips.Add(new ClipDefinition { Clip = clip }); library.Entries.Add(sound);
            }
        }
        private void Register()
        {
            engine = new AudioEngine(settings);
#if UNITY_EDITOR
            typeof(AudioEngine).GetField("editorTime", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(engine, (Func<double>)(() => now));
#endif
            engine.RegisterLibrary(library);
        }
        [TearDown] public void Cleanup() { engine?.Dispose(); foreach (var value in objects) if (value != null) UnityEngine.Object.DestroyImmediate(value); objects.Clear(); }

        [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)] [TestCase(3, false)]
        [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)] [TestCase(3, true)]
        public async Task LimitsSelectOldestOrLowestPriority(int scope, bool priority)
        {
            var behavior = priority ? PlaybackLimitBehavior.StopLowestPriority : PlaybackLimitBehavior.StopOldest;
            if (scope == 0) { settings.MaxPlaybacks = 2; settings.LimitBehavior = behavior; }
            if (scope == 1) { category.MaxPlaybacks = 2; category.LimitBehavior = behavior; }
            if (scope == 2) library.Entries[0].Limits.MaxPlaybacks = 2;
            if (scope == 3) library.Entries[0].Limits.MaxPlaybacksPerOwner = 2;
            library.Entries[0].Limits.Behavior = behavior;
            Register();
            var owner = category;
            now = 1; var oldest = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = owner, Priority = 100 });
            now = 2; var lowest = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = owner, Priority = 10 });
            now = 3; var next = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = owner, Priority = 50 });
            Assert.True(next.Started);
            var victim = priority ? lowest : oldest;
            Assert.AreEqual(PlaybackEndReason.Stolen, (await victim.WaitForCompletionAsync()).EndReason);
            Assert.False((priority ? oldest : lowest).IsCompleted);
            next.Stop(); var again = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = owner });
            Assert.True(again.Started); Assert.False((priority ? oldest : lowest).IsCompleted);
        }

        [Test] public async Task EqualPriorityAndTimestampPreserveInsertionOrder()
        {
            settings.MaxPlaybacks = 2; settings.LimitBehavior = PlaybackLimitBehavior.StopLowestPriority; Register();
            var first = engine.CreatePlayback("a").StartForTest(); var second = engine.CreatePlayback("a").StartForTest(); var third = engine.CreatePlayback("a").StartForTest();
            Assert.True(third.Started); Assert.AreEqual(PlaybackEndReason.Stolen, (await first.WaitForCompletionAsync()).EndReason);
            Assert.False(second.IsCompleted);
        }
        [Test] public void OwnerLimitDoesNotCountOtherOwners()
        {
            library.Entries[0].Limits.MaxPlaybacksPerOwner = 1; Register();
            var a = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = category });
            var b = engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = library });
            Assert.True(a.Started); Assert.True(b.Started);
            Assert.AreEqual(PlayRejectionReason.PlaybackLimit, engine.CreatePlayback("a").StartForTest(new PlayOptions { Owner = category }).RejectionReason);
            Assert.False(a.IsCompleted); Assert.False(b.IsCompleted);
        }
        [Test] public void OverlappingLimitsDoNotStealAnExtraPlayback()
        {
            settings.MaxPlaybacks = 2; settings.LimitBehavior = PlaybackLimitBehavior.StopOldest;
            library.Entries[0].Limits.MaxPlaybacks = 1;
            library.Entries[0].Limits.Behavior = PlaybackLimitBehavior.StopOldest;
            Register();
            now = 1; var a = engine.CreatePlayback("a").StartForTest(); now = 2; var b = engine.CreatePlayback("b").StartForTest();
            now = 3; var next = engine.CreatePlayback("a").StartForTest();
            Assert.True(next.Started); Assert.True(a.IsCompleted); Assert.False(b.IsCompleted);
        }
        [Test] public void MultipleVictimsAreClearedBeforeTheSetIsReused()
        {
            settings.LimitBehavior = PlaybackLimitBehavior.StopOldest; Register();
            var old = new List<PlaybackHandle>();
            for (var i = 0; i < 4; i++) old.Add(engine.CreatePlayback("a").StartForTest());
            settings.MaxPlaybacks = 1;
            var next = engine.CreatePlayback("a").StartForTest(); Assert.True(next.Started);
            foreach (var handle in old) Assert.True(handle.IsCompleted);
            var replacement = engine.CreatePlayback("b").StartForTest();
            Assert.True(replacement.Started); Assert.True(next.IsCompleted);
            replacement.Stop(); Assert.True(engine.CreatePlayback("a").StartForTest().Started);
        }
        private sealed class ImmediateContext : SynchronizationContext
        { public override void Post(SendOrPostCallback callback, object state) => callback(state); }
        [Test] public void ReentrantRejectedPlanDoesNotClearOuterVictims()
        {
            settings.MaxPlaybacks = 3; settings.LimitBehavior = PlaybackLimitBehavior.StopOldest;
            var other = Asset<AudioCategory>(); other.Id = new AudioCategoryId("other"); other.MaxPlaybacks = 1;
            var c = new AudioDefinition { Key = "c", Category = other, Loop = true }; c.Clips.Add(library.Entries[0].Clips[0]); library.Entries.Add(c);
            Register();
            var context = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
                now = 1; var a = engine.CreatePlayback("a").StartForTest(); now = 2; var b = engine.CreatePlayback("b").StartForTest(); now = 3; var existingC = engine.CreatePlayback("c").StartForTest();
                PlaybackHandle nested = null;
                a.StateChanged += (_, state) => {
                    if (state != PlaybackState.Ended) return;
                    settings.MaxPlaybacks = 2;
                    try { nested = engine.CreatePlayback("c").StartForTest(); } finally { settings.MaxPlaybacks = 3; }
                };
                now = 4; var next = engine.CreatePlayback("a").StartForTest();
                Assert.NotNull(nested); Assert.AreEqual(PlayRejectionReason.PlaybackLimit, nested.RejectionReason);
                Assert.True(next.Started); Assert.False(b.IsCompleted);
                next.Stop(); Assert.True(engine.CreatePlayback("a").StartForTest().Started);
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        }
    }
}

