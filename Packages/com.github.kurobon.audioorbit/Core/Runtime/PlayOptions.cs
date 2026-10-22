using UnityEngine;
namespace AudioOrbit
{
    public struct PlayOptions
    {
        public PlaybackMode Mode { get; private set; }
        public Vector3 Position { get; private set; }
        public Transform Target { get; private set; }
        public Vector3 LocalOffset { get; private set; }
        public Object Owner { get; set; }
        public OverrideValue<bool> Loop { get; set; }
        public OverrideValue<float> Volume { get; set; }
        public OverrideValue<float> Pitch { get; set; }
        public OverrideValue<float> FadeInSeconds { get; set; }
        public OverrideValue<int> Priority { get; set; }
        internal OverrideValue<int> ClipIndex { get; set; }
        public SpatialProfile SpatialProfile { get; set; }
        public OverrideValue<float> SpatialBlend { get; set; }
        public OverrideValue<TargetLostBehavior> TargetLostBehavior { get; set; }
        public OverrideValue<OwnerLostBehavior> OwnerLostBehavior { get; set; }
        public OverrideValue<float> LossFadeOutSeconds { get; set; }
        public static PlayOptions TwoD() => new() { Mode = PlaybackMode.TwoD };
        public static PlayOptions At(Vector3 worldPosition) => new() { Mode = PlaybackMode.AtPosition, Position = worldPosition };
        public static PlayOptions Attached(Transform target, Vector3 localOffset = default) =>
            new() { Mode = PlaybackMode.Attached, Target = target, LocalOffset = localOffset };
    }
    public struct ExternalSourceOptions
    {
        public string Key { get; set; }
        public AudioCategory Category { get; set; }
        public Object Owner { get; set; }
        public PlaybackLimitSettings Limits { get; set; }
        public int Priority { get; set; }
        public SpatialProfile DefaultProfile { get; set; }
    }
    public readonly struct PlaybackInfo
    {
        public string Key { get; }
        public AudioCategoryId Category { get; }
        public Object Owner { get; }
        public Vector3 Position { get; }
        internal PlaybackInfo(string id, AudioCategoryId category, Object owner, Vector3 position,
            PlaybackState state, bool paused, bool listenerPaused, bool ignore)
        { Key=id; Category=category; Owner=owner; Position=position; State=state;
          ExplicitlyPaused=paused; ListenerPaused=listenerPaused; IgnoreListenerPause=ignore; }
        public PlaybackState State { get; }
        public bool ExplicitlyPaused { get; }
        public bool ListenerPaused { get; }
        public bool IgnoreListenerPause { get; }
    }
}

