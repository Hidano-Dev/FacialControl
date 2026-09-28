using System;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class PerfectSyncEyeLookTests : SizedTestFixture
    {
        private const float Tolerance = 1e-6f;

        [Test]
        public void Names_StaticArray_IsAsciiAndInPerfectSyncOrder()
        {
            CollectionAssert.AreEqual(new[]
            {
                "eyeLookInLeft",
                "eyeLookOutLeft",
                "eyeLookUpLeft",
                "eyeLookDownLeft",
                "eyeLookInRight",
                "eyeLookOutRight",
                "eyeLookUpRight",
                "eyeLookDownRight"
            }, PerfectSyncEyeLook.Names);

            foreach (var name in PerfectSyncEyeLook.Names)
            {
                foreach (char c in name)
                {
                    Assert.LessOrEqual(c, 0x7f, $"{name} は ASCII 固定名である必要があります。");
                }
            }
        }

        [Test]
        public void GetArKitAddressUtf8Bytes_EachName_ReturnsPrecomputedArKitAddressBytes()
        {
            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                var bytes = PerfectSyncEyeLook.GetArKitAddressUtf8Bytes(i).ToArray();
                var address = Encoding.UTF8.GetString(bytes);

                Assert.AreEqual("/ARKit/" + PerfectSyncEyeLook.Names[i], address);
            }
        }

        [Test]
        public void GetArKitAddressUtf8Bytes_IndexOutOfRange_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfectSyncEyeLook.GetArKitAddressUtf8Bytes(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfectSyncEyeLook.GetArKitAddressUtf8Bytes(PerfectSyncEyeLook.Count));
        }

        [Test]
        public void Compose_AsymmetricVectors_WritesExpectedSignChannels()
        {
            Span<float> output = stackalloc float[PerfectSyncEyeLook.Count];

            PerfectSyncEyeLook.Compose(new Vector2(0.7f, -0.3f), new Vector2(-0.25f, 0.8f), output);

            Assert.AreEqual(0f, output[PerfectSyncEyeLook.EyeLookInLeftIndex], Tolerance);
            Assert.AreEqual(0.7f, output[PerfectSyncEyeLook.EyeLookOutLeftIndex], Tolerance);
            Assert.AreEqual(0f, output[PerfectSyncEyeLook.EyeLookUpLeftIndex], Tolerance);
            Assert.AreEqual(0.3f, output[PerfectSyncEyeLook.EyeLookDownLeftIndex], Tolerance);
            Assert.AreEqual(0.25f, output[PerfectSyncEyeLook.EyeLookInRightIndex], Tolerance);
            Assert.AreEqual(0f, output[PerfectSyncEyeLook.EyeLookOutRightIndex], Tolerance);
            Assert.AreEqual(0.8f, output[PerfectSyncEyeLook.EyeLookUpRightIndex], Tolerance);
            Assert.AreEqual(0f, output[PerfectSyncEyeLook.EyeLookDownRightIndex], Tolerance);
        }

        [Test]
        public void Decompose_SignDefinition_UsesOutMinusInAndUpMinusDown()
        {
            ReadOnlySpan<float> input = stackalloc float[]
            {
                0.2f, 0.9f, 0.8f, 0.1f,
                0.4f, 0.1f, 0.15f, 0.55f
            };

            PerfectSyncEyeLook.Decompose(input, out var left, out var right);

            Assert.AreEqual(0.7f, left.x, Tolerance);
            Assert.AreEqual(0.7f, left.y, Tolerance);
            Assert.AreEqual(-0.3f, right.x, Tolerance);
            Assert.AreEqual(-0.4f, right.y, Tolerance);
        }

        [TestCase(0.75f, 0f, -0.75f, 0f)]
        [TestCase(0.8f, 0.2f, 0f, -0.4f)]
        [TestCase(-0.35f, 0.9f, 0.6f, -0.1f)]
        [TestCase(0f, -1f, 1f, 1f)]
        public void ComposeThenDecompose_AsymmetricRepresentativeCases_RestoresVectors(
            float leftX,
            float leftY,
            float rightX,
            float rightY)
        {
            Span<float> output = stackalloc float[PerfectSyncEyeLook.Count];
            var expectedLeft = new Vector2(leftX, leftY);
            var expectedRight = new Vector2(rightX, rightY);

            PerfectSyncEyeLook.Compose(expectedLeft, expectedRight, output);
            PerfectSyncEyeLook.Decompose(output, out var actualLeft, out var actualRight);

            Assert.AreEqual(expectedLeft.x, actualLeft.x, Tolerance);
            Assert.AreEqual(expectedLeft.y, actualLeft.y, Tolerance);
            Assert.AreEqual(expectedRight.x, actualRight.x, Tolerance);
            Assert.AreEqual(expectedRight.y, actualRight.y, Tolerance);
        }

        [Test]
        public void Compose_OutputTooShort_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(ComposeWithTooShortOutput);
        }

        [Test]
        public void Decompose_InputTooShort_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(DecomposeWithTooShortInput);
        }

        private static void ComposeWithTooShortOutput()
        {
            Span<float> output = stackalloc float[PerfectSyncEyeLook.Count - 1];

            PerfectSyncEyeLook.Compose(Vector2.zero, Vector2.zero, output);
        }

        private static void DecomposeWithTooShortInput()
        {
            ReadOnlySpan<float> input = stackalloc float[PerfectSyncEyeLook.Count - 1];

            PerfectSyncEyeLook.Decompose(input, out _, out _);
        }
    }
}
