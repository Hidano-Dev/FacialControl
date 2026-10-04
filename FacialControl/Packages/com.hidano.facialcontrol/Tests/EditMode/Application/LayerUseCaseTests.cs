using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// <see cref="LayerUseCase"/> の基本契約を検証する: レイヤー weight、遷移補間、GetBlendedOutput / BlendedOutputSpan、
    /// LayerOverrideMask による他レイヤー抑制、additional IInputSource / late-bind 経路、
    /// および ExpressionTrigger ソースの検索。
    /// 3 レイヤー（emotion / lipsync / eye）の既定プロファイルを SetUp で構築する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class LayerUseCaseTests : SizedTestFixture
    {
        // --- ヘルパー ---

        private static LayerDefinition[] CreateDefaultLayers()
        {
            return new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
        }

        private static string[] CreateBlendShapeNames()
        {
            return new[] { "bs_smile", "bs_sad", "bs_blink" };
        }

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion",
            float transitionDuration = 0.25f,
            BlendShapeMapping[] blendShapeValues = null)
        {
            return new Expression(
                id, name, layer, transitionDuration,
                TransitionCurve.Linear,
                blendShapeValues);
        }

        private static FacialProfile CreateProfile(
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
        {
            return new FacialProfile(
                "1.0",
                layers ?? CreateDefaultLayers(),
                expressions ?? Array.Empty<Expression>());
        }

        private LayerUseCase _useCase;
        private ExpressionUseCase _expressionUseCase;
        private FacialProfile _profile;

        private sealed class FakeWeightObserver : ILayerWeightObserver
        {
            public readonly List<(string layerName, float weight)> LayerSamples =
                new List<(string layerName, float weight)>();
            public readonly List<(string layerName, string slotId, float weight)> InputSourceSamples =
                new List<(string layerName, string slotId, float weight)>();

            public void OnLayerWeightSample(string layerName, float weight)
            {
                LayerSamples.Add((layerName, weight));
            }

            public void OnInputSourceWeightSample(string layerName, string slotId, float weight)
            {
                InputSourceSamples.Add((layerName, slotId, weight));
            }
        }

        [SetUp]
        public void SetUp()
        {
            _profile = CreateProfile();
            _expressionUseCase = new ExpressionUseCase(_profile);
            _useCase = new LayerUseCase(_profile, _expressionUseCase, CreateBlendShapeNames());
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_ValidArgs_CreatesInstance()
        {
            Assert.IsNotNull(_useCase);
        }

        [Test]
        public void Constructor_NullBlendShapeNames_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LayerUseCase(_profile, _expressionUseCase, null));
        }

        [Test]
        public void Constructor_EmptyBlendShapeNames_CreatesInstance()
        {
            var useCase = new LayerUseCase(_profile, _expressionUseCase, Array.Empty<string>());
            Assert.IsNotNull(useCase);
        }

        [Test]
        public void SetLayerWeight_WhileSuspended_IsNoOp()
        {
            var gate = (IWeightInjectionGate)_useCase;
            var observer = new FakeWeightObserver();
            _useCase.SetWeightObserver(observer);
            Assert.IsTrue(gate.SuspendLiveWeights());

            _useCase.SetLayerWeight("emotion", 0.25f);
            _useCase.UpdateWeights(0f);

            var layers = new List<LayerWeightEntry>();
            gate.CollectLayerWeights(layers);
            Assert.AreEqual(1f, layers[0].Weight, 1e-6f);
            Assert.IsEmpty(observer.LayerSamples);
        }

        [Test]
        public void SuspendLiveWeights_ThenLayerAndSourceLiveWrites_NeitherReachesNextAggregate()
        {
            var source = new FakeValueWritingSource("source", CreateBlendShapeNames().Length, 1f);
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(),
                new[] { (0, (IInputSource)source, 1f) }, new[] { "source-slot" });
            var gate = (IWeightInjectionGate)useCase;

            // 遮断 → 基準 → ライブ書込 → 消費、の順。ライブ書込が基準の後に来ても消費値が基準のままであることが
            // 遮断の証明になる（基準を後に書くと遮断が壊れていても通ってしまう）。
            Assert.IsTrue(gate.SuspendLiveWeights());
            Assert.IsTrue(gate.TrySetBaselineLayerWeight("emotion", 0.6f));
            Assert.IsTrue(gate.TrySetBaselineInputSourceWeight("emotion", "source-slot", 0.4f));
            useCase.SetLayerWeight("emotion", 0.1f);
            useCase.SetInputSourceWeight(0, 1, 0.9f);
            using (var bulk = useCase.BeginInputSourceWeightBatch())
            {
                bulk.SetWeight(0, 1, 0.95f);
            }
            useCase.UpdateWeights(0f);

            var layers = new List<LayerWeightEntry>();
            var slots = new List<InputSourceWeightEntry>();
            gate.CollectLayerWeights(layers);
            gate.CollectInputSourceWeights(slots);
            Assert.AreEqual(0.6f, layers[0].Weight, 1e-6f);
            Assert.AreEqual(0.4f, slots[1].Weight, 1e-6f);
        }

        [Test]
        public void BaselineWritesDoNotNotify_ButInjectionDoesWhileSuspended()
        {
            var source = new FakeValueWritingSource("source", CreateBlendShapeNames().Length, 1f);
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(),
                new[] { (0, (IInputSource)source, 1f) }, new[] { "source-slot" });
            var gate = (IWeightInjectionGate)useCase;
            var observer = new FakeWeightObserver();
            useCase.SetWeightObserver(observer);
            Assert.IsTrue(gate.SuspendLiveWeights());

            Assert.IsTrue(gate.TrySetBaselineLayerWeight("emotion", 0.6f));
            Assert.IsTrue(gate.TrySetBaselineInputSourceWeight("emotion", "source-slot", 0.4f));
            gate.ResetWeightsToDeclared();
            useCase.UpdateWeights(0f);
            Assert.IsEmpty(observer.LayerSamples);
            Assert.IsEmpty(observer.InputSourceSamples);

            Assert.IsTrue(gate.TryInjectLayerWeight("emotion", 0.3f));
            Assert.IsTrue(gate.TryInjectInputSourceWeight("emotion", "source-slot", 0.2f));
            useCase.UpdateWeights(0f);
            Assert.AreEqual(1, observer.LayerSamples.Count);
            Assert.AreEqual(0.3f, observer.LayerSamples[0].weight, 1e-6f);
            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual(0.2f, observer.InputSourceSamples[0].weight, 1e-6f);
        }

        [Test]
        public void ResetWeightsToDeclared_RestoresDeclaredValuesWithoutNotifying()
        {
            var profile = CreateProfile(
                layers: CreateDefaultLayers(),
                expressions: Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var source = new FakeValueWritingSource("declared", CreateBlendShapeNames().Length, 1f);
            var useCase = new LayerUseCase(
                profile,
                expressionUseCase,
                CreateBlendShapeNames(),
                new[] { (0, (IInputSource)source, 0.4f) },
                new[] { "declared-slot" });
            var gate = (IWeightInjectionGate)useCase;

            Assert.IsTrue(gate.TryInjectLayerWeight("emotion", 0.2f));
            Assert.IsTrue(gate.TryInjectInputSourceWeight("emotion", "declared-slot", 0.8f));
            gate.ResetWeightsToDeclared();
            useCase.UpdateWeights(0f);

            var layers = new List<LayerWeightEntry>();
            var slots = new List<InputSourceWeightEntry>();
            gate.CollectLayerWeights(layers);
            gate.CollectInputSourceWeights(slots);
            Assert.AreEqual(1f, layers[0].Weight, 1e-6f);
            Assert.AreEqual(0.4f, slots[1].Weight, 1e-6f);
        }

        [Test]
        public void TrySetBaselineAndInject_UsesStableKeysAndUnknownKeysReturnFalse()
        {
            var gate = (IWeightInjectionGate)_useCase;
            Assert.IsTrue(gate.TrySetBaselineLayerWeight("emotion", 0.3f));
            Assert.IsTrue(gate.TrySetBaselineInputSourceWeight("emotion", WeightSlotIds.ExpressionSlotId, 0.6f));
            Assert.IsTrue(gate.TryInjectLayerWeight("emotion", 0.7f));
            Assert.IsTrue(gate.TryInjectInputSourceWeight("emotion", WeightSlotIds.ExpressionSlotId, 0.8f));
            Assert.IsFalse(gate.TryInjectLayerWeight("missing", 0.5f));
            Assert.IsFalse(gate.TryInjectInputSourceWeight("emotion", "missing", 0.5f));

            var layers = new List<LayerWeightEntry>();
            var slots = new List<InputSourceWeightEntry>();
            _useCase.UpdateWeights(0f);
            gate.CollectLayerWeights(layers);
            gate.CollectInputSourceWeights(slots);
            Assert.AreEqual(0.7f, layers[0].Weight, 1e-6f);
            Assert.AreEqual("@expression", slots[0].SlotId);
            Assert.AreEqual(0.8f, slots[0].Weight, 1e-6f);
        }

        [Test]
        public void SetWeightObserver_Attach_SyncsWithoutNotifying()
        {
            var observer = new FakeWeightObserver();

            _useCase.SetLayerWeight("emotion", 0.25f);
            _useCase.UpdateWeights(0f);
            _useCase.SetWeightObserver(observer);
            _useCase.UpdateWeights(0f);

            Assert.IsEmpty(observer.LayerSamples);
            Assert.IsEmpty(observer.InputSourceSamples);
        }

        [Test]
        public void UpdateWeights_ChangedLayerAndSourceWeights_NotifiesConsumedValuesOnce()
        {
            var source = new FakeValueWritingSource("declared-source", CreateBlendShapeNames().Length, 0f);
            var useCase = new LayerUseCase(
                _profile,
                _expressionUseCase,
                CreateBlendShapeNames(),
                new[] { (0, (IInputSource)source, 1f) },
                new[] { "declared-id" });
            var observer = new FakeWeightObserver();
            useCase.UpdateWeights(0f);
            useCase.SetWeightObserver(observer);

            useCase.SetLayerWeight("emotion", 0.25f);
            useCase.SetInputSourceWeight(0, 1, 0.75f);
            useCase.UpdateWeights(0f);

            Assert.AreEqual(1, observer.LayerSamples.Count);
            Assert.AreEqual("emotion", observer.LayerSamples[0].layerName);
            Assert.AreEqual(0.25f, observer.LayerSamples[0].weight);
            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual("emotion", observer.InputSourceSamples[0].layerName);
            Assert.AreEqual("declared-id", observer.InputSourceSamples[0].slotId);
            Assert.AreEqual(0.75f, observer.InputSourceSamples[0].weight);

            useCase.Dispose();
        }

        [Test]
        public void UpdateWeights_SameFrameMultipleWeightWrites_NotifiesFinalValuesOnce()
        {
            var source = new FakeValueWritingSource("source", CreateBlendShapeNames().Length, 0f);
            var useCase = new LayerUseCase(
                _profile,
                _expressionUseCase,
                CreateBlendShapeNames(),
                new[] { (0, (IInputSource)source, 1f) });
            var observer = new FakeWeightObserver();
            useCase.UpdateWeights(0f);
            useCase.SetWeightObserver(observer);

            useCase.SetLayerWeight("emotion", 0.2f);
            useCase.SetLayerWeight("emotion", 0.8f);
            useCase.SetInputSourceWeight(0, 1, 0.3f);
            useCase.SetInputSourceWeight(0, 1, 0.7f);
            useCase.UpdateWeights(0f);

            Assert.AreEqual(1, observer.LayerSamples.Count);
            Assert.AreEqual(0.8f, observer.LayerSamples[0].weight);
            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual(0.7f, observer.InputSourceSamples[0].weight);

            useCase.Dispose();
        }

        [Test]
        public void UpdateWeights_SameConsumedWeights_DoesNotNotifyAgain()
        {
            var observer = new FakeWeightObserver();
            _useCase.UpdateWeights(0f);
            _useCase.SetWeightObserver(observer);
            _useCase.SetLayerWeight("emotion", 0.5f);
            _useCase.SetInputSourceWeight(0, 0, 0.5f);

            _useCase.UpdateWeights(0f);
            _useCase.UpdateWeights(0f);

            Assert.AreEqual(1, observer.LayerSamples.Count);
            Assert.AreEqual(1, observer.InputSourceSamples.Count);
        }

        [Test]
        public void UpdateWeights_ExpressionSlot_NotifiesWithReservedSlotId()
        {
            var observer = new FakeWeightObserver();
            _useCase.UpdateWeights(0f);
            _useCase.SetWeightObserver(observer);
            _useCase.SetInputSourceWeight(0, 0, 0.5f);
            _useCase.UpdateWeights(0f);

            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual(WeightSlotIds.ExpressionSlotId, observer.InputSourceSamples[0].slotId);
            Assert.AreEqual(0.5f, observer.InputSourceSamples[0].weight);
        }

        [Test]
        public void UpdateWeights_NoObserver_DoesNotTouchNotificationArrays()
        {
            // 観測者が未設定なら比較ループ自体を走らせない（Req 9.2）。前回通知値は構築時の未観測（NaN）のまま。
            _useCase.SetLayerWeight("emotion", 0.25f);
            _useCase.SetInputSourceWeight(0, 0, 0.5f);
            _useCase.UpdateWeights(0f);
            _useCase.UpdateWeights(0f);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var layerWeights = (float[])typeof(LayerUseCase).GetField("_lastNotifiedLayerWeights", flags).GetValue(_useCase);
            var slotWeights = (float[])typeof(LayerUseCase).GetField("_lastNotifiedSlotWeights", flags).GetValue(_useCase);

            Assert.IsTrue(Array.TrueForAll(layerWeights, float.IsNaN), "レイヤー weight の前回通知値が未観測のまま");
            Assert.IsTrue(Array.TrueForAll(slotWeights, float.IsNaN), "スロット weight の前回通知値が未観測のまま");
        }

        [Test]
        public void BindLateInputSource_CapacityGrowthWhileObserved_NotifiesNewSlotWithoutException()
        {
            // 観測者接続中に late-bind で registry の容量が増えても、前回通知値の配列が追随し
            // 新スロットは宣言 weight で通知される（範囲外例外・スロット取り違えなし）。
            var declared = new FakeValueWritingSource("declared-source", CreateBlendShapeNames().Length, 0f);
            var useCase = new LayerUseCase(
                _profile,
                _expressionUseCase,
                CreateBlendShapeNames(),
                new[] { (0, (IInputSource)declared, 1f) },
                new[] { "declared-id" });
            var observer = new FakeWeightObserver();
            useCase.UpdateWeights(0f);
            useCase.SetWeightObserver(observer);

            var late = new FakeValueWritingSource("late-source", CreateBlendShapeNames().Length, 0f);
            Assert.DoesNotThrow(() =>
            {
                useCase.BindLateInputSource(0, "late-id", late, 0.5f);
                useCase.UpdateWeights(0f);
            });

            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual("emotion", observer.InputSourceSamples[0].layerName);
            Assert.AreEqual("late-id", observer.InputSourceSamples[0].slotId);
            Assert.AreEqual(0.5f, observer.InputSourceSamples[0].weight);

            // 既存スロットの前回通知値は保持されるため、同値の再通知は起きない。
            useCase.UpdateWeights(0f);
            Assert.AreEqual(1, observer.InputSourceSamples.Count);

            useCase.Dispose();
        }

        [Test]
        public void BindLateInputSource_ReplacingExistingId_WhileSuspended_KeepsCurrentWeight()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var source = new FakeValueWritingSource("source", 1, 1f);
            using var useCase = new LayerUseCase(
                profile, expressionUseCase, new[] { "bs_smile" },
                new[] { (0, (IInputSource)source, 0.8f) }, new[] { "slot" });
            var gate = (IWeightInjectionGate)useCase;

            Assert.IsTrue(gate.TryInjectInputSourceWeight("emotion", "slot", 0.7f));
            Assert.IsTrue(gate.SuspendLiveWeights());
            useCase.BindLateInputSource(0, "slot", new FakeValueWritingSource("replacement", 1, 1f), 0.2f);
            useCase.UpdateWeights(0f);

            var slots = new List<InputSourceWeightEntry>();
            gate.CollectInputSourceWeights(slots);
            Assert.AreEqual(0.7f, slots[1].Weight, 1e-6f);
        }

        [Test]
        public void BindLateInputSource_ReplacingExistingId_WhileNotSuspended_AppliesDeclaredWeight()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var source = new FakeValueWritingSource("source", 1, 1f);
            using var useCase = new LayerUseCase(
                profile, expressionUseCase, new[] { "bs_smile" },
                new[] { (0, (IInputSource)source, 0.8f) }, new[] { "slot" });

            useCase.BindLateInputSource(0, "slot", new FakeValueWritingSource("replacement", 1, 1f), 0.2f);
            useCase.UpdateWeights(0f);

            var slots = new List<InputSourceWeightEntry>();
            ((IWeightInjectionGate)useCase).CollectInputSourceWeights(slots);
            Assert.AreEqual(0.2f, slots[1].Weight, 1e-6f);
        }

        [Test]
        public void UnbindLateInputSource_WhileSuspended_CompactsWeightsAndRenotifiesRemainingSlots()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var sourceA = new FakeValueWritingSource("a", 1, 0f);
            var sourceB = new FakeValueWritingSource("b", 1, 0.8f);
            using var useCase = new LayerUseCase(
                profile, expressionUseCase, new[] { "bs_smile" },
                new[]
                {
                    (0, (IInputSource)sourceA, 0.5f),
                    (0, (IInputSource)sourceB, 1f),
                }, new[] { "a", "b" });
            var gate = (IWeightInjectionGate)useCase;
            useCase.UpdateWeights(0f);
            var observer = new FakeWeightObserver();
            useCase.SetWeightObserver(observer);
            Assert.IsTrue(gate.SuspendLiveWeights());
            Assert.IsTrue(gate.TryInjectInputSourceWeight("emotion", "b", 0.7f));
            useCase.UpdateWeights(0f);
            observer.InputSourceSamples.Clear();

            useCase.UnbindLateInputSource(0, "a");
            useCase.UpdateWeights(0f);

            Assert.AreEqual(1, observer.InputSourceSamples.Count);
            Assert.AreEqual("b", observer.InputSourceSamples[0].slotId);
            Assert.AreEqual(0.7f, observer.InputSourceSamples[0].weight, 1e-6f);
        }

        // --- SetLayerWeight ---

        [Test]
        public void SetLayerWeight_ValidLayerAndWeight_SetsWeight()
        {
            _useCase.SetLayerWeight("emotion", 0.5f);

            // 検証: GetBlendedOutput で反映されることを確認
            // レイヤーウェイトのデフォルトが 1.0 なので、設定後は 0.5 に変わる
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", 0.5f));
        }

        [Test]
        public void SetLayerWeight_NullLayer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _useCase.SetLayerWeight(null, 0.5f));
        }

        [Test]
        public void SetLayerWeight_WeightAboveOne_ClampedToOne()
        {
            // 範囲外の値はクランプされる（例外なし）
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", 1.5f));
        }

        [Test]
        public void SetLayerWeight_WeightBelowZero_ClampedToZero()
        {
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", -0.5f));
        }

        [Test]
        public void SetLayerWeight_UndefinedLayer_SetsWeightWithoutError()
        {
            // 未定義レイヤーにも設定可能（後から使われる可能性があるため）
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("unknown", 0.5f));
        }

        // --- UpdateWeights: LayerOverrideMask 抑制（系2 駆動）---
        //
        // 抑制計算は系2(ExpressionTriggerInputSource)の active を見るため、
        // emotion レイヤーに系2 sink を additionalSources で追加し TriggerOn で active 化する。
        // 出力駆動(系1 の LayerExpressionSource)は euc.Activate のまま（Q2=A: 系1 残す）。

        // 系2 の具象は別アセンブリ(InputSystem)にあるため、テストでは基底を継承した Fake を使う。
        private sealed class FakeTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeTriggerSource(FacialProfile profile, string[] blendShapeNames)
                : base(
                    InputSourceId.Parse("input"),
                    blendShapeNames.Length,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
            }
        }

        private static (LayerUseCase luc, ExpressionUseCase euc, FakeTriggerSource emotionTrigger, Expression smile, Expression ov)
            BuildOverrideScenario(LayerOverrideMask smileMask)
        {
            // emotion(bit0, priority0) と overlay(bit1, priority1) の 2 レイヤー。
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("overlay", 1, ExclusionMode.LastWins),
            };
            var bsNames = new[] { "bs_a", "bs_b" };
            var smile = new Expression(
                "smile", "smile", "emotion", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_a", 1f, null) },
                null,
                smileMask);
            var ov = new Expression(
                "ov", "ov", "overlay", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_b", 1f, null) });
            var profile = new FacialProfile("1.0", layers, new[] { smile, ov });
            var euc = new ExpressionUseCase(profile);
            // emotion レイヤー(idx0)に系2 を追加。抑制計算は系2 の active から行われる。
            var emotionTrigger = new FakeTriggerSource(profile, bsNames);
            var luc = new LayerUseCase(
                profile, euc, bsNames,
                new[] { (0, (IInputSource)emotionTrigger, 1f) });
            return (luc, euc, emotionTrigger, smile, ov);
        }

        [Test]
        public void UpdateWeights_ActiveExpressionOverridesLayer_SuppressesTargetLayer()
        {
            // smile(emotion) が overlay(bit1) を OverrideMask で抑制する。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.Bit1);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "emotion(自己)は出力される");
            Assert.AreEqual(0f, output[1], 1e-5f, "overlay は OverrideMask により抑制される");
        }

        [Test]
        public void UpdateWeights_NoOverrideMask_TargetLayerContributes()
        {
            // OverrideMask=None なら overlay は通常どおりブレンドされる（対照）。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.None);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "emotion が出力される");
            Assert.AreEqual(1f, output[1], 1e-5f, "OverrideMask が無いので overlay も出力される");
        }

        [Test]
        public void UpdateWeights_OverrideMaskSelfLayer_DoesNotSuppressSelf()
        {
            // smile が自己レイヤー(emotion=bit0)を mask に含めても自己は抑制しない（レイヤー内ブレンド担保）。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.Bit0);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "自己レイヤー emotion は抑制対象外");
            Assert.AreEqual(1f, output[1], 1e-5f, "overlay は mask 対象外なので出力される");
        }

        // --- UpdateWeights ---

        [Test]
        public void UpdateWeights_NoActiveExpressions_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _useCase.UpdateWeights(0.016f));
        }

        [Test]
        public void UpdateWeights_WithActiveExpression_ProgressesTransition()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);

            // 半分の遷移時間経過
            _useCase.UpdateWeights(0.25f);

            var output = _useCase.GetBlendedOutput();
            // 遷移中なので、bs_smile は 0.5 程度（Linear 補間）
            Assert.Greater(output[0], 0f);
            Assert.Less(output[0], 1f);
        }

        [Test]
        public void UpdateWeights_TransitionComplete_ReachesTargetValues()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.25f);

            _expressionUseCase.Activate(expr);

            // 遷移時間を超えて更新
            _useCase.UpdateWeights(0.5f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_ResetGenerationChange_SnapsToTargetWithoutTransition()
        {
            var expr = CreateExpression(
                transitionDuration: 1f,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("bs_smile", 1f),
                });
            var profile = CreateProfile(expressions: new[] { expr });
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, CreateBlendShapeNames());

            expressionUseCase.Activate(expr);
            useCase.UpdateWeights(0.25f);
            Assert.That(useCase.GetBlendedOutput()[0], Is.GreaterThan(0f).And.LessThan(1f));

            expressionUseCase.ResetActiveExpressions(new[] { expr.Id });
            useCase.UpdateWeights(0f);

            Assert.AreEqual(1f, useCase.GetBlendedOutput()[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_ResetGenerationToEmpty_SnapsPreviouslyActiveLayerToZero()
        {
            var expr = CreateExpression(
                transitionDuration: 1f,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("bs_smile", 1f),
                });
            var profile = CreateProfile(expressions: new[] { expr });
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, CreateBlendShapeNames());

            expressionUseCase.Activate(expr);
            useCase.UpdateWeights(1f);
            Assert.AreEqual(1f, useCase.GetBlendedOutput()[0], 0.001f);

            expressionUseCase.ResetActiveExpressions(Array.Empty<string>());
            useCase.UpdateWeights(0f);

            Assert.AreEqual(0f, useCase.GetBlendedOutput()[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_ZeroTransitionDuration_ImmediateSwitch()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.5f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
            Assert.AreEqual(0.5f, output[1], 0.001f);
        }

        [Test]
        public void UpdateWeights_AfterDeactivate_TransitionsBackToZero()
        {
            // Regression: 直前 active だった expression を Deactivate しても LayerExpressionSource
            // が「ゼロ (= rest) へ補間」を始めず latched ON のままになる不具合があった。
            // 別の表情を入れない限り OFF にならない (Toggle/Hold いずれも同症状)。
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.25f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(1.0f); // 完全に遷移し終わる
            Assert.AreEqual(1.0f, _useCase.GetBlendedOutput()[0], 0.001f, "Activate 後に target に到達していない");

            _expressionUseCase.Deactivate(expr);
            _useCase.UpdateWeights(1.0f); // Deactivate 後の遷移時間も完全に経過

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0f, output[0], 0.001f, "Deactivate 後にゼロへ戻っていない (latched バグ)");
            Assert.AreEqual(0f, output[1], 0.001f);
            Assert.AreEqual(0f, output[2], 0.001f);
        }

        [Test]
        public void UpdateWeights_AfterDeactivate_DuringTransition_ReachesZero()
        {
            // Regression: Deactivate 直後の中間値からゼロへ補間できることを確認する。
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.25f); // 50% 遷移
            float midValue = _useCase.GetBlendedOutput()[0];
            Assert.Greater(midValue, 0f);
            Assert.Less(midValue, 1f);

            _expressionUseCase.Deactivate(expr);
            _useCase.UpdateWeights(1.0f); // 十分な時間で OFF へ補間

            Assert.AreEqual(0f, _useCase.GetBlendedOutput()[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_MultipleDeltaTimeSteps_AccumulatesProgress()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);

            // 5 ステップに分けて遷移
            for (int i = 0; i < 5; i++)
                _useCase.UpdateWeights(0.1f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- GetBlendedOutput ---

        [Test]
        public void GetBlendedOutput_NoActiveExpressions_ReturnsZeroArray()
        {
            var output = _useCase.GetBlendedOutput();

            Assert.AreEqual(3, output.Length);
            Assert.AreEqual(0f, output[0]);
            Assert.AreEqual(0f, output[1]);
            Assert.AreEqual(0f, output[2]);
        }

        [Test]
        public void GetBlendedOutput_ReturnsCorrectLength()
        {
            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(3, output.Length);
        }

        [Test]
        public void GetBlendedOutput_SingleLayerFullyTransitioned_ReturnsExpressionValues()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.2f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.8f, output[0], 0.001f);
            Assert.AreEqual(0.2f, output[1], 0.001f);
            Assert.AreEqual(0.0f, output[2], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_MultipleLayersActive_BlendsByPriority()
        {
            var emotionBs = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var eyeBs = new[]
            {
                new BlendShapeMapping("bs_smile", 0.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 1.0f)
            };

            var emotionExpr = CreateExpression("expr-1", "smile", "emotion",
                transitionDuration: 0f, blendShapeValues: emotionBs);
            var eyeExpr = CreateExpression("expr-2", "blink", "eye",
                transitionDuration: 0f, blendShapeValues: eyeBs);

            _expressionUseCase.Activate(emotionExpr);
            _expressionUseCase.Activate(eyeExpr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // eye (priority=2) は emotion (priority=0) より優先
            // 両レイヤーの weight=1.0 なので、eye の値が優先される
            Assert.AreEqual(1.0f, output[2], 0.001f); // bs_blink は eye レイヤーで 1.0
        }

        [Test]
        public void GetBlendedOutput_LayerWeightZero_LayerIgnored()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.SetLayerWeight("emotion", 0f);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // レイヤーウェイトが 0 なので出力はゼロ
            Assert.AreEqual(0f, output[0], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_LayerWeightHalf_ScalesOutput()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.SetLayerWeight("emotion", 0.5f);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.5f, output[0], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_ReturnsDefensiveCopy()
        {
            var output1 = _useCase.GetBlendedOutput();
            var output2 = _useCase.GetBlendedOutput();

            Assert.AreNotSame(output1, output2);
        }

        // --- BlendedOutputSpan (zero-alloc accessor) ---

        [Test]
        public void BlendedOutputSpan_NoActiveExpressions_AllZero()
        {
            var span = _useCase.BlendedOutputSpan;

            Assert.AreEqual(3, span.Length);
            Assert.AreEqual(0f, span[0]);
            Assert.AreEqual(0f, span[1]);
            Assert.AreEqual(0f, span[2]);
        }

        [Test]
        public void BlendedOutputSpan_AfterUpdateWeights_MatchesGetBlendedOutput()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.2f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var span = _useCase.BlendedOutputSpan;
            var copy = _useCase.GetBlendedOutput();

            Assert.AreEqual(copy.Length, span.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                Assert.AreEqual(copy[i], span[i], 1e-6f,
                    $"index {i}: BlendedOutputSpan と GetBlendedOutput が一致すべき");
            }
        }

        [Test]
        public void BlendedOutputSpan_Length_MatchesBlendShapeCount()
        {
            var span = _useCase.BlendedOutputSpan;
            Assert.AreEqual(3, span.Length);
        }

        // --- 遷移割込 ---

        [Test]
        public void UpdateWeights_TransitionInterrupt_StartsFromCurrentValues()
        {
            var blendShapes1 = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var blendShapes2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.0f),
                new BlendShapeMapping("bs_sad", 1.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "smile", "emotion",
                transitionDuration: 0.5f, blendShapeValues: blendShapes1);
            var expr2 = CreateExpression("expr-2", "sad", "emotion",
                transitionDuration: 0.5f, blendShapeValues: blendShapes2);

            // expr1 をアクティブにして途中まで遷移
            _expressionUseCase.Activate(expr1);
            _useCase.UpdateWeights(0.25f); // 50% 遷移
            var midOutput = _useCase.GetBlendedOutput();
            float midSmile = midOutput[0];

            // expr2 に切り替え（遷移割込）
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f); // 割込直後

            var interruptOutput = _useCase.GetBlendedOutput();
            // 割込直後は遷移元（前のスナップショット値）に近い
            // bs_sad が少し増加し始めているはず
            Assert.GreaterOrEqual(interruptOutput[0], 0f);
        }

        // --- SetProfile ---

        [Test]
        public void SetProfile_ResetsTransitionState()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            // 新しいプロファイルに切り替え
            var newProfile = CreateProfile();
            _useCase.SetProfile(newProfile, CreateBlendShapeNames());

            var output = _useCase.GetBlendedOutput();
            // プロファイル切替後はゼロにリセット
            Assert.AreEqual(0f, output[0], 0.001f);
        }

        // --- Blend モード（lipsync レイヤー） ---

        [Test]
        public void GetBlendedOutput_BlendModeLayer_AddsMutipleExpressions()
        {
            var bs1 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.3f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var bs2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.4f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs1);
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs2);

            _expressionUseCase.Activate(expr1);
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // Blend モードなので加算される（0.3 + 0.4 = 0.7）
            Assert.AreEqual(0.7f, output[0], 0.05f);
        }

        [Test]
        public void GetBlendedOutput_BlendModeLayer_ClampsToOne()
        {
            var bs1 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var bs2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs1);
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs2);

            _expressionUseCase.Activate(expr1);
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // 0.8 + 0.8 = 1.6 → 1.0 にクランプ
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- BlendShape 名マッピング ---

        [Test]
        public void GetBlendedOutput_ExpressionWithPartialBlendShapes_MapsCorrectly()
        {
            // Expression が全 BlendShape を含まない場合、
            // 対応するインデックスのみ更新される
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_sad", 0.7f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.0f, output[0], 0.001f); // bs_smile は未設定
            Assert.AreEqual(0.7f, output[1], 0.001f); // bs_sad は 0.7
            Assert.AreEqual(0.0f, output[2], 0.001f); // bs_blink は未設定
        }

        // --- デフォルト動作 ---

        [Test]
        public void LayerWeight_DefaultIsOne()
        {
            // 何も設定しない場合、レイヤーウェイトはデフォルト 1.0
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- TryGetExpressionTriggerSourceById ---

        private sealed class FakeExpressionTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeExpressionTriggerSource(string id, int blendShapeCount, FacialProfile profile)
                : base(InputSourceId.Parse(id), blendShapeCount, 4, ExclusionMode.LastWins,
                       Array.Empty<string>(), profile)
            {
            }
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_RegisteredId_ReturnsTrue()
        {
            var fake = new FakeExpressionTriggerSource("input", 3, _profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fake, 1.0f),
            };
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(), additional);

            bool found = useCase.TryGetExpressionTriggerSourceById(
                "input", out var source);

            Assert.IsTrue(found);
            Assert.AreSame(fake, source);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_UnregisteredId_ReturnsFalse()
        {
            bool found = _useCase.TryGetExpressionTriggerSourceById(
                "input", out var source);

            Assert.IsFalse(found);
            Assert.IsNull(source);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_NullOrEmptyId_ReturnsFalse()
        {
            Assert.IsFalse(_useCase.TryGetExpressionTriggerSourceById(null, out var s1));
            Assert.IsNull(s1);
            Assert.IsFalse(_useCase.TryGetExpressionTriggerSourceById("", out var s2));
            Assert.IsNull(s2);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_NonExpressionTriggerSource_NotReturned()
        {
            // ValueProvider 型のソースは ExpressionTrigger lookup には該当しない。
            var valueProvider = new FakeValueProviderSource("osc", 3);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, valueProvider, 1.0f),
            };
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(), additional);

            bool found = useCase.TryGetExpressionTriggerSourceById("osc", out var source);

            Assert.IsFalse(found);
            Assert.IsNull(source);
        }

        private sealed class FakeValueProviderSource : ValueProviderInputSourceBase
        {
            public FakeValueProviderSource(string id, int blendShapeCount)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
            }

            public override bool TryWriteValues(Span<float> output) => false;
        }

        // --- additional IInputSource だけで駆動するレイヤーが blend に含まれる契約 ---

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
        /// <summary>
        /// Profile で additional IInputSource を宣言したレイヤーは、LayerExpressionSource
        /// (sourceIdx=0) が一度も activate されていなくても blend 対象となり、
        /// sourceIdx=1+ のソースが TriggerOn した値が最終 BlendShape 出力に届くこと。
        /// </summary>
        [Test]
        public void UpdateWeights_AdditionalSourceOnly_TriggersReachFinalOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var expressionBs = new[] { new BlendShapeMapping("bs_smile", 1.0f) };
            var smileExpr = new Expression(
                "smile", "smile", "emotion", 0f, TransitionCurve.Linear, expressionBs);
            var profile = new FacialProfile("1.0", layers, new[] { smileExpr });
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            var controller = new global::Hidano.FacialControl.Adapters.InputSources.ExpressionTriggerInputSource(
                id: global::Hidano.FacialControl.Domain.Models.InputSourceId.Parse(
                    global::Hidano.FacialControl.Adapters.InputSources.ExpressionTriggerInputSource.InputReservedId),
                blendShapeCount: blendShapeNames.Length,
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: blendShapeNames,
                profile: profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, controller, 1.0f),
            };

            using var useCase = new LayerUseCase(
                profile, expressionUseCase, blendShapeNames, additional);

            // ExpressionUseCase.Activate は呼ばない。sourceIdx=1 (input) だけで駆動する。
            controller.TriggerOn("smile");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 1e-4f,
                "additional source のみで triggered した bs_smile が最終出力に反映されること");
        }
