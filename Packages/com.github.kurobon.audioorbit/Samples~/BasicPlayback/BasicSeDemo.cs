using UnityEngine;
using UnityEngine.UI;

namespace AudioOrbit.Samples.BasicPlayback
{
    public sealed class BasicSeDemo : MonoBehaviour
    {
        [SerializeField] private AudioEngineSettings settings;
        [SerializeReference] private IAudioReference[] audioReferences;
        [SerializeField] private Text status;

        private AudioEngine engine;

        private void OnEnable() => engine = new AudioEngine(settings);
        private void OnDisable() => engine.Dispose();

        public async void Play(int index)
        {
            using var playback = engine.CreatePlayback(audioReferences[index]);
            status.text = "Playing: " + audioReferences[index].Key;
            await playback.PlayAsync(destroyCancellationToken);
            status.text = "Stopped";
        }

        public void StopAll()
        {
            engine.StopAll();
            status.text = "Stopped";
        }
    }
}