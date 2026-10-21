using System;
using System.Collections.Generic;
using UnityEngine;
namespace AudioOrbit
{
    [Serializable]
    public sealed class AudioDefinition
    {
        [AudioKey] public string Key = "new_audio";
        public string DisplayName = "New Audio";
        public AudioCategory Category;
        public SpatialProfile SpatialProfile;
        public bool Loop;
        [Range(0f, 1f)] public float Volume = 1;
        public PlaybackLimitSettings Limits = new();
        [InspectorName("Clips")]
        public List<ClipDefinition> Clips = new();
        public ClipSelectionMode SelectionMode = ClipSelectionMode.Sequence;
    }
}
