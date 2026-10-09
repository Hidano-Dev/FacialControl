using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscFrameLayoutTests : SizedTestFixture
    {
        [Test]
        public void SlotCount_BlendShapesAndGazeChannels_CountsTwoSlotsPerGazeChannel()
        {
            var layout = new OscFrameLayout(
                7,
                new[] { "Smile", "Blink", "まばたき" },
                new[] { new OscFrameLayoutGazeChannel("gaze"), new OscFrameLayoutGazeChannel("gaze2") });

            Assert.That(layout.SlotCount, Is.EqualTo(3 + 4));
        }

        [Test]
        public void GetGazeSlotIndex_SecondChannel_FollowsBlendShapesAndFirstChannel()
        {
            var layout = new OscFrameLayout(
                7,
                new[] { "Smile", "Blink" },
                new[] { new OscFrameLayoutGazeChannel("gaze"), new OscFrameLayoutGazeChannel("gaze2") });

            Assert.That(layout.GetGazeSlotIndex(0), Is.EqualTo(2));
            Assert.That(layout.GetGazeSlotIndex(1), Is.EqualTo(4));
        }

        [Test]
        public void ToEntries_TryFromEntries_RoundTripsNamesGazeChannelsAndAttributes()
        {
            var layout = new OscFrameLayout(
                42,
                new[] { "Smile", "目_閉じ" },
                new[]
                {
                    new OscFrameLayoutGazeChannel("gaze", new[] { "bone.left=Armature/Head/Eye_L", "range=10,8,12,9" }),
                    new OscFrameLayoutGazeChannel("gaze2")
                });

            OscFrameLayoutEntry[] entries = layout.ToEntries();
            bool built = OscFrameLayout.TryFromEntries(42, entries, out OscFrameLayout restored);

            Assert.That(built, Is.True);
            Assert.That(restored.Version, Is.EqualTo(42));
            Assert.That(restored.BlendShapeNames, Is.EqualTo(new[] { "Smile", "目_閉じ" }));
            Assert.That(restored.GazeChannels.Count, Is.EqualTo(2));
            Assert.That(restored.GazeChannels[0].Id, Is.EqualTo("gaze"));
            Assert.That(
                restored.GazeChannels[0].Attributes,
                Is.EqualTo(new[] { "bone.left=Armature/Head/Eye_L", "range=10,8,12,9" }));
            Assert.That(restored.GazeChannels[1].Id, Is.EqualTo("gaze2"));
            Assert.That(restored.GazeChannels[1].Attributes, Is.Empty);
        }

        [Test]
        public void TryFromEntries_AttributeBeforeAnyGazeChannel_ReturnsFalse()
        {
            var entries = new[]
            {
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "Smile"),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeAttribute, "range=10,8,12,9")
            };

            Assert.That(OscFrameLayout.TryFromEntries(1, entries, out _), Is.False);
        }

        [Test]
        public void TryFromEntries_UndefinedKind_ReturnsFalse()
        {
            var entries = new[] { new OscFrameLayoutEntry((OscFrameLayoutEntryKind)99, "Smile") };

            Assert.That(OscFrameLayout.TryFromEntries(1, entries, out _), Is.False);
        }

        [Test]
        public void TryFromEntries_BlendShapeAfterGazeChannel_ReturnsFalse()
        {
            var entries = new[]
            {
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeChannel, "gaze"),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "Smile")
            };

            Assert.That(OscFrameLayout.TryFromEntries(1, entries, out _), Is.False);
        }

        [Test]
        public void EntryConstructor_ValueContainsNul_ReplacesNulSoOscStringStaysIntact()
        {
            var entry = new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "A\0B");

            Assert.That(entry.Value, Is.EqualTo("A\uFFFDB"));
        }
    }
}
