using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// トリガー型入力源をレイヤー別スタックで合成する（HID-418）ことを <see cref="LayerUseCase"/> 経由で確かめる。
    /// 同じ入力源を emotion / eye の 2 レイヤーが宣言した Profile で、smile（Layer=emotion）→ eyeclose（Layer=eye）の順に
    /// on にすると、ライブ・REC 再生の注入・REC のシークのどれでも「笑ったまま目を閉じる」になる。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class LayerUseCasePerLayerTriggerStackTests : SizedTestFixture
    {
        private const string Emotion = "emotion";
        private const string Eye = "eye";
        private const int MouthSmile = 0;
        private const int EyeClose = 1;
        private const int MouthOpen = 2;

        private static readonly string[] BlendShapeNames = { "mouth_smile", "eye_close", "mouth_open" };

        private sealed class TriggerSource : ExpressionTriggerInputSourceBase
        {
            public TriggerSource(FacialProfile profile, ExclusionMode exclusionMode = ExclusionMode.LastWins, int maxStackDepth = 16)
                : base(InputSourceId.Parse("input"), BlendShapeNames.Length, maxStackDepth, exclusionMode, BlendShapeNames, profile)
            {
            }
        }

        /// <summary>
        /// 改修前の接続（入力源そのものを各レイヤーのスロットへ登録する）を再現するための素通しラッパー。
        /// トリガー型ではないので LayerUseCase は子スタックへ差し替えない。
        /// </summary>
        private sealed class LegacySharedSlot : IInputSource
        {
            private readonly IInputSource _inner;

            public LegacySharedSlot(IInputSource inner) => _inner = inner;

            public string Id => _inner.Id;
            public InputSourceType Type => _inner.Type;
            public int BlendShapeCount => _inner.BlendShapeCount;
            public BitArray ContributeMask => _inner.ContributeMask;
            public void Tick(float deltaTime) => _inner.Tick(deltaTime);
            public bool TryWriteValues(Span<float> output) => _inner.TryWriteValues(output);
        }

        private static FacialProfile BuildProfile(
            float smileDuration = 0f,
            ExclusionMode emotionMode = ExclusionMode.LastWins)
        {
            var layers = new[]
            {
                new LayerDefinition(Emotion, 0, emotionMode),
                new LayerDefinition(Eye, 1, ExclusionMode.LastWins),
            };
            var expressions = new[]
            {
                new Expression("smile", "smile", Emotion, smileDuration, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("mouth_smile", 1f) }),
                new Expression("open", "open", Emotion, 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("mouth_open", 0.5f) }),
                new Expression("eyeclose", "eyeclose", Eye, 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("eye_close", 1f) }),
            };
            return new FacialProfile("1.0", layers, expressions);
        }

        private static LayerUseCase CreateUseCase(FacialProfile profile, IInputSource emotionSlot, IInputSource eyeSlot)
        {
            var additional = new List<(int layerIdx, IInputSource source, float weight)>();
            if (emotionSlot != null) additional.Add((0, emotionSlot, 1f));
            if (eyeSlot != null) additional.Add((1, eyeSlot, 1f));
            return new LayerUseCase(profile, new ExpressionUseCase(profile), BlendShapeNames, additional);
        }

        [Test]
        public void UpdateWeights_SameTriggerOnTwoLayers_SmileThenEyeClose_KeepsSmile()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, source);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");
            useCase.UpdateWeights(0.016f);

            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(1f, output[MouthSmile], 1e-4f, "笑ったまま");
            Assert.AreEqual(1f, output[EyeClose], 1e-4f, "目を閉じる");
        }

        [Test]
        public void UpdateWeights_SameTriggerOnTwoLayers_LegacySharedSlot_LosesSmile()
        {
            // 改修前の形（共有スタック）では LastWins の末尾 eyeclose だけが両レイヤーへ出て、笑いが消えていた。
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            var shared = new LegacySharedSlot(source);
            using var useCase = CreateUseCase(profile, shared, shared);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");
            useCase.UpdateWeights(0.016f);

            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(0f, output[MouthSmile], 1e-4f);
            Assert.AreEqual(1f, output[EyeClose], 1e-4f);
        }

        [Test]
        public void UpdateWeights_RecPlaybackInject_SmileThenEyeClose_KeepsSmile()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, source);
            source.SuspendTriggerInput();

            // REC 再生はライブ入力を止めたうえで、記録した TriggerOn を注入する。
            source.InjectTriggerOn("smile");
            source.InjectTriggerOn("eyeclose");
            useCase.UpdateWeights(0.016f);

            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(1f, output[MouthSmile], 1e-4f);
            Assert.AreEqual(1f, output[EyeClose], 1e-4f);
        }

        [Test]
        public void UpdateWeights_RecSeekResetToStack_SmileThenEyeClose_KeepsSmile()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, source);

            // REC のシークは、畳み込んだスタックで即時に確立する。
            source.ResetToExpressionStack(new[] { "smile", "eyeclose" });
            useCase.UpdateWeights(0.016f);

            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(1f, output[MouthSmile], 1e-4f);
            Assert.AreEqual(1f, output[EyeClose], 1e-4f);
        }

        [Test]
        public void UpdateWeights_SameTriggerOnTwoLayers_TransitionAdvancesOncePerFrame()
        {
            // HID-299: 改修前は同じ入力源が 2 スロットで Tick され、遷移が 2 倍速で進んだ。
            var profile = BuildProfile(smileDuration: 1f);
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, source);

            source.TriggerOn("smile");
            useCase.UpdateWeights(0.5f);

            Assert.AreEqual(0.5f, useCase.GetBlendedOutput()[MouthSmile], 1e-3f);
        }

        [Test]
        public void UpdateWeights_LegacySharedSlotOnTwoLayers_TransitionAdvancesTwice()
        {
            // HID-299 の疑いがコード上で起きていたことの記録（改修前の形）。
            var profile = BuildProfile(smileDuration: 1f);
            var source = new TriggerSource(profile);
            var shared = new LegacySharedSlot(source);
            using var useCase = CreateUseCase(profile, shared, shared);

            source.TriggerOn("smile");
            useCase.UpdateWeights(0.5f);

            Assert.AreEqual(1f, useCase.GetBlendedOutput()[MouthSmile], 1e-3f);
        }

        [Test]
        public void UpdateWeights_BlendLayer_UsesLayerExclusionModeInsteadOfSourceMode()
        {
            // 入力源（InputSystem）は LastWins 固定で作られるが、合成はレイヤーの ExclusionMode に従う。
            var profile = BuildProfile(emotionMode: ExclusionMode.Blend);
            var source = new TriggerSource(profile, ExclusionMode.LastWins);
            using var useCase = CreateUseCase(profile, source, null);

            source.TriggerOn("smile");
            source.TriggerOn("open");
            useCase.UpdateWeights(0.016f);

            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(1f, output[MouthSmile], 1e-4f);
            Assert.AreEqual(0.5f, output[MouthOpen], 1e-4f);
        }

        [Test]
        public void UpdateWeights_ExpressionLayerNotDeclared_OutputsOnDeclaredLayer()
        {
            // 未決 1（2026-11-03 決定）: Layer=eye の表情を emotion だけが宣言する入力源で on にしたら、従来どおり emotion に出す。
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, null);

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("Expression 'eyeclose' のレイヤー 'eye'"));
            source.TriggerOn("eyeclose");
            useCase.UpdateWeights(0.016f);

            Assert.AreEqual(1f, useCase.GetBlendedOutput()[EyeClose], 1e-4f);
        }

        [Test]
        public void UpdateWeights_SingleDeclaringLayer_MatchesLegacySharedSlot()
        {
            // 宣言レイヤーが 1 つで Expression.Layer も一致する Profile（同梱サンプルの形）は、改修前と同じ出力になる。
            var profile = BuildProfile(smileDuration: 0.3f);
            var source = new TriggerSource(profile);
            var legacySource = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, null);
            using var legacy = CreateUseCase(profile, new LegacySharedSlot(legacySource), null);

            var script = new (string op, string id)[]
            {
                ("on", "smile"), ("on", "open"), ("off", "open"), ("on", "open"), ("off", "smile"), ("off", "open"),
            };
            for (int step = 0; step < script.Length; step++)
            {
                if (script[step].op == "on")
                {
                    source.TriggerOn(script[step].id);
                    legacySource.TriggerOn(script[step].id);
                }
                else
                {
                    source.TriggerOff(script[step].id);
                    legacySource.TriggerOff(script[step].id);
                }

                for (int frame = 0; frame < 10; frame++)
                {
                    useCase.UpdateWeights(0.05f);
                    legacy.UpdateWeights(0.05f);
                    CollectionAssert.AreEqual(legacy.GetBlendedOutput(), useCase.GetBlendedOutput(), $"step {step} frame {frame}");
                }
            }
        }

        [Test]
        public void UnbindLateInputSource_ReleasesLayerStack_EyeExpressionFallsBackToRemainingLayer()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, null);
            useCase.BindLateInputSource(1, "input", source, 1f);

            source.TriggerOn("eyeclose");
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
            CollectionAssert.IsEmpty(source.GetActiveExpressionIds(Emotion));

            useCase.UnbindLateInputSource(1, "input");
            source.TriggerOff("eyeclose");
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("Expression 'eyeclose' のレイヤー 'eye'"));
            source.TriggerOn("eyeclose");

            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Emotion));
        }

        [Test]
        public void BindLateInputSource_SameSourceRebound_KeepsLayerStackState()
        {
            var profile = BuildProfile(smileDuration: 1f);
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, null, null);
            useCase.BindLateInputSource(0, "input", source, 1f);
            source.TriggerOn("smile");
            useCase.UpdateWeights(0.5f);

            // registry の再登録通知などで同じ入力源が同じスロットへ再バインドされても、遷移は途中から続く。
            useCase.BindLateInputSource(0, "input", source, 1f);
            useCase.UpdateWeights(0.25f);

            Assert.AreEqual(0.75f, useCase.GetBlendedOutput()[MouthSmile], 1e-3f);
            CollectionAssert.AreEqual(new[] { "smile" }, source.GetActiveExpressionIds(Emotion));
        }

        [Test]
        public void SetProfile_SameProfile_KeepsActiveExpressionsOnEveryLayer()
        {
            // 深さ 1 で emotion / eye に 1 つずつ積んだ状態で作り直しても、一時的な切断で表情を失わない。
            var profile = BuildProfile();
            var source = new TriggerSource(profile, maxStackDepth: 1);
            using var useCase = CreateUseCase(profile, source, source);
            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            useCase.SetProfile(profile, BlendShapeNames);
            useCase.UpdateWeights(0.016f);

            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.ActiveExpressionIds);
            float[] output = useCase.GetBlendedOutput();
            Assert.AreEqual(1f, output[MouthSmile], 1e-4f);
            Assert.AreEqual(1f, output[EyeClose], 1e-4f);
        }

        [Test]
        public void Dispose_ReleasesLayerStacks()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            var useCase = CreateUseCase(profile, source, source);
            source.TriggerOn("smile");
            Assert.AreNotSame(source.ActiveExpressionIds, source.GetActiveExpressionIds(Emotion));

            useCase.Dispose();

            Assert.AreSame(source.ActiveExpressionIds, source.GetActiveExpressionIds(Emotion));
        }

        [Test]
        public void Layer2ActiveExpressionProvider_ReadsLayerStack()
        {
            var profile = BuildProfile();
            var source = new TriggerSource(profile);
            using var useCase = CreateUseCase(profile, source, source);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[]
            {
                (Emotion, (ExpressionTriggerInputSourceBase)source),
                (Eye, (ExpressionTriggerInputSourceBase)source),
            });

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            Assert.AreEqual("smile", provider.TryGetTopActiveExpression(Emotion)?.Id);
            Assert.AreEqual("eyeclose", provider.TryGetTopActiveExpression(Eye)?.Id);
        }
    }
}
