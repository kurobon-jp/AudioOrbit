#if ENABLE_ADDRESSABLES
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AudioOrbit.Samples.BasicPlayback.Editor
{
    public static class BasicPlaybackUiSetup
    {
        public static void Build(GameObject root, AudioEngineSettings settings, AudioLibrary[] libraries, AudioDefinition[][] sounds)
        {
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.transform.SetParent(root.transform, false);
            events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            var canvas = Element("Canvas", root.transform, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            var content = Element("Demo", canvas.transform, typeof(Image), typeof(VerticalLayoutGroup));
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            content.GetComponent<Image>().color = new Color(0.055f, 0.07f, 0.10f);
            var layout = content.GetComponent<VerticalLayoutGroup>(); Configure(layout, 28, 12);
            Label(content.transform, "AudioOrbit", 32, 44);
            Label(content.transform, "SE: Library loading   /   VOICE: play and wait   /   BGM: crossfade", 17, 32);
            var columns = Element("Examples", content.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            columns.GetComponent<LayoutElement>().flexibleHeight = 1;
            var row = columns.GetComponent<HorizontalLayoutGroup>(); row.spacing = 18; row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            var se = Card(columns.transform, "SE", "Play a one-shot. Load the Library, then play by Key.");
            var seDemo = se.gameObject.AddComponent<BasicSeDemo>();
            var seStatus = Label(se, "Ready - load on first click", 16, 88);
            ConfigureSection(seDemo, settings, seStatus, libraries[0], sounds[0]);
            Button(se, "Charge", seDemo.PlayFirst); Button(se, "Cutin", seDemo.PlaySecond); Button(se, "Enemy", seDemo.PlayThird);
            Button(se, "Stop SE", seDemo.StopAll);

            var voice = Card(columns.transform, "VOICE", "Play a line, then wait for its end. Load the Library, then play by Key.");
            var voiceDemo = voice.gameObject.AddComponent<BasicVoiceDemo>();
            var voiceStatus = Label(voice, "Ready", 16, 88);
            ConfigureSection(voiceDemo, settings, voiceStatus, libraries[1], sounds[1]);
            Button(voice, "Play Voice 1", voiceDemo.PlayFirst); Button(voice, "Play Voice 2", voiceDemo.PlaySecond); Button(voice, "Play Voice 3", voiceDemo.PlayThird);
            Button(voice, "Stop Voice", voiceDemo.Stop); Button(voice, "Release Library", voiceDemo.Release);

            var bgm = Card(columns.transform, "BGM", "Start a loop. Switching tracks overlaps both fades.");
            var bgmDemo = bgm.gameObject.AddComponent<BasicBgmDemo>();
            var bgmStatus = Label(bgm, "Stopped", 16, 88);
            ConfigureSection(bgmDemo, settings, bgmStatus, libraries[2], sounds[2]);
            Button(bgm, "Play / Crossfade to Track A", bgmDemo.PlayFirst); Button(bgm, "Play / Crossfade to Track B", bgmDemo.PlaySecond);
            Button(bgm, "Fade Out and Stop", bgmDemo.Stop);
            Label(content.transform, "Libraries are Addressable assets. Closing the demo releases its engine and retained Libraries.", 15, 28);
        }
        private static void ConfigureSection(Component component, AudioEngineSettings settings, Text status, AudioLibrary library, AudioDefinition[] sounds)
        {
            using var serialized = new SerializedObject(component);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.FindProperty("status").objectReferenceValue = status;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(library));
            serialized.FindProperty("library").managedReferenceValue = new AudioOrbit.Addressables.AddressablesLibraryReference
            {
                Asset = new UnityEngine.AddressableAssets.AssetReferenceT<AudioLibrary>(guid)
            };
            var keys = serialized.FindProperty("keys"); keys.arraySize = sounds.Length;
            for (var i = 0; i < sounds.Length; i++) keys.GetArrayElementAtIndex(i).stringValue = sounds[i].Key;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static GameObject Element(string name, Transform parent, params System.Type[] types)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            foreach (var type in types) go.AddComponent(type);
            return go;
        }
        private static void Configure(VerticalLayoutGroup layout, int padding, float spacing)
        {
            layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        }
        private static Transform Card(Transform parent, string title, string description)
        {
            var card = Element(title, parent, typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            card.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.18f);
            card.GetComponent<LayoutElement>().flexibleWidth = 1;
            Configure(card.GetComponent<VerticalLayoutGroup>(), 18, 10);
            Label(card.transform, title, 26, 38); Label(card.transform, description, 16, 62);
            return card.transform;
        }
        private static Text Label(Transform parent, string value, int size, float height)
        {
            var go = Element("Label", parent, typeof(Text), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.text = value; text.color = new Color(0.9f, 0.93f, 0.98f);
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.raycastTarget = false;
            return text;
        }
        private static void Button(Transform parent, string title, UnityAction action)
        {
            var go = Element(title, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().preferredHeight = 42;
            go.GetComponent<Image>().color = new Color(0.17f, 0.27f, 0.38f);
            var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            UnityEventTools.AddPersistentListener(button.onClick, action);
            var label = Label(go.transform, title, 16, 42); label.alignment = TextAnchor.MiddleCenter;
            var rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6, 0); rect.offsetMax = new Vector2(-6, 0);
        }
    }
}
#endif