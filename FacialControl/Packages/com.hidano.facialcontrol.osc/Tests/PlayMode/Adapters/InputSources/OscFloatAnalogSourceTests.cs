using System;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="OscFloatAnalogSource"/> のテスト。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscFloatAnalogSourceTests : SizedTestFixture
    {
        private const string TestAddress = "/avatar/parameters/jawOpen";

        private GameObject _receiverObj;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _receiverObj = new GameObject("OscReceiverFloatAnalogTest");
            _receiver = _receiverObj.AddComponent<OscReceiver>();
            _buffer = new OscDoubleBuffer(0);
            _receiver.Initialize(_buffer, Array.Empty<OscMapping>());
        }

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
            if (_receiverObj != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverObj);
                _receiverObj = null;
            }
        }

        private OscFloatAnalogSource CreateSource(float stalenessSeconds = 0f, string id = "x-osc-jaw")
        {
            return new OscFloatAnalogSource(
                InputSourceId.Parse(id),
                _receiver,
                TestAddress,
                stalenessSeconds);
        }

        // ============================================================
        // ctor / 引数バリデーション
        // ============================================================

        [Test]
        public void Ctor_NullReceiver_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new OscFloatAnalogSource(InputSourceId.Parse("x-test"), null, TestAddress, 0f));
        }

        [Test]
        public void Ctor_EmptyAddress_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new OscFloatAnalogSource(InputSourceId.Parse("x-test"), _receiver, string.Empty, 0f));
        }

        [Test]
        public void Ctor_NegativeStaleness_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OscFloatAnalogSource(InputSourceId.Parse("x-test"), _receiver, TestAddress, -0.1f));
        }

        [Test]
        public void Ctor_DefaultId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new OscFloatAnalogSource(default, _receiver, TestAddress, 0f));
        }

        // ============================================================
        // 基本契約
        // ============================================================

        [Test]
        public void AxisCount_IsOne()
        {
            using var source = CreateSource();
            Assert.AreEqual(1, source.AxisCount);
        }

        [Test]
        public void Id_ReturnsConstructorId()
        {
            using var source = CreateSource(id: "x-osc-jaw");
            Assert.AreEqual("x-osc-jaw", source.Id);
        }

        [Test]
        public void IsValid_BeforeAnyReceive_ReturnsFalse()
        {
            using var source = CreateSource();
            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        // ============================================================
        // 値伝搬
        // ============================================================

        [Test]
        public void Tick_AfterReceive_PropagatesValueAndIsValidTrue()
        {
            using var source = CreateSource();

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.5f));
            source.Tick(0.016f);

            Assert.IsTrue(source.IsValid);
            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.5f, value, 0.0001f);
        }

        [Test]
        public void Tick_LatestValueWins_WhenMultipleReceivedBeforeTick()
        {
            using var source = CreateSource();

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.7f));
            source.Tick(0.016f);

            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.7f, value, 0.0001f);
        }

        [Test]
        public void TryReadVector2_ScalarSource_ReturnsFalse()
        {
            using var source = CreateSource();
            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.5f));
            source.Tick(0.016f);

            Assert.IsFalse(source.TryReadVector2(out _, out _));
        }

        [Test]
        public void TryReadAxes_LongerOutput_WritesOnlyAxisZero()
        {
            using var source = CreateSource();
            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.42f));
            source.Tick(0.016f);

            var output = new float[] { 9f, 9f, 9f };
            Assert.IsTrue(source.TryReadAxes(output));

            Assert.AreEqual(0.42f, output[0], 0.0001f);
            Assert.AreEqual(9f, output[1]);
            Assert.AreEqual(9f, output[2]);
        }

        [Test]
        public void TryReadAxes_EmptyOutput_ReturnsFalse()
        {
            using var source = CreateSource();
            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.42f));
            source.Tick(0.016f);

            Assert.IsFalse(source.TryReadAxes(Array.Empty<float>()));
        }

        // ============================================================
        // staleness 動作
        // ============================================================

        [Test]
        public void Tick_StalenessExceeded_IsValidBecomesFalse()
        {
            using var source = CreateSource(stalenessSeconds: 0.5f);

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.3f));
            source.Tick(0.0f);
            Assert.IsTrue(source.IsValid);

            // 受信なしのまま 0.6 秒経過 → staleness を超える
            source.Tick(0.6f);

            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        [Test]
        public void Tick_StalenessZero_RemainsValidIndefinitelyAfterFirstReceive()
        {
            using var source = CreateSource(stalenessSeconds: 0f);

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.3f));
            source.Tick(0f);

            // staleness=0 のとき経過時間で無効化されてはならない (last-valid)
            source.Tick(60f);
            source.Tick(60f);

            Assert.IsTrue(source.IsValid);
            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.3f, value, 0.0001f);
        }

        [Test]
        public void Tick_NewReceiveAfterStaleness_RestoresValid()
        {
            using var source = CreateSource(stalenessSeconds: 0.1f);

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.3f));
            source.Tick(0f);
            source.Tick(0.5f);
            Assert.IsFalse(source.IsValid);

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.8f));
            source.Tick(0f);

            Assert.IsTrue(source.IsValid);
            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.8f, value, 0.0001f);
        }

        // ============================================================
        // Dispose 解放
        // ============================================================

        [Test]
        public void Dispose_StopsReceivingFurtherUpdates()
        {
            var source = CreateSource();

            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.3f));
            source.Tick(0f);
            Assert.IsTrue(source.TryReadScalar(out float v1));
            Assert.AreEqual(0.3f, v1, 0.0001f);

            source.Dispose();

            // Dispose 後の受信は値に反映されない
            _receiver.HandleOscMessage(new uOSC.Message(TestAddress, 0.9f));
            source.Tick(0f);

            // Tick 自体は no-op になり、_cachedValue は変わらない
            Assert.IsTrue(source.TryReadScalar(out float v2));
            Assert.AreEqual(0.3f, v2, 0.0001f);
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var source = CreateSource();
            Assert.DoesNotThrow(() =>
            {
                source.Dispose();
                source.Dispose();
            });
        }
    }
}
