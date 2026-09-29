using System;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class SenderIdentityTests : SizedTestFixture
    {
        [Test]
        public void Constructor_ValidUuidAndStartedAtUnixMs_StoresValues()
        {
            Guid uuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var identity = new SenderIdentity(uuid, 123456789L);

            Assert.AreEqual(uuid, identity.Uuid);
            Assert.AreEqual(uuid, identity.SenderId);
            Assert.AreEqual(123456789L, identity.StartedAtUnixMs);
        }

        [Test]
        public void Generate_NewIdentity_ReturnsNonEmptyUuidAndUtcUnixMs()
        {
            long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SenderIdentity first = SenderIdentityGenerator.Generate();
            SenderIdentity second = SenderIdentityGenerator.Generate();
            long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.AreNotEqual(Guid.Empty, first.Uuid);
            Assert.AreNotEqual(Guid.Empty, second.Uuid);
            Assert.AreNotEqual(first.Uuid, second.Uuid);
            Assert.GreaterOrEqual(first.StartedAtUnixMs, before);
            Assert.LessOrEqual(first.StartedAtUnixMs, after);
            Assert.GreaterOrEqual(second.StartedAtUnixMs, before);
            Assert.LessOrEqual(second.StartedAtUnixMs, after);
        }

        [Test]
        public void OscContract_ConstantsMatchReceiverParserContract()
        {
            Assert.AreEqual("/_facialcontrol/sender_id", SenderIdentity.OscAddress);
            Assert.AreEqual(SenderIdentity.OscAddress, OscReceiverAdapterBinding.SenderIdentityAddress);
            Assert.AreEqual(16, SenderIdentity.UuidByteLength);
        }
    }
}
