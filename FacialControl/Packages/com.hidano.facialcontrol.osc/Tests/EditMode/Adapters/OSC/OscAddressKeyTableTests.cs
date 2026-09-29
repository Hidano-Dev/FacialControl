using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Adapters.OSC.Tests
{
    [SmallTest]
    public sealed class OscAddressKeyTableTests : SizedTestFixture
    {
        [Test]
        public void Builder_UsesExactAddressBeforeFallbackAndLastMappingWins()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Smile", "Smile", "layer"),
                new OscMapping("/custom/Smile", "Smile", "layer"),
                new OscMapping("/custom/Smile", "Smile", "layer")
            };

            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(mappings)
                .Build(7);

            Assert.That(table.TryResolve(Utf8("/custom/Smile"), out var exact), Is.True);
            Assert.That(exact.MappingIndex, Is.EqualTo(2));
            Assert.That(table.TryResolve(Utf8("/ARKit/Smile"), out var fallback), Is.True);
            Assert.That(fallback.MappingIndex, Is.EqualTo(2));
            Assert.That(table.TryResolve(Utf8("/avatar/parameters/Smile"), out var exactPrefix), Is.True);
            Assert.That(exactPrefix.MappingIndex, Is.EqualTo(0));
        }

        [Test]
        public void Builder_SharesUtf8AndCombinesMappingGazeListenerAndControlFlags()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/shared", "shape", "layer") })
                .SetGazeAddresses(new[] { "/shared" })
                .SetListenerAddresses(new[] { "/shared" })
                .Build(11);

            Assert.That(table.TryResolve(Utf8("/shared"), out var resolution), Is.True);
            Assert.That(resolution.MappingIndex, Is.EqualTo(0));
            Assert.That(resolution.GazeRouteSet, Is.EqualTo(0));
            Assert.That(resolution.ListenerSlot, Is.EqualTo(0));
            Assert.That(resolution.Control, Is.EqualTo(OscControlKind.None));

            var controlTable = new OscAddressKeyTable.Builder(pool).Build(12);
            Assert.That(controlTable.TryResolve(Utf8(OscControlAddresses.SenderId), out var sender), Is.True);
            Assert.That(sender.Control, Is.EqualTo(OscControlKind.SenderId));
            Assert.That(ReferenceEquals(pool["/shared"], pool["/shared"]), Is.True);
        }

        [Test]
        public void TryResolve_DoesNotAllocateForKnownOrUnknownAddress()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/known", "known", "layer") })
                .Build(1);
            byte[] known = Utf8("/known");
            byte[] unknown = Utf8("/unknown");

            table.TryResolve(known, out _);
            table.TryResolve(unknown, out _);
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                table.TryResolve(known, out _);
                table.TryResolve(unknown, out _);
            }, 100);

            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void TryResolve_MatchesUtf8SpecialCharactersAndGazeAddressesWithoutSubstring()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("", "笑顔.special", "layer") })
                .SetGazeAddresses(new[]
                {
                    "/avatar/parameters/GazePatternX",
                    "/avatar/parameters/GazePatternY",
                    "/ARKit/eyeLookUpLeft"
                })
                .Build(3);

            Assert.That(table.TryResolve(Utf8("/avatar/parameters/笑顔.special"), out var vrChat), Is.True);
            Assert.That(vrChat.MappingIndex, Is.EqualTo(0));
            Assert.That(table.TryResolve(Utf8("/ARKit/笑顔.special"), out var arKit), Is.True);
            Assert.That(arKit.MappingIndex, Is.EqualTo(0));

            Assert.That(table.TryResolve(Utf8("/avatar/parameters/GazePatternX"), out var gazeX), Is.True);
            Assert.That(gazeX.GazeRouteSet, Is.EqualTo(0));
            Assert.That(table.TryResolve(Utf8("/avatar/parameters/GazePatternY"), out var gazeY), Is.True);
            Assert.That(gazeY.GazeRouteSet, Is.EqualTo(1));
            Assert.That(table.TryResolve(Utf8("/ARKit/eyeLookUpLeft"), out var eyeLook), Is.True);
            Assert.That(eyeLook.GazeRouteSet, Is.EqualTo(2));
        }

        [Test]
        public void TryResolve_ProbesPastInitialBucketCollisionWithoutAllocating()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/known", "known", "layer") })
                .Build(4);
            byte[] known = Utf8("/known");
            string collidingUnknownText = FindDifferentAddressWithSameInitialBucket(known, table);
            byte[] collidingUnknown = Utf8(collidingUnknownText);

            Assert.That(table.TryResolve(known, out var resolution), Is.True);
            Assert.That(resolution.MappingIndex, Is.EqualTo(0));

            bool anyResolved = false;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                if (table.TryResolve(collidingUnknown, out _)) anyResolved = true;
            }, 100);

            Assert.That(anyResolved, Is.False);
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static string FindDifferentAddressWithSameInitialBucket(byte[] known, OscAddressKeyTable table)
        {
            int bucketCount = 1;
            while (bucketCount < table.EntryCount * 2)
            {
                bucketCount <<= 1;
            }

            int mask = bucketCount - 1;
            int knownBucket = (int)OscAddressKeyTable.Hash(known) & mask;
            for (int i = 0; i < 10000; i++)
            {
                string candidate = "/unknown" + i;
                if (candidate == "/known")
                {
                    continue;
                }

                if (((int)OscAddressKeyTable.Hash(Utf8(candidate)) & mask) == knownBucket)
                {
                    return candidate;
                }
            }

            Assert.Fail("Could not construct a deterministic open-addressing collision.");
            return string.Empty;
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    }
}
