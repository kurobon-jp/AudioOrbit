using UnityEngine.UIElements;
namespace AudioOrbit.Editor
{
    internal sealed class ClipChangedEvent : EventBase<ClipChangedEvent>
    {
        public ClipChangedEvent() { Init(); }
        protected override void Init() { base.Init(); bubbles = true; }
    }
    internal sealed class ClipPreviewEvent : EventBase<ClipPreviewEvent>
    {
        internal int Index;
        public ClipPreviewEvent() { Init(); }
        protected override void Init() { base.Init(); bubbles = true; Index = -1; }
    }
}
