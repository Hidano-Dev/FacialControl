using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class LoopbackSuppressionPolicyTests : SizedTestFixture
    {
        [Test]
        public void FromBindings_OscReceiverOnSamePort_SuppressesLoopbackSenderEndpoint()
        {
            int port = 19501;
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-receiver",
                Port = port
            };

            LoopbackSuppressionPolicy policy = LoopbackSuppressionPolicy.FromBindings(
                new AdapterBindingBase[] { receiver, new OscSenderAdapterBinding { Slug = "osc-sender" } });

            Assert.That(policy.Count, Is.EqualTo(1));
            Assert.That(policy.IsSuppressed("127.0.0.1", port), Is.True);
        }

        [Test]
        public void IsSuppressed_LocalhostAndLoopbackAddress_AreEquivalent()
        {
            int port = 19502;
            var policy = new LoopbackSuppressionPolicy();
            policy.AddReceiverEndpoint("localhost", port);

            Assert.That(policy.IsSuppressed("127.0.0.1", port), Is.True);
            Assert.That(policy.IsSuppressed("::1", port), Is.True);
        }

        [Test]
        public void IsSuppressed_DifferentPort_ReturnsFalse()
        {
            var policy = new LoopbackSuppressionPolicy();
            policy.AddReceiverEndpoint("127.0.0.1", 19503);

            Assert.That(policy.IsSuppressed("127.0.0.1", 19504), Is.False);
        }

        [Test]
        public void IsSuppressed_WildcardReceiver_SuppressesLoopbackSenderOnSamePort()
        {
            int port = 19505;
            var policy = new LoopbackSuppressionPolicy();
            policy.AddReceiverEndpoint("0.0.0.0", port);

            Assert.That(policy.IsSuppressed("127.0.0.1", port), Is.True);
        }
    }
}
