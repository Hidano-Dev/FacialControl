using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="ExpressionInputSourceAdapter"/> の dispatch 経路と
    /// subscribe / unsubscribe 対称性を検証する PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UnityEngine.InputSystem.InputAction.bindings"/> の先頭 path を
    /// <see cref="Hidano.FacialControl.Adapters.Input.InputDeviceCategorizer"/> で分類し、
    /// keyboard / controller の 2 sink へ dispatch する MonoBehaviour の挙動を確認する。
    /// </para>
    /// <para>
    /// テスト方針:
    /// <list type="bullet">
    ///   <item>Keyboard / Gamepad device を <see cref="InputTestFixture"/> 経由で simulate</item>
    ///   <item>2 sink の <see cref="ExpressionTriggerInputSourceBase.ActiveExpressionIds"/> を assert で確認</item>
    ///   <item>未認識 device は Controller 側 + warning 1 回 </item>
    ///   <item>OnEnable / OnDisable の繰返しで <see cref="ExpressionInputSourceAdapter.SubscribedBindingCount"/> がリークしないことを保証</item>
    /// </list>
    /// </para>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class ExpressionInputSourceAdapterTests : InputTestFixture
    {
        private static readonly string[] BlendShapeNames =
        {
            "smile", "angry", "sad", "surprised",
        };

        private GameObject _gameObject;
        private ExpressionInputSourceAdapter _adapter;
        private ExpressionTriggerInputSource _keyboardSink;
        private ExpressionTriggerInputSource _controllerSink;
        private Keyboard _keyboard;
        private Gamepad _gamepad;

        public override void Setup()
        {
            base.Setup();
            _keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
        }

        public override void TearDown()
        {
            if (_adapter != null)
            {
                _adapter.UnbindAll();
                _adapter = null;
            }
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
            base.TearDown();
        }

        // ============================================================
        // dispatch 経路: Keyboard / Controller
        // ============================================================

        [Test]
        public void OnKeyboardAction_ActivatesExpression_ViaKeyboardSink()
        {
            CreateAdapter();
            using var action = new InputAction(
                "SmileKey", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            Press(_keyboard.digit1Key);

            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile",
                "Keyboard 経由の Press が Keyboard sink にトリガーされていません。");
            CollectionAssert.DoesNotContain(_controllerSink.ActiveExpressionIds, "smile",
                "Keyboard 経由の Press が Controller sink にも誤って dispatch されています。");
        }

        [Test]
        public void OnGamepadAction_ActivatesExpression_ViaControllerSink()
        {
            CreateAdapter();
            using var action = new InputAction(
                "SmileGamepad", InputActionType.Button, "<Gamepad>/buttonSouth");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            Press(_gamepad.buttonSouth);

            CollectionAssert.Contains(_controllerSink.ActiveExpressionIds, "smile",
                "Gamepad 経由の Press が Controller sink にトリガーされていません。");
            CollectionAssert.DoesNotContain(_keyboardSink.ActiveExpressionIds, "smile",
                "Gamepad 経由の Press が Keyboard sink にも誤って dispatch されています。");
        }

        [Test]
        public void OnUnknownDevice_LogsWarning_AndUsesControllerSink()
        {
            CreateAdapter();

            // 未認識 device prefix を持つ binding (path 構文として有効だが
            // InputDeviceCategorizer の認識リストに無い <Mouse> を使う)。
            // 予期される警告 1 回を LogAssert.Expect で許容する 。
            LogAssert.Expect(LogType.Warning,
                new Regex(@"\[ExpressionInputSourceAdapter\] unrecognized device category"));

            using var action = new InputAction(
                "SmileMouse", InputActionType.Button, "<Mouse>/leftButton");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            // mouse device は AddDevice していないため実 device からの press はできないが、
            // dispatch 経路 (ResolveSink) は performed コールバックで初めて呼ばれる。
            // 直接 InvokeManually せず、performed コールバックを擬似的に呼ぶために
            // bind 直後の dispatch dummy ではなく LogAssert で実発生済の warning を expect している。
            // ここでは ResolveSink の事前 warmup を行うため、Mouse device を追加して 1 度 press する。
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
            Press(mouse.leftButton);

            CollectionAssert.Contains(_controllerSink.ActiveExpressionIds, "smile",
                "未認識 device は Controller sink にフォールバックされる必要があります 。");
            CollectionAssert.DoesNotContain(_keyboardSink.ActiveExpressionIds, "smile",
                "未認識 device が Keyboard sink に誤って dispatch されています。");
        }

        // ============================================================
        // subscribe / unsubscribe 対称性
        // ============================================================

        [Test]
        public void MultipleEnableDisable_DoesNotLeakSubscriptions()
        {
            CreateAdapter();
            using var action = new InputAction(
                "Smile", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            Assert.AreEqual(1, _adapter.BindingCount);
            Assert.AreEqual(1, _adapter.SubscribedBindingCount,
                "OnEnable 直後の subscribe 数が登録 binding 数と一致していません。");

            for (int i = 0; i < 5; i++)
            {
                _gameObject.SetActive(false);
                Assert.AreEqual(0, _adapter.SubscribedBindingCount,
                    $"OnDisable 後 (iteration {i}) に subscribe 数が 0 にならず購読がリークしています。");

                _gameObject.SetActive(true);
                Assert.AreEqual(1, _adapter.SubscribedBindingCount,
                    $"OnEnable 後 (iteration {i}) に subscribe 数が 1 に戻らず復元されていません。");
            }

            // 5 回繰返し後に Press して、最終 subscribe 状態が正常に dispatch することを確認
            Press(_keyboard.digit1Key);
            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile",
                "OnEnable / OnDisable を繰返した後の Press が Keyboard sink に dispatch されていません。");
        }

        // ============================================================
        // ヘルパー
        // ============================================================

        [Test]
        public void TogglePerformed_WhenSinkIsSuspended_DoesNotInvertBindingState()
        {
            CreateAdapter();
            using var action = new InputAction(
                "SmileToggle", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile", TriggerMode.Toggle);
            action.Enable();

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);
            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile");

            _keyboardSink.SuspendTriggerInput();

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);

            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile");

            _keyboardSink.ResumeTriggerInput();
            _adapter.Tick(0f);

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);

            CollectionAssert.DoesNotContain(_keyboardSink.ActiveExpressionIds, "smile");
        }

        [Test]
        public void Tick_AfterSuspensionEnds_SynchronizesToggleBindingStateWithSinkStack()
        {
            CreateAdapter();
            using var action = new InputAction(
                "SmileToggle", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile", TriggerMode.Toggle);
            action.Enable();

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);
            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile");

            _keyboardSink.SuspendTriggerInput();
            _keyboardSink.ResetToExpressionStack(System.Array.Empty<string>());
            _keyboardSink.ResumeTriggerInput();

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);
            CollectionAssert.DoesNotContain(_keyboardSink.ActiveExpressionIds, "smile");

            _adapter.Tick(0f);

            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);

            CollectionAssert.Contains(_keyboardSink.ActiveExpressionIds, "smile");
        }

        private void CreateAdapter()
        {
            _gameObject = new GameObject("ExpressionInputSourceAdapterTest");
            _adapter = _gameObject.AddComponent<ExpressionInputSourceAdapter>();

            var profile = BuildProfile();

            _keyboardSink = new ExpressionTriggerInputSource(
                id: InputSourceId.Parse(ExpressionTriggerInputSource.InputReservedId),
                blendShapeCount: BlendShapeNames.Length,
                maxStackDepth: 8,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: BlendShapeNames,
                profile: profile);

            _controllerSink = new ExpressionTriggerInputSource(
                id: InputSourceId.Parse(ExpressionTriggerInputSource.InputReservedId),
                blendShapeCount: BlendShapeNames.Length,
                maxStackDepth: 8,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: BlendShapeNames,
                profile: profile);

            _adapter.Initialize(_keyboardSink, _controllerSink);
        }

        private static FacialProfile BuildProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", priority: 0, ExclusionMode.LastWins),
            };

            var expressions = new[]
            {
                new Expression(
                    id: "smile",
                    name: "smile",
                    layer: "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[]
                    {
                        new BlendShapeMapping("smile", 1.0f),
                    }),
            };

            return new FacialProfile("1.0", layers: layers, expressions: expressions);
        }
    }
}
