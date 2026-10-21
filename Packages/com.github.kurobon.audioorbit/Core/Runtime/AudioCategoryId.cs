using System;
using UnityEngine;

namespace AudioOrbit
{
    /// <summary>Serialized stable identifier. Renaming an asset must not change this value.</summary>
    [Serializable]
    public struct AudioCategoryId : IEquatable<AudioCategoryId>
    {
        [SerializeField] private string value;
        public string Value => value ?? string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(value);

        public AudioCategoryId(string value)
        {
            this.value = value;
        }

        public bool Equals(AudioCategoryId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is AudioCategoryId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(AudioCategoryId left, AudioCategoryId right) => left.Equals(right);
        public static bool operator !=(AudioCategoryId left, AudioCategoryId right) => !left.Equals(right);
    }
}