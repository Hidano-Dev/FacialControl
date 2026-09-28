using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using Unity.Profiling;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class HeartbeatHashHelperTests : SizedTestFixture
    {
        private const int ZeroAllocIterations = 50000;

        [Test]
        public void ComputeFnv1a_SameInput_ReturnsSameHash()
        {
            var names = new[] { "Blink", "Joy", "Angry" };

            uint first = HeartbeatHashHelper.ComputeFnv1a(names);
            uint second = HeartbeatHashHelper.ComputeFnv1a(names);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void ComputeFnv1a_DifferentOrder_ReturnsDifferentHash()
        {
            uint first = HeartbeatHashHelper.ComputeFnv1a(new[] { "Blink", "Joy", "Angry" });
            uint second = HeartbeatHashHelper.ComputeFnv1a(new[] { "Angry", "Joy", "Blink" });

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void ComputeFnv1a_AdjacentNames_UsesSeparator()
        {
            uint splitAfterA = HeartbeatHashHelper.ComputeFnv1a(new[] { "a", "bc" });
            uint splitAfterB = HeartbeatHashHelper.ComputeFnv1a(new[] { "ab", "c" });

            Assert.AreNotEqual(splitAfterA, splitAfterB);
        }

        [Test]
        public void ComputeFnv1a_NullOrEmpty_ReturnsOffsetBasis()
        {
            Assert.AreEqual(HeartbeatHashHelper.Fnv1aOffsetBasis, HeartbeatHashHelper.ComputeFnv1a(null));
            Assert.AreEqual(HeartbeatHashHelper.Fnv1aOffsetBasis, HeartbeatHashHelper.ComputeFnv1a(new string[0]));
            Assert.AreEqual(
                HeartbeatHashHelper.Fnv1aOffsetBasis,
                HeartbeatHashHelper.ComputeFnv1a(new[] { "Blink" }, 0, 0));
        }

        [Test]
        public void ComputeFnv1a_NullName_TreatsNameAsEmpty()
        {
            uint nullNameHash = HeartbeatHashHelper.ComputeFnv1a(new string[] { null });
            uint emptyNameHash = HeartbeatHashHelper.ComputeFnv1a(new[] { string.Empty });

            Assert.AreEqual(emptyNameHash, nullNameHash);
        }

        [Test]
        public void ComputeFnv1a_Range_MatchesEquivalentSubsequence()
        {
            var names = new[] { "IgnoredHead", "Blink", "Joy", "IgnoredTail" };

            uint ranged = HeartbeatHashHelper.ComputeFnv1a(names, 1, 2);
            uint direct = HeartbeatHashHelper.ComputeFnv1a(new[] { "Blink", "Joy" });

            Assert.AreEqual(direct, ranged);
        }

        [Test]
        public void ComputeFnv1a_InvalidRange_ThrowsArgumentOutOfRangeException()
        {
            var names = new[] { "Blink" };

            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeartbeatHashHelper.ComputeFnv1a(names, -1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeartbeatHashHelper.ComputeFnv1a(names, 0, -1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeartbeatHashHelper.ComputeFnv1a(names, 1, 1));
        }

        [Test]
        public void ComputeFnv1a_UsesUtf16BytesAndFnv1aConstants()
        {
            uint expected = ComputeReferenceFnv1a(new[] { "Blink", "Joy" }, 0, 2);

            uint actual = HeartbeatHashHelper.ComputeFnv1a(new[] { "Blink", "Joy" });

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ComputeFnv1a_HotPath_AllocatesZeroManagedMemory()
        {
            IReadOnlyList<string> names = new[] { "Blink", "Joy", "Angry", "Smile" };

            for (int i = 0; i < 16; i++)
            {
                HeartbeatHashHelper.ComputeFnv1a(names);
                HeartbeatHashHelper.ComputeFnv1a(names, 1, 2);
            }

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int i = 0; i < ZeroAllocIterations; i++)
            {
                HeartbeatHashHelper.ComputeFnv1a(names);
                HeartbeatHashHelper.ComputeFnv1a(names, 1, 2);
            }

            Assert.That(recorder.LastValue, Is.LessThanOrEqualTo(0L));
        }

        private static uint ComputeReferenceFnv1a(IReadOnlyList<string> names, int startIndex, int count)
        {
            unchecked
            {
                uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;
                int endIndex = startIndex + count;
                for (int i = startIndex; i < endIndex; i++)
                {
                    string name = names[i];
                    if (!string.IsNullOrEmpty(name))
                    {
                        for (int charIndex = 0; charIndex < name.Length; charIndex++)
                        {
                            char value = name[charIndex];
                            hash ^= (byte)value;
                            hash *= HeartbeatHashHelper.Fnv1aPrime;
                            hash ^= (byte)(value >> 8);
                            hash *= HeartbeatHashHelper.Fnv1aPrime;
                        }
                    }

                    hash ^= 0;
                    hash *= HeartbeatHashHelper.Fnv1aPrime;
                }

                return hash;
            }
        }
    }
}
