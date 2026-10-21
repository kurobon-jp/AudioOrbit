using System;
namespace AudioOrbit
{
    internal struct RegionPlaybackCursor
    {
        private readonly int frames, start, end;
        private int position;
        internal bool Looping;
        internal int Position => position;
        internal void Seek(int frame) => position = Math.Max(0, Math.Min(frames - 1, frame));
        internal RegionPlaybackCursor(int frames, int start, int end, bool looping)
        { this.frames = frames; this.start = start; this.end = end; position = 0; Looping = looping; }
        internal int Read(int capacity, out int sourceFrame)
        {
            sourceFrame = position;
            if (position >= frames) return 0;
            int boundary = Looping && position < end ? end : frames;
            int count = Math.Min(capacity, boundary - position);
            position += count;
            if (Looping && position == end) position = start;
            return count;
        }
    }
}
