using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class RuntimeMappingResolver
    {
        public readonly struct ResolveResult
        {
            public ResolveResult(
                OscMapping[] runtimeMappings,
                OscReceiverAdapterBinding.MappingOrigin[] origins,
                int manualCount)
            {
                RuntimeMappings = runtimeMappings ?? Array.Empty<OscMapping>();
                Origins = origins ?? Array.Empty<OscReceiverAdapterBinding.MappingOrigin>();
                ManualCount = manualCount;
            }

            public OscMapping[] RuntimeMappings { get; }
            public OscReceiverAdapterBinding.MappingOrigin[] Origins { get; }
            public int ManualCount { get; }
        }

        public static ResolveResult ResolveInitialMappings(IReadOnlyList<OscMappingEntry> manualEntries)
        {
            int manualCount = CountManualEntries(manualEntries);
            if (manualCount == 0)
            {
                return new ResolveResult(
                    Array.Empty<OscMapping>(),
                    Array.Empty<OscReceiverAdapterBinding.MappingOrigin>(),
                    0);
            }

            var runtimeMappings = new OscMapping[manualCount];
            var origins = new OscReceiverAdapterBinding.MappingOrigin[manualCount];
            FillManualMappings(manualEntries, runtimeMappings, origins, out int written);

            return new ResolveResult(runtimeMappings, origins, written);
        }

        private static int CountManualEntries(IReadOnlyList<OscMappingEntry> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidManualEntry(entries[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static void FillManualMappings(
            IReadOnlyList<OscMappingEntry> entries,
            OscMapping[] runtimeMappings,
            OscReceiverAdapterBinding.MappingOrigin[] origins,
            out int written)
        {
            written = 0;
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                OscMappingEntry entry = entries[i];
                if (!IsValidManualEntry(entry))
                {
                    continue;
                }

                runtimeMappings[written] = new OscMapping(entry.addressPattern, entry.expressionId, string.Empty);
                origins[written] = OscReceiverAdapterBinding.MappingOrigin.Manual;
                written++;
            }
        }

        private static bool IsValidManualEntry(OscMappingEntry entry)
        {
            return entry != null &&
                entry.mode == OscMappingMode.Normal_BlendShape &&
                !string.IsNullOrEmpty(entry.expressionId) &&
                !string.IsNullOrEmpty(entry.addressPattern);
        }
    }
}
