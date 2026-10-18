using System.Collections.Generic;
using System.Linq;

namespace EnumClass.Generator
{
    /// <summary>
    /// Compile-time construction of a minimal perfect hash (hash-and-displace,
    /// cf. Belazzougui/Botelho/Dietzfelbinger "CHD" and Hanov's simplification).
    ///
    /// For n keys: n buckets, n slots.
    ///   b    = Reduce(H(key, 0), n)
    ///   d    = G[b]
    ///   slot = d &lt; 0 ? -d - 1 : Reduce(H(key, d), n)
    /// Multi-key buckets get a seed d &gt;= 1 that scatters them into free slots;
    /// single-key buckets are placed directly (negative entry). Empty buckets keep 0;
    /// a foreign key hitting one lands on some slot and fails the final comparison.
    ///
    /// IMPORTANT: <see cref="Hash"/> must stay bit-identical to
    /// EnumClass.Internal.PerfectHash.Hash in <see cref="RuntimeSources.PerfectHash"/>.
    /// </summary>
    internal static class PerfectHashBuilder
    {
        private const uint MaxSeed = 1u << 20;

        public static uint Hash(string key, uint seed, bool fold)
        {
            unchecked
            {
                uint h = 2166136261u ^ (seed * 0x9E3779B9u);
                foreach (char ch in key)
                {
                    uint c = ch;
                    if (fold && c - 'A' <= 25u) c |= 0x20u; // ASCII fold only
                    h = (h ^ c) * 16777619u;                 // FNV-1a step
                }
                // murmur3 fmix32 finalizer: makes the high bits usable for Reduce
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Lemire's multiply-shift range reduction: maps h uniformly to [0, n).</summary>
        public static int Reduce(uint h, int n) => (int)(((ulong)h * (uint)n) >> 32);

        public static string FoldAscii(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if ((uint)(chars[i] - 'A') <= 25u) chars[i] = (char)(chars[i] | 0x20);
            return new string(chars);
        }

        /// <param name="displacements">G, indexed by bucket.</param>
        /// <param name="slots">slot → index into <paramref name="keys"/>.</param>
        public static bool TryBuild(string[] keys, bool fold, out int[] displacements, out int[] slots)
        {
            int n = keys.Length;
            displacements = new int[n];
            slots = Enumerable.Repeat(-1, n).ToArray();
            if (n == 0) return true;

            // Keys equal under the hash's notion of equality can never be separated.
            var seen = new HashSet<string>();
            foreach (var k in keys)
                if (!seen.Add(fold ? FoldAscii(k) : k)) return false;

            var buckets = new List<int>[n];
            for (int i = 0; i < n; i++) buckets[i] = new List<int>();
            for (int i = 0; i < n; i++) buckets[Reduce(Hash(keys[i], 0, fold), n)].Add(i);

            // Largest buckets first; ties broken by index so output is deterministic.
            int[] order = Enumerable.Range(0, n)
                .OrderByDescending(b => buckets[b].Count)
                .ThenBy(b => b)
                .ToArray();

            var trial = new List<int>();
            int pos = 0;
            for (; pos < n && buckets[order[pos]].Count > 1; pos++)
            {
                var bucket = buckets[order[pos]];
                uint seed = 1;
                while (!TryPlace(keys, bucket, seed, fold, slots, trial))
                {
                    if (++seed >= MaxSeed) return false;
                }
                for (int j = 0; j < bucket.Count; j++) slots[trial[j]] = bucket[j];
                displacements[order[pos]] = (int)seed;
            }

            int free = 0;
            for (; pos < n && buckets[order[pos]].Count == 1; pos++)
            {
                while (slots[free] >= 0) free++;
                slots[free] = buckets[order[pos]][0];
                displacements[order[pos]] = -free - 1;
            }

            return true;
        }

        private static bool TryPlace(string[] keys, List<int> bucket, uint seed, bool fold, int[] slots, List<int> trial)
        {
            trial.Clear();
            int n = keys.Length;
            foreach (int key in bucket)
            {
                int s = Reduce(Hash(keys[key], seed, fold), n);
                if (slots[s] >= 0 || trial.Contains(s)) return false;
                trial.Add(s);
            }
            return true;
        }
    }
}
