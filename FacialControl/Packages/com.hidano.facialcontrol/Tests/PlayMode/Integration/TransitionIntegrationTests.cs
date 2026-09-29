using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// 旧 PlayableGraph 直接テストを、現行のライブ更新経路
    /// (ExpressionUseCase -> LayerUseCase.UpdateWeights -> BlendedOutputSpan)
    /// へ付け替えた遷移補間の非回帰テスト。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class TransitionIntegrationTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        [Test]
        public void LastWins_ZeroDuration_ImmediateSwitch()
        {
            using var harness = CreateHarness(
                new[] { "smile", "frown" },
                CreateExpression("happy", "emotion", 0f, TransitionCurve.Linear, ("smile", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);

            AssertOutput(harness.LayerUseCase, 1.0f, 0.0f);
        }

        [Test]
        public void LastWins_LinearTransition_MidpointValues()
        {
            using var harness = CreateHarness(
                new[] { "smile", "frown" },
                CreateExpression("happy", "emotion", 0.5f, TransitionCurve.Linear,
                    ("smile", 1.0f),
                    ("frown", 0.6f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.25f);

            AssertOutput(harness.LayerUseCase, 0.5f, 0.3f);
        }

        [Test]
        public void LastWins_SequentialExpressions_CrossfadesFromPrevious()
        {
            using var harness = CreateHarness(
                new[] { "smile", "frown" },
                CreateExpression("happy", "emotion", 0f, TransitionCurve.Linear, ("smile", 1.0f)),
                CreateExpression("sad", "emotion", 0.5f, TransitionCurve.Linear, ("frown", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[1]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.25f);

            AssertOutput(harness.LayerUseCase, 0.5f, 0.5f);
        }

        [Test]
        public void TransitionInterrupt_DuringTransition_SnapshotsCurrentAndStartsNew()
        {
            using var harness = CreateHarness(
                new[] { "smile", "frown" },
                CreateExpression("happy", "emotion", 1.0f, TransitionCurve.Linear, ("smile", 1.0f)),
                CreateExpression("surprised", "emotion", 1.0f, TransitionCurve.Linear, ("frown", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.5f);

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[1]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.5f);

            AssertOutput(harness.LayerUseCase, 0.25f, 0.5f);
        }

        [Test]
        public void TransitionCurve_EaseInOut_MidpointIsHalf()
        {
            using var harness = CreateHarness(
                new[] { "blend" },
                CreateExpression("ease", "emotion", 1.0f, new TransitionCurve(TransitionCurveType.EaseInOut), ("blend", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.5f);

            AssertOutput(harness.LayerUseCase, 0.5f);
        }

        [Test]
        public void TransitionCurve_EaseIn_MidpointIsBelowHalf()
        {
            using var harness = CreateHarness(
                new[] { "blend" },
                CreateExpression("ease-in", "emotion", 1.0f, new TransitionCurve(TransitionCurveType.EaseIn), ("blend", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.5f);

            Assert.Less(harness.LayerUseCase.BlendedOutputSpan[0], 0.5f);
        }

        [Test]
        public void TransitionCurve_EaseOut_MidpointIsAboveHalf()
        {
            using var harness = CreateHarness(
                new[] { "blend" },
                CreateExpression("ease-out", "emotion", 1.0f, new TransitionCurve(TransitionCurveType.EaseOut), ("blend", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.5f);

            Assert.Greater(harness.LayerUseCase.BlendedOutputSpan[0], 0.5f);
        }

        [Test]
        public void TransitionDuration_QuarterSecond_CompletesAtConfiguredDuration()
        {
            using var harness = CreateHarness(
                new[] { "blend" },
                CreateExpression("quarter", "emotion", 0.25f, TransitionCurve.Linear, ("blend", 1.0f)));

            harness.ExpressionUseCase.Activate(harness.Profile.Expressions.Span[0]);
            harness.LayerUseCase.UpdateWeights(0f);
            harness.LayerUseCase.UpdateWeights(0.125f);
            Assert.That(harness.LayerUseCase.BlendedOutputSpan[0], Is.EqualTo(0.5f).Within(Tolerance));

            harness.LayerUseCase.UpdateWeights(0.125f);
            AssertOutput(harness.LayerUseCase, 1.0f);
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

        private static TestHarness CreateHarness(string[] blendShapeNames, params Expression[] expressions)
        {
            var profile = new FacialProfile(
                "1.0.0",
                new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                expressions);
            var expressionUseCase = new ExpressionUseCase(profile);
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);
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

            return new Expression(
                id,
                id,
                layer,
                duration,
                curve,
                mappings);
        }

        private readonly struct TestHarness : System.IDisposable
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
    }
}
