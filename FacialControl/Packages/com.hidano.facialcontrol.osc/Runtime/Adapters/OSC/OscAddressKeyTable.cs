using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly struct OscAddressResolution
    {
        public OscAddressResolution(int mappingIndex, int gazeRouteSet, int listenerSlot, OscControlKind control)
        {
            MappingIndex = mappingIndex;
            GazeRouteSet = gazeRouteSet;
            ListenerSlot = listenerSlot;
            Control = control;
        }

        public readonly int MappingIndex;
        public readonly int GazeRouteSet;
        public readonly int ListenerSlot;
        public readonly OscControlKind Control;

        public bool IsKnownNormalBlendShape => MappingIndex >= 0;
        public bool IsUnmapped => MappingIndex < 0 && GazeRouteSet < 0 && ListenerSlot < 0 && Control == OscControlKind.None;
    }

    public sealed class OscAddressKeyTable
    {
        private readonly Entry[] _entries;
        private readonly int[] _buckets;

        public static readonly OscAddressKeyTable Empty = new OscAddressKeyTable(0, 0, Array.Empty<Entry>(), new int[1]);

        private OscAddressKeyTable(int version, int entryCount, Entry[] entries, int[] buckets)
        {
            Version = version;
            EntryCount = entryCount;
            _entries = entries;
            _buckets = buckets;
        }

        public int Version { get; }
        public int EntryCount { get; }

        public bool TryResolve(ReadOnlySpan<byte> addressUtf8, out OscAddressResolution resolution)
        {
            uint hash = Hash(addressUtf8);
            int mask = _buckets.Length - 1;
            int bucket = (int)hash & mask;
            while (true)
            {
                int stored = _buckets[bucket];
                if (stored == 0)
                {
                    resolution = default;
                    return false;
                }

                Entry entry = _entries[stored - 1];
                if (entry.Hash == hash && entry.KeyUtf8.Length == addressUtf8.Length && BytesEqual(entry.KeyUtf8, addressUtf8))
                {
                    resolution = new OscAddressResolution(entry.MappingIndex, entry.GazeRouteSet, entry.ListenerSlot, entry.Control);
                    return true;
                }

                bucket = (bucket + 1) & mask;
            }
        }

        public static uint Hash(ReadOnlySpan<byte> bytes)
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }

            return hash;
        }

        private static bool BytesEqual(byte[] left, ReadOnlySpan<byte> right)
        {
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class MutableEntry
        {
            public byte[] KeyUtf8;
            public int MappingIndex = -1;
            public int GazeRouteSet = -1;
            public int ListenerSlot = -1;
            public OscControlKind Control;
        }

        private readonly struct Entry
        {
            public Entry(MutableEntry source)
            {
                KeyUtf8 = source.KeyUtf8;
                Hash = OscAddressKeyTable.Hash(KeyUtf8);
                MappingIndex = source.MappingIndex;
                GazeRouteSet = source.GazeRouteSet;
                ListenerSlot = source.ListenerSlot;
                Control = source.Control;
            }

            public readonly byte[] KeyUtf8;
            public readonly uint Hash;
            public readonly int MappingIndex;
            public readonly int GazeRouteSet;
            public readonly int ListenerSlot;
            public readonly OscControlKind Control;
        }

        public sealed class Builder
        {
            private readonly Dictionary<string, byte[]> _utf8Pool;
            private readonly Dictionary<string, MutableEntry> _entries = new Dictionary<string, MutableEntry>(StringComparer.Ordinal);
            private readonly HashSet<string> _exactAddresses = new HashSet<string>(StringComparer.Ordinal);
            private OscMapping[] _runtimeMappings = Array.Empty<OscMapping>();
            private IReadOnlyList<string> _gazeAddresses;
            private IReadOnlyList<string> _listenerAddresses;

            public Builder(Dictionary<string, byte[]> utf8Pool)
            {
                _utf8Pool = utf8Pool ?? throw new ArgumentNullException(nameof(utf8Pool));
            }

            public Builder SetMappings(OscMapping[] runtimeMappings)
            {
                _runtimeMappings = runtimeMappings ?? Array.Empty<OscMapping>();
                return this;
            }

            public Builder SetGazeAddresses(IReadOnlyList<string> addresses)
            {
                _gazeAddresses = addresses;
                return this;
            }

            public Builder SetListenerAddresses(IReadOnlyList<string> addresses)
            {
                _listenerAddresses = addresses;
                return this;
            }

            public OscAddressKeyTable Build(int version)
            {
                _entries.Clear();
                _exactAddresses.Clear();

                for (int i = 0; i < _runtimeMappings.Length; i++)
                {
                    string address = _runtimeMappings[i].OscAddress;
                    if (!string.IsNullOrEmpty(address))
                    {
                        _exactAddresses.Add(address);
                        AddMapping(address, i, false);
                    }
                }

                for (int i = 0; i < _runtimeMappings.Length; i++)
                {
                    string name = _runtimeMappings[i].BlendShapeName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        AddMapping(OscAddressFormatter.VRChatParameterPrefix + name, i, true);
                        AddMapping(OscAddressFormatter.ARKitParameterPrefix + name, i, true);
                    }
                }

                AddRouteFlags(_gazeAddresses, true);
                AddRouteFlags(_listenerAddresses, false);
                AddControl(OscControlAddresses.SenderId, OscControlKind.SenderId);
                AddControl(OscControlAddresses.Values, OscControlKind.Values);
                AddControl(OscControlAddresses.Layout, OscControlKind.Layout);

                var entries = new Entry[_entries.Count];
                int index = 0;
                foreach (MutableEntry entry in _entries.Values)
                {
                    entries[index++] = new Entry(entry);
                }

                int bucketCount = 1;
                while (bucketCount < entries.Length * 2)
                {
                    bucketCount <<= 1;
                }

                var buckets = new int[bucketCount];
                int mask = bucketCount - 1;
                for (int i = 0; i < entries.Length; i++)
                {
                    int bucket = (int)entries[i].Hash & mask;
                    while (buckets[bucket] != 0)
                    {
                        bucket = (bucket + 1) & mask;
                    }

                    buckets[bucket] = i + 1;
                }

                return new OscAddressKeyTable(version, entries.Length, entries, buckets);
            }

            private void AddMapping(string address, int mappingIndex, bool fallback)
            {
                if (string.IsNullOrEmpty(address) || (fallback && _exactAddresses.Contains(address)))
                {
                    return;
                }

                MutableEntry entry = GetOrAdd(address);
                entry.MappingIndex = mappingIndex;
            }

            private void AddRouteFlags(IReadOnlyList<string> addresses, bool gaze)
            {
                if (addresses == null)
                {
                    return;
                }

                for (int i = 0; i < addresses.Count; i++)
                {
                    string address = addresses[i];
                    if (string.IsNullOrEmpty(address))
                    {
                        continue;
                    }

                    MutableEntry entry = GetOrAdd(address);
                    if (gaze)
                    {
                        entry.GazeRouteSet = i;
                    }
                    else
                    {
                        entry.ListenerSlot = i;
                    }
                }
            }

            private void AddControl(string address, OscControlKind control)
            {
                MutableEntry entry = GetOrAdd(address);
                entry.Control = control;
            }

            private MutableEntry GetOrAdd(string address)
            {
                if (_entries.TryGetValue(address, out MutableEntry entry))
                {
                    return entry;
                }

                entry = new MutableEntry { KeyUtf8 = OscAddressFormatter.GetOrAddAddressUtf8(_utf8Pool, address) };
                _entries.Add(address, entry);
                return entry;
            }
        }
    }
}
