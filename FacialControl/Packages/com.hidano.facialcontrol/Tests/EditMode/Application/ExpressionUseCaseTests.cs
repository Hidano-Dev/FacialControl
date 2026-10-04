using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// <see cref="ExpressionUseCase"/> の Activate / Deactivate / GetActiveExpressions / SetProfile と
    /// レイヤーの排他モード (LastWins / Blend) に基づく active 管理を検証する。
    /// あわせて <see cref="IActiveExpressionProvider"/> 実装（レイヤー別 top の取得）と、
    /// それに依存する <see cref="OverlayInputSource"/> の slot 解決が active 切替に追従することを確認する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ExpressionUseCaseTests : SizedTestFixture
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

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion")
        {
            return new Expression(id, name, layer);
        }

        private static FacialProfile CreateProfile(
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
        {
            return new FacialProfile(
                "1.0",
                layers ?? CreateDefaultLayers(),
                expressions ?? new[] { CreateExpression() });
        }

        private ExpressionUseCase _useCase;

        [SetUp]
        public void SetUp()
        {
            var profile = CreateProfile();
            _useCase = new ExpressionUseCase(profile);
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_ValidProfile_CreatesInstance()
        {
            var profile = CreateProfile();
            var useCase = new ExpressionUseCase(profile);

            Assert.IsNotNull(useCase);
        }

        [Test]
        public void Constructor_InitiallyNoActiveExpressions()
        {
            var activeExpressions = _useCase.GetActiveExpressions();

            Assert.AreEqual(0, activeExpressions.Count);
        }

        // --- Activate ---

        [Test]
        public void Activate_ValidExpression_AddsToActiveList()
        {
            var expr = CreateExpression();

            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_LastWinsLayer_ReplacesExistingExpression()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        [Test]
        public void Activate_BlendLayer_AllowsMultipleExpressions()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [Test]
        public void Activate_DifferentLayers_BothActive()
        {
            var emotionExpr = CreateExpression("expr-1", "smile", "emotion");
            var eyeExpr = CreateExpression("expr-2", "wink", "eye");

            _useCase.Activate(emotionExpr);
            _useCase.Activate(eyeExpr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [Test]
        public void Activate_SameExpressionTwice_LastWins_NoDuplicate()
        {
            var expr = CreateExpression("expr-1", "smile", "emotion");

            _useCase.Activate(expr);
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_SameExpressionTwice_Blend_NoDuplicate()
        {
            var expr = CreateExpression("expr-1", "talk_a", "lipsync");

            _useCase.Activate(expr);
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_MultipleExpressionsInBlendLayer_AllRetained()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");
            var expr3 = CreateExpression("expr-3", "talk_u", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);
        }

        [Test]
        public void Activate_LastWinsLayer_MultipleReplacements_OnlyLastRemains()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");
            var expr3 = CreateExpression("expr-3", "angry", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-3", active[0].Id);
        }

        [Test]
        public void Activate_UndefinedLayer_FallsBackToEmotion()
        {
            // プロファイルにない "unknown" レイヤーの Expression → emotion にフォールバック
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "custom", "unknown");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // emotion は LastWins なので、expr2 がフォールバックして emotion レイヤーに入り、
            // expr1 を置き換える
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        // --- Deactivate ---

        [Test]
        public void Deactivate_ActiveExpression_RemovesFromActiveList()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            _useCase.Deactivate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void Deactivate_NonActiveExpression_DoesNothing()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");
            _useCase.Activate(expr1);

            // expr2 はアクティブではないので、何もしない
            _useCase.Deactivate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Deactivate_NoActiveExpressions_DoesNothing()
        {
            var expr = CreateExpression();

            // アクティブな Expression がない状態で Deactivate しても例外なし
            Assert.DoesNotThrow(() => _useCase.Deactivate(expr));
        }

        [Test]
        public void Deactivate_OneOfMultipleInBlendLayer_OthersRemain()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");
            var expr3 = CreateExpression("expr-3", "talk_u", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            _useCase.Deactivate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
            Assert.AreEqual("expr-3", active[1].Id);
        }

        [Test]
        public void Deactivate_FromOneLayer_OtherLayersUnaffected()
        {
            var emotionExpr = CreateExpression("expr-1", "smile", "emotion");
            var lipsyncExpr = CreateExpression("expr-2", "talk_a", "lipsync");

            _useCase.Activate(emotionExpr);
            _useCase.Activate(lipsyncExpr);

            _useCase.Deactivate(emotionExpr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        [Test]
        public void Deactivate_ById_RemovesCorrectExpression()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // ID が一致する Expression のみ削除される
            _useCase.Deactivate(expr1);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        // --- GetActiveExpressions ---

        [Test]
        public void GetActiveExpressions_ReturnsDefensiveCopy()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var active1 = _useCase.GetActiveExpressions();
            var active2 = _useCase.GetActiveExpressions();

            // 別のリストインスタンスが返される
            Assert.AreNotSame(active1, active2);
        }

        [Test]
        public void GetActiveExpressions_ModifyingReturnedList_DoesNotAffectInternal()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            active.Clear();

            // 内部状態は変更されない
            var activeAgain = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, activeAgain.Count);
        }

        [Test]
        public void GetActiveExpressions_MultipleLayersActive_ReturnsAll()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "talk_a", "lipsync");
            var expr3 = CreateExpression("expr-3", "wink", "eye");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);
        }

        [Test]
        public void CollectActiveExpressions_PopulatesProvidedBuffer()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "talk_a", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            var buffer = new List<Expression>();

            _useCase.CollectActiveExpressions(buffer);

            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual("expr-1", buffer[0].Id);
            Assert.AreEqual("expr-2", buffer[1].Id);
        }

        [Test]
        public void CollectActiveExpressions_ClearsAndReusesProvidedBuffer()
        {
            var expr = CreateExpression("expr-1", "smile", "emotion");
            _useCase.Activate(expr);

            var buffer = new List<Expression> { CreateExpression("stale", "stale", "eye") };

            _useCase.CollectActiveExpressions(buffer);

            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual("expr-1", buffer[0].Id);
        }

        [Test]
        public void CollectActiveExpressions_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _useCase.CollectActiveExpressions(null));
        }

        // --- SetProfile ---

        [Test]
        public void SetProfile_ClearsActiveExpressions()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var newProfile = CreateProfile();
            _useCase.SetProfile(newProfile);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void SetProfile_NewProfileLayerRulesApply()
        {
            // emotion を Blend モードに変更した新しいプロファイル
            var newLayers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.Blend),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var newProfile = CreateProfile(layers: newLayers);
            _useCase.SetProfile(newProfile);

            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // emotion が Blend モードなので、両方アクティブ
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        // --- 日本語 Expression 名 ---

        [Test]
        public void Activate_JapaneseExpressionName_Works()
        {
            var expr = CreateExpression("expr-jp", "笑顔", "emotion");

            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("笑顔", active[0].Name);
        }

        // --- 複雑なシナリオ ---

        [Test]
        public void ComplexScenario_ActivateDeactivateAcrossLayers()
        {
            var smile = CreateExpression("expr-1", "smile", "emotion");
            var sad = CreateExpression("expr-2", "sad", "emotion");
            var talkA = CreateExpression("expr-3", "talk_a", "lipsync");
            var talkO = CreateExpression("expr-4", "talk_o", "lipsync");
            var wink = CreateExpression("expr-5", "wink", "eye");

            // 初期: 各レイヤーにアクティブ化
            _useCase.Activate(smile);    // emotion: smile
            _useCase.Activate(talkA);    // lipsync: talk_a
            _useCase.Activate(wink);     // eye: wink

            Assert.AreEqual(3, _useCase.GetActiveExpressions().Count);

            // emotion を sad に切り替え（LastWins なので smile が消える）
            _useCase.Activate(sad);
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);

            // lipsync に追加（Blend なので両方残る）
            _useCase.Activate(talkO);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(4, active.Count);

            // lipsync から talk_a を非アクティブ化
            _useCase.Deactivate(talkA);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);

            // eye を非アクティブ化
            _useCase.Deactivate(wink);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        // --- IActiveExpressionProvider（レイヤー別 top の取得） ---
        //
        // OverlayInputSource の解決ロジックは本 provider に依存するため、
        // レイヤー別 top の取得が確実に動くことを保証する。

        private const string EmotionLayer = "emotion";
        private const string EyeLayer = "eye";
        private const string BlinkSlot = "blink";
        private const string BlinkBlendShapeName = "bs_eye_blink";
        private const string MouthBlendShapeName = "bs_mouth";

        private static FacialProfile CreateLastWinsProfile()
        {
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                new LayerDefinition(EyeLayer, 1, ExclusionMode.LastWins),
            };
            return new FacialProfile("1.0", layers);
        }

        private static FacialProfile CreateBlendProfile()
        {
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.Blend),
            };
            return new FacialProfile("1.0", layers);
        }

        private static (FacialProfile profile, string[] blendShapeNames) CreateOverlayProfile()
        {
            var blendShapeNames = new[] { BlinkBlendShapeName, MouthBlendShapeName };
            var layers = new[]
            {
                new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
            };

            var smileBlinkSnapshot = CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, BlinkBlendShapeName, 1f));
            var defaultBlinkSnapshot = CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, BlinkBlendShapeName, 0.25f));

            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(MouthBlendShapeName, 0.5f),
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
                    new BlendShapeMapping(MouthBlendShapeName, 0.5f),
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
                    new BlendShapeMapping(MouthBlendShapeName, 0.25f),
                },
                overlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: null),
                });

            var profile = new FacialProfile(
                schemaVersion: "1.0",
                layers: layers,
                expressions: new[] { smile, smileClosedEye, neutral },
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlinkSnapshot),
                },
                slots: new[] { BlinkSlot });

            return (profile, blendShapeNames);
        }

        private static ExpressionSnapshot CreateSnapshot(
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

        private static OverlayInputSource CreateOverlaySource(
            FacialProfile profile,
            string[] blendShapeNames,
            IActiveExpressionProvider activeProvider)
        {
            return new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: blendShapeNames.Length,
                blendShapeNames: blendShapeNames,
                profile: profile,
                activeProvider: activeProvider,
                emotionLayerName: EmotionLayer);
        }

        private static Expression GetExpression(FacialProfile profile, string id)
        {
            var expression = profile.FindExpressionById(id);
            Assert.IsTrue(expression.HasValue, $"Test profile must contain '{id}'.");
            return expression.Value;
        }

        [Test]
        public void TryGetTop_NoActive_ReturnsNull()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_AfterActivate_ReturnsThatExpression()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            var smile = new Expression("smile", "Smile", "emotion");
            sut.Activate(smile);

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("smile", top.Value.Id);
        }

        [Test]
        public void TryGetTop_LastWins_ReplacementReplacesTop()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("anger", "Anger", "emotion"));

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("anger", top.Value.Id, "LastWins では最後 Activate が top");
        }

        [Test]
        public void TryGetTop_Blend_ReturnsLastAddedAsTop()
        {
            var sut = new ExpressionUseCase(CreateBlendProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("anger", "Anger", "emotion"));

            var top = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            Assert.IsNotNull(top);
            Assert.AreEqual("anger", top.Value.Id, "Blend モードでも最後 Activate を top として返す");
        }

        [Test]
        public void TryGetTop_AfterDeactivate_ReturnsNullWhenLayerEmpties()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            var smile = new Expression("smile", "Smile", "emotion");
            sut.Activate(smile);
            sut.Deactivate(smile);

            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_DifferentLayer_IndependentlyTracked()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            sut.Activate(new Expression("blink", "Blink", "eye"));

            var emotion = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion");
            var eye = ((IActiveExpressionProvider)sut).TryGetTopActiveExpression("eye");
            Assert.AreEqual("smile", emotion?.Id);
            Assert.AreEqual("blink", eye?.Id);
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryGetTop_NullOrEmptyLayer_ReturnsNull(string layerName)
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression(layerName));
        }

        [Test]
        public void TryGetTop_UnknownLayer_ReturnsNull()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));
            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("does_not_exist"));
        }

        [Test]
        public void TryGetTop_AfterSetProfile_IsCleared()
        {
            var sut = new ExpressionUseCase(CreateLastWinsProfile());
            sut.Activate(new Expression("smile", "Smile", "emotion"));

            sut.SetProfile(CreateLastWinsProfile());

            Assert.IsNull(((IActiveExpressionProvider)sut).TryGetTopActiveExpression("emotion"));
        }

        [Test]
        public void TryGetTop_ActiveExpressionSwitches_OverlayInputSourceFollowsSlotBinding()
        {
            var (profile, blendShapeNames) = CreateOverlayProfile();
            Assert.IsEmpty(profile.ValidateSlotReferences());
            Assert.IsFalse(profile.FindExpressionById("blink_overlay").HasValue);

            var sut = new ExpressionUseCase(profile);
            var overlaySource = CreateOverlaySource(profile, blendShapeNames, sut);
            var output = new float[blendShapeNames.Length];

            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(0.25f, output[0], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "smile"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(1f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "smile_closed_eye"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsFalse(overlaySource.TryWriteValues(output));
            Assert.AreEqual(-1f, output[0], 1e-6f);
            Assert.AreEqual(-1f, output[1], 1e-6f);
            Assert.IsFalse(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);

            sut.Activate(GetExpression(profile, "neutral"));
            output[0] = -1f;
            output[1] = -1f;
            Assert.IsTrue(overlaySource.TryWriteValues(output));
            Assert.AreEqual(0.25f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.IsTrue(overlaySource.ContributeMask[0]);
            Assert.IsFalse(overlaySource.ContributeMask[1]);
        }

        [Test]
        public void Activate_ObserverRegistered_NotifiesWithReservedId()
        {
            var expression = new Expression("expr-1", "Smile", "emotion");
            var observer = new ExpressionObserverSpy();
            _useCase.SetActivationObserver(observer);

            _useCase.Activate(expression);

            Assert.AreEqual("@expression", observer.ActivatedSourceId);
            Assert.AreEqual("expr-1", observer.ActivatedExpressionId);
        }

        [Test]
        public void Deactivate_InactiveExpression_DoesNotNotify()
        {
            var observer = new ExpressionObserverSpy();
            _useCase.SetActivationObserver(observer);

            _useCase.Deactivate(new Expression("expr-1", "Smile", "emotion"));

            Assert.AreEqual(0, observer.DeactivatedCount);
        }

        [Test]
        public void SuspendActivation_LiveActivate_IsIgnoredWithoutNotification()
        {
            var observer = new ExpressionObserverSpy();
            _useCase.SetActivationObserver(observer);
            Assert.IsTrue(_useCase.SuspendActivation());

            _useCase.Activate(new Expression("expr-1", "Smile", "emotion"));

            Assert.IsEmpty(_useCase.GetActiveExpressions());
            Assert.AreEqual(0, observer.ActivatedCount);
        }

        [Test]
        public void InjectActivate_LastWins_MatchesLiveResult()
        {
            var profile = CreateProfile(expressions: new[]
            {
                new Expression("a", "A", "emotion"),
                new Expression("b", "B", "emotion")
            });
            var sut = new ExpressionUseCase(profile);
            sut.SuspendActivation();

            Assert.IsTrue(sut.InjectActivate("a"));
            Assert.IsTrue(sut.InjectActivate("b"));

            CollectionAssert.AreEqual(new[] { "b" }, sut.GetActiveExpressions().ConvertAll(e => e.Id));
        }

        [Test]
        public void ResetActiveExpressions_Blend_PreservesOrderWithoutNotificationAndAdvancesGeneration()
        {
            var profile = CreateProfile(
                layers: new[] { new LayerDefinition("lipsync", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("a", "A", "lipsync"),
                    new Expression("b", "B", "lipsync")
                });
            var sut = new ExpressionUseCase(profile);
            var observer = new ExpressionObserverSpy();
            sut.SetActivationObserver(observer);
            var before = sut.ResetGeneration;

            sut.ResetActiveExpressions(new[] { "a", "b" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, sut.GetActiveExpressions().ConvertAll(e => e.Id));
            Assert.AreEqual(before + 1, sut.ResetGeneration);
            Assert.AreEqual(0, observer.ActivatedCount);
        }

        [Test]
        public void CollectActiveExpressionIds_UsesDeclaredLayerOrderThenInternalOrder()
        {
            var profile = CreateProfile(
                layers: new[]
                {
                    new LayerDefinition("eye", 0, ExclusionMode.Blend),
                    new LayerDefinition("emotion", 1, ExclusionMode.Blend)
                },
                expressions: new[]
                {
                    new Expression("eye-a", "Eye A", "eye"),
                    new Expression("emotion-a", "Emotion A", "emotion"),
                    new Expression("emotion-b", "Emotion B", "emotion")
                });
            var sut = new ExpressionUseCase(profile);
            sut.Activate(profile.FindExpressionById("emotion-a").Value);
            sut.Activate(profile.FindExpressionById("eye-a").Value);
            sut.Activate(profile.FindExpressionById("emotion-b").Value);
            var ids = new List<string>();

            sut.CollectActiveExpressionIds(ids);

            CollectionAssert.AreEqual(new[] { "eye-a", "emotion-a", "emotion-b" }, ids);
        }

        private sealed class ExpressionObserverSpy : IExpressionActivationObserver
        {
            public int ActivatedCount;
            public int DeactivatedCount;
            public string ActivatedSourceId;
            public string ActivatedExpressionId;

            public void OnExpressionActivated(string sourceId, string expressionId)
            {
                ActivatedCount++;
                ActivatedSourceId = sourceId;
                ActivatedExpressionId = expressionId;
            }

            public void OnExpressionDeactivated(string sourceId, string expressionId)
            {
                DeactivatedCount++;
            }
        }
    }
}
