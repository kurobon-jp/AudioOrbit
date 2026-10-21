using System;
using System.Reflection;
using System.Threading.Tasks;
using AudioOrbit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Tests.Editor
{
    public sealed class AudioLibraryDefaultsTests
    {
        private AudioLibrary library;
        private AudioCategory category;
        private SpatialProfile profile;
        private AudioClip clip;
        [SetUp] public void Setup()
        {
            library = ScriptableObject.CreateInstance<AudioLibrary>(); library.name = "Defaults Test " + Guid.NewGuid().ToString("N");
            category = ScriptableObject.CreateInstance<AudioCategory>();
            profile = ScriptableObject.CreateInstance<SpatialProfile>();
            clip = AudioClip.Create("Default clip", 100, 1, 44100, false);
            var defaults = library.Defaults;
            defaults.Category = category; defaults.SpatialProfile = profile;
            defaults.Volume = 0.35f; defaults.Loop = true; defaults.SelectionMode = ClipSelectionMode.Shuffle;
            defaults.Limits = new PlaybackLimitSettings { MaxPlaybacks = 3, MaxPlaybacksPerOwner = 2,
                CooldownSeconds = 0.8f, OwnerCooldownSeconds = 0.4f, Behavior = PlaybackLimitBehavior.StopOldest };
        }
        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(library); UnityEngine.Object.DestroyImmediate(category);
            UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(clip);
        }
        private void AssertDefaults(AudioDefinition sound)
        {
            Assert.AreSame(category, sound.Category); Assert.AreSame(profile, sound.SpatialProfile);
            Assert.AreEqual(0.35f, sound.Volume); Assert.True(sound.Loop);
            Assert.AreEqual(ClipSelectionMode.Shuffle, sound.SelectionMode);
            Assert.AreEqual(3, sound.Limits.MaxPlaybacks); Assert.AreEqual(2, sound.Limits.MaxPlaybacksPerOwner);
            Assert.AreEqual(0.8f, sound.Limits.CooldownSeconds); Assert.AreEqual(0.4f, sound.Limits.OwnerCooldownSeconds);
            Assert.AreEqual(PlaybackLimitBehavior.StopOldest, sound.Limits.Behavior);
            Assert.AreNotSame(library.Defaults.Limits, sound.Limits);
        }
        [Test] public void FactoryCopiesSettingsAndKeepsExistingSoundsIndependent()
        {
            var first = library.CreateAudio(); var second = library.CreateAudio();
            AssertDefaults(first); AssertDefaults(second);
            Assert.AreNotSame(first.Limits, second.Limits);
            library.Defaults.Volume = 0.9f; library.Defaults.Limits.MaxPlaybacks = 9;
            second.Limits.CooldownSeconds = 2;
            Assert.AreEqual(0.35f, first.Volume); Assert.AreEqual(3, first.Limits.MaxPlaybacks);
            Assert.AreEqual(0.8f, first.Limits.CooldownSeconds);
            Assert.IsEmpty(library.Entries); Assert.IsEmpty(first.Clips);
        }
        [Test] public void InspectorInitializesOnlyNewEntries()
        {
            var existing = new AudioDefinition { Key = "existing", Volume = 0.1f };
            library.Entries.Add(existing); library.Entries.Add(new AudioDefinition());
            AudioLibraryEditor.InitializeAddedEntries(library, 1);
            AssertDefaults(library.Entries[1]);
            Assert.AreEqual(0.1f, existing.Volume); Assert.False(existing.Loop); Assert.IsNull(existing.Category);
        }
        [Test] public void FolderImportUsesDefaultsAndAllowsCategoryOverride()
        {
            var result = AudioFolderImporter.Import(library, new[] { clip });
            Assert.AreEqual(1, result.Added); AssertDefaults(library.Entries[0]);
            library.Entries.Clear();
            AudioFolderImporter.Import(library, new[] { clip }, null);
            Assert.IsNull(library.Entries[0].Category); Assert.AreSame(profile, library.Entries[0].SpatialProfile);
            Assert.AreEqual(0.35f, library.Entries[0].Volume);
        }
        [Test] public async Task ManagerAddAndDragUseDefaultsWhileDuplicatePreservesSettings()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<AudioLibraryWindow>();
            try
            {
                typeof(AudioLibraryWindow).GetField("library", flags).SetValue(window, library);
                window.Show(); await Task.Delay(100);
                var add = typeof(AudioLibraryWindow).GetMethod("AddClips", flags);
                add.Invoke(window, new object[] { Array.Empty<AudioClip>() });
                AssertDefaults(library.Entries[0]);
                add.Invoke(window, new object[] { new[] { clip } });
                AssertDefaults(library.Entries[1]); Assert.AreSame(clip, library.Entries[1].Clips[0].Clip);
                library.Entries[1].Volume = 0.7f; library.Entries[1].Limits.MaxPlaybacks = 8;
                typeof(AudioLibraryWindow).GetMethod("Duplicate", flags).Invoke(window, null);
                Assert.AreEqual(0.7f, library.Entries[2].Volume); Assert.AreEqual(8, library.Entries[2].Limits.MaxPlaybacks);
                Assert.AreNotSame(library.Entries[1].Limits, library.Entries[2].Limits);
            }
            finally { window.Close(); }
        }
    }
}
