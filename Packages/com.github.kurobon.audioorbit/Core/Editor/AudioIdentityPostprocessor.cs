using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace AudioOrbit.Editor
{
    public sealed class AudioIdentityPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported) EnsureAtPath(path);
            foreach (var path in moved) EnsureAtPath(path);
        }
        private static void EnsureAtPath(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return;
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            EnsureIdentity(asset);
            if (asset is AudioLibrary)
                foreach (var child in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (child is AudioCategory) EnsureIdentity(child);
        }
        public static void EnsureIdentity(UnityEngine.Object asset)
        {
            if (!(asset is AudioCategory)) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            if (string.IsNullOrEmpty(guid)) return;
            var serialized = new SerializedObject(asset);
            var origin = serialized.FindProperty("editorAssetGuid");
            bool copied = !string.IsNullOrEmpty(origin.stringValue) && origin.stringValue != guid;
            bool changed = origin.stringValue != guid;
            {
                var id = serialized.FindProperty("Id").FindPropertyRelative("value");
                if (copied || string.IsNullOrEmpty(id.stringValue))
                {
                    string identity = guid;
                    if (AssetDatabase.IsSubAsset(asset) && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId))
                        identity += ":" + localId;
                    id.stringValue = identity;
                    changed = true;
                }
            }
            if (!changed) return;
            origin.stringValue = guid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            EditorApplication.delayCall += () => { if (asset != null) AssetDatabase.SaveAssetIfDirty(asset); };
        }
    }
}
