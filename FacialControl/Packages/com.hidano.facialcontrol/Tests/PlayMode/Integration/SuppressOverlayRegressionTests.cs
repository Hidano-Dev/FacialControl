using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public sealed class SuppressOverlayRegressionTests : SizedTestFixture
    {
        private const float Tolerance = 0.0001f;
        private const string EmotionLayer = "emotion";
        private const string OverlayLayer = "overlay";
        private const string BlinkSlot = "blink";
        private const string BrowName = "bs_brow";
        private const string EyeMakeupName = "bs_eye_lift";
        private const string EyeBlinkName = "bs_eye_blink";
        private const string MouthName = "bs_mouth";

        [Test]
        public void LayerOverrideMask_ActiveExpression_SuppressesTargetLayer()
        {
            var blendShapeNames = CreateBlendShapeNames();
            var profile = new FacialProfile(
                "1.0",
                new[]
                {
                    new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                    new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
                },
                new[]
                {
                    new Expression(
                        "smile",
                        "Smile",
                        EmotionLayer,
                        0f,
                        TransitionCurve.Linear,
                        new[] { new BlendShapeMapping(BrowName, 1f) },
                        null,
                        LayerOverrideMask.Bit1),
                    new Expression(
                        "blink",
                        "Blink",
                        OverlayLayer,
                        0f,
                        TransitionCurve.Linear,
                        new[] { new BlendShapeMapping(EyeBlinkName, 1f) }),
                });

            var expressionUseCase = new ExpressionUseCase(profile);
            var triggerSource = new FakeTriggerSource(profile, blendShapeNames);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, triggerSource, 1f),
            };
            using var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);
            expressionUseCase.Activate(profile.FindExpressionById("smile").Value);
            expressionUseCase.Activate(profile.FindExpressionById("blink").Value);
            triggerSource.TriggerOn("smile");

            layerUseCase.UpdateWeights(0f);

            AssertOutput(layerUseCase, 1f, 0f, 0f, 0f);
        }

        [Test]
        public void OverlaySnapshot_FullWeight_ReplacesBlinkSlot()
        {
            var profile = BuildOverlayProfile();
            using var harness = CreateOverlayHarness(profile);

            harness.ExpressionUseCase.Activate(profile.FindExpressionById("smile").Value);
            harness.LayerUseCase.SetLayerWeight(OverlayLayer, 1f);
            harness.LayerUseCase.UpdateWeights(0f);

            AssertOutput(harness.LayerUseCase, 1f, 0f, 1f, 0.5f);
        }

        [Test]
        public void OverlaySuppress_FullWeight_KeepsBaseExpressionOutput()
        {
            var profile = BuildOverlayProfile();
            using var harness = CreateOverlayHarness(profile);

            harness.ExpressionUseCase.Activate(profile.FindExpressionById("smile_closed_eye").Value);
            harness.LayerUseCase.SetLayerWeight(OverlayLayer, 1f);
            harness.LayerUseCase.UpdateWeights(0f);

            AssertOutput(harness.LayerUseCase, 0f, 0.75f, 1f, 0.5f);
        }

        private static TestHarness CreateOverlayHarness(FacialProfile profile)
        {
            var blendShapeNames = CreateBlendShapeNames();
            var expressionUseCase = new ExpressionUseCase(profile);
            var overlayInputSource = new OverlayInputSource(
                InputSourceId.Parse("overlay:blink"),
                BlinkSlot,
                blendShapeNames.Length,
                blendShapeNames,
                profile,
                expressionUseCase,
                EmotionLayer);

            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (1, overlayInputSource, 1f),
            };

            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);
            return new TestHarness(profile, expressionUseCase, layerUseCase);
        }

        private static FacialProfile BuildOverlayProfile()
        {
            var smileBlinkSnapshot = CreateSnapshot(
                "smile_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1f));

            var defaultBlinkSnapshot = CreateSnapshot(
                "default_blink_snapshot",
                new BlendShapeSnapshot(string.Empty, EyeMakeupName, 0f),
                new BlendShapeSnapshot(string.Empty, EyeBlinkName, 1f));

            return new FacialProfile(
                "1.0",
                new[]
                {
                    new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                    new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
                },
                new[]
                {
                    new Expression(
                        "smile",
                        "Smile",
                        EmotionLayer,
                        0f,
                        TransitionCurve.Linear,
                        new[]
                        {
                            new BlendShapeMapping(BrowName, 1f),
                            new BlendShapeMapping(EyeMakeupName, 1f),
                            new BlendShapeMapping(MouthName, 0.5f),
                        },
                        new[]
                        {
                            new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: smileBlinkSnapshot),
                        }),
                    new Expression(
                        "smile_closed_eye",
                        "SmileClosedEye",
                        EmotionLayer,
                        0f,
                        TransitionCurve.Linear,
                        new[]
                        {
                            new BlendShapeMapping(EyeMakeupName, 0.75f),
                            new BlendShapeMapping(EyeBlinkName, 1f),
                            new BlendShapeMapping(MouthName, 0.5f),
                        },
                        new[]
                        {
                            new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                        }),
                },
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration("input", 1f, null) },
                    new[] { new InputSourceDeclaration("input:overlay:blink", 1f, null) },
                },
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlinkSnapshot),
                },
                slots: new[] { BlinkSlot });
        }

        private static ExpressionSnapshot CreateSnapshot(string id, params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot(
                id,
                Expression.DefaultTransitionDuration,
                TransitionCurvePreset.Linear,
                blendShapes,
                bones: null,
                rendererPaths: null);
        }

        private static string[] CreateBlendShapeNames()
        {
            return new[] { BrowName, EyeMakeupName, EyeBlinkName, MouthName };
        }

        private static void AssertOutput(LayerUseCase useCase, params float[] expected)
        {
            var actual = useCase.BlendedOutputSpan;
            Assert.That(actual.Length, Is.EqualTo(expected.Length));

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Tolerance),
                    $"BlendShape index {i} did not match.");
            }
        }

        private readonly struct TestHarness : IDisposable
        {
            public TestHarness(FacialProfile profile, ExpressionUseCase expressionUseCase, LayerUseCase layerUseCase)
            {
                Profile = profile;
                ExpressionUseCase = expressionUseCase;
                LayerUseCase = layerUseCase;
            }

            public FacialProfile Profile { get; }
            public ExpressionUseCase ExpressionUseCase { get; }
            public LayerUseCase LayerUseCase { get; }

            public void Dispose()
            {
                LayerUseCase?.Dispose();
            }
        }

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
    }
}
