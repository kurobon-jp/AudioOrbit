using UnityEngine;
namespace AudioOrbit
{
    internal sealed class SpatialSettings
    {
        internal readonly float Blend,Min,Max,Spread,Doppler;
        internal readonly AudioRolloffMode Rolloff;
        internal readonly AnimationCurve Curve;
        internal SpatialSettings(SpatialProfile p)
        {
            Blend=p.SpatialBlend;Min=p.MinDistance;Max=p.MaxDistance;Spread=p.Spread;Doppler=p.DopplerLevel;Rolloff=p.RolloffMode;
            if(p.RolloffCurve!=null)Curve=new AnimationCurve(p.RolloffCurve.keys)
                {preWrapMode=p.RolloffCurve.preWrapMode,postWrapMode=p.RolloffCurve.postWrapMode};
        }
    }
}
