using System;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Adapters.Processors;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Performance
{
    /// <summary>
    //: 6 種 stateless アナログ <see cref="InputProcessor{TValue}"/>
    /// （DeadZone / Scale / Offset / Clamp / Curve / Invert）の per-Process 0-alloc を検証する
    /// PlayMode/Performance テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ProcessorRegistration を経由する InputAction 経路ではなく、各 processor を直接インスタンス化し
    /// 6 種を連結した <c>Process(value, control)</c> 呼出を 1000 回繰り返して GC アロケーションを計測する。
    /// 6 種は全て stateless かつ <see cref="float"/> field のみを保持するため、純粋関数として
    /// 0-alloc であることが期待される。
    /// </para>
    /// <para>
    /// 計測手段は <see cref="ProfilerRecorder"/>(<c>"GC.Alloc"</c>) で fine-grained に計測する。
    /// <c>GC.GetTotalMemory</c> による heap delta 計測は Editor のバックグラウンド処理
    /// (アセット再インポート / Profiler サンプラ等) のヒープ活動を拾い flaky になるため採用しない。
    /// </para>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class AnalogProcessorAllocationTests : SizedTestFixture
    {
        private const int IterationCount = 1000;

        private AnalogDeadZoneProcessor _deadZone;
        private AnalogInvertProcessor _invert;
        private AnalogScaleProcessor _scale;
        private AnalogOffsetProcessor _offset;
        private AnalogCurveProcessor _curve;
        private AnalogClampProcessor _clamp;

        [SetUp]
        public void SetUp()
        {
            _deadZone = new AnalogDeadZoneProcessor { min = 0.05f, max = 1f };
            _invert = new AnalogInvertProcessor();
            _scale = new AnalogScaleProcessor { factor = 0.8f };
            _offset = new AnalogOffsetProcessor { offset = 0.1f };
            _curve = new AnalogCurveProcessor { preset = 3 };
            _clamp = new AnalogClampProcessor { min = -1f, max = 1f };
        }

        [Test]
        public void Chain_PerProcess_ZeroGCAllocation_ProfilerRecorder()
        {
            float input = 0.6f;

            // ウォームアップ
            float warmup = ProcessChain(input);
            Assert.That(warmup, Is.Not.NaN);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            float result = 0f;
            for (int i = 0; i < IterationCount; i++)
            {
                result = ProcessChain(input);
            }

            long gcAlloc = recorder.LastValue;

            Assert.That(result, Is.Not.NaN);
            Assert.AreEqual(0, gcAlloc,
                $"6 種 processor 連結 Process で GC アロケーションが検出されました: {gcAlloc} bytes");
        }

        [Test]
        public void DeadZoneProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _deadZone.Process(v, control: null), 0.5f);
        }

        [Test]
        public void ScaleProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _scale.Process(v, control: null), 0.5f);
        }

        [Test]
        public void OffsetProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _offset.Process(v, control: null), 0.5f);
        }

        [Test]
        public void ClampProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _clamp.Process(v, control: null), 0.5f);
        }

        [Test]
        public void CurveProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _curve.Process(v, control: null), 0.5f);
        }

        [Test]
        public void InvertProcessor_PerProcess_ZeroGCAllocation()
        {
            AssertSingleProcessorZeroAllocation(v => _invert.Process(v, control: null), 0.5f);
        }

        // ============================================================
        // ヘルパー
        // ============================================================

        private float ProcessChain(float input)
        {
            float v = input;
            v = _deadZone.Process(v, control: null);
            v = _invert.Process(v, control: null);
            v = _scale.Process(v, control: null);
            v = _offset.Process(v, control: null);
            v = _curve.Process(v, control: null);
            v = _clamp.Process(v, control: null);
            return v;
        }

        private static void AssertSingleProcessorZeroAllocation(Func<float, float> process, float input)
        {
            // ウォームアップ
            float warmup = process(input);
            Assert.That(warmup, Is.Not.NaN);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            float result = 0f;
            for (int i = 0; i < IterationCount; i++)
            {
                result = process(input);
            }

            long gcAlloc = recorder.LastValue;

            Assert.That(result, Is.Not.NaN);
            Assert.AreEqual(0, gcAlloc,
                $"単独 processor Process で GC アロケーションが検出されました: {gcAlloc} bytes");
        }
    }
}
