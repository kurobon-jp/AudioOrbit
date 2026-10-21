using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AudioOrbit.Samples.BasicPlayback
{
    public sealed class BasicVoiceDemo : MonoBehaviour
    {
        [SerializeField] private AudioEngineSettings settings;
        [SerializeField] private AudioLibrary library;
        [SerializeField] private Text status;
        private AudioEngine engine;
        private LibraryHandle handle;

        private void OnEnable() => engine = new AudioEngine(settings);
        private void OnDisable() => engine.Dispose();

        public void Preload()
        {
            handle = engine.RegisterLibrary(library);
            status.text = "Library Preloaded.";
        }
        
        public async void Play(string key)
        {
            engine.StopAll();
            using var playback = engine.CreatePlayback(key);
            status.text = "Playing: " + key;
            var result = await playback.PlayAsync(destroyCancellationToken);
            status.text = result.EndReason switch
            {
                PlaybackEndReason.Failed => $"{result.Error.Message}",
                PlaybackEndReason.Completed => "Voice finished. Continue dialogue.",
                _ => status.text
            };
        }

        public void Stop()
        {
            engine.StopAll();
            status.text = "Voice stopped";
        }

        public void Release()
        {
            engine.StopAll();
            if (handle != null)
            {
                engine.UnregisterLibrary(handle);
                handle = null;
            }

            status.text = "Not loaded";
        }
    }
}
