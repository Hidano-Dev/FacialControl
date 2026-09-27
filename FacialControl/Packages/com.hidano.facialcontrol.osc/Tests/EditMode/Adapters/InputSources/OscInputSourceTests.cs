using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="OscInputSource"/> の EditMode 契約テスト。
    /// 予約 id / <see cref="InputSourceType.ValueProvider"/> / <see cref="OscDoubleBuffer"/> からの値コピーと
    /// staleness 判定（閾値 0 で恒常 true、閾値超過で false かつ output 非変更、新規受信でリセット）、
    /// および mapping index → mesh index 変換付きコンストラクタの ContributeMask / 書き込み位置と Aggregator 経由の長さ整合を検証する。
    /// </summary>
    [TestFixture]
    public class OscInputSourceTests
    {
        // ---------------------------------------------------------------
        // 基本契約: Id / Type / BlendShapeCount / Tick
        // ---------------------------------------------------------------

        [Test]
        public void Id_MatchesOscReservedId()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(OscInputSource.ReservedId, source.Id);
            Assert.AreEqual("osc", source.Id);
        }

        [Test]
        public void Type_IsValueProviderViaIInputSource()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            IInputSource source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(InputSourceType.ValueProvider, source.Type);
        }

        [Test]
        public void BlendShapeCount_MatchesBufferSize()
        {
            using var buffer = new OscDoubleBuffer(7);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(7, source.BlendShapeCount);
        }

        [Test]
        public void Tick_IsNoOp_DoesNotThrow()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.DoesNotThrow(() => source.Tick(0.016f));
        }

        // ---------------------------------------------------------------
        // TryWriteValues: バッファ内容のコピーと staleness 判定
        // ---------------------------------------------------------------

        [Test]
        public void TryWriteValues_AfterWrite_WritesBufferContents()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.25f);
            buffer.Write(1, 0.5f);
            buffer.Write(2, 0.75f);
            buffer.Write(3, 1.0f);
            buffer.Swap();

            var output = new float[4];
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.25f, output[0], 1e-5f);
            Assert.AreEqual(0.5f, output[1], 1e-5f);
            Assert.AreEqual(0.75f, output[2], 1e-5f);
            Assert.AreEqual(1.0f, output[3], 1e-5f);
        }

        [Test]
        public void TryWriteValues_StalenessZero_AlwaysReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            // 一度も Write がなくても、staleness 無効時は常に true。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 長時間経過しても true のまま。
            time.UnscaledTimeSeconds = 1000.0;
            Assert.IsTrue(source.TryWriteValues(output));
        }

        [Test]
        public void TryWriteValues_ImmediatelyAfterWrite_ReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.42f);
            buffer.Swap();

            // 受信直後は経過時間 0 なので有効。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));
            Assert.AreEqual(0.42f, output[0], 1e-5f);
        }

        /// <summary>
        /// staleness 判定の決定論シナリオ。
        /// time=0 で write → time=2.0 で閾値 1.0 を超過 → IsValid=false。
        /// </summary>
        [Test]
        public void TryWriteValues_ExceedsStalenessThreshold_ReturnsFalse()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            // time=0 で一度観測させ _lastDataTime を 0 に固定。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 閾値超過 → false かつ output 非変更。
            output[0] = 99f;
            time.UnscaledTimeSeconds = 2.0;
            bool wrote = source.TryWriteValues(output);

            Assert.IsFalse(wrote);
            Assert.AreEqual(99f, output[0], 1e-5f,
                "false を返した場合は output を変更しないこと (IInputSource 契約)。");
        }

        [Test]
        public void TryWriteValues_WithinStalenessThreshold_ReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 0.5 秒経過 (閾値 1.0 以内) → 引き続き true。
            time.UnscaledTimeSeconds = 0.5;
            Assert.IsTrue(source.TryWriteValues(output));
            Assert.AreEqual(0.5f, output[0], 1e-5f);
        }

        [Test]
        public void TryWriteValues_NewWriteResetsStaleness()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Swap();

            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 閾値超過で一度 false に。
            time.UnscaledTimeSeconds = 2.0;
            Assert.IsFalse(source.TryWriteValues(output));

            // 新規受信: WriteTick が進む → _lastDataTime が更新され true に復帰。
            buffer.Write(0, 0.9f);
            buffer.Swap();
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.9f, output[0], 1e-5f);
        }

        [Test]
        public void TryWriteValues_OutputShorterThanBuffer_WritesOverlapOnly()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Write(2, 0.3f);
            buffer.Write(3, 0.4f);
            buffer.Swap();

            var output = new float[2];
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.1f, output[0], 1e-5f);
            Assert.AreEqual(0.2f, output[1], 1e-5f);
        }

        [Test]
        public void TryWriteValues_OutputLongerThanBuffer_WritesOverlapOnly()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Swap();

            var output = new float[4] { 7f, 7f, 7f, 7f };
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.1f, output[0], 1e-5f);
            Assert.AreEqual(0.2f, output[1], 1e-5f);
            Assert.AreEqual(7f, output[2], 1e-5f, "残余は呼出側責務で保持 (IInputSource 契約)。");
            Assert.AreEqual(7f, output[3], 1e-5f);
        }

        // ---------------------------------------------------------------
        // mapping index → mesh index 変換付きコンストラクタ: ContributeMask と書き込み位置
        // ---------------------------------------------------------------

        [Test]
        public void ContributeMask_MappingCountDiffersFromMeshCount_ReturnsMeshIndexMask()
        {
            using var buffer = new OscDoubleBuffer(2);
            var contributeMask = new BitArray(4, false)
            {
                [0] = true,
                [3] = true
            };

            var source = CreateMeshMappedSource(
                buffer,
                contributeMask: contributeMask,
                mappingIndexToMeshIndex: new[] { 3, 0 });

            Assert.That(source.ContributeMask.Length, Is.EqualTo(4));
            Assert.That(source.ContributeMask[0], Is.True);
            Assert.That(source.ContributeMask[1], Is.False);
            Assert.That(source.ContributeMask[2], Is.False);
            Assert.That(source.ContributeMask[3], Is.True);
        }

        [Test]
        public void TryWriteValues_MappingOrderDiffersFromMeshOrder_WritesToCorrectMeshIndex()
        {
            using var buffer = new OscDoubleBuffer(2);
            var source = CreateMeshMappedSource(
                buffer,
                contributeMask: new BitArray(new[] { true, false, false, true }),
                mappingIndexToMeshIndex: new[] { 3, 0 });

            buffer.Write(0, 0.75f);
            buffer.Write(1, 0.25f);
            buffer.Swap();

            var output = new[] { -1f, -1f, -1f, -1f };
            bool wrote = source.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[2], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[3], Is.EqualTo(0.75f).Within(1e-6f));
        }

        [Test]
        public void TryWriteValues_UnmappedMappingIndex_LeavesMeshOutputUntouched()
        {
            using var buffer = new OscDoubleBuffer(3);
            var source = CreateMeshMappedSource(
                buffer,
                contributeMask: new BitArray(new[] { false, true, false, true }),
                mappingIndexToMeshIndex: new[] { 3, -1, 1 });

            buffer.Write(0, 0.9f);
            buffer.Write(1, 0.5f);
            buffer.Write(2, 0.1f);
            buffer.Swap();

            var output = new[] { -1f, -1f, -1f, -1f };
            bool wrote = source.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output[0], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(output[2], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[3], Is.EqualTo(0.9f).Within(1e-6f));
        }

        [Test]
        public void TryWriteValues_ContributeMaskFalseForMappedMeshIndex_StillWritesValue()
        {
            using var buffer = new OscDoubleBuffer(1);
            var source = CreateMeshMappedSource(
                buffer,
                contributeMask: new BitArray(new[] { false, true }),
                mappingIndexToMeshIndex: new[] { 0 });

            buffer.Write(0, 0.6f);
            buffer.Swap();

            var output = new[] { -1f, -1f };
            bool wrote = source.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output[0], Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(-1f).Within(1e-6f));
        }

        [Test]
        public void Aggregate_OscReceiverAndOutputDemoShapeMismatch_DoesNotThrow()
        {
            const int meshBlendShapeCount = 4;
            using var buffer = new OscDoubleBuffer(2);
            var source = CreateMeshMappedSource(
                buffer,
                contributeMask: new BitArray(new[] { true, false, false, true }),
                mappingIndexToMeshIndex: new[] { 3, 0 });

            buffer.Write(0, 0.75f);
            buffer.Write(1, 0.25f);
            buffer.Swap();

            var profile = new FacialProfile(
                "1.0",
                layers: new[] { new LayerDefinition("osc", priority: 0, ExclusionMode.LastWins) });

            using var registry = new LayerInputSourceRegistry(
                profile,
                meshBlendShapeCount,
                new List<(int layerIdx, int sourceIdx, IInputSource source)>
                {
                    (0, 0, source)
                });
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount,
                registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, meshBlendShapeCount);
            var outputPerLayer = new LayerBlender.LayerInput[registry.LayerCount];

            Assert.DoesNotThrow(() => aggregator.Aggregate(0f, outputPerLayer));

            var values = outputPerLayer[0].BlendShapeValues.Span;
            Assert.That(values.Length, Is.EqualTo(meshBlendShapeCount));
            Assert.That(values[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(values[1], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(values[2], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(values[3], Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(outputPerLayer[0].ContributeMask.Length, Is.EqualTo(meshBlendShapeCount));
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// mesh index 変換付きコンストラクタ (buffer, staleness, time, failSafe, contributeMask, mappingIndexToMeshIndex)
        /// を reflection で解決して <see cref="OscInputSource"/> を生成する。
        /// </summary>
        private static OscInputSource CreateMeshMappedSource(
            OscDoubleBuffer buffer,
            BitArray contributeMask,
            int[] mappingIndexToMeshIndex)
        {
            ConstructorInfo constructor = typeof(OscInputSource).GetConstructor(new[]
            {
                typeof(OscDoubleBuffer),
                typeof(float),
                typeof(ITimeProvider),
                typeof(FailSafeMode),
                typeof(BitArray),
                typeof(int[])
            });

            Assert.That(
                constructor,
                Is.Not.Null,
                "OscInputSource must expose a mesh-index constructor that accepts mappingIndexToMeshIndex.");

            return (OscInputSource)constructor.Invoke(new object[]
            {
                buffer,
                0f,
                new ManualTimeProvider(),
                FailSafeMode.HoldLastValue,
                contributeMask,
                mappingIndexToMeshIndex
            });
        }
    }
}
