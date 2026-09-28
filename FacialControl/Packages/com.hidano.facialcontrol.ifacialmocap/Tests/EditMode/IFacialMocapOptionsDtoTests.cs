using Hidano.FacialControl.Adapters.Json.Dto;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapOptionsDtoTests : SizedTestFixture
    {
        [Test]
        public void RoundTrip_PreservesValues()
        {
            var dto = new IFacialMocapOptionsDto
            {
                schemaVersion = 1,
                label = "demo",
                receiverEnabled = true,
                listenPort = 49983,
                deviceAddress = "192.168.0.5",
                sendHandshake = true,
                dataVersion = "v2",
                handshakeIntervalSeconds = 2f,
                stalenessSeconds = 0.5f,
                failSafeMode = "holdLastValue",
                enableGaze = false,
                eyeMaxYawDegrees = 12f,
                eyeMaxPitchDegrees = 9f,
                enableHead = true,
                includeHeadPosition = true,
            };

            IFacialMocapOptionsDto back = IFacialMocapOptionsDto.FromJson(dto.ToJson());

            Assert.That(back.listenPort, Is.EqualTo(49983));
            Assert.That(back.deviceAddress, Is.EqualTo("192.168.0.5"));
            Assert.That(back.sendHandshake, Is.True);
            Assert.That(back.dataVersion, Is.EqualTo("v2"));
            Assert.That(back.handshakeIntervalSeconds, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(back.stalenessSeconds, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(back.failSafeMode, Is.EqualTo("holdLastValue"));
            Assert.That(back.enableGaze, Is.False);
            Assert.That(back.eyeMaxYawDegrees, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(back.enableHead, Is.True);
            Assert.That(back.includeHeadPosition, Is.True);
        }

        [Test]
        public void FromJson_NullOrEmpty_ReturnsDefaultInstance()
        {
            IFacialMocapOptionsDto fromNull = IFacialMocapOptionsDto.FromJson(null);
            IFacialMocapOptionsDto fromEmpty = IFacialMocapOptionsDto.FromJson("   ");

            Assert.That(fromNull, Is.Not.Null);
            Assert.That(fromNull.listenPort, Is.EqualTo(49983));
            Assert.That(fromEmpty, Is.Not.Null);
            Assert.That(fromEmpty.dataVersion, Is.EqualTo("standard"));
        }
    }
}
