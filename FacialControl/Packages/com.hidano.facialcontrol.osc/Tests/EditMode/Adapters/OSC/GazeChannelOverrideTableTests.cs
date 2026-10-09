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

            bool changed = table.Update(new[] { Channel("gaze", "bone.left=Eye_L", "range=15,9,15,18") });

            Assert.That(changed, Is.True);
            Assert.That(table.Version, Is.EqualTo(1));
            Assert.That(table.TryGet("gaze", out GazeChannelOverride value), Is.True);
            Assert.That(value.LeftEyeBonePath, Is.EqualTo("Eye_L"));
        }

        [Test]
        public void Update_SameAttributesTwice_KeepsVersion()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            bool changed = table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            Assert.That(changed, Is.False);
            Assert.That(table.Version, Is.EqualTo(1));
        }

        [Test]
        public void Update_RangeChanged_AdvancesVersionAndReturnsNewValue()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            bool changed = table.Update(new[] { Channel("gaze", "range=30,9,15,18") });

            Assert.That(changed, Is.True);
            Assert.That(table.Version, Is.EqualTo(2));
            table.TryGet("gaze", out GazeChannelOverride value);
            Assert.That(value.LookUpAngle, Is.EqualTo(30f));
        }

        [Test]
        public void Update_RangeWithoutPath_RemovesPathOverride()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "bone.left=Eye_L", "range=15,9,15,18") });

            bool changed = table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            Assert.That(changed, Is.True);
            Assert.That(table.TryGet("gaze", out GazeChannelOverride value), Is.True);
            Assert.That(value.HasLeftEyeBonePath, Is.False);
        }

        [Test]
        public void Update_ChannelRemovedFromLayout_RemovesItsOverride()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "range=15,9,15,18"), Channel("camera", "range=1,2,3,4") });

            bool changed = table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            Assert.That(changed, Is.True);
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.TryGet("camera", out _), Is.False);
        }

        [Test]
        public void Update_ChannelWithoutRange_HasNoOverride()
        {
            var table = new GazeChannelOverrideTable();

            bool changed = table.Update(new[] { Channel("gaze", "bone.left=Eye_L"), Channel("bare") });

            Assert.That(changed, Is.False);
            Assert.That(table.Version, Is.EqualTo(0));
            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void Update_DuplicateIds_FirstChannelWins()
        {
            var table = new GazeChannelOverrideTable();

            table.Update(new[] { Channel("gaze", "range=10,9,15,18"), Channel("gaze", "range=20,9,15,18") });

            table.TryGet("gaze", out GazeChannelOverride value);
            Assert.That(value.LookUpAngle, Is.EqualTo(10f));
        }

        [Test]
        public void Update_NullOrEmpty_ClearsOverrides()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "range=15,9,15,18") });

            bool changed = table.Update(null);

            Assert.That(changed, Is.True);
            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.Update(new OscFrameLayoutGazeChannel[0]), Is.False);
        }

        [Test]
        public void Clear_WithOverrides_AdvancesVersionOnlyWhenSomethingWasRemoved()
        {
            var table = new GazeChannelOverrideTable();
            table.Update(new[] { Channel("gaze", "bone.left=Eye_L", "range=15,9,15,18") });

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
            table.Update(new[] { Channel("gaze", "bone.left=Eye_L", "range=15,9,15,18") });

            Assert.That(table.TryGet(null, out _), Is.False);
            Assert.That(table.TryGet(string.Empty, out _), Is.False);
        }

        private static OscFrameLayoutGazeChannel Channel(string id, params string[] attributes)
        {
            return new OscFrameLayoutGazeChannel(id, attributes);
        }
    }
}
