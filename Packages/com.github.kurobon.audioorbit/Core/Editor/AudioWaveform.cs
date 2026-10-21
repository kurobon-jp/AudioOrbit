using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    internal sealed class AudioWaveform : VisualElement, IDisposable
    {
        private readonly Dictionary<AudioClip, WaveformEnvelope> cache = new();
        private readonly List<AudioClip> cacheOrder = new();
        private readonly Label message = new();
        private readonly VisualElement channelLabels = new() { pickingMode = PickingMode.Ignore };
        private readonly Label time = new();
        private float playhead;
        private bool seekable;
        internal event Action<float> SeekRequested;
        private float PlotHeight => Mathf.Max(0, contentRect.height - 28);
        private AudioClip clip;
        private WaveformEnvelope envelope;
        private float[] buffer, minimum, maximum;
        private int frameOffset, columns;
        private bool analyzing;
        private bool assigned;
        private double started;
        private static readonly MethodInfo ImporterPeaks = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil")?
            .GetMethod("GetMinMaxData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(AudioImporter) }, null);

        internal AudioWaveform()
        {
            style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            message.style.position = Position.Absolute;
            message.style.left = message.style.top = 6;
            message.style.right = 6; message.style.whiteSpace = WhiteSpace.Normal;
            channelLabels.style.position = Position.Absolute;
            channelLabels.style.left = channelLabels.style.top = channelLabels.style.right = channelLabels.style.bottom = 0;
            Add(channelLabels); Add(message);
            var controls = new VisualElement();
            controls.style.position = Position.Absolute;
            controls.style.left = controls.style.right = controls.style.bottom = 4;
            controls.style.height = 20; controls.style.flexDirection = FlexDirection.Row;
            controls.style.justifyContent = Justify.FlexStart;
            time.style.minWidth = 128; time.style.fontSize = 11;
            time.style.unityTextAlign = TextAnchor.MiddleLeft;
            controls.Add(time); Add(controls);
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (!seekable || e.button != 0 || e.localPosition.y >= PlotHeight) return;
                this.CapturePointer(e.pointerId); RequestSeek(e.localPosition.x); e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) { RequestSeek(e.localPosition.x); e.StopPropagation(); } });
            RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) { this.ReleasePointer(e.pointerId); e.StopPropagation(); } });
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => RebuildColumns());
            RegisterCallback<AttachToPanelEvent>(_ => WaveformImportObserver.Imported += Invalidate);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }
        private void RequestSeek(float x)
        {
            if (seekable && contentRect.width > 0) SeekRequested?.Invoke(Mathf.Clamp01((x - contentRect.x) / contentRect.width));
        }
        internal void SetPlaybackPosition(float seconds, float duration, bool enabled)
        {
            seekable = enabled;
            float position = duration > 0 ? Mathf.Clamp01(seconds / duration) : 0;
            time.text = $"{FormatTime(seconds)} / {FormatTime(duration)}";
            if (playhead != position) { playhead = position; MarkDirtyRepaint(); }
        }
        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return $"{(int)(seconds / 60):00}:{seconds % 60:00.00}";
        }
        internal void SetClip(AudioClip value)
        {
            var next = value != null ? value : null;
            if (assigned && ReferenceEquals(clip, next)) { RebuildColumns(); return; }
            assigned = true;
            StopAnalysis(); clip = next; envelope = null; minimum = maximum = null; columns = 0;
            channelLabels.Clear();
            if (clip == null) { ShowMessage("No clip selected"); return; }
            if (cache.TryGetValue(clip, out var saved)) { envelope = saved; Ready(); return; }
            if (clip.samples <= 0 || clip.channels <= 0) { ShowMessage("No waveform data"); return; }
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                ReadImporterPeaks(); return;
            }
            envelope = new WaveformEnvelope(clip.samples, clip.channels, Math.Max(1, (int)Math.Ceiling(clip.samples / 65536d)));
            buffer = new float[Math.Min(16384, clip.samples) * clip.channels]; frameOffset = 0;
            started = EditorApplication.timeSinceStartup;
            if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData()) { ReadImporterPeaks(); return; }
            analyzing = true; EditorApplication.update += Analyze;
            ShowMessage("Analyzing waveform...");
        }
        private void Analyze()
        {
            if (clip == null) { StopAnalysis(); return; }
            if (clip.loadState == AudioDataLoadState.Loading)
            {
                if (EditorApplication.timeSinceStartup - started > 30) { StopAnalysis(); ShowMessage("PCM loading timed out"); }
                return;
            }
            if (clip.loadState != AudioDataLoadState.Loaded) { ReadImporterPeaks(); return; }
            var until = EditorApplication.timeSinceStartup + 0.008;
            do
            {
                if (!clip.GetData(buffer, frameOffset)) { ReadImporterPeaks(); return; }
                int count = Math.Min(buffer.Length / clip.channels, clip.samples - frameOffset);
                envelope.Add(buffer, frameOffset, count); frameOffset += count;
            } while (frameOffset < clip.samples && EditorApplication.timeSinceStartup < until);
            if (frameOffset >= clip.samples) { StopAnalysis(); Store(); Ready(); }
            else message.text = $"Analyzing waveform... {frameOffset * 100L / clip.samples}%";
        }
        private void ReadImporterPeaks()
        {
            StopAnalysis(); envelope = null;
            try
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
                if (importer != null && ImporterPeaks != null)
                    envelope = WaveformEnvelope.FromImporter(clip.samples, clip.channels, (float[])ImporterPeaks.Invoke(null, new object[] { importer }));
            }
            catch (Exception) { envelope = null; }
            if (envelope == null) { ShowMessage("PCM and waveform data are unavailable for this clip"); return; }
            Store(); Ready();
        }
        private void Store()
        {
            cache[clip] = envelope; cacheOrder.Remove(clip); cacheOrder.Add(clip);
            while (cacheOrder.Count > 4) { cache.Remove(cacheOrder[0]); cacheOrder.RemoveAt(0); }
        }
        private void Ready()
        {
            message.style.display = DisplayStyle.None;
            channelLabels.Clear();
            for (int channel = 0; channel < envelope.Channels; channel++)
            {
                var label = new Label(envelope.Channels == 2 ? channel == 0 ? "L" : "R" : $"ch {channel + 1}");
                label.style.position = Position.Absolute; label.style.left = 4;
                label.style.top = Length.Percent(100f * channel / envelope.Channels);
                label.style.color = new Color(0.65f, 0.65f, 0.65f); channelLabels.Add(label);
            }
            columns = 0; RebuildColumns();
        }
        private float WaveformPixelsPerPoint =>
