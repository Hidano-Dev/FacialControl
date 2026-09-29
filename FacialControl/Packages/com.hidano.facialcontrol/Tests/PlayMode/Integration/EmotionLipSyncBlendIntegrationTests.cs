using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// 旧 per-layer playable / mixer 直叩きの回帰観点を、
    /// ライブ経路 (ExpressionUseCase -> LayerUseCase.UpdateWeights -> BlendedOutputSpan)
    /// に付け替えて検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class EmotionLipSyncBlendIntegrationTests : SizedTestFixture
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void EmotionAndLipSync_MixedSources_BlendByPriorityMaskAndWeightedSum()
        {
            var blendShapeNames = new[]
            {
                "EyeBlinkLeft",
                "BrowDownLeft",
                "MouthSmileLeft",
                "MouthA",
                "CheekPuff",
            };

            using var harness = CreateHarness(
                blendShapeNames,
                new[]
                {
                    CreateExpression(
                        "emotion-angry",
                        "emotion",
                        0f,
                        TransitionCurve.Linear,
                        ("EyeBlinkLeft", 0.70f),
                        ("BrowDownLeft", 0.60f),
                        ("MouthSmileLeft", 0.20f)),
                },
                new List<(int layerIdx, IInputSource source, float weight)>
                {
                    (1, new MaskedValueProviderSource(
                        "lipsync-a",
                        blendShapeNames.Length,
                        new[] { 1f, 1f, 0.10f, 0.90f, 1f },
                        new[] { 2, 3 }), 0.50f),
                    (1, new MaskedValueProviderSource(
                        "lipsync-b",
                        blendShapeNames.Length,
                        new[] { 1f, 1f, 0.60f, 0.80f, 1f },
                        new[] { 2, 3 }), 0.75f),
                });

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);

            AssertOutput(harness.LayerUseCase,
                0.70f,
                0.60f,
                0.50f,
                1.00f,
                0.00f);
        }

        [UnityTest]
        public IEnumerator LipSyncWeightUpdate_FromBackgroundThread_ObservedOnNextUpdate()
        {
            var blendShapeNames = new[]
            {
                "EyeBlinkLeft",
                "BrowDownLeft",
                "MouthSmileLeft",
                "MouthA",
            };

            using var harness = CreateHarness(
                blendShapeNames,
                new[]
                {
                    CreateExpression(
                        "emotion-angry",
                        "emotion",
                        0f,
                        TransitionCurve.Linear,
                        ("EyeBlinkLeft", 0.70f),
                        ("BrowDownLeft", 0.60f),
                        ("MouthSmileLeft", 0.20f)),
                },
                new List<(int layerIdx, IInputSource source, float weight)>
                {
                    (1, new MaskedValueProviderSource(
                        "lipsync-a",
                        blendShapeNames.Length,
                        new[] { 1f, 1f, 0.40f, 0.90f },
                        new[] { 2, 3 }), 0.00f),
                });

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            AssertOutput(harness.LayerUseCase, 0.70f, 0.60f, 0.00f, 0.00f);

            int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int? backgroundThreadId = null;
            var task = Task.Run(() =>
            {
                backgroundThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                harness.LayerUseCase.SetInputSourceWeight(1, sourceIdx: 1, weight: 0.75f);
            });
            task.Wait();

            Assert.That(backgroundThreadId.HasValue, Is.True);
            Assert.That(backgroundThreadId.Value, Is.Not.EqualTo(mainThreadId));

            yield return null;

            harness.LayerUseCase.UpdateWeights(0.016f);
            AssertOutput(harness.LayerUseCase, 0.70f, 0.60f, 0.30f, 0.675f);
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

        private static TestHarness CreateHarness(
            string[] blendShapeNames,
            Expression[] expressions,
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalInputSources)
        {
            var profile = new FacialProfile(
                "1.0.0",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                    new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                },
                expressions);
            var expressionUseCase = new ExpressionUseCase(profile);
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additionalInputSources);
            return new TestHarness(profile, expressionUseCase, layerUseCase);
        }

        private static Expression CreateExpression(
            string id,
            string layer,
            float duration,
            TransitionCurve curve,
            params (string name, float value)[] blendShapes)
        {
            var mappings = new BlendShapeMapping[blendShapes.Length];
            for (int i = 0; i < blendShapes.Length; i++)
            {
                mappings[i] = new BlendShapeMapping(blendShapes[i].name, blendShapes[i].value);
            }

            return new Expression(id, id, layer, duration, curve, mappings);
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
                int len = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < len; i++)
                {
                    output[i] = _values[i];
                }

                return true;
            }
        }
    }
}
