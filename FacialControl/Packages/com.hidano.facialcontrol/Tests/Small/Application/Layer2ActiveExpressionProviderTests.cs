using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// <see cref="Layer2ActiveExpressionProvider"/> が系2(<see cref="ExpressionTriggerInputSourceBase"/>)の
    /// TriggerOn/Off 状態から active 表情を解決することを検証する。
    /// </summary>
    [TestFixture]
    public class Layer2ActiveExpressionProviderTests
    {
        private const string EmotionLayer = "emotion";
        private static readonly string[] BlendShapeNames = { "bs_a", "bs_b" };

        // 系2 の具象は別アセンブリ(InputSystem)にあるため、テストでは基底を継承した Fake を使う。
        private sealed class FakeTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeTriggerSource(FacialProfile profile)
                : base(
                    InputSourceId.Parse("input"),
                    BlendShapeNames.Length,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: BlendShapeNames,
                    profile: profile)
            {
            }
        }

        private static FacialProfile BuildProfile()
        {
            var smile = new Expression(
                "smile", "Smile", EmotionLayer, 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_a", 1f, null) });
            var anger = new Expression(
                "anger", "Anger", EmotionLayer, 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_b", 1f, null) });
            return new FacialProfile(
                "1.0",
                new[] { new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins) },
                new[] { smile, anger });
        }

        [Test]
        public void TryGetTopActiveExpression_系2にTriggerOnした表情を返す()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)trigger) });

            trigger.TriggerOn("smile");

            var active = provider.TryGetTopActiveExpression(EmotionLayer);
            Assert.IsTrue(active.HasValue);
            Assert.AreEqual("smile", active.Value.Id);
        }

        [Test]
        public void TryGetTopActiveExpression_TriggerOnなし_Null()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)trigger) });

            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue);
        }

        [Test]
        public void TryGetTopActiveExpression_SetSources前_Null()
        {
            var profile = BuildProfile();
            var provider = new Layer2ActiveExpressionProvider(profile);

            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue);
        }

        [Test]
        public void TryGetTopActiveExpression_別レイヤー指定_Null()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)trigger) });
            trigger.TriggerOn("smile");

            Assert.IsFalse(provider.TryGetTopActiveExpression("overlay").HasValue);
        }

        [Test]
        public void TryGetTopActiveExpression_TriggerOffで空に戻ると_Null()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)trigger) });

            trigger.TriggerOn("smile");
            trigger.TriggerOff("smile");

            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue);
        }

        [Test]
        public void TryGetTopActiveExpression_LastWins_最新TriggerOnを返す()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)trigger) });

            trigger.TriggerOn("smile");
            trigger.TriggerOn("anger");

            var active = provider.TryGetTopActiveExpression(EmotionLayer);
            Assert.IsTrue(active.HasValue);
            Assert.AreEqual("anger", active.Value.Id, "スタック末尾(最新 TriggerOn)が top");
        }
    }
}
