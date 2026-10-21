using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AudioOrbit.Editor
{
    public sealed class AudioKeySaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            var allowed = new List<string>();
            foreach (var path in paths)
            {
                if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                { allowed.Add(path); continue; }
                var errors = new List<string>();
                foreach (var library in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AudioLibrary>())
                    errors.AddRange(AudioKeyAuthoring.Errors(library));
                if (errors.Count == 0) allowed.Add(path);
                else Debug.LogError("Cannot save AudioLibrary: " + path + "\n" + string.Join("\n", errors.Distinct()));
            }
            return allowed.ToArray();
        }
    }
}
