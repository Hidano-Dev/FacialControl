using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscFrameLayoutVersionTests : SizedTestFixture
    {
        private static readonly Guid SenderUuid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        [Test]
        public void Compute_SameIdentityAndEntries_ReturnsSameVersion()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);

            int first = OscFrameLayoutVersion.Compute(identity, Entries("Smile", "Blink"));
            int second = OscFrameLayoutVersion.Compute(identity, Entries("Smile", "Blink"));

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.Not.EqualTo(OscFrameLayoutVersion.Unknown));
        }

        [Test]
        public void Compute_SenderRestarted_ReturnsDifferentVersion()
        {
            int beforeRestart = OscFrameLayoutVersion.Compute(new SenderIdentity(SenderUuid, 1000L), Entries("Smile"));
            int afterRestart = OscFrameLayoutVersion.Compute(new SenderIdentity(SenderUuid, 1001L), Entries("Smile"));

            Assert.That(afterRestart, Is.Not.EqualTo(beforeRestart));
        }

        [Test]
        public void Compute_DifferentSenderUuid_ReturnsDifferentVersion()
        {
            int first = OscFrameLayoutVersion.Compute(new SenderIdentity(SenderUuid, 1000L), Entries("Smile"));
            int second = OscFrameLayoutVersion.Compute(
                new SenderIdentity(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"), 1000L),
                Entries("Smile"));

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void Compute_BlendShapeOrderChanged_ReturnsDifferentVersion()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);

            int original = OscFrameLayoutVersion.Compute(identity, Entries("Smile", "Blink"));
            int reordered = OscFrameLayoutVersion.Compute(identity, Entries("Blink", "Smile"));

            Assert.That(reordered, Is.Not.EqualTo(original));
        }

        [Test]
        public void Compute_BlendShapeAdded_ReturnsDifferentVersion()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);

            int original = OscFrameLayoutVersion.Compute(identity, Entries("Smile"));
            int added = OscFrameLayoutVersion.Compute(identity, Entries("Smile", "まばたき"));

            Assert.That(added, Is.Not.EqualTo(original));
        }

        [Test]
        public void Compute_SameValueWithDifferentKind_ReturnsDifferentVersion()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);
            var asBlendShape = new[] { new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "gaze") };
            var asGazeChannel = new[] { new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeChannel, "gaze") };

            int first = OscFrameLayoutVersion.Compute(identity, asBlendShape);
            int second = OscFrameLayoutVersion.Compute(identity, asGazeChannel);

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void Compute_HashEqualsPreviousVersion_ReturnsDifferentNonZeroVersion()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);
            int previous = OscFrameLayoutVersion.Compute(identity, Entries("Smile"));

            int next = OscFrameLayoutVersion.Compute(identity, Entries("Smile"), previous);

            Assert.That(next, Is.Not.EqualTo(previous));
            Assert.That(next, Is.Not.EqualTo(OscFrameLayoutVersion.Unknown));
        }

        [Test]
        public void Compute_ManyLayouts_NeverReturnsUnknown()
        {
            var identity = new SenderIdentity(SenderUuid, 1000L);
            for (int i = 0; i < 2000; i++)
            {
                int version = OscFrameLayoutVersion.Compute(identity, Entries("Shape" + i));
                Assert.That(version, Is.Not.EqualTo(OscFrameLayoutVersion.Unknown), "index " + i);
            }
        }

        private static IReadOnlyList<OscFrameLayoutEntry> Entries(params string[] blendShapeNames)
        {
            return OscFrameLayout.ToEntries(blendShapeNames, null);
        }
    }
}
