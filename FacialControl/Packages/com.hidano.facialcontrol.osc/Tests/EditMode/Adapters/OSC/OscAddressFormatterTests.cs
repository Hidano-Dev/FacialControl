using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class OscAddressFormatterTests : SizedTestFixture
    {
        [Test]
        public void TryFormatBlendShapeAddress_VRChatNameWithMultibyteAndSymbols_WritesCompleteAddress()
        {
            const string blendShapeName = "\u7b11\u9854_\u53e3.\u3042";
            Span<char> destination = stackalloc char[64];

            bool formatted = OscAddressFormatter.TryFormatBlendShapeAddress(
                AddressPresetKind.VRChat,
                blendShapeName.AsSpan(),
                destination,
                out int written);

            Assert.IsTrue(formatted);
            Assert.AreEqual("/avatar/parameters/" + blendShapeName, ToString(destination.Slice(0, written)));
        }

        [Test]
        public void TryFormatBlendShapeAddress_ARKitNameWithMultibyteAndSymbols_WritesCompleteAddress()
        {
            const string blendShapeName = "\u7b11\u9854_\u53e3.\u3042";
            Span<char> destination = stackalloc char[64];

            bool formatted = OscAddressFormatter.TryFormatBlendShapeAddress(
                AddressPresetKind.ARKit,
                blendShapeName.AsSpan(),
                destination,
                out int written);

            Assert.IsTrue(formatted);
            Assert.AreEqual("/ARKit/" + blendShapeName, ToString(destination.Slice(0, written)));
        }

        [Test]
        public void TryFormatBlendShapeAddress_DestinationTooShort_ReturnsFalseAndRequiredLength()
        {
            const string blendShapeName = "jawOpen";
            Span<char> destination = stackalloc char[4];

            bool formatted = OscAddressFormatter.TryFormatBlendShapeAddress(
                AddressPresetKind.VRChat,
                blendShapeName.AsSpan(),
                destination,
                out int written);

            Assert.IsFalse(formatted);
            Assert.AreEqual(OscAddressFormatter.GetBlendShapeAddressLength(
                AddressPresetKind.VRChat,
                blendShapeName.AsSpan()), written);
        }

        [Test]
        public void TryFormatGazeAddress_VRChatExpressionIdWithSymbols_WritesAxisAddress()
        {
            const string expressionId = "\u8996\u7dda.left_01";
            Span<char> destination = stackalloc char[64];

            bool formatted = OscAddressFormatter.TryFormatGazeAddress(
                AddressPresetKind.VRChat,
                expressionId.AsSpan(),
                OscAddressFormatter.VRChatGazeXAxis,
                destination,
                out int written);

            Assert.IsTrue(formatted);
            Assert.AreEqual("/avatar/parameters/" + expressionId + "X", ToString(destination.Slice(0, written)));
        }

        [Test]
        public void FormatBlendShapeAddress_VRChatName_ReturnsCompleteAddressString()
        {
            Assert.AreEqual(
                "/avatar/parameters/eyeBlinkLeft",
                OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.VRChat, "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_ARKitName_ReturnsCompleteAddressString()
        {
            Assert.AreEqual(
                "/ARKit/eyeBlinkLeft",
                OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.ARKit, "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_CustomPrefix_ReturnsCompleteAddressString()
        {
            Assert.AreEqual(
                "/myapp/eyeBlinkLeft",
                OscAddressFormatter.FormatBlendShapeAddress("/myapp/", "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_CustomPrefixWithoutLeadingSlash_DoesNotAddSlash()
        {
            Assert.AreEqual(
                "myapp/eyeBlinkLeft",
                OscAddressFormatter.FormatBlendShapeAddress("myapp/", "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_CustomPreset_ThrowsNotSupportedException()
        {
            Assert.Throws<NotSupportedException>(() =>
                OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.Custom, "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_NullCustomPrefix_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                OscAddressFormatter.FormatBlendShapeAddress(null, "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddress_EmptyCustomPrefix_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                OscAddressFormatter.FormatBlendShapeAddress(string.Empty, "eyeBlinkLeft"));
        }

        [Test]
        public void FormatBlendShapeAddressUtf8_CustomPrefix_ReturnsCompleteAddressBytes()
        {
            byte[] bytes = OscAddressFormatter.FormatBlendShapeAddressUtf8("/myapp/", "eyeBlinkLeft");

            Assert.AreEqual("/myapp/eyeBlinkLeft", Encoding.UTF8.GetString(bytes));
        }

        [Test]
        public void GetOrAddBlendShapeAddressUtf8_SameNameAndPreset_ReturnsCachedBytes()
        {
            var pool = new Dictionary<(string name, AddressPresetKind preset), byte[]>();

            byte[] first = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                AddressPresetKind.ARKit,
                "eyeBlinkLeft");
            byte[] second = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                AddressPresetKind.ARKit,
                "eyeBlinkLeft");

            Assert.AreSame(first, second);
            Assert.AreEqual("/ARKit/eyeBlinkLeft", Encoding.UTF8.GetString(first));
        }

        [Test]
        public void GetOrAddBlendShapeAddressUtf8_SameNameDifferentPreset_ReturnsDifferentAddresses()
        {
            var pool = new Dictionary<(string name, AddressPresetKind preset), byte[]>();

            byte[] vrchat = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                AddressPresetKind.VRChat,
                "eyeBlinkLeft");
            byte[] arkit = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                AddressPresetKind.ARKit,
                "eyeBlinkLeft");

            Assert.AreNotSame(vrchat, arkit);
            Assert.AreEqual("/avatar/parameters/eyeBlinkLeft", Encoding.UTF8.GetString(vrchat));
            Assert.AreEqual("/ARKit/eyeBlinkLeft", Encoding.UTF8.GetString(arkit));
        }

        [Test]
        public void GetOrAddBlendShapeAddressUtf8_SameNameAndCustomPrefix_ReturnsCachedBytes()
        {
            var pool = new Dictionary<(string name, string customPrefix), byte[]>();

            byte[] first = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                "/myapp/",
                "eyeBlinkLeft");
            byte[] second = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                "/myapp/",
                "eyeBlinkLeft");

            Assert.AreSame(first, second);
            Assert.AreEqual("/myapp/eyeBlinkLeft", Encoding.UTF8.GetString(first));
        }

        [Test]
        public void GetOrAddBlendShapeAddressUtf8_SameNameDifferentCustomPrefix_ReturnsDifferentAddresses()
        {
            var pool = new Dictionary<(string name, string customPrefix), byte[]>();

            byte[] first = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                "/first/",
                "eyeBlinkLeft");
            byte[] second = OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                pool,
                "/second/",
                "eyeBlinkLeft");

            Assert.AreNotSame(first, second);
            Assert.AreEqual("/first/eyeBlinkLeft", Encoding.UTF8.GetString(first));
            Assert.AreEqual("/second/eyeBlinkLeft", Encoding.UTF8.GetString(second));
        }

        private static string ToString(ReadOnlySpan<char> value)
        {
            return new string(value.ToArray());
        }
    }
}
