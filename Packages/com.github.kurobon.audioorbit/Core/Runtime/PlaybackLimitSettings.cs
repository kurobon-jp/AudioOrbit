using System;
using UnityEngine;
namespace AudioOrbit
{
    [Serializable]
    public sealed class PlaybackLimitSettings
    {
        [Min(0)] public int MaxPlaybacks;
        [Min(0)] public int MaxPlaybacksPerOwner;
        [Min(0)] public float CooldownSeconds;
        [Min(0)] public float OwnerCooldownSeconds;
        public PlaybackLimitBehavior Behavior = PlaybackLimitBehavior.RejectNew;
    }
}
