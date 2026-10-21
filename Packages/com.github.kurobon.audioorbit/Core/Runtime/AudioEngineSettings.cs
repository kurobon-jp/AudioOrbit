using UnityEngine;

namespace AudioOrbit
{
    [CreateAssetMenu(menuName = "AudioOrbit/Engine Settings")]
    public sealed class AudioEngineSettings : ScriptableObject
    {
        [Min(0)] public int InitialPoolSize = 16;
        [Min(1)] public int MaxPoolSize = 64;
        [Min(1)] public int MaxPlaybacks = 64;
        public PlaybackLimitBehavior LimitBehavior;
        public TargetLostBehavior TargetLostBehavior = TargetLostBehavior.FadeOut;
        public OwnerLostBehavior OwnerLostBehavior = OwnerLostBehavior.FadeOut;
        [Min(0), UnityEngine.Serialization.FormerlySerializedAs("LifetimeFadeSeconds")]
        public float LossFadeOutSeconds = 0.1f;
    }
}
