using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace AudioOrbit.Editor
{
    internal static class AudioKeyAuthoring
    {
        private static IEnumerable<AudioLibrary> Libraries(AudioLibrary owner) =>
            AssetDatabase.FindAssets("t:AudioLibrary").Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AudioLibrary>()
                .Concat(owner == null ? Array.Empty<AudioLibrary>() : new[] { owner }).Distinct();

        internal static string Conflict(AudioLibrary owner, int index, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "Audio Key cannot be empty.";
            foreach (var library in Libraries(owner))
                for (var i = 0; i < library.Entries.Count; i++)
                {
                    var sound = library.Entries[i];
                    if (sound == null || (library == owner && i == index) || sound.Key != key) continue;
                    var path = AssetDatabase.GetAssetPath(library);
                    return $"Key '{key}' is already used by '{sound.DisplayName}' in {library.name} ({path}).";
                }
            return null;
        }

        internal static string Unique(AudioLibrary owner, string basis, AudioDefinition exclude = null)
        {
            var used = new HashSet<string>(Libraries(owner).SelectMany(l => l.Entries)
                .Where(s => s != null && !ReferenceEquals(s, exclude)).Select(s => s.Key), StringComparer.Ordinal);
            basis = string.IsNullOrWhiteSpace(basis) ? "new_audio" : basis;
            basis = basis.Replace('/', '_');
            var key = basis;
            for (var suffix = 2; used.Contains(key); suffix++) key = basis + "_" + suffix;
            return key;
        }

        internal static string[] Errors(AudioLibrary owner)
        {
            var entries = Libraries(owner).SelectMany(l => l.Entries.Select((s, i) => (library: l, sound: s, index: i)))
                .Where(e => e.sound != null && !string.IsNullOrWhiteSpace(e.sound.Key))
                .ToLookup(e => e.sound.Key, StringComparer.Ordinal);
            var errors = new List<string>();
            for (var i = 0; i < owner.Entries.Count; i++)
            {
                var sound = owner.Entries[i];
                if (sound == null) { errors.Add("Audio definition is null."); continue; }
                if (string.IsNullOrWhiteSpace(sound.Key)) { errors.Add("Audio Key cannot be empty."); continue; }
                foreach (var other in entries[sound.Key])
                    if (other.library != owner || other.index != i)
                    {
                        errors.Add($"Key '{sound.Key}' is already used in {other.library.name} ({AssetDatabase.GetAssetPath(other.library)}).");
                        break;
                    }
            }
            return errors.ToArray();
        }

        internal static int Index(SerializedProperty property)
        {
            const string marker = ".Array.data[";
            var start = property.propertyPath.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return -1;
            start += marker.Length;
            var end = property.propertyPath.IndexOf(']', start);
            return end > start && int.TryParse(property.propertyPath.Substring(start, end - start), out var index) ? index : -1;
        }
    }
}
