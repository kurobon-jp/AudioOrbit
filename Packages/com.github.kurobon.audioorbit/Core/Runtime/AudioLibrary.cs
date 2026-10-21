using System.Collections.Generic;
using UnityEngine;
namespace AudioOrbit
{
    [CreateAssetMenu(menuName = "AudioOrbit/Audio Library")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [SerializeField, InspectorName("New Audio Defaults")] private AudioDefinitionDefaults defaults = new();
        public AudioDefinitionDefaults Defaults => defaults ??= new AudioDefinitionDefaults();
        /// <summary>Creates a new definition with copied defaults; does not add it to Entries.</summary>
        public AudioDefinition CreateAudio()
        {
            var sound = new AudioDefinition();
            Defaults.ApplyTo(sound);
            return sound;
        }
        [SerializeField, InspectorName("Audio Entries")] private List<AudioDefinition> definitions = new();
        public List<AudioDefinition> Entries => definitions;
    }
}