#if UNITY_6000_3_OR_NEWER
            scaledPixelsPerPoint;
#else
            EditorGUIUtility.pixelsPerPoint;
#endif
        private void RebuildColumns()
        {
            if (envelope == null || analyzing || contentRect.width <= 0) return;
            for (int channel = 0; channel < channelLabels.childCount; channel++)
                channelLabels[channel].style.top = PlotHeight * channel / envelope.Channels;
            int width = Mathf.Clamp(Mathf.CeilToInt(contentRect.width * WaveformPixelsPerPoint), 1, 16384);
            if (width == columns) return;
            columns = width; envelope.Columns(columns, out minimum, out maximum); MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (minimum == null || columns == 0 || envelope == null) return;
            var painter = context.painter2D;
            float width = contentRect.width, height = PlotHeight / envelope.Channels;
            float pixel = 1f / WaveformPixelsPerPoint;
            painter.lineWidth = pixel;
            for (int channel = 0; channel < envelope.Channels; channel++)
            {
                float center = contentRect.y + (channel + 0.5f) * height;
                painter.strokeColor = new Color(0.35f, 0.35f, 0.35f);
                painter.BeginPath(); painter.MoveTo(new Vector2(contentRect.x, center)); painter.LineTo(new Vector2(contentRect.xMax, center)); painter.Stroke();
                painter.strokeColor = new Color(1f, 0.75f, 0.15f);
                painter.BeginPath();
                for (int x = 0; x < columns; x++)
                {
                    int index = x * envelope.Channels + channel;
                    float top = center - Mathf.Clamp(maximum[index], -1, 1) * height * 0.44f;
                    float bottom = center - Mathf.Clamp(minimum[index], -1, 1) * height * 0.44f;
                    if (bottom - top < pixel) { float middle = (top + bottom) * 0.5f; top = middle - pixel * 0.5f; bottom = middle + pixel * 0.5f; }
                    float position = contentRect.x + (x + 0.5f) * width / columns;
                    painter.MoveTo(new Vector2(position, top)); painter.LineTo(new Vector2(position, bottom));
                }
                painter.Stroke();
            }
            painter.strokeColor = new Color(0.3f, 0.8f, 1f);
            painter.lineWidth = pixel * 2;
            float cursor = contentRect.x + playhead * width;
            painter.BeginPath(); painter.MoveTo(new Vector2(cursor, contentRect.y));
            painter.LineTo(new Vector2(cursor, contentRect.y + PlotHeight)); painter.Stroke();
        }
        private void ShowMessage(string text) { minimum = maximum = null; message.text = text; message.style.display = DisplayStyle.Flex; MarkDirtyRepaint(); }
        private void StopAnalysis() { EditorApplication.update -= Analyze; analyzing = false; buffer = null; }
        private void Invalidate(string[] paths)
        {
            foreach (var key in cache.Keys.ToArray())
                if (key == null || paths.Contains(AssetDatabase.GetAssetPath(key))) { cache.Remove(key); cacheOrder.Remove(key); }
            if (clip != null && paths.Contains(AssetDatabase.GetAssetPath(clip))) { var current = clip; assigned = false; SetClip(current); }
        }
        public void Dispose() { StopAnalysis(); WaveformImportObserver.Imported -= Invalidate; cache.Clear(); cacheOrder.Clear(); envelope = null; minimum = maximum = null; assigned = false; }
    }
    internal sealed class WaveformImportObserver : AssetPostprocessor
    {
        internal static event Action<string[]> Imported;
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            Imported?.Invoke(imported.Concat(deleted).Concat(moved).Concat(movedFrom).ToArray());
    }
}
