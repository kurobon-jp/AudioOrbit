namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private void StartDirectRequest(DirectAudioReference reference, PlaybackRequest handle, PlayOptions options)
        {
            try
            {
                var clip = SnapshotClip(reference.Clip, "Clip", false);
                var definition = new Definition
                {
                    ExplicitLimitKey = reference,
                    Category = RegisterCategory(null),
                    Clips = new[] { clip }, Volume = 1,
                    Limits = new PlaybackLimitSettings(), Selection = ClipSelectionMode.Sequence,
                    Loop = reference.Loop
                };
                RunRequest(handle, LoadPlayback(definition, handle, options, handle.RequestToken));
            }
            catch (System.Exception error) { FailRequest(handle, error); }
        }
    }
}
