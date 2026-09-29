using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    /// <summary>
    /// 受信スレッドの解析 <see cref="OscMessageClassifier.ParseAndClassify"/> がマネージド確保ゼロで動くことを固定する。
    /// </summary>
    [MediumTest]
    public sealed class OscMessageClassifierAllocationTests : SizedTestFixture
    {
        private const int Datagrams = 20;

        private static byte[] BuildNormalPacket()
        {
            byte[][] addresses =
            {
                Encoding.UTF8.GetBytes("/avatar/parameters/smile"),
                Encoding.UTF8.GetBytes("/avatar/parameters/frown")
            };
            float[] values = { 0.25f, 0.75f };
            byte[] senderAddress = Encoding.UTF8.GetBytes("/_facialcontrol/sender_id");
            byte[] senderUuid = new byte[16];
            for (int i = 0; i < senderUuid.Length; i++) senderUuid[i] = (byte)(i + 1);

            using (var builder = new OscBundleBuilder())
            {
                int count = builder.BuildFrameBundle(1, senderAddress, senderUuid, "1700000000000", addresses, values, 2);
                Assert.That(count, Is.GreaterThan(0));
                OscBundlePacket packet = builder.GetPacket(0);
                var bytes = new byte[packet.Length];
                System.Buffer.BlockCopy(packet.Buffer, 0, bytes, 0, packet.Length);
                return bytes;
            }
        }

        [Test]
        public void ParseAndClassify_RealNormalPacket_DoesNotAllocate()
        {
            byte[] packet = BuildNormalPacket();
            var table = new OscAddressKeyTable.Builder(new Dictionary<string, byte[]>())
                .SetMappings(new[]
                {
                    new OscMapping("/avatar/parameters/smile", "smile", "layer"),
                    new OscMapping("/avatar/parameters/frown", "frown", "layer")
                })
                .Build(1);
            var diagnostics = new OscReceiveDiagnostics();
            var records = new OscResolvedMessage[64];

            int lastCount = -1;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                lastCount = OscMessageClassifier.ParseAndClassify(packet, table, records, diagnostics);
            }, Datagrams);

            Assert.That(lastCount, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0), "ParseAndClassify が " + Datagrams + " 回の呼び出しで " + allocated + " byte 確保した");
        }
    }
}
