using System;
using UnityEngine;

namespace AudioOrbit
{
    [Serializable]
    public sealed class AudioDefinitionDefaults
    {
        public AudioCategory Category;
        public SpatialProfile SpatialProfile;
        [Range(0, 1)] public float Volume = 1;
        public bool Loop;
        public ClipSelectionMode SelectionMode = ClipSelectionMode.Sequence;
        public PlaybackLimitSettings Limits = new();

        public void ApplyTo(AudioDefinition sound)
        {
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            sound.Category = Category;
            sound.SpatialProfile = SpatialProfile;
            sound.Volume = Volume;
            sound.Loop = Loop;
            sound.SelectionMode = SelectionMode;
            sound.Limits = Limits == null ? new PlaybackLimitSettings() : new PlaybackLimitSettings
            {
                MaxPlaybacks = Limits.MaxPlaybacks,
                MaxPlaybacksPerOwner = Limits.MaxPlaybacksPerOwner,
                CooldownSeconds = Limits.CooldownSeconds,
                OwnerCooldownSeconds = Limits.OwnerCooldownSeconds,
                Behavior = Limits.Behavior
            };
        }
    }
}
