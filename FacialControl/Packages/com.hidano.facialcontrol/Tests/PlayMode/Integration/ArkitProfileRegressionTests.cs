using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class ArkitProfileRegressionTests : SizedTestFixture
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void DetectAndGenerate_ArkitPerfectSyncProfile_ExactMatchesDriveOutputAndUnknownNamesStayZero()
        {
            var blendShapeNames = new[]
            {
                "eyeBlinkLeft",
                "eyeBlinkRight",
                "jawOpen",
                "tongueUp",
                "customSmile",
            };

            using var harness = CreateHarness(blendShapeNames);

            CollectionAssert.AreEquivalent(
                new[] { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "tongueUp" },
                harness.DetectResult.DetectedNames);

            harness.ExpressionUseCase.Activate(FindExpressionByLayer(harness.Profile, "eye"));
            harness.ExpressionUseCase.Activate(FindExpressionByLayer(harness.Profile, "mouth"));

            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(Expression.DefaultTransitionDuration);

            AssertOutput(harness.LayerUseCase, 1f, 1f, 1f, 1f, 0f);
        }

        [Test]
        public void GeneratedProfile_DefaultTransition_PreservesHalfwayInterpolation()
        {
            var blendShapeNames = new[]
            {
                "jawOpen",
                "mouthSmileLeft",
                "customShape",
            };

            using var harness = CreateHarness(blendShapeNames);

            harness.ExpressionUseCase.Activate(FindExpressionByLayer(harness.Profile, "mouth"));

            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(Expression.DefaultTransitionDuration * 0.5f);

            AssertOutput(harness.LayerUseCase, 0.5f, 0.5f, 0f);
        }

        [Test]
        public void GeneratedProfile_WithLipSyncLayer_KeepsArkitOutputAndBlendsAdditionalSource()
        {
            var blendShapeNames = new[]
            {
                "eyeBlinkLeft",
                "jawOpen",
                "tongueUp",
            };

            var lipsyncValues = new[] { 0f, 0.90f, 0.60f };
            using var harness = CreateHarness(
                blendShapeNames,
                additionalInputSources: new List<(int layerIdx, IInputSource source, float weight)>
                {
                    (2, new MaskedValueProviderSource(
                        "lipsync",
                        blendShapeNames.Length,
                        lipsyncValues,
                        new[] { 1, 2 }), 0.5f),
                },
                extraLayers: new[]
                {
                    new LayerDefinition("lipsync", 2, ExclusionMode.Blend),
                });

            harness.ExpressionUseCase.Activate(FindExpressionByLayer(harness.Profile, "eye"));

            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(Expression.DefaultTransitionDuration);

            AssertOutput(harness.LayerUseCase, 1f, 0.45f, 0.30f);
        }

        private static TestHarness CreateHarness(
            string[] blendShapeNames,
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalInputSources = null,
            LayerDefinition[] extraLayers = null)
        {
            var useCase = new ARKitUseCase();
            var detectResult = useCase.DetectAndGenerate(blendShapeNames);
            var profile = new FacialProfile(
                "1.0.0",
                BuildLayers(detectResult.GeneratedExpressions, extraLayers),
                detectResult.GeneratedExpressions);
            var expressionUseCase = new ExpressionUseCase(profile);
            var layerUseCase = new LayerUseCase(
                profile,
                expressionUseCase,
                blendShapeNames,
                additionalInputSources);

            return new TestHarness(detectResult, profile, expressionUseCase, layerUseCase);
        }

        private static LayerDefinition[] BuildLayers(Expression[] expressions, LayerDefinition[] extraLayers)
        {
            var layers = new List<LayerDefinition>();
            for (int i = 0; i < expressions.Length; i++)
            {
                string layer = expressions[i].Layer;
                if (ContainsLayer(layers, layer))
                {
                    continue;
                }

                layers.Add(new LayerDefinition(layer, layers.Count, ExclusionMode.LastWins));
            }

            if (extraLayers != null)
            {
                for (int i = 0; i < extraLayers.Length; i++)
                {
                    if (!ContainsLayer(layers, extraLayers[i].Name))
                    {
                        layers.Add(extraLayers[i]);
                    }
                }
            }

            return layers.ToArray();
        }

        private static bool ContainsLayer(List<LayerDefinition> layers, string layerName)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (string.Equals(layers[i].Name, layerName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Expression FindExpressionByLayer(FacialProfile profile, string layer)
        {
            var expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                if (string.Equals(expressions[i].Layer, layer, StringComparison.Ordinal))
                {
                    return expressions[i];
                }
            }

            throw new AssertionException($"Layer '{layer}' の生成 Expression が見つかりません。");
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
            public TestHarness(
                ARKitUseCase.DetectResult detectResult,
                FacialProfile profile,
                ExpressionUseCase expressionUseCase,
                LayerUseCase layerUseCase)
            {
                DetectResult = detectResult;
                Profile = profile;
                ExpressionUseCase = expressionUseCase;
                LayerUseCase = layerUseCase;
            }

            public ARKitUseCase.DetectResult DetectResult { get; }
            public FacialProfile Profile { get; }
            public ExpressionUseCase ExpressionUseCase { get; }
            public LayerUseCase LayerUseCase { get; }

            public void Dispose()
            {
                LayerUseCase?.Dispose();
            }
        }

        private sealed class MaskedValueProviderSource : ValueProviderInputSourceBase
        {
            private readonly float[] _values;

            public MaskedValueProviderSource(
                string id,
                int blendShapeCount,
                float[] values,
                int[] contributeIndices)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _values = values ?? throw new ArgumentNullException(nameof(values));
                ContributeMask = new BitArray(blendShapeCount, false);
                for (int i = 0; i < contributeIndices.Length; i++)
                {
                    int index = contributeIndices[i];
                    if ((uint)index < (uint)blendShapeCount)
                    {
                        ContributeMask[index] = true;
                    }
                }
            }

            public override BitArray ContributeMask { get; }

            public override bool TryWriteValues(Span<float> output)
            {
                int length = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < length; i++)
                {
                    output[i] = _values[i];
                }

                return true;
            }
        }
    }
}
