using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode
{
    [TestFixture]
    [MediumTest]
    public sealed class LipSyncPhonemeOverlayRegressionTests : SizedTestFixture
    {
        private const float Tolerance = 0.0001f;
        private const string OverlayLayer = "overlay";
        private const string SlotA = "a";
        private const string PhonemeA = "A";
        private static readonly string[] BlendShapeNames = { "Mouth_A", "Mouth_I" };

        [Test]
        public void LipSyncOverlayA_FullWeight_ReachesConfiguredMaximumBlendShape()
        {
            var profile = new FacialProfile(
                "1.0",
                new[] { new LayerDefinition(OverlayLayer, 0, ExclusionMode.Blend) },
                expressions: Array.Empty<Expression>(),
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}", 1f, null) },
                },
                defaultOverlays: null,
                slots: PhonemeOverlaySlots.ReservedNames.ToArray());

            var weightSource = new FakePhonemeWeightSource();
            weightSource.SetFrame(1f, (PhonemeA, 1f));

            using var provider = new ULipSyncProvider(
                weightSource,
                new[]
                {
                    new PhonemeSnapshot(PhonemeA, new[] { 0.85f, 0f }),
                },
                BlendShapeNames.Length);

            var source = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{SlotA}"),
                PhonemeA,
                provider,
                BlendShapeNames.Length);

            var expressionUseCase = new ExpressionUseCase(profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, source, 1f),
            };

            using var layerUseCase = new LayerUseCase(profile, expressionUseCase, BlendShapeNames, additional);
            layerUseCase.UpdateWeights(0f);

            var output = layerUseCase.BlendedOutputSpan;
            Assert.That(output[0], Is.EqualTo(0.85f).Within(Tolerance));
            Assert.That(output[1], Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
