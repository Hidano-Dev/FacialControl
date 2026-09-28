using System;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Performance
{
    /// <summary>
    //: <see cref="ExpressionInputSourceAdapter"/> の
    /// <c>InputAction.performed</c> dispatch ホットパスが 100 回連続呼出で 0-alloc であることを
    /// 検証する PlayMode/Performance テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// adapter の <c>BindingEntry</c> は構築時に delegate を 1 度だけキャッシュし、
    /// 購読 / 解除に伴う closure 確保が無いことを保証する。dispatch 経路の値読取は
    /// <c>InputAction.CallbackContext.ReadValue&lt;float&gt;</c> を struct context 経由で呼ぶため
    /// boxing が発生しない。本テストは <see cref="InputTestFixture"/> の <c>Press</c> / <c>Release</c>
    /// で keyboard / gamepad 両 device 経由の dispatch を 100 回繰り返し、ProfilerRecorder で
    /// adapter 経路に GC アロケーションが発生しないことを検証する。
    /// </para>
    /// <para>
    /// InputSystem 内部のイベントキュー処理は本 adapter の責務外だが、
    /// Press / Release を <c>InputState.Change</c> 経由で driving した場合の総アロケーションが
    /// 0 であることを期待する（InputSystem 1.x の steady-state 振る舞い）。
    /// </para>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class ExpressionInputSourceAdapterAllocationTests : InputTestFixture
    {
        private const int DispatchCount = 100;

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

        [Test]
        public void KeyboardPerformed_100Dispatches_ZeroGCAllocation()
        {
            CreateAdapter();
            using var action = new InputAction("Smile", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            // ウォームアップ: 1 回 Press/Release で JIT 等の初期化を済ませる
            Press(_keyboard.digit1Key);
            Release(_keyboard.digit1Key);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int i = 0; i < DispatchCount; i++)
            {
                Press(_keyboard.digit1Key);
                Release(_keyboard.digit1Key);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"Keyboard dispatch で GC アロケーションが検出されました: {gcAlloc} bytes (100 回 dispatch)");
        }

        [Test]
        public void GamepadPerformed_100Dispatches_ZeroGCAllocation()
        {
            CreateAdapter();
            using var action = new InputAction("Smile", InputActionType.Button, "<Gamepad>/buttonSouth");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            // ウォームアップ
            Press(_gamepad.buttonSouth);
            Release(_gamepad.buttonSouth);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int i = 0; i < DispatchCount; i++)
            {
                Press(_gamepad.buttonSouth);
                Release(_gamepad.buttonSouth);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"Gamepad dispatch で GC アロケーションが検出されました: {gcAlloc} bytes (100 回 dispatch)");
        }

        [Test]
        public void Tick_100Frames_ZeroGCAllocation()
        {
            CreateAdapter();
            using var action = new InputAction("Smile", InputActionType.Button, "<Keyboard>/1");
            _adapter.BindExpression(action, "smile");
            action.Enable();

            // 1 件アクティブにしてから Tick の遷移進行を計測する
            Press(_keyboard.digit1Key);

            // ウォームアップ
            _adapter.Tick(0.016f);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < DispatchCount; frame++)
            {
                _adapter.Tick(0.016f);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"adapter.Tick で GC アロケーションが検出されました: {gcAlloc} bytes (100 frames)");
        }

        [Test]
        public void BindUnbind_DoesNotLeakAfterWarmup()
        {
            CreateAdapter();

            // 計測対象は adapter の BindExpression / UnbindExpression の純粋な確保のみに絞るため、
            // InputAction の new / Dispose を計測区間の外で済ませる (InputSystem 内部の確保は本テストの責務外)。
            var actions = new InputAction[DispatchCount];
            for (int i = 0; i < DispatchCount; i++)
            {
                actions[i] = new InputAction("Bind" + i, InputActionType.Button, "<Keyboard>/1");
            }

            try
            {
                // ウォームアップ: bind → unbind を 1 回行い、内部 Dictionary の容量確保等を済ませる
                _adapter.BindExpression(actions[0], "smile");
                _adapter.UnbindExpression(actions[0]);

                using var recorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory, "GC.Alloc");

                for (int i = 0; i < DispatchCount; i++)
                {
                    _adapter.BindExpression(actions[i], "smile");
                    _adapter.UnbindExpression(actions[i]);
                }

                long gcAlloc = recorder.LastValue;

                // BindingEntry (1) + delegate (2) を毎回生成するため per-iter ~200 bytes は許容する。
                // 100 iter × ~256 bytes = 25KB を上限とした緩めの上界で予期せぬ追加確保を検出する。
                long upperBound = DispatchCount * 256L;
                Assert.That(gcAlloc, Is.LessThanOrEqualTo(upperBound),
                    $"BindExpression / UnbindExpression で予想外の GC アロケーションが検出されました: " +
                    $"{gcAlloc} bytes (upper bound: {upperBound} bytes)");

                // adapter 内部の bindings 辞書がリークしていないことも確認する。
                Assert.AreEqual(0, _adapter.BindingCount,
                    "BindExpression / UnbindExpression のペアで adapter 内部辞書にリークが発生しています。");
            }
            finally
            {
                for (int i = 0; i < DispatchCount; i++)
                {
                    actions[i]?.Dispose();
                }
            }
        }

        // ============================================================
        // ヘルパー
        // ============================================================

        private void CreateAdapter()
        {
            _gameObject = new GameObject("ExpressionInputSourceAdapterAllocationTest");
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
