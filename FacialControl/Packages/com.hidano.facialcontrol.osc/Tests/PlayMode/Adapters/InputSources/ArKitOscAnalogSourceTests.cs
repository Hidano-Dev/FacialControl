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
    /// <see cref="ArKitOscAnalogSource"/> のテスト。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ArKitOscAnalogSourceTests : SizedTestFixture
    {
        private GameObject _receiverObj;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _receiverObj = new GameObject("OscReceiverArKitAnalogTest");
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

        private static string[] BuildArKit52ParameterNames()
        {
            // ARKit 52 PerfectSync の代表名（テスト用）
            return new[]
            {
                "browDownLeft", "browDownRight", "browInnerUp",
                "browOuterUpLeft", "browOuterUpRight",
                "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
                "eyeBlinkLeft", "eyeBlinkRight",
                "eyeLookDownLeft", "eyeLookDownRight",
                "eyeLookInLeft", "eyeLookInRight",
                "eyeLookOutLeft", "eyeLookOutRight",
                "eyeLookUpLeft", "eyeLookUpRight",
                "eyeSquintLeft", "eyeSquintRight",
                "eyeWideLeft", "eyeWideRight",
                "jawForward", "jawLeft", "jawOpen", "jawRight",
                "mouthClose", "mouthDimpleLeft", "mouthDimpleRight",
                "mouthFrownLeft", "mouthFrownRight",
                "mouthFunnel", "mouthLeft",
                "mouthLowerDownLeft", "mouthLowerDownRight",
                "mouthPressLeft", "mouthPressRight",
                "mouthPucker", "mouthRight",
                "mouthRollLower", "mouthRollUpper",
                "mouthShrugLower", "mouthShrugUpper",
                "mouthSmileLeft", "mouthSmileRight",
                "mouthStretchLeft", "mouthStretchRight",
                "mouthUpperUpLeft", "mouthUpperUpRight",
                "noseSneerLeft", "noseSneerRight",
                "tongueOut",
            };
        }

        // ============================================================
        // ctor / 引数バリデーション
        // ============================================================

        [Test]
        public void Ctor_NullReceiver_Throws()
        {
            var names = new[] { "jawOpen" };
            Assert.Throws<ArgumentNullException>(() =>
                new ArKitOscAnalogSource(InputSourceId.Parse("x-arkit"), null, names, 0f));
        }

        [Test]
        public void Ctor_NullParameterNames_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ArKitOscAnalogSource(InputSourceId.Parse("x-arkit"), _receiver, null, 0f));
        }

        [Test]
        public void Ctor_EmptyParameterNames_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ArKitOscAnalogSource(InputSourceId.Parse("x-arkit"), _receiver, Array.Empty<string>(), 0f));
        }

        [Test]
        public void Ctor_NullElementInParameterNames_Throws()
        {
            var names = new[] { "jawOpen", null };
            Assert.Throws<ArgumentException>(() =>
                new ArKitOscAnalogSource(InputSourceId.Parse("x-arkit"), _receiver, names, 0f));
        }

        [Test]
        public void Ctor_NegativeStaleness_Throws()
        {
            var names = new[] { "jawOpen" };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ArKitOscAnalogSource(InputSourceId.Parse("x-arkit"), _receiver, names, -1f));
        }

        // ============================================================
        // 基本契約
        // ============================================================

        [Test]
        public void AxisCount_MatchesParameterNamesLength()
        {
            var names = BuildArKit52ParameterNames();
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            Assert.AreEqual(52, source.AxisCount);
        }

        [Test]
        public void IsValid_BeforeAnyReceive_ReturnsFalse()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        // ============================================================
        // 値伝搬
        // ============================================================

        [Test]
        public void Tick_AfterReceiveOnSpecificAxis_PropagatesToCorrectIndex()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkLeft", 0.66f));
            source.Tick(0.016f);

            var output = new float[3];
            Assert.IsTrue(source.TryReadAxes(output));

            Assert.AreEqual(0f, output[0], 0.0001f, "jawOpen 未受信のため 0");
            Assert.AreEqual(0.66f, output[1], 0.0001f, "eyeBlinkLeft が axis 1 に書込まれる");
            Assert.AreEqual(0f, output[2], 0.0001f, "eyeBlinkRight 未受信のため 0");
        }

        [Test]
        public void Tick_AllAxesReceived_AllValuesPropagated()
        {
            var names = BuildArKit52ParameterNames();
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            for (int i = 0; i < names.Length; i++)
            {
                float v = 0.01f * (i + 1);
                _receiver.HandleOscMessage(new uOSC.Message("/ARKit/" + names[i], v));
            }
            source.Tick(0.016f);

            var output = new float[names.Length];
            Assert.IsTrue(source.TryReadAxes(output));
            for (int i = 0; i < names.Length; i++)
            {
                Assert.AreEqual(0.01f * (i + 1), output[i], 0.0001f);
            }
        }

        [Test]
        public void TryReadScalar_ReturnsAxisZero()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.42f));
            source.Tick(0f);

            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.42f, value, 0.0001f);
        }

        [Test]
        public void TryReadVector2_ReturnsAxisZeroAndOne()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkLeft", 0.2f));
            source.Tick(0f);

            Assert.IsTrue(source.TryReadVector2(out float x, out float y));
            Assert.AreEqual(0.1f, x, 0.0001f);
            Assert.AreEqual(0.2f, y, 0.0001f);
        }

        [Test]
        public void TryReadAxes_OutputShorterThanAxisCount_WritesOnlyOverlap()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkLeft", 0.2f));
            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkRight", 0.3f));
            source.Tick(0f);

            var output = new float[2];
            Assert.IsTrue(source.TryReadAxes(output));
            Assert.AreEqual(0.1f, output[0], 0.0001f);
            Assert.AreEqual(0.2f, output[1], 0.0001f);
        }

        [Test]
        public void TryReadAxes_OutputLongerThanAxisCount_WritesOnlyAxes()
        {
            var names = new[] { "jawOpen", "eyeBlinkLeft" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.1f));
            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkLeft", 0.2f));
            source.Tick(0f);

            var output = new float[] { 9f, 9f, 9f, 9f };
            Assert.IsTrue(source.TryReadAxes(output));

            Assert.AreEqual(0.1f, output[0], 0.0001f);
            Assert.AreEqual(0.2f, output[1], 0.0001f);
            Assert.AreEqual(9f, output[2]);
            Assert.AreEqual(9f, output[3]);
        }

        // ============================================================
        // staleness
        // ============================================================

        [Test]
        public void Tick_StalenessExceeded_IsValidBecomesFalse()
        {
            var names = new[] { "jawOpen" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0.1f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.5f));
            source.Tick(0f);
            Assert.IsTrue(source.IsValid);

            source.Tick(1f);

            Assert.IsFalse(source.IsValid);
        }

        [Test]
        public void Tick_StalenessZero_StaysValid()
        {
            var names = new[] { "jawOpen" };
            using var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.5f));
            source.Tick(0f);
            source.Tick(60f);

            Assert.IsTrue(source.IsValid);
        }

        // ============================================================
        // Dispose
        // ============================================================

        [Test]
        public void Dispose_StopsReceivingFurtherUpdates()
        {
            var names = new[] { "jawOpen" };
            var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.4f));
            source.Tick(0f);
            Assert.IsTrue(source.TryReadScalar(out float v1));
            Assert.AreEqual(0.4f, v1, 0.0001f);

            source.Dispose();

            _receiver.HandleOscMessage(new uOSC.Message("/ARKit/jawOpen", 0.9f));
            source.Tick(0f);

            Assert.IsTrue(source.TryReadScalar(out float v2));
            Assert.AreEqual(0.4f, v2, 0.0001f, "Dispose 後の受信は反映されない");
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var names = new[] { "jawOpen" };
            var source = new ArKitOscAnalogSource(
                InputSourceId.Parse("x-arkit"), _receiver, names, 0f);

            Assert.DoesNotThrow(() =>
            {
                source.Dispose();
                source.Dispose();
            });
        }
    }
}
