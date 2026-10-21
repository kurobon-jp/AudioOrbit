using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace AudioOrbit
{
    [CreateAssetMenu(menuName = "AudioOrbit/Orbit Context")]
    public sealed partial class OrbitContext : ScriptableObject
    {
        public const string DefaultKey = "Default";
        private static OrbitContext instance;
        [SerializeField] private EngineDefinition[] definitions = { new EngineDefinition() };
        [NonSerialized] private Dictionary<string, AudioEngine> engines;
        [NonSerialized] private Dictionary<string, EngineDefinition> definitionIndex;
        [NonSerialized] private EngineDefinition[] indexedDefinitions;
        [NonSerialized] private int definitionVersion;
        [NonSerialized] private int indexedDefinitionVersion = -1;
        public IReadOnlyList<EngineDefinition> Definitions => definitions ?? Array.Empty<EngineDefinition>();

        public static AudioEngine Get(string key = DefaultKey)
        {
            ValidateKey(key);
#if UNITY_EDITOR
            if (!Application.isPlaying && instance == null) ResolveEditorContext();
#endif
            if (instance == null)
                throw new InvalidOperationException("OrbitContext is not preloaded. Include it in Preloaded Assets.");
            return instance.GetEngine(key);
        }

        public static bool TryGet(string key, out AudioEngine engine)
        {
            ValidateKey(key);
            engine = null;
            return instance != null && instance.engines != null && instance.engines.TryGetValue(key, out engine);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Engine key cannot be empty.", nameof(key));
        }

        internal string[] ValidationErrors()
        {
            var errors = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in Definitions)
            {
                if (definition == null) { errors.Add("Engine definition cannot be null."); continue; }
                if (string.IsNullOrWhiteSpace(definition.Key)) errors.Add("Engine key cannot be empty.");
                else if (!keys.Add(definition.Key)) errors.Add("Duplicate Engine key: " + definition.Key);
                if (definition.Settings == null) errors.Add("Assign AudioEngineSettings for Engine '" + definition.Key + "'.");
            }
            return errors.ToArray();
        }

        private void EnsureDefinitionIndex()
        {
            var version = Volatile.Read(ref definitionVersion);
            if (definitionIndex != null && ReferenceEquals(indexedDefinitions, definitions) && indexedDefinitionVersion == version)
                return;
            indexedDefinitions = null;
            indexedDefinitionVersion = -1;
            var index = definitionIndex ??= new Dictionary<string, EngineDefinition>(StringComparer.Ordinal);
            index.Clear();
            foreach (var definition in definitions ?? Array.Empty<EngineDefinition>())
            {
                if (definition == null) throw new ArgumentException("Engine definition cannot be null.");
                ValidateKey(definition.Key);
                if (!index.TryAdd(definition.Key, definition)) throw new ArgumentException("Duplicate Engine key: " + definition.Key);
            }
            indexedDefinitions = definitions;
            indexedDefinitionVersion = version;
        }

        private void OnValidate() => Interlocked.Increment(ref definitionVersion);

        private AudioEngine GetEngine(string key)
        {
            EnsureDefinitionIndex();
            if (!definitionIndex.TryGetValue(key, out var selected))
                throw new KeyNotFoundException("Engine key '" + key + "' is not defined in OrbitContext '" + name + "'.");
            engines ??= new Dictionary<string, AudioEngine>(StringComparer.Ordinal);
            if (engines.TryGetValue(key, out var existing)) return existing;
            if (selected.Settings == null) throw new InvalidOperationException("Assign AudioEngineSettings for Engine '" + key + "'.");
            var engine = new AudioEngine(selected.Settings);
            try
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) ConfigureEditorEngine(engine);
#endif
                foreach (var library in selected.Libraries ?? Array.Empty<AudioLibrary>())
                    engine.RegisterLibrary(library);
                engines.Add(key, engine);
                return engine;
            }
            catch { engine.Dispose(); throw; }
        }

        private void ReleaseEngines()
        {
            indexedDefinitions = null;
            indexedDefinitionVersion = -1;
            definitionIndex?.Clear();
            if (engines != null)
            {
                foreach (var engine in engines.Values) engine.Dispose();
                engines.Clear();
            }
#if UNITY_EDITOR
            ReleaseEditorPreview();
#endif
        }

        private static void ResetEngines() { if (instance != null) instance.ReleaseEngines(); }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!IsPreloadedEditorContext()) return;
            if (instance != null && instance != this)
            {
                instance.ReleaseEngines();
                instance = null;
            }
#endif
            RegisterInstance();
        }

        private void RegisterInstance()
        {
            if (instance != null && instance != this)
            {
                Debug.LogError("Another OrbitContext is already registered. Only one Context can be active.", this);
                return;
            }
            instance = this;
        }

        private void OnDisable()
        {
            ReleaseEngines();
            if (instance == this) instance = null;
        }
    }
}
