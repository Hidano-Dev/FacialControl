using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="InputActionAnalogSource"/> のテスト。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class InputActionAnalogSourceTests : InputTestFixture
    {
        private Gamepad _gamepad;

        public override void Setup()
        {
            base.Setup();
            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
        }

        public override void TearDown()
        {
            base.TearDown();
        }

        // ============================================================
        // ctor / 引数バリデーション
        // ============================================================

        [Test]
        public void Ctor_NullAction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new InputActionAnalogSource(InputSourceId.Parse("x-test"), null, AnalogInputShape.Scalar));
        }

        [Test]
        public void Ctor_DefaultId_Throws()
        {
            using var action = new InputAction("anyAction", InputActionType.Value, "<Gamepad>/leftTrigger");
            Assert.Throws<ArgumentException>(() =>
                new InputActionAnalogSource(default, action, AnalogInputShape.Scalar));
        }

        [Test]
        public void Ctor_UndefinedShape_Throws()
        {
            using var action = new InputAction("anyAction", InputActionType.Value, "<Gamepad>/leftTrigger");
            Assert.Throws<ArgumentException>(() =>
                new InputActionAnalogSource(
                    InputSourceId.Parse("x-test"),
                    action,
                    (AnalogInputShape)99));
        }

        // ============================================================
        // 基本契約
        // ============================================================

        [Test]
        public void AxisCount_Scalar_IsOne()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);
            Assert.AreEqual(1, source.AxisCount);
        }

        [Test]
        public void AxisCount_Vector2_IsTwo()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);
            Assert.AreEqual(2, source.AxisCount);
        }

        [Test]
        public void Id_ReturnsConstructorId()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);
            Assert.AreEqual("x-stick", source.Id);
        }

        [Test]
        public void IsValid_BeforeTick_ReturnsFalse()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);
            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadVector2(out _, out _));
        }

        // ============================================================
        // 値伝搬
        // ============================================================

        [Test]
        public void Tick_Vector2Action_PropagatesValueAndIsValidTrue()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);

            Set(_gamepad.leftStick, new Vector2(0.5f, -0.25f));
            source.Tick(0.016f);

            // Gamepad の leftStick は stickDeadzone 等のプロセッサが付与されるため、
            // 注入した raw 値と action.ReadValue<Vector2>() は一致しない。
            // 本ソースはアクションの値を忠実に転写することを契約とする。
            var expected = action.ReadValue<Vector2>();

            Assert.IsTrue(source.IsValid);
            Assert.IsTrue(source.TryReadVector2(out float x, out float y));
            Assert.AreEqual(expected.x, x, 0.0001f);
            Assert.AreEqual(expected.y, y, 0.0001f);
            // 入力が原点でないことの sanity check
            Assert.That(Mathf.Abs(x) + Mathf.Abs(y), Is.GreaterThan(0.1f),
                "Vector2 値が伝搬していません (ほぼゼロのまま)");
        }

        [Test]
        public void Tick_ScalarAction_PropagatesValueAndIsValidTrue()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.75f);
            source.Tick(0.016f);

            Assert.IsTrue(source.IsValid);
            Assert.IsTrue(source.TryReadScalar(out float value));
            Assert.AreEqual(0.75f, value, 0.01f);
        }

        [Test]
        public void TryReadVector2_ScalarSource_ReturnsFalse()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.5f);
            source.Tick(0.016f);

            Assert.IsFalse(source.TryReadVector2(out _, out _));
        }

        [Test]
        public void TryReadAxes_Vector2Source_WritesBothAxes()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);

            Set(_gamepad.leftStick, new Vector2(0.3f, 0.6f));
            source.Tick(0.016f);

            // stickDeadzone 等のプロセッサで raw 値は変換されるため、
            // action.ReadValue<Vector2>() の値を期待値とする。
            var expected = action.ReadValue<Vector2>();

            var output = new float[] { 9f, 9f, 9f };
            Assert.IsTrue(source.TryReadAxes(output));

            Assert.AreEqual(expected.x, output[0], 0.0001f);
            Assert.AreEqual(expected.y, output[1], 0.0001f);
            Assert.AreEqual(9f, output[2]);
            Assert.That(Mathf.Abs(output[0]) + Mathf.Abs(output[1]), Is.GreaterThan(0.1f),
                "Vector2 値が伝搬していません (ほぼゼロのまま)");
        }

        [Test]
        public void TryReadAxes_ScalarSource_WritesOnlyAxisZero()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.42f);
            source.Tick(0.016f);

            var output = new float[] { 9f, 9f };
            Assert.IsTrue(source.TryReadAxes(output));
            Assert.AreEqual(0.42f, output[0], 0.01f);
            Assert.AreEqual(9f, output[1]);
        }

        [Test]
        public void TryReadAxes_EmptyOutput_ReturnsFalse()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.5f);
            source.Tick(0.016f);

            Assert.IsFalse(source.TryReadAxes(Array.Empty<float>()));
        }

        // ============================================================
        // disable / unbound 経路
        // ============================================================

        [Test]
        public void Tick_DisabledAction_IsValidFalseAndNoThrow()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            // Enable しない
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Assert.DoesNotThrow(() => source.Tick(0.016f));

            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        [Test]
        public void Tick_DisableAfterValid_IsValidBecomesFalse()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.5f);
            source.Tick(0.016f);
            Assert.IsTrue(source.IsValid);

            action.Disable();
            source.Tick(0.016f);

            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        [Test]
        public void Tick_UnboundAction_IsValidFalseAndNoThrow()
        {
            // どの control にもバインドされない action を作成（binding 文字列は存在するが
            // パスが解決不能な場合 controls.Count == 0 になる）。
            using var action = new InputAction("noBinding", InputActionType.Value);
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-unbound"), action, AnalogInputShape.Scalar);

            Assert.DoesNotThrow(() => source.Tick(0.016f));
            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        // ============================================================
        // GC ゼロ
        // ============================================================

        [Test]
        public void Tick_RepeatedReadValue_ZeroManagedAllocation()
        {
            using var action = new InputAction("vec2", InputActionType.Value, "<Gamepad>/leftStick");
            action.Enable();
            using var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-stick"), action, AnalogInputShape.Vector2);

            Set(_gamepad.leftStick, new Vector2(0.5f, 0.25f));

            // Warmup: JIT と初回 ReadValue キャッシュを安定化させる。
            for (int i = 0; i < 32; i++)
            {
                source.Tick(0.016f);
                source.TryReadVector2(out _, out _);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            long monoBefore = Profiler.GetMonoUsedSizeLong();

            for (int i = 0; i < 1000; i++)
            {
                source.Tick(0.016f);
                source.TryReadVector2(out _, out _);
            }

            long managedAfter = GC.GetTotalMemory(forceFullCollection: false);
            long monoAfter = Profiler.GetMonoUsedSizeLong();

            long managedDiff = managedAfter - managedBefore;
            long monoDiff = monoAfter - monoBefore;

            Assert.LessOrEqual(managedDiff, 0,
                $"InputActionAnalogSource.Tick の 1000 回ループで managed 差分が 0 を超えました: " +
                $"{managedDiff} bytes (mono diff={monoDiff})");
        }

        // ============================================================
        // Dispose
        // ============================================================

        [Test]
        public void Dispose_StopsFurtherUpdates()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            action.Enable();
            var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);

            Set(_gamepad.leftTrigger, 0.3f);
            source.Tick(0.016f);
            Assert.IsTrue(source.TryReadScalar(out float v1));
            Assert.AreEqual(0.3f, v1, 0.01f);

            source.Dispose();

            Set(_gamepad.leftTrigger, 0.9f);
            source.Tick(0.016f);

            Assert.IsFalse(source.IsValid);
            Assert.IsFalse(source.TryReadScalar(out _));
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            using var action = new InputAction("scalar", InputActionType.Value, "<Gamepad>/leftTrigger");
            var source = new InputActionAnalogSource(
                InputSourceId.Parse("x-trig"), action, AnalogInputShape.Scalar);
            Assert.DoesNotThrow(() =>
            {
                source.Dispose();
                source.Dispose();
            });
        }
    }
}
