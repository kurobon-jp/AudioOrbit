using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioOrbit
{
    public sealed partial class AudioEngine : IDisposable
    {
        private sealed class Definition
        {
            internal Exception ValidationError;
            internal string Key;
            internal object ExplicitLimitKey;
            internal object LimitKey => ExplicitLimitKey ?? Key;
            internal LibraryHandle Library;
            internal LibrarySession Session;
            internal AudioCategoryId Category;
            internal ClipDefinition[] Clips;
            internal bool Loop;
            internal float Volume;
            internal PlaybackLimitSettings Limits;
            internal SpatialSettings Profile;
            internal ClipSelectionMode Selection;
            internal int LastClip = -1;
            internal int[] Shuffle;
            internal int ShufflePosition;
        }

        private sealed class CategoryState
        {
            internal float Volume = 1;
            internal bool Muted, Ignore;
            internal int Max, Priority;
            internal PlaybackLimitBehavior Behavior;
            internal UnityEngine.Audio.AudioMixerGroup Mixer;
        }

        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly AudioEngineSettings settings;
        private readonly Dictionary<string, Definition> definitions = new();

        private readonly Dictionary<AudioCategoryId, CategoryState> categories = new();

        private readonly List<Playback> playbacks = new();
        private readonly List<ExternalSourceHandle> external = new();
        private readonly Stack<AudioSource> pool = new();
        private readonly Dictionary<object, double> cooldown = new();
        private readonly Dictionary<(object, int), double> ownerCooldown = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly GameObject root;
        private readonly AudioEngineDriver driver;
        private int poolCount;
        private float master = 1;
        private bool disposed;

        public AudioEngine(AudioEngineSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.InitialPoolSize < 0 || settings.MaxPoolSize < 1 ||
                settings.InitialPoolSize > settings.MaxPoolSize || settings.MaxPlaybacks < 1)
                throw new ArgumentException("Invalid pool or playback limits.");
            ValidateNonNegative(settings.LossFadeOutSeconds, "LossFadeOutSeconds");
            this.settings = settings;
            root = new GameObject("AudioOrbit");
            root.hideFlags = HideFlags.DontSave;
            if (Application.isPlaying) Object.DontDestroyOnLoad(root);
            driver = root.AddComponent<AudioEngineDriver>();
            driver.Engine = this;
            for (var i = 0; i < settings.InitialPoolSize; i++) pool.Push(CreateSource());
        }

        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread)
                throw new InvalidOperationException("AudioOrbit requires the Unity main thread.");
        }

        private void Check()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(AudioEngine));
        }

        internal static void ValidateNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        }

        internal static void ValidatePitch(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value == 0 || value < -3 || value > 3)
                throw new ArgumentOutOfRangeException("pitch", value, "Pitch must be between -3 and 3, excluding 0. Normal playback uses 1.");
        }

        private static PlaybackLimitSettings CopyLimits(PlaybackLimitSettings s)
        {
            s = s ?? new PlaybackLimitSettings();
            if (s.MaxPlaybacks < 0 || s.MaxPlaybacksPerOwner < 0) throw new ArgumentException("Negative playback limit.");
            ValidateNonNegative(s.CooldownSeconds, "CooldownSeconds");
            ValidateNonNegative(s.OwnerCooldownSeconds, "OwnerCooldownSeconds");
            return new PlaybackLimitSettings
            {
                MaxPlaybacks = s.MaxPlaybacks, MaxPlaybacksPerOwner = s.MaxPlaybacksPerOwner,
                CooldownSeconds = s.CooldownSeconds, OwnerCooldownSeconds = s.OwnerCooldownSeconds,
                Behavior = s.Behavior
            };
        }

        private AudioCategoryId RegisterCategory(AudioCategory asset)
        {
            var id = asset != null ? asset.Id : default;
            if (categories.ContainsKey(id)) return id;
            var c = new CategoryState();
            if (asset != null)
            {
                if (!id.IsValid) throw new ArgumentException("Category has no ID.");
                ValidateNonNegative(asset.Volume, "category.Volume");
                if (asset.MaxPlaybacks < 0) throw new ArgumentException("Negative category limit.");
                c.Volume = asset.Volume;
                c.Muted = asset.Muted;
                c.Ignore = asset.IgnoreListenerPause;
                c.Max = asset.MaxPlaybacks;
                c.Priority = asset.Priority;
                c.Behavior = asset.LimitBehavior;
                c.Mixer = asset.MixerOutput;
            }

            categories.Add(id, c);
            return id;
        }

        public LibraryHandle RegisterLibrary(AudioLibrary library)
        {
            Check();
            if (library == null) throw new ArgumentNullException(nameof(library));
            var handle = new LibraryHandle(this);
            var pending = new List<Definition>();
            var ids = new HashSet<string>();
            foreach (var s in library.Entries)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Key) || !ids.Add(s.Key) || definitions.ContainsKey(s.Key))
                    throw new ArgumentException("Null sound, invalid or duplicate Audio key.");
                try
                {
                    if (s.Clips == null || s.Clips.Count == 0)
                        throw new ArgumentException("Audio has no Clips. Assign at least one Clip.");
                    ValidateNonNegative(s.Volume, "Volume");
                    ValidateProfile(s.SpatialProfile);
                    var clips = new ClipDefinition[s.Clips.Count];
                    float total = 0;
                    for (var i = 0; i < clips.Length; i++)
                    {
                        clips[i] = SnapshotClip(s.Clips[i], "Clip " + (i + 1));
                        total += clips[i].Weight;
                    }

                    if (total <= 0) throw new ArgumentException("Clip weights require a positive total.");
                    pending.Add(new Definition
                    {
                        Key = s.Key, Library = handle, Category = RegisterCategory(s.Category), Clips = clips,
                        Loop = s.Loop, Volume = s.Volume, Selection = s.SelectionMode, Limits = CopyLimits(s.Limits),
                        Profile = s.SpatialProfile != null ? new SpatialSettings(s.SpatialProfile) : null
                    });
                }
                catch (ArgumentException error)
                {
                    pending.Add(new Definition
                    {
                        Key = s.Key, Library = handle, Clips = Array.Empty<ClipDefinition>(),
                        ValidationError = new ArgumentException($"Library '{library.name}', Audio '{s.Key}': {error.Message}", error)
                    });
                }
            }

            foreach (var d in pending)
            {
                definitions.Add(d.Key, d);
                handle.Keys.Add(d.Key);
            }

            return handle;
        }

        private static ClipDefinition SnapshotClip(ClipDefinition clip, string label, bool weighted = true)
        {
            if (clip == null || clip.Resource == null) throw new ArgumentException(label + " is not assigned.");
            if (!clip.HasSupportedResource)
                throw new ArgumentException(label + " is unsupported. Use an AudioClip; audio generators require Unity 6.3 or later.");
            if (weighted) ValidateNonNegative(clip.Weight, "Weight");
            ValidateNonNegative(clip.Volume, "Volume");
            ValidateNonNegative(clip.VolumeRandomRange, "VolumeRandomRange");
            ValidateNonNegative(clip.PitchRandomRange, "PitchRandomRange");
            ValidatePitch(clip.Pitch);
            return new ClipDefinition
            {
                Resource = clip.Resource, Volume = clip.Volume, Pitch = clip.Pitch,
                Weight = weighted ? clip.Weight : 1,
                VolumeRandomRange = clip.VolumeRandomRange, PitchRandomRange = clip.PitchRandomRange
            };
        }

        private void ValidateLibrary(LibraryHandle library)
        {
            if (library == null || library.Engine != this)
                throw new ArgumentException("Library belongs to another engine.");
        }

        public void UnregisterLibrary(LibraryHandle library)
        {
            Check();
            ValidateLibrary(library);
            if (!library.Registered) return;
            foreach (var id in library.Keys)
                definitions.Remove(id);

            library.Registered = false;
        }

        private Definition Resolve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Invalid Audio key.");
            if (definitions.TryGetValue(id, out var d)) return d;
            throw new KeyNotFoundException("Unknown Audio key: " + id);
        }

        private int ChooseClipIndex(Definition d, PlayOptions options)
        {
            if (d.ValidationError != null) throw d.ValidationError;
            if (options.ClipIndex.IsSpecified)
            {
                var index = options.ClipIndex.Value;
                if (index < 0 || index >= d.Clips.Length) throw new ArgumentOutOfRangeException("ClipIndex");
                return index;
            }

            if (d.Selection == ClipSelectionMode.Sequence)
            {
                for (var offset = 1; offset <= d.Clips.Length; offset++)
                {
                    var index = (d.LastClip + offset) % d.Clips.Length;
                    if (d.Clips[index].Weight <= 0) continue;
                    d.LastClip = index;
                    return index;
                }
                throw new InvalidOperationException("Sequence requires an eligible clip.");
            }

            if (d.Selection == ClipSelectionMode.Shuffle)
            {
                if (d.Shuffle == null || d.ShufflePosition == d.Shuffle.Length)
                {
                    var count = 0;
                    for (var i = 0; i < d.Clips.Length; i++)
                        if (d.Clips[i].Weight > 0) count++;
                    if (d.Shuffle == null || d.Shuffle.Length != count) d.Shuffle = new int[count];
                    var next = 0;
                    for (var i = 0; i < d.Clips.Length; i++)
                        if (d.Clips[i].Weight > 0) d.Shuffle[next++] = i;
                    d.ShufflePosition = 0;
                    for (var i = d.Shuffle.Length - 1; i > 0; i--)
                    {
                        var j = UnityEngine.Random.Range(0, i + 1);
                        (d.Shuffle[i], d.Shuffle[j]) = (d.Shuffle[j], d.Shuffle[i]);
                    }
                }

                var selected = d.Shuffle[d.ShufflePosition++];
                d.LastClip = selected;
                return selected;
            }

            var avoid = d.Selection == ClipSelectionMode.AvoidImmediateRepeat;
            float total = 0;
            for (var i = 0; i < d.Clips.Length; i++)
                if (!avoid || i != d.LastClip)
                    total += d.Clips[i].Weight;
            if (total <= 0)
            {
                avoid = false;
                foreach (var v in d.Clips) total += v.Weight;
            }

            var roll = UnityEngine.Random.value * total;
            var fallback = 0;
            for (var i = 0; i < d.Clips.Length; i++)
            {
                if ((avoid && i == d.LastClip) || d.Clips[i].Weight <= 0) continue;
                fallback = i;
                roll -= d.Clips[i].Weight;
                if (roll < 0)
                {
                    d.LastClip = i;
                    return i;
                }
            }

            d.LastClip = fallback;
            return fallback;
        }

        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            StopPending(_ => true, PlaybackEndReason.EngineDisposed);
            lifetime.Cancel();
            for (var i = playbacks.Count - 1; i >= 0; i--) End(playbacks[i], PlaybackEndReason.EngineDisposed);
            foreach (var session in new List<LibrarySession>(librarySessions.Values)) ReleaseLibrarySession(session);
            libraryLoads.Clear();
            referenceLoads.Clear();
            referencedLibraries.Clear();
            librarySelections.Clear();
            foreach (var h in external.ToArray()) UnregisterExternal(h);
            definitions.Clear();
            categories.Clear();
            if (root != null)
            {
                driver.Engine = null;
                if (Application.isPlaying) Object.Destroy(root);
                else Object.DestroyImmediate(root);
            }

            lifetime.Dispose();
            pool.Clear();
        }
    }
}
