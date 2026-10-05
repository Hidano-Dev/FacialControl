using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration
{
    /// <summary>
    /// task 10.1 PlayMode 観測可能完了条件:
    /// <see cref="InputSystemAdapterBinding"/> が <c>OnStart</c> で
    /// <c>InputActionAsset.Instantiate</c> + <c>ActionMap.Enable</c> を実行し、
    /// InputAction 仮想 device → ExpressionTrigger / Analog / Gaze の 3 経路（D-8 集約）で
    /// 入力源が登録 / 解決可能になることを assert する（gaze の目ボーン適用は core の
    /// FacialController に集約したため、本 binding は gaze 入力源の登録までを担う）。
    /// <c>Dispose</c> で <c>ActionMap.Disable</c> + 内部 Asset destroy が
    /// 走り再 <c>Dispose</c> 呼び出しが冪等であることも検証する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding</c>
    /// が未実装のためコンパイル時に CS0246 / CS0234 が発生して Red 状態となる
    /// （task 10.2 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class InputSystemAdapterBindingIntegrationTests : SizedTestFixture
    {
        private GameObject _hostGameObject;
        private InputSourceRegistry _registry;
        private InputSystemAdapterBinding _binding;
        private InputActionAsset _sourceAsset;
        private bool _bindingStarted;
        private Keyboard _keyboard;
        private Gamepad _gamepad;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("InputSystemAdapterBindingIntegrationTestsHost");
            _bindingStarted = false;

            // 仮想 device を test runtime に register。
            _keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
                    // TearDown では例外を握り潰し、テスト本体の assertion を優先する。
                }
            }
            _binding = null;
            _bindingStarted = false;

            if (_sourceAsset != null)
            {
                UnityEngine.Object.DestroyImmediate(_sourceAsset);
                _sourceAsset = null;
            }

            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }

            if (_keyboard != null)
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }
            if (_gamepad != null)
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(_gamepad);
                _gamepad = null;
            }
        }

        // ---------------------------------------------------------------
        // OnStart: ActionMap.Enable + InputSourceRegistry に primary 登録
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_EnablesActionMapOnInstantiatedRuntimeAsset()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-action-map-enable",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            InputActionMap runtimeMap = _binding.RuntimeActionMap;
            Assert.IsNotNull(runtimeMap,
                "OnStart 後は RuntimeActionMap が解決済みであるべき。");
            Assert.IsTrue(runtimeMap.enabled,
                "OnStart は instantiate した runtime ActionMap を Enable するべき。");
        }

        [Test]
        public void OnStart_DoesNotEnableSourceAssetActionMap_OnlyRuntimeClone()
        {
            // Source asset は未変更で、Instantiate された clone のみが Enable される。
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-no-source-mutation",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            InputActionMap sourceMap = _sourceAsset.FindActionMap("Expression");
            Assert.IsNotNull(sourceMap);
            Assert.IsFalse(sourceMap.enabled,
                "Source InputActionAsset 側の ActionMap は Enable されないべき（Instantiate された clone のみ Enable される）。");
        }

        [Test]
        public void OnStart_RegistersPrimaryInputSourceUnderSlug()
        {
            const string slug = "input-system-primary-resolve";
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug, out IInputSource source);
            Assert.IsTrue(resolved,
                $"InputSourceRegistry.TryResolve(\"{slug}\") は OnStart 後に true を返すべき。");
            Assert.IsNotNull(source,
                "解決結果の IInputSource は non-null であるべき。");
        }

        // ---------------------------------------------------------------
        // OnStart: ExpressionTrigger / Analog / Gaze の 3 経路集約（D-8）
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_ExpressionTriggerPath_AddsExpressionInputSourceAdapterToHostGameObject()
        {
            // D-8 集約: ExpressionTrigger 経路は host GameObject 上の ExpressionInputSourceAdapter 経由で実装される。
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-trigger-path",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            ExpressionInputSourceAdapter adapter =
                _hostGameObject.GetComponent<ExpressionInputSourceAdapter>();
            Assert.IsNotNull(adapter,
                "OnStart は ExpressionTrigger 経路のために ExpressionInputSourceAdapter を host GameObject に AddComponent するべき。");
        }

        [Test]
        public void OnStart_AnalogPath_RegistersAnalogSourceUnderCompositeSlug()
        {
            // Gaze の source はチャネル id を sub-id とした規約キーで登録される。
            const string slug = "input-system-analog-path";
            _sourceAsset = CreateGazeActionAsset(
                actionMapName: "Expression",
                gazeActionName: "GazeLook");

            var gazeBinding = new ExpressionBindingEntry
            {
                bindingMode = BindingMode.Gaze,
                expressionId = "expr-gaze",
                actionName = "GazeLook",
            };

            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry> { gazeBinding });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug + ":expr-gaze", out IInputSource source);
            Assert.IsTrue(resolved,
                $"Gaze 経路の InputSource は \"{slug}:expr-gaze\" で解決できるべき。");
            Assert.IsNotNull(source);

            Assert.IsFalse(_registry.TryResolve(slug + ":GazeLook", out _),
                "Gaze 経路で Action 名由来の旧エイリアスを登録してはならない。");
        }

        [Test]
        public void OnStart_GazePath_DistinctMode_RegistersLeftRightExpressionAliases()
        {
            const string slug = "input-system-gaze-distinct-alias";
            _sourceAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _sourceAsset.AddActionMap("Expression");
            map.AddAction("LeftLook", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Gamepad>/leftStick");
            map.AddAction("RightLook", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Gamepad>/rightStick");

            var gazeBinding = new ExpressionBindingEntry
            {
                bindingMode = BindingMode.Gaze,
                expressionId = "expr-gaze",
                useDistinctLeftRight = true,
                actionNameLeft = "LeftLook",
                actionNameRight = "RightLook",
            };

            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry> { gazeBinding });
            ((IGazeChannelConsumer)_binding).ConfigureGazeChannels(new[] { "expr-gaze" });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.IsTrue(_registry.TryResolve(slug + ":expr-gaze.left", out IInputSource leftAlias));
            Assert.IsTrue(_registry.TryResolve(slug + ":expr-gaze.right", out IInputSource rightAlias));
            Assert.IsInstanceOf<IAnalogInputSource>(leftAlias);
            Assert.IsInstanceOf<IAnalogInputSource>(rightAlias);
        }

        [Test]
        public void OnStart_OverlayPath_DeclaredSlot_RegistersOverlayInputSource()
        {
            const string slug = "input-system-overlay-declared-slot";
            _sourceAsset = CreateValueActionAsset(
                actionMapName: "Expression",
                actionName: "BlinkWeight",
                binding: "<Gamepad>/rightTrigger");

            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = BindingMode.Overlay,
                        actionName = "BlinkWeight",
                        overlaySlot = "blink",
                    },
                });

            AdapterBuildContext ctx = CreateContext(slots: new[] { "blink" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug + ":overlay:blink", out IInputSource source);
            Assert.IsTrue(resolved, "Slots に宣言された overlaySlot は overlay source として登録されるべき。");
            Assert.IsInstanceOf<OverlayInputSource>(source);
        }

        [Test]
        public void OnStart_OverlayPath_UndeclaredSlot_LogsWarningAndSkipsOverlaySource()
        {
            const string slug = "input-system-overlay-undeclared-slot";
            _sourceAsset = CreateValueActionAsset(
                actionMapName: "Expression",
                actionName: "BlinkWeight",
                binding: "<Gamepad>/rightTrigger");

            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = BindingMode.Overlay,
                        actionName = "BlinkWeight",
                        overlaySlot = "missing",
                    },
                });

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"\[InputSystemAdapterBinding\].*'missing'"));

            AdapterBuildContext ctx = CreateContext(slots: new[] { "blink" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug + ":overlay:missing", out IInputSource source);
            Assert.IsFalse(resolved, "Slots 未宣言の overlaySlot は overlay source として登録されてはならない。");
            Assert.IsNull(source);
        }

        [Test]
        public void OnStart_GazePath_ConfigWithoutBinding_SkipsSilently()
        {
            _sourceAsset = CreateGazeActionAsset(
                actionMapName: "Expression",
                gazeActionName: "GazeLook");

            _binding = CreateBinding(
                slug: "input-system-gaze-pairing-missing-binding",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>(),
                injectedGazeConfigs: new List<GazeChannel> { CreateGazeConfig("expr-gaze") });

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------
        // Dispose: ActionMap.Disable + Asset destroy
        // ---------------------------------------------------------------

        [Test]
        public void Dispose_DisablesRuntimeActionMap()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-dispose-disable",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            InputActionMap runtimeMap = _binding.RuntimeActionMap;
            Assert.IsNotNull(runtimeMap);
            Assert.IsTrue(runtimeMap.enabled, "Sanity: OnStart 後は ActionMap が Enable されている。");

            _binding.Dispose();
            _bindingStarted = false;

            Assert.IsFalse(runtimeMap.enabled,
                "Dispose は runtime ActionMap を Disable するべき。");
        }

        [UnityTest]
        public System.Collections.IEnumerator Dispose_DestroysInstantiatedRuntimeActionAsset()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-dispose-destroy",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            InputActionAsset runtimeAsset = _binding.RuntimeActionAsset;
            Assert.IsNotNull(runtimeAsset, "Sanity: OnStart 後は RuntimeActionAsset が Instantiate 済み。");
            Assert.AreNotSame(_sourceAsset, runtimeAsset,
                "RuntimeActionAsset は source asset の Instantiate clone であるべき。");

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            // Unity の Object 等価性: Destroy 後の参照は == null となる。
            Assert.IsTrue(runtimeAsset == null,
                "Dispose は Instantiate した runtime ActionAsset を Destroy するべき。");
        }

        [Test]
        public void Dispose_DoesNotDestroySourceAsset()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-dispose-source-preserved",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            _binding.Dispose();
            _bindingStarted = false;

            Assert.IsTrue(_sourceAsset != null,
                "Dispose は source InputActionAsset を破棄しないべき（外部参照のため）。");
        }

        [Test]
        public void Dispose_TogglesIsStartedToFalse()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-dispose-flag",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;
            Assert.IsTrue(_binding.IsStarted, "Sanity: OnStart 後は IsStarted == true。");

            _binding.Dispose();
            _bindingStarted = false;

            Assert.IsFalse(_binding.IsStarted,
                "Dispose 後は IsStarted == false に戻るべき。");
        }

        [Test]
        public void Dispose_IsIdempotent_DoesNotThrowOnSecondCall()
        {
            _sourceAsset = CreateExpressionActionAsset(
                actionMapName: "Expression",
                buttonActionName: "TriggerHappy",
                buttonBinding: "<Keyboard>/digit1");
            _binding = CreateBinding(
                slug: "input-system-dispose-idempotent",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry { actionName = "TriggerHappy", expressionId = "expr-001" },
                });

            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            _binding.Dispose();
            _bindingStarted = false;

            Assert.DoesNotThrow(() => _binding.Dispose(),
                "Dispose は冪等で 2 回目以降の呼び出しでも例外を投げないべき。");
        }

        // ---------------------------------------------------------------
        [Test]
        public void OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes()
        {
            var registry = new FakeInputSourceRegistry();
            _registry = null;
            _sourceAsset = CreateMultiPathActionAsset();
            _binding = CreateBinding(
                slug: "input-system-fake-registry",
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry
                    {
                        actionName = "analog-action",
                        expressionId = "expr-001",
                        bindingMode = BindingMode.Analog,
                    },
                    new ExpressionBindingEntry
                    {
                        actionName = "overlay-action",
                        expressionId = "expr-001",
                        bindingMode = BindingMode.Overlay,
                        overlaySlot = "happy",
                    },
                });

            var ctx = new AdapterBuildContext(
                profile: new FacialProfile(
                    "1.0",
                    layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                    expressions: new[]
                    {
                        new Expression(
                            "expr-001",
                            "Happy",
                            "emotion",
                            0.25f,
                            TransitionCurve.Linear,
                            new[] { new BlendShapeMapping("smile", 1f) }),
                    },
                    slots: new[] { "happy" }),
                blendShapeNames: new List<string> { "smile", "frown" },
                inputSourceRegistry: registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            const string wrapperType = "Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding+AnalogInputSourceWrapper";
            var allowedTypes = new HashSet<string>
            {
                wrapperType,
                typeof(ExpressionTriggerInputSource).FullName,
                typeof(AnalogExpressionInputSource).FullName,
                typeof(OverlayInputSource).FullName,
            };

            Assert.That(registry.Calls, Is.Not.Empty);
            foreach (var call in registry.Calls)
            {
                if (call.Operation != "Register" && call.Operation != "Replace") continue;
                Assert.That(call.Source, Is.Not.Null, call.Id);
                Assert.That(allowedTypes, Does.Contain(call.Source.GetType().FullName), call.Id);
                Assert.That(call.Source, Is.Not.TypeOf<InputActionAnalogSource>(), call.Id);
            }

            var wrapperIds = registry.Calls
                .Where(call => call.Source != null && call.Source.GetType().FullName == wrapperType)
                .Select(call => call.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(wrapperIds, Does.Contain("input-system-fake-registry:analog-action"));
            Assert.That(wrapperIds, Does.Contain("input-system-fake-registry:overlay-action"));

            Assert.IsTrue(registry.TryResolve(
                "input-system-fake-registry:analog-expression", out var analogExpression));
            Assert.IsInstanceOf<AnalogExpressionInputSource>(analogExpression);

            var field = typeof(AnalogExpressionInputSource).GetField(
                "_resolvedBindings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var resolvedBindings = field.GetValue(analogExpression) as Array;
            Assert.IsNotNull(resolvedBindings);
            foreach (var binding in resolvedBindings)
            {
                var source = (IAnalogInputSource)binding.GetType().GetField(
                    "Source",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).GetValue(binding);
                Assert.IsTrue(
                    wrapperIds.Any(id => id.EndsWith(":" + source.Id, StringComparison.Ordinal)),
                    source.Id);
            }
        }

        // ---------------------------------------------------------------
        // Analog expression: registry の Replace / Unregister への追従（timeline-playback-ux 3）
        // ---------------------------------------------------------------

        private const string AnalogReplaceActionName = "AnalogSmile";
        private const float AnalogReplaceExpressionValue = 0.8f;

        [Test]
        public void OnStart_AnalogExpression_ReplaceSourceWithStub_WritesStubTimesExpressionValue()
        {
            const string slug = "input-system-analog-replace";
            AnalogExpressionInputSource sink = StartAnalogExpressionBinding(slug);
            var output = new float[2];

            _registry.Replace(
                AdapterSlug.Parse(slug),
                AnalogReplaceActionName,
                new StubAnalogInputSource(AnalogReplaceActionName, 0.5f));

            Assert.IsTrue(sink.TryWriteValues(output),
                "Replace 後は stub の値で analog expression が寄与するべき。");
            Assert.AreEqual(0.5f * AnalogReplaceExpressionValue, output[0], 1e-5f,
                "smile は stub 値 × Expression 値で書かれるべき。");
            Assert.AreEqual(0f, output[1], 1e-5f);
        }

        [Test]
        public void OnStart_AnalogExpression_UnregisterAfterReplace_RestoresConstructedSource()
        {
            const string slug = "input-system-analog-unregister";
            AnalogExpressionInputSource sink = StartAnalogExpressionBinding(slug);
            var baseline = new float[2];
            bool baselineWritten = sink.TryWriteValues(baseline);

            var parsedSlug = AdapterSlug.Parse(slug);
            _registry.Replace(parsedSlug, AnalogReplaceActionName, new StubAnalogInputSource(AnalogReplaceActionName, 0.5f));
            var replaced = new float[2];
            Assert.IsTrue(sink.TryWriteValues(replaced), "Sanity: Replace 中は stub の値で寄与する。");

            _registry.Unregister(parsedSlug, AnalogReplaceActionName);

            var restored = new float[2];
            bool restoredWritten = sink.TryWriteValues(restored);
            Assert.AreEqual(baselineWritten, restoredWritten,
                "Unregister 後は構築時 source（スティック無入力）の結果に戻るべき。");
            Assert.AreEqual(baseline[0], restored[0], 1e-5f);
            Assert.AreEqual(baseline[1], restored[1], 1e-5f);
        }

        [Test]
        public void OnStart_AnalogExpression_ReplaceBackToOriginalWrapper_MatchesConstructedValue()
        {
            const string slug = "input-system-analog-replace-back";
            AnalogExpressionInputSource sink = StartAnalogExpressionBinding(slug);
            Assert.IsTrue(_registry.TryResolve(slug + ":" + AnalogReplaceActionName, out IInputSource originalWrapper),
                "Sanity: analog action の wrapper が registry に登録されている。");
            var baseline = new float[2];
            bool baselineWritten = sink.TryWriteValues(baseline);

            var parsedSlug = AdapterSlug.Parse(slug);
            _registry.Replace(parsedSlug, AnalogReplaceActionName, new StubAnalogInputSource(AnalogReplaceActionName, 0.5f));
            Assert.IsTrue(sink.TryWriteValues(new float[2]), "Sanity: Replace 中は stub の値で寄与する。");

            _registry.Replace(parsedSlug, AnalogReplaceActionName, originalWrapper);

            var restored = new float[2];
            bool restoredWritten = sink.TryWriteValues(restored);
            Assert.AreEqual(baselineWritten, restoredWritten,
                "元の wrapper へ Replace し直すと構築時と同じ結果になるべき。");
            Assert.AreEqual(baseline[0], restored[0], 1e-5f);
            Assert.AreEqual(baseline[1], restored[1], 1e-5f);
        }

        /// <summary>
        /// Analog モードの action 1 本（<c>&lt;Gamepad&gt;/leftTrigger</c>）で実 binding を OnStart し、
        /// registry から analog expression 消費者を取得して返す。
        /// </summary>
        private AnalogExpressionInputSource StartAnalogExpressionBinding(string slug)
        {
            _sourceAsset = CreateValueActionAsset(
                actionMapName: "Expression",
                actionName: AnalogReplaceActionName,
                binding: "<Gamepad>/leftTrigger");
            _binding = CreateBinding(
                slug: slug,
                asset: _sourceAsset,
                actionMapName: "Expression",
                expressionBindings: new List<ExpressionBindingEntry>
                {
                    new ExpressionBindingEntry
                    {
                        actionName = AnalogReplaceActionName,
                        expressionId = "expr-smile",
                        bindingMode = BindingMode.Analog,
                    },
                });

            var ctx = new AdapterBuildContext(
                profile: new FacialProfile(
                    "1.0",
                    layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                    expressions: new[]
                    {
                        new Expression(
                            "expr-smile",
                            "Smile",
                            "emotion",
                            0.25f,
                            TransitionCurve.Linear,
                            new[] { new BlendShapeMapping("smile", AnalogReplaceExpressionValue) }),
                    }),
                blendShapeNames: new List<string> { "smile", "frown" },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.IsTrue(_registry.TryResolve(slug + ":" + AnalogExpressionInputSource.ReservedId, out IInputSource resolved),
                "Sanity: analog expression 消費者が registry に登録されている。");
            var sink = resolved as AnalogExpressionInputSource;
            Assert.IsNotNull(sink, "Sanity: analog expression 消費者は AnalogExpressionInputSource。");
            return sink;
        }

        // Helpers
        // ---------------------------------------------------------------

        private InputSystemAdapterBinding CreateBinding(
            string slug,
            InputActionAsset asset,
            string actionMapName,
            IReadOnlyList<ExpressionBindingEntry> expressionBindings,
            IReadOnlyList<GazeChannel> injectedGazeConfigs = null)
        {
            var binding = new InputSystemAdapterBinding();
            binding.Slug = slug;
            binding.Configure(asset, actionMapName, expressionBindings);
            if (injectedGazeConfigs != null)
            {
                binding.ConfigureGazeChannels(injectedGazeConfigs
                    .Where(config => config != null)
                    .Select(config => config.id)
                    .ToArray());
            }
            return binding;
        }

        private AdapterBuildContext CreateContext(IReadOnlyList<string> blendShapeNames = null, string[] slots = null)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: slots),
                blendShapeNames: blendShapeNames ?? new List<string> { "smile", "frown" },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        private static InputActionAsset CreateExpressionActionAsset(
            string actionMapName,
            string buttonActionName,
            string buttonBinding)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap(actionMapName);
            var action = map.AddAction(buttonActionName, InputActionType.Button);
            action.AddBinding(buttonBinding);
            return asset;
        }

        private static InputActionAsset CreateMultiPathActionAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap("Expression");
            var analogAction = map.AddAction("analog-action", InputActionType.Value);
            analogAction.AddBinding("<Gamepad>/leftTrigger");
            var overlayAction = map.AddAction("overlay-action", InputActionType.Value);
            overlayAction.AddBinding("<Gamepad>/rightTrigger");
            return asset;
        }

        private static InputActionAsset CreateValueActionAsset(
            string actionMapName,
            string actionName,
            string binding)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap(actionMapName);
            var action = map.AddAction(actionName, InputActionType.Value);
            action.AddBinding(binding);
            return asset;
        }

        private static InputActionAsset CreateGazeActionAsset(
            string actionMapName,
            string gazeActionName)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap(actionMapName);
            var action = map.AddAction(gazeActionName, InputActionType.Value, expectedControlLayout: "Vector2");
            action.AddBinding("<Gamepad>/leftStick");
            return asset;
        }

        private static GazeChannel CreateGazeConfig(string expressionId)
        {
            return new GazeChannel
            {
                id = expressionId,
                leftEyeBonePath = "LeftEye",
                rightEyeBonePath = "RightEye",
            };
        }
    }
}
