#if UNITY_6000_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace AudioOrbit
{
    /// <summary>One-source, sample-frame region playback. Control state is separate for every playback.</summary>
    internal sealed class LoopRegionPlayback : IDisposable
    {
        private sealed class Pcm
        {
            internal AudioClip Clip;
            internal NativeArray<float> Data;
            internal int References;
            internal int Channels, Rate, Frames;
            internal void Retain() => References++;
            internal void Release()
            {
                if (--References != 0) return;
                cache.Remove(Clip);
                Data.Dispose();
                Clip = null;
            }
        }
        private static readonly Dictionary<AudioClip, Pcm> cache = new();
        internal static bool HasPcm(AudioClip clip) => cache.ContainsKey(clip);
        private static readonly Dictionary<ProcessorInstance, LoopRegionPlayback> instances = new();
        internal static LoopRegionPlayback Find(ProcessorInstance instance) => instances.TryGetValue(instance, out var playback) ? playback : null;
        private Pcm pcm;
        private sealed class RuntimeOwner
        {
            internal Pcm Pcm;
            internal LoopRegionPlayback Playback;
        }
        private int start, end;
        private int revision;
        internal int Frames { get; private set; }
        internal int SampleRate { get; private set; }
        internal int PositionFrames { get; private set; }
        private GeneratorInstance instance;
        private ControlContext controlContext;
        private bool disposed;
        private bool hasInstance;
        internal bool Finished { get; private set; }
        public bool Loop { get; private set; }
        internal static LoopRegionPlayback Create(AudioClip clip, int loopStart, int loopEndExclusive, bool loop)
        {
            var generator = new LoopRegionPlayback();
            try { generator.Initialize(clip, loopStart, loopEndExclusive, loop); return generator; }
            catch { generator.Dispose(); throw; }
        }
        internal static void Validate(AudioClip clip, int loopStart, int loopEndExclusive)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (loopStart < 0 || loopEndExclusive <= loopStart || loopEndExclusive > clip.samples)
                throw new ArgumentException("Loop region must satisfy 0 <= start < end <= Clip.samples.");
            if (clip.channels != 1 && clip.channels != 2)
                throw new ArgumentException("Region playback currently supports mono and stereo clips.");
        }
        private void Initialize(AudioClip clip, int loopStart, int loopEndExclusive, bool loop)
        {
            Validate(clip, loopStart, loopEndExclusive);
            start = loopStart; end = loopEndExclusive; Loop = loop;
            if (!cache.TryGetValue(clip, out pcm))
            {
                var data = new float[checked(clip.samples * clip.channels)];
                if (!clip.GetData(data, 0))
                    throw new InvalidOperationException("Region playback requires loaded PCM. Use Decompress On Load for this clip.");
                pcm = new Pcm { Clip = clip, Channels = clip.channels, Rate = clip.frequency, Frames = clip.samples,
                    Data = new NativeArray<float>(data, Allocator.Persistent) };
                cache.Add(clip, pcm);
            }
            pcm.Retain();
            Frames = pcm.Frames; SampleRate = pcm.Rate;
        }
        internal GeneratorInstance Allocate(ControlContext context, AudioFormat? nestedConfiguration,
            CreationParameters creationParameters)
        {
            if (pcm == null) throw new ObjectDisposedException(nameof(LoopRegionPlayback));
            pcm.Retain();
            var owner = GCHandle.Alloc(new RuntimeOwner { Pcm = pcm, Playback = this });
            try
            {
                creationParameters.realtimeUpdateSetting = UpdateSetting.UpdateIfDataIsAvailable;
                creationParameters.controlUpdateSetting = UpdateSetting.UpdateIfDataIsAvailable;
                var generator = context.AllocateGenerator(
                    new Realtime { Data = pcm.Data, Channels = pcm.Channels,
                        Cursor = new RegionPlaybackCursor(pcm.Frames, start, end, Loop) },
                    new Control { Owner = GCHandle.ToIntPtr(owner), Channels = pcm.Channels, Rate = pcm.Rate },
                    nestedConfiguration, creationParameters);
                instance = generator;
                controlContext = context;
                hasInstance = true;
                instances.Add(generator, this);
                ReleasePcm();
                return generator;
            }
            catch { owner.Free(); pcm.Release(); throw; }
        }
        internal void SetLoop(bool value)
        {
            Loop = value;
            if (!hasInstance || !controlContext.Exists(instance)) return;
            var message = new LoopMessage { Enabled = value };
            controlContext.SendMessage(instance, ref message);
        }
        internal bool Seek(int frame)
        {
            if (disposed || !hasInstance || !controlContext.Exists(instance)) return false;
            var message = new SeekMessage { Frame = Math.Max(0, Math.Min(Frames - 1, frame)), Revision = revision + 1 };
            if (controlContext.SendMessage(instance, ref message) != Response.Handled) return false;
            revision = message.Revision; PositionFrames = message.Frame; Finished = false;
            return true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (hasInstance && controlContext.Exists(instance)) controlContext.Destroy(instance);
            ReleasePcm();
        }
        private void ReleasePcm()
        {
            var released = pcm; pcm = null;
            released?.Release();
        }
        private struct LoopMessage { internal bool Enabled; }
        private struct SeekMessage { internal int Frame, Revision; }
        private struct RenderMessage { internal bool Finished; internal int Frame, Revision; }
        private struct Realtime : GeneratorInstance.IRealtime
        {
            [NativeDisableContainerSafetyRestriction] internal NativeArray<float> Data;
            internal int Channels;
            internal RegionPlaybackCursor Cursor;
            private bool reportedFinish;
            private int revision;
            public bool isFinite => true;
            public bool isRealtime => false;
            public DiscreteTime? length => null;
            public void Update(UpdatedDataContext context, Pipe pipe)
            {
                foreach (var data in pipe.GetAvailableData(context))
                {
                    if (data.TryGetData(out LoopMessage message)) Cursor.Looping = message.Enabled;
                    else if (data.TryGetData(out SeekMessage seek)) { Cursor.Seek(seek.Frame); revision = seek.Revision; reportedFinish = false; }
                }
            }
            public GeneratorInstance.Result Process(in RealtimeContext context, Pipe pipe,
                ChannelBuffer buffer, GeneratorInstance.Arguments args)
            {
                int written = 0;
                while (written < buffer.frameCount)
                {
                    int count = Cursor.Read(buffer.frameCount - written, out int position);
                    if (count == 0) break;
                    for (int frame = 0; frame < count; frame++)
                        for (int channel = 0; channel < buffer.channelCount; channel++)
                            buffer[channel, written + frame] = Data[(position + frame) * Channels + Math.Min(channel, Channels - 1)];
                    written += count;
                }
                // Finite generator: report only valid frames; zero on subsequent calls after EOF.
                if (!reportedFinish && written == 0)
                {
                    reportedFinish = pipe.SendData(context, new RenderMessage { Finished = true, Frame = Cursor.Position, Revision = revision });
                }
                else if (written > 0)
                    pipe.SendData(context, new RenderMessage { Frame = Cursor.Position, Revision = revision });
                for (int frame = written; frame < buffer.frameCount; frame++)
                    for (int channel = 0; channel < buffer.channelCount; channel++) buffer[channel, frame] = 0;
                return written;
            }
        }
        private struct Control : GeneratorInstance.IControl<Realtime>
        {
            internal IntPtr Owner;
            internal int Channels, Rate;
            public void Dispose(ControlContext context, ref Realtime realtime)
            {
                var owner = GCHandle.FromIntPtr(Owner);
                var runtime = (RuntimeOwner)owner.Target;
                runtime.Pcm.Release();
                owner.Free();
                instances.Remove(runtime.Playback.instance);
                runtime.Playback.hasInstance = false;
                runtime.Playback.disposed = true;
            }
            public void Update(ControlContext context, Pipe pipe)
            {
                var owner = (RuntimeOwner)GCHandle.FromIntPtr(Owner).Target;
                foreach (var data in pipe.GetAvailableData(context))
                    if (data.TryGetData(out RenderMessage message))
                    {
                        if (message.Revision != owner.Playback.revision) continue;
                        owner.Playback.PositionFrames = message.Frame;
                        if (message.Finished) owner.Playback.Finished = true;
                    }
            }
            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (message.Is<LoopMessage>())
                {
                    var change = message.Get<LoopMessage>();
                    return pipe.SendData(context, change) ? Response.Handled : Response.Unhandled;
                }
                if (message.Is<SeekMessage>())
                {
                    var seek = message.Get<SeekMessage>();
                    return pipe.SendData(context, seek) ? Response.Handled : Response.Unhandled;
                }
                return Response.Unhandled;
            }
            public void Configure(ControlContext context, ref Realtime realtime, in AudioFormat format,
                out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties properties)
            { setup = new GeneratorInstance.Setup(Channels == 1 ? AudioSpeakerMode.Mono : AudioSpeakerMode.Stereo, Rate); }
        }
    }
}

#endif
