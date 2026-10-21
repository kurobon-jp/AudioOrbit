using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AudioOrbit.Editor
{
    internal static class AudioFolderImporter
    {
        internal static AudioClip[] FindClips(string folder, bool includeSubfolders)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Select a project folder.", nameof(folder));
            return AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Where(p => includeSubfolders || string.Equals(Path.GetDirectoryName(p)?.Replace('\\', '/'), folder, StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AudioClip>()
                .OrderBy(c => AssetDatabase.GetAssetPath(c), StringComparer.Ordinal).ThenBy(c => c.name, StringComparer.Ordinal)
                .Distinct().ToArray();
        }

        internal static (int Added, int Skipped) Import(AudioLibrary library, IEnumerable<AudioClip> clips) => Import(library, clips, null, false);
        internal static (int Added, int Skipped) Import(AudioLibrary library, IEnumerable<AudioClip> clips, AudioCategory category) => Import(library, clips, category, true);
        private static (int Added, int Skipped) Import(AudioLibrary library, IEnumerable<AudioClip> clips, AudioCategory category, bool overrideCategory)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            var existing = new HashSet<AudioClip>(library.Entries.Where(s => s?.Clips != null)
                .SelectMany(s => s.Clips).Where(c => c?.Clip != null).Select(c => c.Clip));
            var additions = new List<AudioDefinition>();
            var reservedKeys = new HashSet<string>(StringComparer.Ordinal);
            int skipped = 0;
            foreach (var clip in clips)
            {
                if (clip == null || !existing.Add(clip)) { skipped++; continue; }
                var sound = library.CreateAudio();
                var basis = clip.name.Replace('/', '_');
                var key = AudioKeyAuthoring.Unique(library, basis);
                for (var suffix = 2; !reservedKeys.Add(key); suffix++)
                    key = AudioKeyAuthoring.Unique(library, basis + "_" + suffix);
                sound.Key = key; sound.DisplayName = clip.name;
                if (overrideCategory) sound.Category = category;
                sound.Clips.Add(new ClipDefinition { Clip = clip });
                additions.Add(sound);
            }
            if (additions.Count > 0)
            {
                Undo.RecordObject(library, "Import audio folder");
                library.Entries.AddRange(additions);
                EditorUtility.SetDirty(library);
            }
            return (additions.Count, skipped);
        }
    }
}
