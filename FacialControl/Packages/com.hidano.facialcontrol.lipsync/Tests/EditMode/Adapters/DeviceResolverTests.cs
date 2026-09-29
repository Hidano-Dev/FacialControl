using System;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class DeviceResolverTests : SizedTestFixture
    {
        [Test]
        public void Resolve_AsioMatch_ReturnsAsioKind()
        {
            var descriptor = Descriptor("Shared Device", 0);
            var asio = new FakeAsioDriverEnumerator("Shared Device");
            var mic = new FakeMicrophoneDeviceEnumerator("Shared Device");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Asio));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(0));
            Assert.That(resolution.DeviceNameMatched, Is.EqualTo("Shared Device"));
        }

        [Test]
        public void Resolve_MicMatch_ReturnsMicrophoneKind()
        {
            var descriptor = Descriptor("USB Microphone", 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator("Built-in Mic", "USB Microphone");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Microphone));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(1));
            Assert.That(resolution.DeviceNameMatched, Is.EqualTo("USB Microphone"));
        }

        [Test]
        public void Resolve_DisambiguatorIndex_SelectsNthMatch()
        {
            var descriptor = Descriptor("Dante Mic", 1);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator("Dante Mic", "Other Mic", "Dante Mic");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Microphone));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(2));
            Assert.That(resolution.DeviceNameMatched, Is.EqualTo("Dante Mic"));
        }

        [Test]
        public void Resolve_NoMatch_ReturnsUnresolvedWithSnapshots()
        {
            var descriptor = Descriptor("Missing Device", 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface", "Dante ASIO");
            var mic = new FakeMicrophoneDeviceEnumerator("Built-in Mic", "USB Microphone");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Unresolved));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(-1));
            Assert.That(resolution.DeviceNameMatched, Is.Null);
            CollectionAssert.AreEqual(new[] { "ASIO Fireface", "Dante ASIO" }, resolution.AvailableAsio);
            CollectionAssert.AreEqual(new[] { "Built-in Mic", "USB Microphone" }, resolution.AvailableMic);
        }

        [Test]
        public void Resolve_AsioDisambiguatorOutOfRange_ReturnsUnresolvedWithoutMicFallback()
        {
            var descriptor = Descriptor("Shared Device", 1);
            var asio = new FakeAsioDriverEnumerator("Shared Device");
            var mic = new FakeMicrophoneDeviceEnumerator("Shared Device", "Other Mic", "Shared Device");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Unresolved));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(-1));
            Assert.That(resolution.DeviceNameMatched, Is.Null);
            CollectionAssert.AreEqual(new[] { "Shared Device" }, resolution.AvailableAsio);
            CollectionAssert.AreEqual(new[] { "Shared Device", "Other Mic", "Shared Device" }, resolution.AvailableMic);
        }

        [Test]
        public void Resolve_EmptyDeviceName_FallsBackToFirstMicrophone()
        {
            var descriptor = Descriptor(string.Empty, 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator("Built-in Mic", "USB Microphone");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Microphone));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(0));
            Assert.That(resolution.DeviceNameMatched, Is.EqualTo("Built-in Mic"));
        }

        [Test]
        public void Resolve_NullDeviceName_FallsBackToFirstMicrophone()
        {
            var descriptor = Descriptor(null, 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator("Built-in Mic");

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Microphone));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(0));
            Assert.That(resolution.DeviceNameMatched, Is.EqualTo("Built-in Mic"));
        }

        [Test]
        public void Resolve_EmptyDeviceName_NoMicrophones_ReturnsUnresolved()
        {
            var descriptor = Descriptor(string.Empty, 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator();

            DeviceResolution resolution = DeviceResolver.Resolve(descriptor, asio, mic);

            Assert.That(resolution.Kind, Is.EqualTo(DeviceKind.Unresolved));
            Assert.That(resolution.ResolvedIndex, Is.EqualTo(-1));
            Assert.That(resolution.DeviceNameMatched, Is.Null);
        }

        [Test]
        public void Resolve_NullEnumerator_ThrowsArgumentNullException()
        {
            var descriptor = Descriptor("USB Microphone", 0);
            var asio = new FakeAsioDriverEnumerator("ASIO Fireface");
            var mic = new FakeMicrophoneDeviceEnumerator("USB Microphone");

            Assert.That(
                () => DeviceResolver.Resolve(descriptor, null, mic),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("asioEnumerator"));
            Assert.That(
                () => DeviceResolver.Resolve(descriptor, asio, null),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("micEnumerator"));
        }

        private static DeviceDescriptor Descriptor(string deviceName, int disambiguatorIndex)
        {
            return new DeviceDescriptor
            {
                DeviceName = deviceName,
                DisambiguatorIndex = disambiguatorIndex,
            };
        }
    }
}
