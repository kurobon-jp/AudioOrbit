using UnityEngine;
using UnityEngine.Audio;

namespace AudioOrbit
{
    [CreateAssetMenu(menuName = "AudioOrbit/Audio Category")]
    public sealed class AudioCategory : ScriptableObject
    {
        [HideInInspector] public AudioCategoryId Id;
        [SerializeField, HideInInspector] private string editorAssetGuid;

        private void OnEnable()
        {
            if (!Id.IsValid) Id = new AudioCategoryId(System.Guid.NewGuid().ToString("N"));
        }

        public AudioMixerGroup MixerOutput;
        [Range(0, 1)] public float Volume = 1;
        public bool Muted;

        [Min(0)]
        public int MaxPlaybacks;

        public int Priority;
        public PlaybackLimitBehavior LimitBehavior;
        public bool IgnoreListenerPause;
    }
}
