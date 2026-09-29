using Hidano.FacialControl.Adapters.IFacialMocap;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapRuntimeSettingsSOTests : SizedTestFixture
    {
        [Test]
        public void FromJson_AppliesAllFields()
        {
            var so = ScriptableObject.CreateInstance<IFacialMocapRuntimeSettingsSO>();
            try
            {
                var dto = new IFacialMocapOptionsDto
                {
                    listenPort = 50003,
                    deviceAddress = "10.0.0.2",
                    sendHandshake = true,
                    dataVersion = "v2",
                    handshakeIntervalSeconds = 2f,
                    stalenessSeconds = 1.5f,
                    failSafeMode = "holdLastValue",
                    enableGaze = true,
                    eyeMaxYawDegrees = 22f,
                    eyeMaxPitchDegrees = 18f,
                    enableHead = false,
                    includeHeadPosition = true,
                };

                so.FromJson(dto.ToJson());

                Assert.That(so.ListenPort, Is.EqualTo(50003));
                Assert.That(so.DeviceAddress, Is.EqualTo("10.0.0.2"));
                Assert.That(so.SendHandshake, Is.True);
                Assert.That(so.DataVersion, Is.EqualTo(IFacialMocapDataVersion.V2));
                Assert.That(so.StalenessSeconds, Is.EqualTo(1.5f).Within(1e-4f));
                Assert.That(so.FailSafeMode, Is.EqualTo(FailSafeMode.HoldLastValue));
                Assert.That(so.EnableHead, Is.False);
                Assert.That(so.IncludeHeadPosition, Is.True);
                Assert.That(so.EyeGaze.maxYawDegrees, Is.EqualTo(22f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void FromJson_InvalidValues_AreNormalized()
        {
            var so = ScriptableObject.CreateInstance<IFacialMocapRuntimeSettingsSO>();
            try
            {
                var dto = new IFacialMocapOptionsDto
                {
                    listenPort = 0,
                    handshakeIntervalSeconds = 0f,
                    stalenessSeconds = -5f,
                    dataVersion = "bogus",
                    failSafeMode = "bogus",
                };

                so.FromJson(dto.ToJson());

                Assert.That(so.ListenPort, Is.EqualTo(IFacialMocapRuntimeSettingsSO.DefaultListenPort));
                Assert.That(
                    so.HandshakeIntervalSeconds,
                    Is.EqualTo(IFacialMocapRuntimeSettingsSO.DefaultHandshakeIntervalSeconds).Within(1e-4f));
                Assert.That(so.StalenessSeconds, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(so.DataVersion, Is.EqualTo(IFacialMocapDataVersion.Standard));
                Assert.That(so.FailSafeMode, Is.EqualTo(FailSafeMode.RevertToBase));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ToDataVersionString_RoundTrips()
        {
            Assert.That(
                IFacialMocapRuntimeSettingsSO.ToDataVersion(
                    IFacialMocapRuntimeSettingsSO.ToDataVersionString(IFacialMocapDataVersion.V2)),
                Is.EqualTo(IFacialMocapDataVersion.V2));
            Assert.That(
                IFacialMocapRuntimeSettingsSO.ToDataVersion(
                    IFacialMocapRuntimeSettingsSO.ToDataVersionString(IFacialMocapDataVersion.Standard)),
                Is.EqualTo(IFacialMocapDataVersion.Standard));
        }
    }
}
