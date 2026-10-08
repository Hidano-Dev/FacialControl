using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// 複数ワーカースレッドとメインスレッドの gate / swap / resize 競合を検証する。
    /// スレッド同期を伴うため Small にはせず、Medium として実行する（Small の fixture とはファイルを分ける。
    /// docs/testing.md「同じ対象クラスでも Small と Medium の fixture は別ファイル」）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class LayerInputSourceWeightBufferConcurrencyTests : SizedTestFixture
    {
        private const int Iterations = 40;

        [Test]
        public void LiveWritesDuringSuspendAndBaselineSwap_AlwaysReadBaseline()
        {
            using var buffer = new LayerInputSourceWeightBuffer(layerCount: 2, maxSourcesPerLayer: 2);
            var stop = new CancellationTokenSource();
            var failures = new List<Exception>();
            var workers = StartWorkers(4, stop.Token, failures, (worker, iteration) =>
                buffer.SetWeight(worker % 2, iteration % 2, (iteration % 10) / 10f));

            try
            {
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    buffer.SuspendLiveWrites();
                    buffer.SetWeightBypassingLiveGate(0, 0, 0.37f);
                    buffer.SetWeightBypassingLiveGate(0, 1, 0.37f);
                    buffer.SetWeightBypassingLiveGate(1, 0, 0.37f);
                    buffer.SetWeightBypassingLiveGate(1, 1, 0.37f);
                    buffer.SwapIfDirty();

                    Assert.AreEqual(0.37f, buffer.GetWeight(0, 0));
                    Assert.AreEqual(0.37f, buffer.GetWeight(0, 1));
                    Assert.AreEqual(0.37f, buffer.GetWeight(1, 0));
                    Assert.AreEqual(0.37f, buffer.GetWeight(1, 1));
                    buffer.ResumeLiveWrites();
                }
            }
            finally
            {
                StopWorkers(stop, workers);
            }

            AssertNoWorkerFailures(failures);
        }

        [Test]
        public void BulkCommitsDuringSuspend_NeverOverwriteBaseline()
        {
            using var buffer = new LayerInputSourceWeightBuffer(layerCount: 2, maxSourcesPerLayer: 2);
            var stop = new CancellationTokenSource();
            var failures = new List<Exception>();
            var workers = StartWorkers(3, stop.Token, failures, (worker, iteration) =>
            {
                using var scope = buffer.BeginBulk();
                scope.SetWeight(0, worker % 2, (iteration % 10) / 10f);
                scope.SetWeight(1, (worker + 1) % 2, (iteration % 10) / 10f);
            });

            try
            {
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    buffer.SuspendLiveWrites();
                    buffer.SetWeightBypassingLiveGate(0, 0, 0.61f);
                    buffer.SetWeightBypassingLiveGate(0, 1, 0.61f);
                    buffer.SetWeightBypassingLiveGate(1, 0, 0.61f);
                    buffer.SetWeightBypassingLiveGate(1, 1, 0.61f);
                    buffer.SwapIfDirty();

                    Assert.AreEqual(0.61f, buffer.GetWeight(0, 0));
                    Assert.AreEqual(0.61f, buffer.GetWeight(0, 1));
                    Assert.AreEqual(0.61f, buffer.GetWeight(1, 0));
                    Assert.AreEqual(0.61f, buffer.GetWeight(1, 1));
                    buffer.ResumeLiveWrites();
                }
            }
            finally
            {
                StopWorkers(stop, workers);
            }

            AssertNoWorkerFailures(failures);
        }

        [Test]
        public void LiveWritesDuringResize_PreserveExistingSlotsWithoutExceptions()
        {
            using var buffer = new LayerInputSourceWeightBuffer(layerCount: 2, maxSourcesPerLayer: 2);
            buffer.SetWeightBypassingLiveGate(0, 0, 0.23f);
            buffer.SetWeightBypassingLiveGate(0, 1, 0.47f);
            buffer.SetWeightBypassingLiveGate(1, 0, 0.71f);
            buffer.SetWeightBypassingLiveGate(1, 1, 0.89f);
            buffer.SwapIfDirty();

            var stop = new CancellationTokenSource();
            var failures = new List<Exception>();
            var workers = StartWorkers(4, stop.Token, failures, (worker, iteration) =>
                buffer.SetWeight(worker % 2, 2, (iteration % 10) / 10f));

            try
            {
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    // 毎回 1 スロットずつ拡張し、全反復で実際の resize（配列差し替え）がワーカー書込と競合するようにする。
                    buffer.EnsureMaxSourcesPerLayer(3 + iteration);
                    buffer.SwapIfDirty();
                    Assert.AreEqual(0.23f, buffer.GetWeight(0, 0));
                    Assert.AreEqual(0.47f, buffer.GetWeight(0, 1));
                    Assert.AreEqual(0.71f, buffer.GetWeight(1, 0));
                    Assert.AreEqual(0.89f, buffer.GetWeight(1, 1));
                }
            }
            finally
            {
                StopWorkers(stop, workers);
            }

            AssertNoWorkerFailures(failures);
        }

        [Test]
        public void SuspendLiveWrites_WriterStillInFlight_DoesNotReturnUntilWriterDrains()
        {
            using var buffer = new LayerInputSourceWeightBuffer(layerCount: 1, maxSourcesPerLayer: 1);
            // フラグ確認後にプリエンプトされた writer を模擬する（固定回数のスピンを超えて居座る）
            SetLiveWritersInFlight(buffer, 1);

            bool returned = false;
            var suspender = new Thread(() =>
            {
                buffer.SuspendLiveWrites();
                Volatile.Write(ref returned, true);
            });
            suspender.IsBackground = true;
            suspender.Start();

            try
            {
                Thread.Sleep(200);
                Assert.IsFalse(Volatile.Read(ref returned), "in-flight の writer が残っている間は遮断完了を返さないべき。");
            }
            finally
            {
                SetLiveWritersInFlight(buffer, 0);
            }

            Assert.IsTrue(suspender.Join(5000), "writer が抜けたら遮断完了を返すべき。");
            Assert.IsTrue(buffer.IsLiveWritesSuspended);
        }

        [Test]
        public void EnsureMaxSourcesPerLayer_WriterStillInFlight_DoesNotSwapBuffersUntilWriterDrains()
        {
            using var buffer = new LayerInputSourceWeightBuffer(layerCount: 1, maxSourcesPerLayer: 1);
            SetLiveWritersInFlight(buffer, 1);

            var resizer = new Thread(() => buffer.EnsureMaxSourcesPerLayer(4));
            resizer.IsBackground = true;
            resizer.Start();

            try
            {
                Thread.Sleep(200);
                Assert.AreEqual(1, buffer.MaxSourcesPerLayer, "in-flight の writer が残っている間はバッファを差し替えないべき。");
            }
            finally
            {
                SetLiveWritersInFlight(buffer, 0);
            }

            Assert.IsTrue(resizer.Join(5000), "writer が抜けたら拡張を完了するべき。");
            Assert.AreEqual(4, buffer.MaxSourcesPerLayer);
        }

        private static void SetLiveWritersInFlight(LayerInputSourceWeightBuffer buffer, int value)
        {
            var field = typeof(LayerInputSourceWeightBuffer).GetField(
                "_liveWritersInFlight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "_liveWritersInFlight が見つからない。");
            field.SetValue(buffer, value);
            Thread.MemoryBarrier();
        }

        private static Thread[] StartWorkers(
            int count,
            CancellationToken token,
            List<Exception> failures,
            Action<int, int> write)
        {
            var workers = new Thread[count];
            for (int worker = 0; worker < count; worker++)
            {
                int workerIndex = worker;
                workers[worker] = new Thread(() =>
                {
                    int iteration = 0;
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            write(workerIndex, iteration++);
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (failures)
                        {
                            failures.Add(exception);
                        }
                    }
                });
                workers[worker].IsBackground = true;
                workers[worker].Start();
            }

            return workers;
        }

        private static void StopWorkers(CancellationTokenSource stop, Thread[] workers)
        {
            stop.Cancel();
            foreach (var worker in workers)
            {
                worker.Join(1000);
            }

            stop.Dispose();
        }

        private static void AssertNoWorkerFailures(List<Exception> failures)
        {
            lock (failures)
            {
                Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
            }
        }
    }
}
