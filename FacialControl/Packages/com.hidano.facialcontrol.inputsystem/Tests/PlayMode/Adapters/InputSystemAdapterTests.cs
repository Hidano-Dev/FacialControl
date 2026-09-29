using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Input;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P08-T04: InputSystemAdapter の Button / Value トリガーテスト。
    /// InputTestFixture を使用して InputAction のシミュレーションを行う。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class InputSystemAdapterTests : InputTestFixture
    {
        private GameObject _gameObject;
        private InputSystemAdapter _adapter;
        private Keyboard _keyboard;
        private Gamepad _gamepad;

        public override void Setup()
        {
            base.Setup();
            // namespace 衝突回避のため UnityEngine.InputSystem.InputSystem を完全修飾
            _keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
        }

        public override void TearDown()
        {
            if (_adapter != null)
            {
                _adapter.Dispose();
                _adapter = null;
            }
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
            base.TearDown();
        }

        // ================================================================
        // 初期化
        // ================================================================

        [Test]
        public void SetFacialController_ValidController_SetsReference()
        {
            var adapter = CreateAdapter(out var controller);

            Assert.IsNotNull(adapter);
            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void SetFacialController_Null_DoesNotThrow()
        {
            _adapter = new InputSystemAdapter(null);

            Assert.DoesNotThrow(() => _adapter.FacialController = null);
        }

        // ================================================================
        // Button トリガー（トグル）
        // ================================================================

        [Test]
        public void ButtonTrigger_Press_ActivatesExpression()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            // Expression バインディングを登録
            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            adapter.BindExpression(action, expression);
            action.Enable();

            // ボタン押下シミュレーション
            Press(_keyboard.digit1Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(expression.Id, active[0].Id);
        }

        [Test]
        public void ButtonTrigger_PressAgain_DeactivatesExpression()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            adapter.BindExpression(action, expression);
            action.Enable();

            // 1回目: アクティブ化
            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);

            // 2回目: 非アクティブ化
            Press(_keyboard.digit1Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void ButtonTrigger_MultipleDifferentButtons_ActivatesDifferentExpressions()
        {
            var adapter = CreateAdapterWithBlendLayer(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expr1 = profile.Expressions.Span[0];
            var expr2 = profile.Expressions.Span[1];

            var action1 = CreateButtonAction("Trigger1", "<Keyboard>/1");
            var action2 = CreateButtonAction("Trigger2", "<Keyboard>/2");
            adapter.BindExpression(action1, expr1);
            adapter.BindExpression(action2, expr2);
            action1.Enable();
            action2.Enable();

            Press(_keyboard.digit1Key);
            Press(_keyboard.digit2Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [Test]
        public void ButtonTrigger_DisabledAction_DoesNotActivate()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            adapter.BindExpression(action, expression);
            // action は Enable されない

            Press(_keyboard.digit1Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // Value トリガー（アナログ強度）
        // ================================================================

        [Test]
        public void ValueTrigger_FullValue_ActivatesExpression()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateValueAction("IntensityHappy", "<Gamepad>/leftTrigger");
            adapter.BindExpression(action, expression);
            action.Enable();

            // フルプレス
            Set(_gamepad.leftTrigger, 1.0f);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(expression.Id, active[0].Id);
        }

        [Test]
        public void ValueTrigger_ZeroValue_DeactivatesExpression()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateValueAction("IntensityHappy", "<Gamepad>/leftTrigger");
            adapter.BindExpression(action, expression);
            action.Enable();

            // アクティブ化してからゼロに戻す
            Set(_gamepad.leftTrigger, 1.0f);
            Set(_gamepad.leftTrigger, 0.0f);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void ValueTrigger_PartialValue_ActivatesExpression()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateValueAction("IntensityHappy", "<Gamepad>/leftTrigger");
            adapter.BindExpression(action, expression);
            action.Enable();

            // 中間値（0 より大きければアクティブ化される）
            Set(_gamepad.leftTrigger, 0.5f);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
        }

        // ================================================================
        // バインディング管理
        // ================================================================

        [Test]
        public void UnbindExpression_RemovesBinding()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            adapter.BindExpression(action, expression);
            adapter.UnbindExpression(action);
            action.Enable();

            Press(_keyboard.digit1Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void UnbindAll_RemovesAllBindings()
        {
            var adapter = CreateAdapterWithBlendLayer(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expr1 = profile.Expressions.Span[0];
            var expr2 = profile.Expressions.Span[1];

            var action1 = CreateButtonAction("Trigger1", "<Keyboard>/1");
            var action2 = CreateButtonAction("Trigger2", "<Keyboard>/2");
            adapter.BindExpression(action1, expr1);
            adapter.BindExpression(action2, expr2);
            adapter.UnbindAll();
            action1.Enable();
            action2.Enable();

            Press(_keyboard.digit1Key);
            Press(_keyboard.digit2Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void BindExpression_WithoutController_DoesNotThrow()
        {
            _adapter = new InputSystemAdapter(null);
            var expression = CreateTestExpression("expr-001", "Happy", "emotion");

            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            Assert.DoesNotThrow(() => _adapter.BindExpression(action, expression));
        }

        // ================================================================
        // device 推定経路（InputDeviceCategorizer 連携）
        // ================================================================

        [Test]
        public void BindExpression_KeyboardBinding_DispatchesToKeyboardCategory()
        {
            var adapter = CreateAdapterWithBlendLayer(out var controller);
            var expr = controller.CurrentProfile.Value.Expressions.Span[0];
            var keyboardAction = CreateButtonAction("KeyboardTrigger", "<Keyboard>/1");

            adapter.BindExpression(keyboardAction, expr);

            Assert.IsTrue(adapter.TryGetDeviceCategory(keyboardAction, out var category));
            Assert.AreEqual(DeviceCategory.Keyboard, category);
        }

        [Test]
        public void BindExpression_GamepadBinding_DispatchesToControllerCategory()
        {
            var adapter = CreateAdapterWithBlendLayer(out var controller);
            var expr = controller.CurrentProfile.Value.Expressions.Span[0];
            var gamepadAction = CreateValueAction("GamepadTrigger", "<Gamepad>/leftTrigger");

            adapter.BindExpression(gamepadAction, expr);

            Assert.IsTrue(adapter.TryGetDeviceCategory(gamepadAction, out var category));
            Assert.AreEqual(DeviceCategory.Controller, category);
        }

        [Test]
        public void BindExpression_UnknownDevice_FallsBackToControllerWithWarning()
        {
            var adapter = CreateAdapterWithBlendLayer(out var controller);
            var expr = controller.CurrentProfile.Value.Expressions.Span[0];
            var unknownAction = CreateButtonAction("UnknownTrigger", "<Mystery>/button");

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("InputSystemAdapter.*unrecognized device category"));
            adapter.BindExpression(unknownAction, expr);

            Assert.IsTrue(adapter.TryGetDeviceCategory(unknownAction, out var category));
            Assert.AreEqual(DeviceCategory.Controller, category);
        }

        [Test]
        public void TryGetDeviceCategory_UnregisteredAction_ReturnsFalse()
        {
            var adapter = CreateAdapterWithBlendLayer(out _);
            var unboundAction = CreateButtonAction("Unbound", "<Keyboard>/2");

            Assert.IsFalse(adapter.TryGetDeviceCategory(unboundAction, out var category));
            Assert.AreEqual(DeviceCategory.Controller, category);
        }

        // ================================================================
        // Dispose
        // ================================================================

        [Test]
        public void Dispose_CleansUpBindings()
        {
            var adapter = CreateAdapter(out var controller);
            var profile = controller.CurrentProfile.Value;
            var expression = profile.Expressions.Span[0];

            var action = CreateButtonAction("TriggerHappy", "<Keyboard>/1");
            adapter.BindExpression(action, expression);
            action.Enable();

            // Adapter を破棄
            adapter.Dispose();
            _adapter = null;

            // ボタン押下してもアクティブにならない
            Press(_keyboard.digit1Key);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private InputSystemAdapter CreateAdapter(out FacialController controller)
        {
            var profile = CreateTestProfile();
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            controller = _gameObject.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);

            _adapter = new InputSystemAdapter(controller);
            return _adapter;
        }

        private InputSystemAdapter CreateAdapterWithBlendLayer(out FacialController controller)
        {
            var profile = CreateBlendProfile();
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            controller = _gameObject.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);

            _adapter = new InputSystemAdapter(controller);
            return _adapter;
        }

        private static GameObject CreateGameObjectWithAnimatorAndRenderer()
        {
            var go = new GameObject("InputAdapterTest");
            go.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(go.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-001", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-002", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateBlendProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.Blend)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-001", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-002", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static Expression CreateTestExpression(string id, string name, string layer)
        {
            return new Expression(id, name, layer);
        }

        private static InputAction CreateButtonAction(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button, binding);
            return action;
        }

        private static InputAction CreateValueAction(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Value, binding);
            return action;
        }
    }
}
