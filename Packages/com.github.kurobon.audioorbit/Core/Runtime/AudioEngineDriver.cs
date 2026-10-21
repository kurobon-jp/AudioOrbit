using UnityEngine;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AudioOrbit.Tests")]
#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AudioOrbit.Editor.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AudioOrbit.Editor")]
#endif
namespace AudioOrbit
{
    [AddComponentMenu("")]
    public sealed class AudioEngineDriver : MonoBehaviour
    {
        internal AudioEngine Engine;
        private void LateUpdate() { Engine?.Tick(Time.unscaledDeltaTime); }
        private void OnDestroy() { Engine?.Dispose(); }
    }
}
