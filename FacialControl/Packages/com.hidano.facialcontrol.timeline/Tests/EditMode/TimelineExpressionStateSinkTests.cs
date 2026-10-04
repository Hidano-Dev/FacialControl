using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineExpressionStateSinkTests : SizedTestFixture
    {
        // ホスト（SkinnedMeshRenderer 側）の BlendShape 名列。Profile の Expression が参照する名前と一致させる。
        private static readonly string[] HostBlendShapeNames = { "smile", "angry", "sad" };

        [Test]
        public void Aggregate_StateSinkBoundAsLayerInputSource_DoesNotThrowAfterTriggerOn()
        {
            FacialProfile profile = BuildProfile();
            var sink = CreateSink();
            using var registry = new LayerInputSourceRegistry(
                profile,
                HostBlendShapeNames.Length,
                new[] { (0, 0, (IInputSource)sink) });
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, HostBlendShapeNames.Length);
            var output = new LayerBlender.LayerInput[registry.LayerCount];

            sink.TriggerOn("smile");

            Assert.DoesNotThrow(() => aggregator.Aggregate(0f, output));
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, output[0].BlendShapeValues.ToArray());
        }

        [Test]
        public void ContributeMask_MatchesHostBlendShapeCount_AndIsAllFalse()
        {
            var sink = CreateSink();

            sink.TriggerOn("smile");

            Assert.That(sink.ContributeMask.Length, Is.EqualTo(HostBlendShapeNames.Length));
            for (int i = 0; i < sink.ContributeMask.Length; i++)
            {
                Assert.That(sink.ContributeMask[i], Is.False, $"ContributeMask[{i}]");
            }
        }

        [Test]
        public void TriggerStack_RetainsBaseLifoSemantics_WithRetriggerAndDepthLimit()
        {
            var sink = CreateSink(maxStackDepth: 2);

            sink.TriggerOn("smile");
            sink.TriggerOn("angry");
            sink.TriggerOn("smile");

            CollectionAssert.AreEqual(
                new[] { "angry", "smile" },
                sink.ActiveExpressionIds);

            sink.TriggerOn("sad");
            sink.TriggerOff("smile");

            CollectionAssert.AreEqual(
                new[] { "sad" },
                sink.ActiveExpressionIds);
        }

        [Test]
        public void TryWriteValues_DoesNotWriteAnyBlendShapeValues_WhenStateIsActive()
        {
            var sink = CreateSink();
            var output = new[] { 0.25f, 0.5f, 0.75f };

            sink.TriggerOn("smile");
            sink.Tick(1.0f);

            bool wrote = sink.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f, 0.75f }, output);
            Assert.That(sink.BlendShapeCount, Is.Zero);
            Assert.That(sink.ContributeMask.Length, Is.EqualTo(HostBlendShapeNames.Length));
        }

        [Test]
        public void ActiveExpressionIds_AreReadableByLayer2ActiveExpressionProvider()
        {
            var sink = CreateSink(exclusionMode: ExclusionMode.LastWins);
            var provider = new Layer2ActiveExpressionProvider(BuildProfile());
            provider.SetSources(new[]
            {
                (layer: "emotion", source: (ExpressionTriggerInputSourceBase)sink),
            });

            sink.TriggerOn("smile");
            sink.TriggerOn("angry");

            Expression? top = provider.TryGetTopActiveExpression("emotion");

            Assert.That(top.HasValue, Is.True);
            Assert.That(top.Value.Id, Is.EqualTo("angry"));
        }

        private static TimelineExpressionStateSink CreateSink(
            int maxStackDepth = 8,
            ExclusionMode exclusionMode = ExclusionMode.LastWins)
        {
            return new TimelineExpressionStateSink(
                InputSourceId.Parse("timeline:emotion"),
                maxStackDepth,
                exclusionMode,
                HostBlendShapeNames,
                BuildProfile());
        }

        private static FacialProfile BuildProfile()
        {
            return new FacialProfile(
                "1.0",
                layers: new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "emotion",
                        transitionDuration: 0.2f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("smile", 1.0f),
                        }),
                    new Expression(
                        id: "angry",
                        name: "Angry",
                        layer: "emotion",
                        transitionDuration: 0.2f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("angry", 1.0f),
                        }),
                    new Expression(
                        id: "sad",
                        name: "Sad",
                        layer: "emotion",
                        transitionDuration: 0.2f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("sad", 1.0f),
                        }),
                });
        }
    }
}
