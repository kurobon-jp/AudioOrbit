using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.Audio;
#endif
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AudioOrbit.Editor
{
    internal static class AudioGeneratorPicker
    {
        private const string SearchTextKey = "AudioOrbit.AudioGeneratorPicker.SearchText";
#if UNITY_6000_3_OR_NEWER
        internal static UnityEditor.Search.ObjectField CreateField(string label)
#else
        internal static UnityEditor.UIElements.ObjectField CreateField(string label)
#endif
        {
#if UNITY_6000_3_OR_NEWER
            var field = new UnityEditor.Search.ObjectField(label) { objectType = typeof(Object) };
            field.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                field.searchContext = CreateContext(text =>
                {
                    SessionState.SetString(SearchTextKey, text);
                    if (field.panel != null && field.searchContext != null && field.searchContext.searchText != text)
                        field.searchContext.searchText = text;
                });
                field.searchContext.searchText = SessionState.GetString(SearchTextKey, string.Empty);
                field.searchContext.options |= SearchFlags.UseSessionSettings;
                field.searchViewState = SearchViewState.CreatePickerState("Select Audio Generator", field.searchContext,
                    (Object asset, bool canceled) => { if (!canceled) field.value = asset; },
                    null, nameof(IAudioGenerator), typeof(Object), field.searchViewFlags);
                field.searchViewState.sessionName = "AudioOrbit.AudioGeneratorPicker";
            });
            field.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                field.searchContext?.Dispose();
                field.searchContext = null;
            });
            return field;
#else
            return new UnityEditor.UIElements.ObjectField(label) { objectType = typeof(AudioClip), allowSceneObjects = false };
#endif
        }

        internal static SearchContext CreateContext(Action<string> rememberSearch = null)
        {
            var provider = new SearchProvider("audioorbit-generators", "Audio Generators")
            {
                fetchItems = (context, items, source) =>
                {
                    rememberSearch?.Invoke(context.searchText);
                    return Fetch(context, source);
                },
                toObject = (item, type) => item.data as Object,
                fetchThumbnail = (item, context) => AssetPreview.GetMiniThumbnail(item.data as Object)
            };
            return SearchService.CreateContext(new[] { provider }, string.Empty);
        }

        private static IEnumerable<SearchItem> Fetch(SearchContext context, SearchProvider provider)
        {
#if UNITY_6000_3_OR_NEWER
            var types = TypeCache.GetTypesDerivedFrom<IAudioGenerator>()
                .Where(t => typeof(Object).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
                .Select(t => "t:" + t.Name).Distinct().ToArray();
#else
            var types = new[] { "t:AudioClip" };
#endif
            if (types.Length == 0) yield break;
            foreach (var guid in AssetDatabase.FindAssets(string.Join(" ", types)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
#if UNITY_6000_3_OR_NEWER
                    if (asset == null || !(asset is IAudioGenerator)) continue;
#else
                    if (!(asset is AudioClip)) continue;
#endif
                    if (!string.IsNullOrEmpty(context.searchText) &&
                        asset.name.IndexOf(context.searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                        path.IndexOf(context.searchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    yield return provider.CreateItem(context, asset.GetInstanceID().ToString(), asset.name, path, null, asset);
                }
            }
        }
    }
}
