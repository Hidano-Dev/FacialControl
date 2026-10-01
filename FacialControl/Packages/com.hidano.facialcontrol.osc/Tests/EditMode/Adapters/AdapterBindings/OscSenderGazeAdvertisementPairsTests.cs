using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// 送信側が gaze 広告に載せる [id, value, ...] の組み立てを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class OscSenderGazeAdvertisementPairsTests : SizedTestFixture
    {
        [Test]
        public void BuildGazeAdvertisementPairs_ChannelWithPaths_AppendsRouteThenAttributes()
        {
            var settings = new[]
            {
                new GazeChannel
                {
                    id = "gaze",
                    leftEyeBonePath = "Armature/Head/Eye_L",
                    lookUpAngle = 20f,
                    lookDownAngle = 10f,
                    outerYawAngle = 25f,
                    innerYawAngle = 30f,
                },
            };

            string[] pairs = OscSenderAdapterBinding.BuildGazeAdvertisementPairs(
                AddressPresetKind.VRChat,
                new[] { "gaze" },
                settings);

            CollectionAssert.AreEqual(
                new[]
                {
                    "gaze", "VRChat_XY",
                    "gaze", "bone.left=Armature/Head/Eye_L",
                    "gaze", "range=20,10,25,30",
                },
                pairs);
        }

        [Test]
        public void BuildGazeAdvertisementPairs_NoSettingsForId_SendsRouteOnly()
        {
            string[] pairs = OscSenderAdapterBinding.BuildGazeAdvertisementPairs(
                AddressPresetKind.VRChat,
                new[] { "gaze" },
                new[] { new GazeChannel { id = "camera" } });

            CollectionAssert.AreEqual(new[] { "gaze", "VRChat_XY" }, pairs);
        }

        [Test]
        public void BuildGazeAdvertisementPairs_ChannelWithoutPaths_SendsRangeEveryAdvertisement()
        {
            string[] pairs = OscSenderAdapterBinding.BuildGazeAdvertisementPairs(
                AddressPresetKind.ARKit,
                new[] { "gaze" },
                new[] { new GazeChannel { id = "gaze" } });

            CollectionAssert.AreEqual(
                new[] { "gaze", "ARKit_8BS", "gaze", "range=15,9,15,18" },
                pairs);
        }

        [Test]
        public void ComputeGazeSettingsSignature_RangeEditedAfterStart_ReturnsDifferentValue()
        {
            var channel = new GazeChannel { id = "gaze" };
            var settings = new[] { channel };
            string[] ids = { "gaze" };
            int before = OscSenderAdapterBinding.ComputeGazeSettingsSignature(ids, settings);

            channel.lookUpAngle = 30f;

            Assert.That(OscSenderAdapterBinding.ComputeGazeSettingsSignature(ids, settings), Is.Not.EqualTo(before));
        }

        [Test]
        public void ComputeGazeSettingsSignature_SettingsUnchanged_ReturnsSameValue()
        {
            var settings = new[] { new GazeChannel { id = "gaze", leftEyeBonePath = "Eye_L" } };
            string[] ids = { "gaze" };

            Assert.That(
                OscSenderAdapterBinding.ComputeGazeSettingsSignature(ids, settings),
                Is.EqualTo(OscSenderAdapterBinding.ComputeGazeSettingsSignature(ids, settings)));
        }

        [Test]
        public void BuildGazeAdvertisementPairs_CustomPreset_ReturnsNull()
        {
            string[] pairs = OscSenderAdapterBinding.BuildGazeAdvertisementPairs(
                AddressPresetKind.Custom,
                new[] { "gaze" },
                new[] { new GazeChannel { id = "gaze", leftEyeBonePath = "Eye_L" } });

            Assert.That(pairs, Is.Null);
        }
    }
}
