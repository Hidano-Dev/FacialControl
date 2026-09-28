using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Integration
{
    /// <summary>
    /// Expression の phoneme Override / Suppress が、実経路
    /// (<see cref="LayerUseCase"/> + Aggregator の加重和) で
    /// <see cref="LipSyncPhonemeOverlayInputSource"/> の既定出力へ正しく作用することを検証する。
    /// </summary>
    /// <remarks>
    /// Override のセマンティクスは「音素の口形状 snapshot の差し替え」であり、
    /// 静的なオーバーレイ出力ではない。駆動 weight（音素 weight × 音量）は
    /// 既定 snapshot と同じ値を使い回すため、無音時は override 中でも出力されない。
    /// あわせて予約音素 slot の <see cref="OverlayInputSource"/> が静的出力を
    /// しないこと（「表情中ずっと 100% 出力」の禁止）も固定する。
    /// precedence: Expression Override → Suppress → DefaultOverlays → LipSync default。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class PhonemeOverlayPreemptionTests : SizedTestFixture
    {
        private const float Tolerance = 0.0001f;
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string SlotA = "a";
        private const string PhonemeA = "A";
        private const string MouthA = "Mouth_A";
        private const string MouthI = "Mouth_I";
        private const float LipSyncDefaultValue = 0.85f;
        private const float OverrideValue = 0.6f;

        private static readonly string[] BlendShapeNames = { MouthA, MouthI };

        [Test]
        public void ActiveExpressionOverride_FullDrive_SubstitutesSnapshot()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(1f, (PhonemeA, 1f));

            harness.LayerUseCase.UpdateWeights(0f);

            // Override active 中は既定 snapshot (0.85) の代わりに override 形状が
            // 同じ駆動 weight (1×1) で出力される。加算されないこと。
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(OverrideValue).Within(Tolerance),
                "Override active 中は override snapshot が既定 snapshot を置き換えるべき（加算されてはならない）。");
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[1], Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ActiveExpressionOverride_ReusesPhonemeDriveWeight()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(1f, (PhonemeA, 0.5f));

            harness.LayerUseCase.UpdateWeights(0f);

            // 駆動 weight は「元の a に流れてきた値」（phonemeWeight × volume）を使い回す。
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(OverrideValue * 0.5f).Within(Tolerance),
                "override 形状は既定 snapshot と同じ駆動 weight（phonemeWeight×volume）でスケールされるべき。");
        }

        [Test]
        public void ActiveExpressionOverride_SilentFrame_ProducesNoOutput()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(0f, (PhonemeA, 1f));

            harness.LayerUseCase.UpdateWeights(0f);

            // Override は静的オーバーレイではない。無音（volume=0）なら口は閉じたまま。
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(0f).Within(Tolerance),
                "無音フレームでは override 中でも出力されないべき（表情中ずっと 100% 出力の禁止）。");
        }

        [Test]
        public void ActiveExpressionSuppress_BlocksLipSyncDefaultOutput()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: true, snapshot: null),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(1f, (PhonemeA, 1f));

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(0f).Within(Tolerance),
                "Suppress active 中は当該 slot のリップシンク出力が完全に止まるべき。");
        }

        [Test]
        public void ActiveExpressionEmptySnapshotBinding_FallsBackToLipSyncDefault()
        {
            // Inspector 未設定のまま出力された「suppress=false + 空 snapshot」の binding は
            // default fallback として扱い、既定出力を差し替えない（全滅事故の防止）。
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: EmptySnapshot()),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(1f, (PhonemeA, 1f));

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(LipSyncDefaultValue).Within(Tolerance),
                "空 snapshot の binding は default fallback 扱いで、lipsync 既定出力が出るべき。");
        }

        [Test]
        public void NoActiveExpression_DefaultOverlayOverride_SubstitutesSnapshotWithDrive()
        {
            FacialProfile profile = BuildProfile(
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = null;
            harness.WeightSource.SetFrame(1f, (PhonemeA, 0.5f));

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(OverrideValue * 0.5f).Within(Tolerance),
                "DefaultOverlays の override も駆動 weight でスケールされた差し替えとして出力されるべき。");
        }

        [Test]
        public void NoOverlayBinding_LipSyncDefaultOutputIsPreserved()
        {
            FacialProfile profile = BuildProfile();
            using Harness harness = CreateHarness(profile);
            harness.ActiveProvider.Active = profile.FindExpressionById("smile");
            harness.WeightSource.SetFrame(1f, (PhonemeA, 1f));

            harness.LayerUseCase.UpdateWeights(0f);

            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(LipSyncDefaultValue).Within(Tolerance),
                "overlay binding が無い場合は lipsync 既定出力がそのまま出るべき。");
        }

        [Test]
        public void ReservedPhonemeSlot_OverlayInputSource_NeverWritesStatically()
        {
            FacialProfile profile = BuildProfile(
                smileOverlays: new[]
                {
                    new OverlaySlotBinding(SlotA, suppress: false, snapshot: OverrideSnapshot(OverrideValue)),
                });
            var activeProvider = new StubActiveExpressionProvider
            {
                Active = profile.FindExpressionById("smile"),
            };

            var overlaySource = new OverlayInputSource(
                InputSourceId.Parse($"{OverlayInputSource.ReservedIdPrefix}:{SlotA}"),
                SlotA,
                BlendShapeNames.Length,
                BlendShapeNames,
                profile,
                activeProvider,
                EmotionLayer);

            Span<float> output = stackalloc float[BlendShapeNames.Length];
            overlaySource.Tick(0f);
            bool wrote = overlaySource.TryWriteValues(output);

            Assert.That(wrote, Is.False,
                "予約音素 slot の OverlayInputSource は静的出力してはならない（差し替えは lipsync-overlay 側が行う）。");
        }

        private Harness CreateHarness(FacialProfile profile)
        {
            var activeProvider = new StubActiveExpressionProvider();
            var weightSource = new FakePhonemeWeightSource();

            var provider = new ULipSyncProvider(
                weightSource,
                new[] { new PhonemeSnapshot(PhonemeA, new[] { LipSyncDefaultValue, 0f }) },
                BlendShapeNames.Length);

            var overlaySource = new OverlayInputSource(
                InputSourceId.Parse($"{OverlayInputSource.ReservedIdPrefix}:{SlotA}"),
                SlotA,
                BlendShapeNames.Length,
                BlendShapeNames,
                profile,
                activeProvider,
                EmotionLayer);

            var lipSyncSource = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}"),
                PhonemeA,
                provider,
                BlendShapeNames.Length,
                SlotA,
                BlendShapeNames,
                profile,
                activeProvider,
                EmotionLayer);

            var expressionUseCase = new ExpressionUseCase(profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, overlaySource, 1f),
                (0, lipSyncSource, 1f),
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames, additional);

            return new Harness(provider, layerUseCase, activeProvider, weightSource);
        }

        private static FacialProfile BuildProfile(
            OverlaySlotBinding[] smileOverlays = null,
            OverlaySlotBinding[] defaultOverlays = null)
        {
            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: EmotionLayer,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: smileOverlays);

            return new FacialProfile(
                "1.0",
                new[] { new LayerDefinition(OverlayLayer, 0, ExclusionMode.Blend) },
                expressions: new[] { smile },
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[]
                    {
                        new InputSourceDeclaration($"{OverlayInputSource.ReservedIdPrefix}:{SlotA}", 1f, null),
                        new InputSourceDeclaration($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}", 1f, null),
                    },
                },
                defaultOverlays: defaultOverlays,
                slots: PhonemeOverlaySlots.ReservedNames.ToArray());
        }

        private static ExpressionSnapshot OverrideSnapshot(float value)
        {
            return new ExpressionSnapshot(
                id: "override-a",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[] { new BlendShapeSnapshot(string.Empty, MouthA, value) },
                bones: null,
                rendererPaths: null);
        }

        private static ExpressionSnapshot EmptySnapshot()
        {
            return new ExpressionSnapshot(
                id: "empty-a",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: null);
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                ULipSyncProvider provider,
                LayerUseCase layerUseCase,
                StubActiveExpressionProvider activeProvider,
                FakePhonemeWeightSource weightSource)
            {
                Provider = provider;
                LayerUseCase = layerUseCase;
                ActiveProvider = activeProvider;
                WeightSource = weightSource;
            }

            public ULipSyncProvider Provider { get; }

            public LayerUseCase LayerUseCase { get; }

            public StubActiveExpressionProvider ActiveProvider { get; }

            public FakePhonemeWeightSource WeightSource { get; }

            public void Dispose()
            {
                LayerUseCase.Dispose();
                Provider.Dispose();
            }
        }

        private sealed class StubActiveExpressionProvider : IActiveExpressionProvider
        {
            public Expression? Active;

            public Expression? TryGetTopActiveExpression(string layerName)
            {
                return Active;
            }
        }
    }
}
