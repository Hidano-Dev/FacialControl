using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// 送信側が対応表の gaze 属性（目ボーン path・可動範囲）の変化を検出する処理を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class OscSenderGazeSettingsSnapshotTests : SizedTestFixture
    {
        [Test]
        public void UpdateGazeSettingsSnapshots_RangeEditedAfterStart_ReturnsTrue()
        {
            var channel = new GazeChannel { id = "gaze" };
            var settings = new[] { channel };
            string[] ids = { "gaze" };
            var snapshots = new OscSenderAdapterBinding.GazeSettingsSnapshot[1];
            OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots);

            channel.lookUpAngle = 30f;

            Assert.That(OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots), Is.True);
        }

        [Test]
        public void UpdateGazeSettingsSnapshots_SettingsUnchanged_ReturnsFalse()
        {
            var settings = new[] { new GazeChannel { id = "gaze", leftEyeBonePath = "Eye_L" } };
            string[] ids = { "gaze" };
            var snapshots = new OscSenderAdapterBinding.GazeSettingsSnapshot[1];

            Assert.That(OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots), Is.True);
            Assert.That(OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots), Is.False);
        }

        [Test]
        public void UpdateGazeSettingsSnapshots_PathEditedAfterStart_ReturnsTrue()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "Eye_L" };
            var settings = new[] { channel };
            string[] ids = { "gaze" };
            var snapshots = new OscSenderAdapterBinding.GazeSettingsSnapshot[1];
            OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots);

            channel.leftEyeBonePath = "Head/Eye_L";

            Assert.That(OscSenderAdapterBinding.UpdateGazeSettingsSnapshots(ids, settings, snapshots), Is.True);
        }
    }
}
