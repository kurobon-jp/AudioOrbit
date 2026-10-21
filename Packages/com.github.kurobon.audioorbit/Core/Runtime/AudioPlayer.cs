using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    public class AudioPlayer : MonoBehaviour
    {
        [SerializeReference] private IAudioReference reference;
        [SerializeField] private string engineKey = OrbitContext.DefaultKey;
        [SerializeField, Tooltip("Automatically play when this component becomes enabled in Play Mode.")]
        private bool playOnAwake = true;
        [SerializeField] private PlaybackMode mode = PlaybackMode.TwoD;
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset;
        [SerializeField, Min(0)] private float fadeInSeconds;
        public PlaybackHandle Playback => playback;
        private PlaybackHandle playback;
        private AudioEngine playbackEngine;
        private IAudioReference playbackReference;

        protected virtual void OnEnable()
        {
            if (!playOnAwake) return;
            try
            {
                Play();
            }
            catch (Exception error)
            {
                Debug.LogException(error, gameObject);
            }
        }

        protected virtual void OnDisable() => Stop();

        protected virtual void OnDestroy() => playback?.Dispose();

        public void Play()
        {
            if (!isActiveAndEnabled) throw new InvalidOperationException("Enable the AudioPlayer before playing.");
            var soundReference = reference ??
                                 throw new InvalidOperationException("Assign a Audio reference to the AudioPlayer.");
            var engine = OrbitContext.Get(engineKey);
            var options = mode switch
            {
                PlaybackMode.AtPosition => PlayOptions.At(transform.TransformPoint(localOffset)),
                PlaybackMode.Attached => PlayOptions.Attached(target != null ? target : transform, localOffset),
                _ => PlayOptions.TwoD()
            };
            options.Owner = this;
            options.FadeInSeconds = fadeInSeconds;
            Stop();
            if (playback == null || playback.IsDisposed || playbackEngine != engine || playbackReference != soundReference)
            {
                playback?.Dispose();
                playback = engine.CreatePlayback(soundReference);
                playbackEngine = engine;
                playbackReference = soundReference;
            }
            playback.Play(options);
        }

        public void Stop(float fadeSeconds = 0) => Playback?.Stop(fadeSeconds);

        public Task<PlaybackResult> StopAsync(float fadeSeconds = 0, CancellationToken cancellationToken = default)
        {
            if (Playback != null) return Playback.StopAsync(fadeSeconds, cancellationToken);
            AudioEngine.ValidateNonNegative(fadeSeconds, nameof(fadeSeconds));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PlaybackResult(false, PlayRejectionReason.None, PlaybackEndReason.Stopped));
        }
    }
}
