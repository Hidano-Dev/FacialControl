using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    /// <summary>
    /// <see cref="ManagedAllocationProbe"/> の自己検証。
    /// 「確保 0」を assert するテスト群が空振りしていないこと（計測器が実際の確保を検出すること）を保証する。
    /// </summary>
    [MediumTest]
    public sealed class ManagedAllocationProbeTests : SizedTestFixture
    {
        private const int AllocationBytes = 64 * 1024;

        [Test]
        public void MeasureAllocatedBytes_BodyAllocates_ReportsAtLeastAllocatedBytes()
        {
            byte[] sink = null;
            long measured = ManagedAllocationProbe.MeasureAllocatedBytes(() => { sink = new byte[AllocationBytes]; }, 1);
            TestContext.Out.WriteLine("[ManagedAllocationProbeTests] injected=" + AllocationBytes + " measured=" + measured);

            Assert.That(sink, Is.Not.Null);
            Assert.That(measured, Is.GreaterThanOrEqualTo(AllocationBytes),
                "計測器が " + AllocationBytes + " byte の確保を検出できない（" + measured + " byte）。確保 0 の assert は空振りになる。");
        }

        [Test]
        public void MeasureAllocatedBytes_BodyDoesNotAllocate_ReportsZero()
        {
            int counter = 0;
            long measured = ManagedAllocationProbe.MeasureAllocatedBytes(() => { counter++; }, 100);

            Assert.That(counter, Is.EqualTo(101));
            Assert.That(measured, Is.EqualTo(0));
        }
    }
}