#endif

        // --- late-bind 経路 (BindLateInputSource) の回帰 ---
        //
        // init 時点で registry 未解決だった入力源（auto mapping OSC の heartbeat 受信後など）が
        // 購読経由で後から BindLateInputSource された場合でも、その値が最終 BlendShape 出力へ届くこと。
        // 修正前は 2 段でゼロ化されていた:
        //   (1) weight バッファへ宣言 weight が焼かれず、Aggregator の `w > 0f` ガードで書込値が破棄。
        //   (2) _layerHasAdditionalSources が立たず、UpdateWeights の blend フィルタ
        //       (HasBeenActive || hasAdditional) からレイヤーごと除外。
        // さらに OSC 単独レイヤーは init 時 MaxSourcesPerLayer=1 のため、late-bind で registry のみ
        // 容量拡張され weight バッファが範囲外になり SetWeight すら no-op になっていた。

        // 値提供型 (ValueProvider) の Fake。TryWriteValues で固定値を index0 に書き true を返す
        // = OscInputSource が非ゼロ受信値を書く挙動の最小モック。ContributeMask は基底が全 true。
        private sealed class FakeValueWritingSource : ValueProviderInputSourceBase
        {
            private readonly float _value;

            public FakeValueWritingSource(string id, int blendShapeCount, float value)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _value = value;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _value;
                }
                return true;
            }
        }

        [Test]
        public void BindLateInputSource_UnresolvedAtInit_ValueReachesFinalOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            // additional なしで構築 → MaxSourcesPerLayer=1（OSC が init 未解決だった状況を模す）。
            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 1.0f);

            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.6f, output[0], 1e-4f,
                "late-bind された入力源の値が最終出力へ届くこと（修正前はゼロ）");
        }

        [Test]
        public void BindLateInputSource_AppliesDeclaredWeight_ScalesOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 0.5f); // 宣言 weight 0.5

            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.3f, output[0], 1e-4f,
                "late-bind の宣言 weight が intra-layer 加重で反映されること (0.6 * 0.5)");
        }
        [Test]
        public void UnbindLateInputSource_AfterLateBind_RevertsToUnresolvedBehavior()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 1.0f);
            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.6f, useCase.GetBlendedOutput()[0], 1e-4f);

            useCase.UnbindLateInputSource(0, "osc");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0f, output[0], 1e-4f,
                "Unbind 後は late source が合成から外れ、未解決時と同じゼロ出力へ戻ること");
        }

        [Test]
        public void UnbindLateInputSource_RemovesOnlySpecifiedId()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, new FakeValueWritingSource("osc-a", blendShapeNames.Length, 0.4f), 1.0f),
                (0, new FakeValueWritingSource("osc-b", blendShapeNames.Length, 0.2f), 1.0f),
            };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.6f, useCase.GetBlendedOutput()[0], 1e-4f);

            useCase.UnbindLateInputSource(0, "osc-a");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.2f, output[0], 1e-4f,
                "指定 id のみ除去し、残存 source の寄与は維持すること");
        }

        [Test]
        public void BindLateInputSource_ReplacingExistingId_KeepsOtherSourceWeights()
        {
            // REC の Replace（注入体の装着・原本の復元）は購読経由でここに来る。remove + append だと
            // [vp=.2, other=.8] が [other=.2, vp=.2] になり、other の weight が恒久的に失われていた。
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, new FakeValueWritingSource("vp", blendShapeNames.Length, 0.5f), 0.2f),
                (0, new FakeValueWritingSource("other", blendShapeNames.Length, 1.0f), 0.8f),
            };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);
            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.9f, useCase.GetBlendedOutput()[0], 1e-4f, "0.5*0.2 + 1.0*0.8");
            int slotCountBefore = useCase.GetInputSourceWeightsSnapshot().Count;

            useCase.BindLateInputSource(0, new FakeValueWritingSource("vp", blendShapeNames.Length, 0.5f), 0.2f);
            useCase.UpdateWeights(0.001f);

            Assert.AreEqual(0.9f, useCase.GetBlendedOutput()[0], 1e-4f,
                "同 id の置換は同じスロットで行い、他 source の weight を入れ替えないこと");
            Assert.AreEqual(slotCountBefore, useCase.GetInputSourceWeightsSnapshot().Count,
                "同 id の置換でスロット数は増えないこと");
        }

        [Test]
        public void BindLateInputSource_WithDeclaredId_ReplacesOnlyTheDeclaredSlotWhenSourceIdsCollide()
        {
            // 同一レイヤーに slug 違いの OSC receiver（どちらも source.Id == "osc"）を 2 つ宣言した構成。
            // 宣言 id（registry キー）でスロットを同定しないと、2 件目の Replace が 1 件目のスロットを奪う。
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };
            var liveA = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.4f);
            var liveB = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.2f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, liveA, 1.0f),
                (0, liveB, 1.0f),
            };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional, new[] { "oscA", "oscB" });
            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.6f, useCase.GetBlendedOutput()[0], 1e-4f, "両方の osc がスロットを持つ");

            useCase.BindLateInputSource(0, "oscB", new FakeValueWritingSource("osc", blendShapeNames.Length, 0.3f), 1.0f);
            useCase.UpdateWeights(0.001f);

            Assert.AreEqual(0.7f, useCase.GetBlendedOutput()[0], 1e-4f,
                "oscB のスロットだけが置換され、oscA（0.4）はそのまま残ること");

            useCase.UnbindLateInputSource(0, "oscA");
            useCase.UpdateWeights(0.001f);

            Assert.AreEqual(0.3f, useCase.GetBlendedOutput()[0], 1e-4f,
                "宣言 id で解除でき、残る oscB の置換後の値だけになること");
        }

        [Test]
        public void UpdateWeights_SourceValueObserver_ReceivesDeclaredIdAsSourceId()
        {
            // OscInputSource は常に Id "osc" だが、観測 ID はレイヤー宣言の id（registry キー）でなければ
            // REC の基準捕捉・注入（registry キー単位）と対応が取れない。
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile" };
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, new FakeValueWritingSource("osc", blendShapeNames.Length, 0.4f), 1.0f),
            };
            var observer = new RecordingSourceValueObserver();

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional, new[] { "ifm" });
            useCase.SetSourceValueObserver(observer);
            useCase.UpdateWeights(0.001f);

            Assert.That(observer.ObservedIds, Does.Contain("ifm"));
            Assert.That(observer.ObservedIds, Does.Not.Contain("osc"));

            useCase.BindLateInputSource(0, "ifm", new FakeValueWritingSource("osc", blendShapeNames.Length, 0.5f), 1.0f);
            observer.ObservedIds.Clear();
            useCase.UpdateWeights(0.001f);

            Assert.That(observer.ObservedIds, Does.Contain("ifm"), "置換後も同じスロットは宣言 id で観測される");
        }

        private sealed class RecordingSourceValueObserver : Hidano.FacialControl.Domain.Adapters.ILayerSourceValueObserver
        {
            public List<string> ObservedIds { get; } = new List<string>();

            public void OnSourceValuesObserved(int layerIdx, int sourceIdx, IInputSource source, InputSourceId sourceId,
                bool isValid, ReadOnlySpan<float> preWeightValues)
            {
                ObservedIds.Add(sourceId.Value);
            }
        }

        [Test]
        public void UnbindLateInputSource_RemovingFirstSource_ShiftsRemainingWeights()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, new FakeValueWritingSource("a", blendShapeNames.Length, 0.4f), 0.5f),
                (0, new FakeValueWritingSource("b", blendShapeNames.Length, 0.2f), 1.0f),
            };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);
            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.4f, useCase.GetBlendedOutput()[0], 1e-4f, "0.4*0.5 + 0.2*1.0");

            useCase.UnbindLateInputSource(0, "a");
            useCase.UpdateWeights(0.001f);

            Assert.AreEqual(0.2f, useCase.GetBlendedOutput()[0], 1e-4f,
                "詰められた source b は自分の weight 1.0 を保つこと（a の 0.5 を引き継がない）");
        }
    }

    /// <summary>
    /// ベース表情 (<see cref="FacialProfile.BaseExpression"/>) を持つプロファイルで、
    /// <see cref="LayerUseCase.UpdateWeights"/> の出力初期値としてベース表情が適用されることを検証する。
    /// どのレイヤーも contribute しない BlendShape index にはベース表情の値が残り、
    /// contribute する index はレイヤー出力で上書きされる。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class LayerUseCaseWithBaseExpressionTests : SizedTestFixture
    {
        private static readonly string[] BlendShapeNames = { "bs_a", "bs_b" };

        private static FacialProfile CreateProfile(
            BlendShapeSnapshot[] baseExpression,
            params Expression[] expressions)
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };

            return new FacialProfile(
                "1.0",
                layers,
                expressions ?? Array.Empty<Expression>(),
                baseExpression: baseExpression);
        }

        private static Expression CreateSmile()
        {
            return new Expression(
                "smile", "smile", "emotion", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_a", 1f, null) });
        }

        private static BlendShapeSnapshot[] CreateBaseExpression()
        {
            return new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 0.25f),
                new BlendShapeSnapshot(string.Empty, "bs_b", 0.75f),
            };
        }

        [Test]
        public void UpdateWeights_NoActiveExpression_KeepsBaseExpressionValues()
        {
            var profile = CreateProfile(CreateBaseExpression());
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-5f), "表情非活性時は base 値が残る");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f), "表情非活性時は base 値が残る");
        }

        [Test]
        public void UpdateWeights_LayerContributesSubset_KeepsBaseOnNonContributingIndex()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(CreateBaseExpression(), smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f), "contribute する index は表情値で上書きされる");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f), "contribute しない index は base 値が残る");
        }

        [Test]
        public void UpdateWeights_NoBaseExpression_InitializesOutputToZero()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(baseExpression: null, expressions: smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f), "base 未設定時は全 0 初期化（現状互換）");
        }

        [Test]
        public void UpdateWeights_BaseExpressionUnknownBlendShapeName_IsIgnored()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_not_on_this_model", 1f),
                new BlendShapeSnapshot(string.Empty, "bs_b", 0.4f),
            };
            var profile = CreateProfile(baseExpression);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            Assert.DoesNotThrow(() => useCase.UpdateWeights(1f));
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0f).Within(1e-5f), "モデルに無い BlendShape 名は無視される");
            Assert.That(output[1], Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void UpdateWeights_BaseExpressionValueOutOfRange_IsClampedTo01()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 1.5f),
                new BlendShapeSnapshot(string.Empty, "bs_b", -0.5f),
            };
            var profile = CreateProfile(baseExpression);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void UpdateWeights_CalledRepeatedly_ReappliesBaseExpressionEachFrame()
        {
            var smile = CreateSmile();
            var profile = CreateProfile(CreateBaseExpression(), smile);
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            expressionUseCase.Activate(smile);
            useCase.UpdateWeights(1f);
            expressionUseCase.Deactivate(smile);
            useCase.UpdateWeights(1f);
            useCase.UpdateWeights(1f);

            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-5f),
                "表情が rest に戻った index は base 値へ戻る（前フレーム値の残留も base の二重適用も起きない）");
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void SetProfile_NewBaseExpression_ReplacesBaseValues()
        {
            var profile = CreateProfile(CreateBaseExpression());
            var expressionUseCase = new ExpressionUseCase(profile);
            var useCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames);

            useCase.UpdateWeights(1f);

            var newProfile = CreateProfile(new[]
            {
                new BlendShapeSnapshot(string.Empty, "bs_a", 0.1f),
            });
            useCase.SetProfile(newProfile, BlendShapeNames);
            useCase.UpdateWeights(1f);
            var output = useCase.GetBlendedOutput();

            Assert.That(output[0], Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-5f), "新プロファイルに無い index は 0 に戻る");
        }
    }

    /// <summary>
    /// emotion + overlay の 2 レイヤー構成で、overlay レイヤーの <see cref="OverlayInputSource"/> が
    /// active な Expression の slot / snapshot binding（inline overlay スキーマ）から blink overlay を解決することを検証する。
    /// overlay レイヤーの weight は <see cref="LayerUseCase.SetLayerWeight"/> で直接与えるか、
    /// アナログ入力値（<see cref="FakeScalarSource"/>）から転写して与える。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class LayerUseCaseWithOverlayLayerTests : SizedTestFixture
    {
        private const string BlinkSlot = "blink";
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string BrowName = "bs_brow";
        private const string EyeMakeupName = "bs_eye_lift";
        private const string EyeBlinkName = "bs_eye_blink";
        private const string MouthName = "bs_mouth";

        private static (FacialProfile profile, string[] blendShapeNames) BuildProfile()
        {
            var blendShapeNames = new[] { BrowName, EyeMakeupName, EyeBlinkName, MouthName };
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
            };

            var smileBlinkSnapshot = LayerUseCaseTestSupport.CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var defaultBlinkSnapshot = LayerUseCaseTestSupport.CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(BrowName, 1.0f),
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: smileBlinkSnapshot),
                });

            var smileClosedEye = new Expression(
                id: "smile_closed_eye",
                name: "SmileClosedEye",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 0.75f),
                    new BlendShapeMapping(EyeBlinkName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                });

            var neutral = new Expression(
                id: "neutral",
                name: "Neutral",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.25f),
                });

            var inputSources = new[]
            {
                new[] { new InputSourceDeclaration("input", 1f, null) },
                new[] { new InputSourceDeclaration("input:overlay:blink", 1f, null) },
            };

            var profile = new FacialProfile(
                schemaVersion: "1.0",
                layers: layers,
                expressions: new[] { smile, smileClosedEye, neutral },
                rendererPaths: null,
                layerInputSources: inputSources,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlinkSnapshot),
                },
                slots: new[] { BlinkSlot });

            return (profile, blendShapeNames);
        }

        private static (LayerUseCase useCase, ExpressionUseCase exprUseCase, FakeScalarSource trigger)
            BuildPipeline(FacialProfile profile, string[] blendShapeNames)
        {
            var exprUseCase = new ExpressionUseCase(profile);
            var trigger = new FakeScalarSource("trigger");

            var overlayInputSource = new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                activeProvider: exprUseCase,
                emotionLayerName: EmotionLayer);

            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (1, overlayInputSource, 1f),
            };
            var useCase = new LayerUseCase(profile, exprUseCase, blendShapeNames, additional);
            return (useCase, exprUseCase, trigger);
        }

        private static void Activate(ExpressionUseCase exprUseCase, FacialProfile profile, string expressionId)
        {
            var expression = profile.FindExpressionById(expressionId);
            Assert.IsTrue(expression.HasValue, $"テスト profile に '{expressionId}' が存在すること。");
            exprUseCase.Activate(expression.Value);
        }

        private static void ApplyAnalogOverlayWeight(LayerUseCase useCase, FakeScalarSource trigger)
        {
            Assert.IsTrue(trigger.TryReadScalar(out float value));
            useCase.SetLayerWeight(OverlayLayer, value);
        }

        [Test]
        public void BuildProfile_UsesInlineOverlaySchema()
        {
            var (profile, _) = BuildProfile();

            Assert.AreEqual(1, profile.Slots.Length);
            Assert.AreEqual(BlinkSlot, profile.Slots.Span[0]);
            Assert.AreEqual(3, profile.Expressions.Length);
            Assert.IsFalse(profile.FindExpressionById("blink_overlay").HasValue);

            var smile = profile.FindExpressionById("smile").Value;
            Assert.IsTrue(smile.TryGetOverlay(BlinkSlot, out var smileBinding));
            Assert.IsFalse(smileBinding.Suppress);
            Assert.IsTrue(smileBinding.Snapshot.HasValue);
            Assert.AreEqual("smile_blink_snapshot", smileBinding.Snapshot.Value.Id);

            var smileClosedEye = profile.FindExpressionById("smile_closed_eye").Value;
            Assert.IsTrue(smileClosedEye.TryGetOverlay(BlinkSlot, out var closedEyeBinding));
            Assert.IsTrue(closedEyeBinding.Suppress);
            Assert.IsFalse(closedEyeBinding.Snapshot.HasValue);
        }

        // --- overlay weight を SetLayerWeight で直接与える ---

        [Test]
        public void SmileHold_FullTrigger_InlineOverlayReplacesEyeBlendShapes()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-3f);
                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void SmileHold_HalfTrigger_InlineOverlayInterpolatesLinearly()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile").Value);
                useCase.SetLayerWeight("overlay", 0.5f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-3f);
                Assert.AreEqual(0.5f, output[1], 1e-3f);
                Assert.AreEqual(0.5f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void SmileClosedEyeHold_FullTrigger_OverlaySuppressed()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("smile_closed_eye").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.75f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.5f, output[3], 1e-3f);
            }
        }

        [Test]
        public void NoActiveExpression_FullTrigger_DefaultBlinkFires()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, _, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[0], 1e-3f);
                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.0f, output[3], 1e-3f);
            }
        }

        [Test]
        public void NeutralHold_FullTrigger_FallsBackToDefaultBlink()
        {
            var (profile, bsNames) = BuildProfile();
            var (useCase, exprUseCase, _) = BuildPipeline(profile, bsNames);
            using (useCase)
            {
                exprUseCase.Activate(profile.FindExpressionById("neutral").Value);
                useCase.SetLayerWeight("overlay", 1f);
                useCase.UpdateWeights(1f);

                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[1], 1e-3f);
                Assert.AreEqual(1.0f, output[2], 1e-3f);
                Assert.AreEqual(0.25f, output[3], 1e-3f);
            }
        }

        // --- overlay weight をアナログ入力値から転写する ---

        [Test]
        public void AnalogZero_SmileInlineOverlayDoesNotAffectBaseExpression()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(1.0f, output[1], 1e-4f);
                Assert.AreEqual(0.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogHalf_SmileInlineOverlayInterpolatesBlinkSlot()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0.5f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.5f, output[1], 1e-4f);
                Assert.AreEqual(0.5f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogFull_SmileInlineOverlayReplacesBlinkSlot()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.0f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogFull_SmileClosedEyeSuppressesOverlay()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile_closed_eye");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[0], 1e-4f);
                Assert.AreEqual(0.75f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
            }
        }

        [Test]
        public void AnalogSweep_SmileInlineOverlayFollowsTriggerLinearly()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                useCase.UpdateWeights(1f);

                foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1.0f })
                {
                    trigger.Value = t;
                    ApplyAnalogOverlayWeight(useCase, trigger);
                    useCase.UpdateWeights(0.016f);
                    var output = useCase.GetBlendedOutput();

                    Assert.AreEqual(1.0f, output[0], 1e-4f, $"Trigger={t} で Brow が smile の値を維持すること。");
                    Assert.AreEqual(1f - t, output[1], 1e-4f, $"Trigger={t} で EyeMakeup が線形に blend out すること。");
                    Assert.AreEqual(t, output[2], 1e-4f, $"Trigger={t} に Blink が線形追従すること。");
                    Assert.AreEqual(0.5f, output[3], 1e-4f, $"Trigger={t} で Mouth が smile の値を維持すること。");
                }
            }
        }
    }

    /// <summary>
    /// emotion レイヤー内でアナログ入力により加算される Expression（<see cref="AnalogExpressionInputSource"/>）と、
    /// 同じアナログ値で weight 付けされる overlay レイヤー（<see cref="OverlayInputSource"/>）が共存する構成の回帰テスト。
    /// アナログ加算表情と inline overlay がともにトリガー値へ線形に追従することを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class LayerUseCaseWithAnalogExpressionAdditionTests : SizedTestFixture
    {
        private const string BlinkSlot = "blink";
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string BrowName = "bs_brow";
        private const string EyeMakeupName = "bs_eye_lift";
        private const string EyeBlinkName = "bs_eye_blink";
        private const string MouthName = "bs_mouth";
        private const string CheekName = "bs_cheek";
        private const string AnalogExpressionId = "analog_cheek";

        private static (FacialProfile profile, string[] blendShapeNames) BuildProfile()
        {
            var blendShapeNames = new[] { BrowName, EyeMakeupName, EyeBlinkName, MouthName, CheekName };
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
            };

            var smileBlinkSnapshot = LayerUseCaseTestSupport.CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var defaultBlinkSnapshot = LayerUseCaseTestSupport.CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0.0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1.0f));

            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(BrowName, 1.0f),
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: smileBlinkSnapshot),
                });

            var smileClosedEye = new Expression(
                id: "smile_closed_eye",
                name: "SmileClosedEye",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 0.75f),
                    new BlendShapeMapping(EyeBlinkName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.5f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                });

            var analogCheek = new Expression(
                id: AnalogExpressionId,
                name: "AnalogCheek",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(CheekName, 1.0f),
                });

            var neutral = new Expression(
                id: "neutral",
                name: "Neutral",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(EyeMakeupName, 1.0f),
                    new BlendShapeMapping(MouthName, 0.25f),
                });

            var profile = new FacialProfile(
                schemaVersion: "1.0",
                layers: layers,
                expressions: new[] { smile, smileClosedEye, analogCheek, neutral },
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlinkSnapshot),
                },
                slots: new[] { BlinkSlot });

            return (profile, blendShapeNames);
        }

        private static (LayerUseCase useCase, ExpressionUseCase exprUseCase, FakeScalarSource trigger)
            BuildPipeline(FacialProfile profile, string[] blendShapeNames)
        {
            var exprUseCase = new ExpressionUseCase(profile);
            var trigger = new FakeScalarSource("trigger");

            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { trigger.Id, trigger },
            };
            var bindings = new[]
            {
                new AnalogExpressionBinding(
                    sourceId: trigger.Id,
                    sourceAxis: 0,
                    expressionId: AnalogExpressionId,
                    scale: 1f),
            };

            var analogExpression = new AnalogExpressionInputSource(
                id: InputSourceId.Parse(AnalogExpressionInputSource.ReservedId),
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                sources: sources,
                bindings: bindings);

            var overlayInputSource = new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                activeProvider: exprUseCase,
                emotionLayerName: EmotionLayer);

            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, analogExpression, 1f),
                (1, overlayInputSource, 1f),
            };

            var useCase = new LayerUseCase(profile, exprUseCase, blendShapeNames, additional);
            return (useCase, exprUseCase, trigger);
        }

        private static void Activate(ExpressionUseCase exprUseCase, FacialProfile profile, string expressionId)
        {
            var expression = profile.FindExpressionById(expressionId);
            Assert.IsTrue(expression.HasValue, $"Test profile must contain '{expressionId}'.");
            exprUseCase.Activate(expression.Value);
        }

        private static void ApplyAnalogOverlayWeight(LayerUseCase useCase, FakeScalarSource trigger)
        {
            Assert.IsTrue(trigger.TryReadScalar(out float value));
            useCase.SetLayerWeight(OverlayLayer, value);
        }

        [Test]
        public void BuildProfile_UsesInlineOverlaySchema()
        {
            var (profile, _) = BuildProfile();

            Assert.AreEqual(1, profile.Slots.Length);
            Assert.AreEqual(BlinkSlot, profile.Slots.Span[0]);
            Assert.IsFalse(profile.FindExpressionById("blink_overlay").HasValue);
            Assert.IsEmpty(profile.ValidateSlotReferences());

            var smile = profile.FindExpressionById("smile").Value;
            Assert.IsTrue(smile.TryGetOverlay(BlinkSlot, out var smileBinding));
            Assert.IsFalse(smileBinding.Suppress);
            Assert.IsTrue(smileBinding.Snapshot.HasValue);
            Assert.AreEqual("smile_blink_snapshot", smileBinding.Snapshot.Value.Id);

            var smileClosedEye = profile.FindExpressionById("smile_closed_eye").Value;
            Assert.IsTrue(smileClosedEye.TryGetOverlay(BlinkSlot, out var closedEyeBinding));
            Assert.IsTrue(closedEyeBinding.Suppress);
            Assert.IsFalse(closedEyeBinding.Snapshot.HasValue);
        }

        [Test]
        public void TriggerZero_KeepsExpressionAndLeavesAnalogPathsInactive()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(1.0f, output[1], 1e-4f);
                Assert.AreEqual(0.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
                Assert.AreEqual(0.0f, output[4], 1e-4f);
            }
        }

        [Test]
        public void TriggerHalf_AddsAnalogExpressionAndInterpolatesInlineOverlay()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0.5f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.5f, output[1], 1e-4f);
                Assert.AreEqual(0.5f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
                Assert.AreEqual(0.5f, output[4], 1e-4f);
            }
        }

        [Test]
        public void TriggerFull_AddsAnalogExpressionAndReplacesBlinkSlot()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f);
                Assert.AreEqual(0.0f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
                Assert.AreEqual(1.0f, output[4], 1e-4f);
            }
        }

        [Test]
        public void TriggerFull_SuppressedOverlayStillAddsAnalogExpression()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile_closed_eye");
                trigger.Value = 1f;
                ApplyAnalogOverlayWeight(useCase, trigger);

                useCase.UpdateWeights(1f);
                var output = useCase.GetBlendedOutput();

                Assert.AreEqual(0.0f, output[0], 1e-4f);
                Assert.AreEqual(0.75f, output[1], 1e-4f);
                Assert.AreEqual(1.0f, output[2], 1e-4f);
                Assert.AreEqual(0.5f, output[3], 1e-4f);
                Assert.AreEqual(1.0f, output[4], 1e-4f);
            }
        }

        [Test]
        public void TriggerSweep_AnalogExpressionAndInlineOverlayFollowTriggerLinearly()
        {
            var (profile, blendShapeNames) = BuildProfile();
            var (useCase, exprUseCase, trigger) = BuildPipeline(profile, blendShapeNames);
            using (useCase)
            {
                Activate(exprUseCase, profile, "smile");
                trigger.Value = 0f;
                ApplyAnalogOverlayWeight(useCase, trigger);
                useCase.UpdateWeights(1f);

                foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1.0f })
                {
                    trigger.Value = t;
                    ApplyAnalogOverlayWeight(useCase, trigger);
                    useCase.UpdateWeights(0.016f);
                    var output = useCase.GetBlendedOutput();

                    Assert.AreEqual(1.0f, output[0], 1e-4f, $"Trigger={t} should keep brow from smile.");
                    Assert.AreEqual(1f - t, output[1], 1e-4f, $"Trigger={t} should blend eye makeup out.");
                    Assert.AreEqual(t, output[2], 1e-4f, $"Trigger={t} should blend blink in.");
                    Assert.AreEqual(0.5f, output[3], 1e-4f, $"Trigger={t} should keep mouth from smile.");
                    Assert.AreEqual(t, output[4], 1e-4f, $"Trigger={t} should add analog cheek expression.");
                }
            }
        }
    }

    /// <summary>
    /// <see cref="LayerUseCase"/> テスト共用の補助。overlay slot に割り当てる
    /// <see cref="ExpressionSnapshot"/> を最小引数で生成する。
    /// </summary>
    internal static class LayerUseCaseTestSupport
    {
        public static ExpressionSnapshot CreateSnapshot(
            string id,
            params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: blendShapes,
                bones: null,
                rendererPaths: null);
        }
    }

    /// <summary>
    /// スカラー値を外部から設定できる <see cref="IAnalogInputSource"/> フェイク。
    /// 常に valid で、1 軸の値をそのまま返す。
    /// </summary>
    internal sealed class FakeScalarSource : IAnalogInputSource
    {
        public FakeScalarSource(string id) { Id = id; }

        public string Id { get; }
        public bool IsValid => true;
        public int AxisCount => 1;
        public float Value { get; set; }

        public void Tick(float deltaTime) { }

        public bool TryReadScalar(out float value)
        {
            value = Value;
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            x = Value;
            y = 0f;
            return true;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (output.Length >= 1) output[0] = Value;
            return true;
        }
    }
}
