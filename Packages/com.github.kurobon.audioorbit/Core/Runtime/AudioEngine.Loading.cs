using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private static PlayStartResult Reject(PlayRejectionReason reason) => new(false, reason, null);

        private ValueTask<PlayStartResult> LoadPlayback(string id, PlaybackRequest request, PlayOptions options,
            CancellationToken cancellationToken = default)
        {
            Check(); cancellationToken.ThrowIfCancellationRequested();
            var definition = Resolve(id);
            return LoadPlayback(definition, request, options, cancellationToken);
        }

        private ValueTask<PlayStartResult> LoadPlayback(Definition d, PlaybackRequest request, PlayOptions options,
            CancellationToken cancellationToken = default)
        {
            Check(); cancellationToken.ThrowIfCancellationRequested();
            request.Library = d.Library;
            request.Category = d.Category;
            ValidateOptions(options);
            var clipIndex = ChooseClipIndex(d, options);
            var clipDefinition = d.Clips[clipIndex];
            var clip = clipDefinition.Clip;
            SessionReference playbackLease = null;
            if (d.Session != null)
            {
                d.Session.Users++;
                playbackLease = new SessionReference(this, d.Session);
            }

            try
            {
                if (disposed || cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Check();
                }
                if (d.Library != null && !d.Library.Registered)
                {
                    return new ValueTask<PlayStartResult>(Reject(PlayRejectionReason.LibraryUnregistered));
                }

#if UNITY_6000_3_OR_NEWER
                var preparation = PrepareRegion(clipDefinition, clip, request, cancellationToken);
                if (!preparation.IsCompleted)
                {
                    var pendingLease = playbackLease;
                    playbackLease = null;
                    return new ValueTask<PlayStartResult>(StartPreparedPlayback(preparation, d, clipIndex, clipDefinition, clip,
                        pendingLease, request, options, request.RequestToken));
                }
                preparation.GetAwaiter().GetResult();
#endif
                Check(); cancellationToken.ThrowIfCancellationRequested();
                if (d.Library != null && !d.Library.Registered)
                    return new ValueTask<PlayStartResult>(Reject(PlayRejectionReason.LibraryUnregistered));
                var transferred = playbackLease; playbackLease = null;
                return new ValueTask<PlayStartResult>(Start(d, clipIndex, clipDefinition, clip, transferred, options, null, request));
            }
            finally
            {
                playbackLease?.Dispose();
            }
        }

#if UNITY_6000_3_OR_NEWER
        private async Task<PlayStartResult> StartPreparedPlayback(Task preparation, Definition definition, int clipIndex,
            ClipDefinition clipDefinition, UnityEngine.AudioClip clip, SessionReference lease, PlaybackRequest request, PlayOptions options, CancellationToken token)
        {
            try
            {
                await preparation;
                Check(); token.ThrowIfCancellationRequested();
                if (definition.Library != null && !definition.Library.Registered)
                    return Reject(PlayRejectionReason.LibraryUnregistered);
                var transferred = lease; lease = null;
                return Start(definition, clipIndex, clipDefinition, clip, transferred, options, null, request);
            }
            finally { lease?.Dispose(); }
        }

        private Task PrepareRegion(ClipDefinition clipDefinition, UnityEngine.AudioClip clip, PlaybackRequest request, CancellationToken token)
        {
            if (!(clipDefinition.Resource is LoopRegionClip) || clip == null || LoopRegionPlayback.HasPcm(clip))
                return Task.CompletedTask;
            return PrepareRegionPcm(clip, clip.loadState == UnityEngine.AudioDataLoadState.Loaded
                ? token : request.GetRequestToken());
        }

        private async Task PrepareRegionPcm(UnityEngine.AudioClip clip, CancellationToken token)
        {
            if (clip.loadType != UnityEngine.AudioClipLoadType.DecompressOnLoad)
                throw new InvalidOperationException("Region playback requires Decompress On Load in the clip import settings.");
            if (clip.loadState == UnityEngine.AudioDataLoadState.Unloaded && !clip.LoadAudioData())
                throw new InvalidOperationException("Could not load PCM for region playback.");
            while (clip.loadState == UnityEngine.AudioDataLoadState.Loading)
            {
                Check(); token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            Check(); token.ThrowIfCancellationRequested();
            if (clip.loadState != UnityEngine.AudioDataLoadState.Loaded)
                throw new InvalidOperationException("PCM loading failed for region playback.");
        }
#endif
        private static void ValidateOptions(PlayOptions o)
        {
            if (o.Volume.IsSpecified) ValidateNonNegative(o.Volume.Value, "Volume");
            if (o.Pitch.IsSpecified) ValidatePitch(o.Pitch.Value);
            if (o.FadeInSeconds.IsSpecified) ValidateNonNegative(o.FadeInSeconds.Value, "FadeInSeconds");
            if (o.LossFadeOutSeconds.IsSpecified)
                ValidateNonNegative(o.LossFadeOutSeconds.Value, "LossFadeOutSeconds");
            if (o.SpatialBlend.IsSpecified && (float.IsNaN(o.SpatialBlend.Value) || o.SpatialBlend.Value < 0 ||
                                               o.SpatialBlend.Value > 1))
                throw new ArgumentOutOfRangeException("SpatialBlend");
        }
    }
}
