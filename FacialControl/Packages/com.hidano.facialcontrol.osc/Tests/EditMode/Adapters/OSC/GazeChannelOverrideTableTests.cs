using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class GazeChannelOverrideTableTests : SizedTestFixture
    {
        [Test]
        public void Update_NewAttributes_ReturnsTrueAndAdvancesVersion()
        {
            var table = new GazeChannelOverrideTable();

            bool changed = table.Update(new[] { "gaze", "VRChat_XY", "gaze", "bone.left=Eye_L" });

            Assert.That(changed, Is.True);
            Assert.That(table.Version, Is.EqualTo(1));
            Assert.That(table.TryGet("gaze", out GazeChannelOverride value), Is.True);
            Assert.That(value.LeftEyeBonePath, Is.EqualTo("Eye_L"));
        }

        [Test]
        public void Update_SameAttributesTwice_KeepsVersion()
        {
            var table = new GazeChannelOverrideTable();
            string[] payload = { "gaze", "VRChat_XY", "gaze", "range=15,9,15,18" };
            table.Update(payload);

            bool changed = table.Update(new[] { "gaze", "range=15,9,15,18", "gaze", "VRChat_XY" });

            Assert.That(changed, Is.False);
            Assert.That(table.Version, Is.EqualTo(1));
        }

        [Test]
        public void Update_RangeChanged_AdvancesVersionAndReturnsNewValue()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { "gaze", "range=15,9,15,18" });

            bool changed = table.Update(new[] { "gaze", "range=30,9,15,18" });

            Assert.That(changed, Is.True);
            Assert.That(table.Version, Is.EqualTo(2));
            table.TryGet("gaze", out GazeChannelOverride value);
            Assert.That(value.LookUpAngle, Is.EqualTo(30f));
        }

        [Test]
        public void Update_AttributesRemoved_DropsOverride()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { "gaze", "bone.left=Eye_L" });

            bool changed = table.Update(new[] { "gaze", "VRChat_XY" });

            Assert.That(changed, Is.True);
            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.TryGet("gaze", out _), Is.False);
        }

        [Test]
        public void Update_OldFormatOnly_ReturnsFalseAndKeepsVersionZero()
        {
            var table = new GazeChannelOverrideTable();

            bool changed = table.Update(new[] { "gaze", "VRChat_XY" });

            Assert.That(changed, Is.False);
            Assert.That(table.Version, Is.EqualTo(0));
        }

        [Test]
        public void Clear_WithOverrides_AdvancesVersionOnlyWhenSomethingWasRemoved()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { "gaze", "bone.left=Eye_L" });

            table.Clear();
            int versionAfterFirstClear = table.Version;
            table.Clear();

            Assert.That(versionAfterFirstClear, Is.EqualTo(2));
            Assert.That(table.Version, Is.EqualTo(2));
            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryGet_NullOrEmptyId_ReturnsFalse()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { "gaze", "bone.left=Eye_L" });

            Assert.That(table.TryGet(null, out _), Is.False);
            Assert.That(table.TryGet(string.Empty, out _), Is.False);
        }
    }
}
