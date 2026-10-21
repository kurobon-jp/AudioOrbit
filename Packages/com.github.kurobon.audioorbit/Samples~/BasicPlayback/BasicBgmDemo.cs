using UnityEngine;
using UnityEngine.UI;

namespace AudioOrbit.Samples.BasicPlayback
{
    public sealed class BasicBgmDemo : MonoBehaviour
    {
        [SerializeField] private AudioEngineSettings settings;
        [SerializeReference] private IAudioLibraryReference library;
        [SerializeField] private Text status;
        [SerializeField, Min(0)] private float fadeSeconds = 0.6f;

        private AudioEngine engine;
        private PlaybackHandle current;

        private void OnEnable() => engine = new AudioEngine(settings);

        private void OnDisable()
        {
            engine.Dispose();
            current?.Dispose();
            current = null;
        }

        public async void Switch(string key)
        {
            var token = destroyCancellationToken;
            await engine.PreloadAsync(library, token);
            engine.StopAll(fadeSeconds);
            var options = PlayOptions.TwoD();
            options.Loop = true;
            options.FadeInSeconds = fadeSeconds;
            using var playback = engine.CreatePlayback(key);
            playback.Play(options);
            current = playback;
            status.text = "Playing: " + key;
            await playback.WaitForCompletionAsync(token);
        }

        public async void Stop()
        {
            status.text = "Fading out...";
            await engine.StopAllAsync(fadeSeconds, destroyCancellationToken);
            status.text = "Stopped";
        }

        public async void OutroAndStop()
        {
            current.Loop = false;
            status.text = "Playing outro...";
            await current.WaitForCompletionAsync(destroyCancellationToken);
            status.text = "Stopped";
        }
    }
}