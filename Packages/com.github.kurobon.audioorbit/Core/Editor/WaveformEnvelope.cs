using System;
using UnityEngine;

namespace AudioOrbit.Editor
{
    internal sealed class WaveformEnvelope
    {
        internal readonly int Frames, Channels, BucketFrames, Buckets;
        internal readonly float[] Minimum, Maximum;
        internal WaveformEnvelope(int frames, int channels, int bucketFrames)
        {
            Frames = frames; Channels = channels; BucketFrames = bucketFrames;
            Buckets = (int)(((long)frames + bucketFrames - 1) / bucketFrames);
            Minimum = new float[Buckets * channels]; Maximum = new float[Buckets * channels];
            for (int i = 0; i < Minimum.Length; i++) { Minimum[i] = float.PositiveInfinity; Maximum[i] = float.NegativeInfinity; }
        }
        private WaveformEnvelope(int frames, int channels, float[] peaks)
        {
            Frames = frames; Channels = channels; Buckets = peaks.Length / (2 * channels);
            Minimum = new float[Buckets * channels]; Maximum = new float[Buckets * channels];
            for (int i = 0; i < Minimum.Length; i++)
            {
                Minimum[i] = Mathf.Min(peaks[i * 2], peaks[i * 2 + 1]);
                Maximum[i] = Mathf.Max(peaks[i * 2], peaks[i * 2 + 1]);
            }
        }
        internal static WaveformEnvelope FromImporter(int frames, int channels, float[] peaks) =>
            peaks != null && peaks.Length >= channels * 2 && peaks.Length % (channels * 2) == 0
                ? new WaveformEnvelope(frames, channels, peaks) : null;
        internal void Add(float[] pcm, int frameOffset, int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                int bucket = (frameOffset + frame) / BucketFrames;
                for (int channel = 0; channel < Channels; channel++)
                {
                    int index = bucket * Channels + channel;
                    float sample = pcm[frame * Channels + channel];
                    Minimum[index] = Mathf.Min(Minimum[index], sample);
                    Maximum[index] = Mathf.Max(Maximum[index], sample);
                }
            }
        }
        internal void Columns(int width, out float[] minimum, out float[] maximum)
        {
            minimum = new float[width * Channels]; maximum = new float[width * Channels];
            for (int x = 0; x < width; x++)
            {
                int first, last;
                if (BucketFrames > 0)
                {
                    int start = (int)((long)x * Frames / width);
                    int end = Math.Max(start + 1, (int)((long)(x + 1) * Frames / width));
                    first = start / BucketFrames; last = (end - 1) / BucketFrames;
                }
                else
                {
                    first = (int)((long)x * Buckets / width);
                    last = (int)(((long)(x + 1) * Buckets + width - 1) / width) - 1;
                }
                first = Math.Min(first, Buckets - 1); last = Math.Min(Math.Max(first, last), Buckets - 1);
                for (int channel = 0; channel < Channels; channel++)
                {
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    for (int bucket = first; bucket <= last; bucket++)
                    {
                        int index = bucket * Channels + channel;
                        min = Mathf.Min(min, Minimum[index]); max = Mathf.Max(max, Maximum[index]);
                    }
                    minimum[x * Channels + channel] = min; maximum[x * Channels + channel] = max;
                }
            }
        }
    }
}
