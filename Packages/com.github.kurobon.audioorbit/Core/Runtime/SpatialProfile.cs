using UnityEngine;
namespace AudioOrbit
{
    [CreateAssetMenu(menuName = "AudioOrbit/Spatial Profile")]
    public sealed class SpatialProfile : ScriptableObject
    {
        [Range(0, 1)] public float SpatialBlend = 1;
        [Min(0)] public float MinDistance = 1;
        [Min(0)] public float MaxDistance = 500;
        public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;
        public AnimationCurve RolloffCurve = AnimationCurve.Linear(0, 1, 1, 0);
        [Range(0, 360)] public float Spread;
        [Min(0)] public float DopplerLevel = 1;
    }
}
