using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// <see cref="Layer2ActiveExpressionProvider"/> が系2(<see cref="ExpressionTriggerInputSourceBase"/>)の
    /// TriggerOn/Off 状態から active 表情を解決することを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class Layer2ActiveExpressionProviderTests : SizedTestFixture
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

        // --- 単一 source の増減 (AddSource / RemoveSource) ---

        [Test]
        public void AddSource_TriggerOnAfterAdd_ResolvesTopActiveExpression()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);

            provider.AddSource(EmotionLayer, trigger);
            trigger.TriggerOn("smile");

            var active = provider.TryGetTopActiveExpression(EmotionLayer);
            Assert.IsTrue(active.HasValue);
            Assert.AreEqual("smile", active.Value.Id);
        }

        [Test]
        public void AddSource_KeepsSourcesSetBySetSources()
        {
            var profile = BuildProfile();
            var first = new FakeTriggerSource(profile);
            var second = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.SetSources(new[] { (EmotionLayer, (ExpressionTriggerInputSourceBase)first) });

            provider.AddSource(EmotionLayer, second);
            first.TriggerOn("smile");

            var active = provider.TryGetTopActiveExpression(EmotionLayer);
            Assert.IsTrue(active.HasValue, "AddSource は一括設定済みの source を置き換えないこと");
            Assert.AreEqual("smile", active.Value.Id);
        }

        [Test]
        public void AddSource_SamePairTwice_SingleRemoveClearsResolution()
        {
            // 同 (layer, source) は重複追加しない: 1 回の RemoveSource で完全に消えることで検証する。
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);

            provider.AddSource(EmotionLayer, trigger);
            provider.AddSource(EmotionLayer, trigger);
            trigger.TriggerOn("smile");

            Assert.IsTrue(provider.RemoveSource(EmotionLayer, trigger));
            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue,
                "重複追加されていれば 1 回の削除では残るため、ここで null になることが重複なしの証明");
            Assert.IsFalse(provider.RemoveSource(EmotionLayer, trigger), "2 回目の削除は対象なしで false");
        }

        [Test]
        public void AddSource_SameSourceOnDifferentLayers_RegisteredPerLayer()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);

            provider.AddSource(EmotionLayer, trigger);
            provider.AddSource("overlay", trigger);
            trigger.TriggerOn("smile");

            Assert.IsTrue(provider.RemoveSource("overlay", trigger));
            Assert.IsTrue(provider.TryGetTopActiveExpression(EmotionLayer).HasValue,
                "別レイヤーの登録は独立しており、overlay 側の削除で emotion 側は消えないこと");
        }

        [Test]
        public void RemoveSource_Registered_ReturnsTrueAndStopsResolving()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.AddSource(EmotionLayer, trigger);
            trigger.TriggerOn("smile");
            Assert.IsTrue(provider.TryGetTopActiveExpression(EmotionLayer).HasValue);

            bool removed = provider.RemoveSource(EmotionLayer, trigger);

            Assert.IsTrue(removed);
            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue,
                "削除後は TriggerOn 状態でも解決されないこと");
        }

        [Test]
        public void RemoveSource_NotRegistered_ReturnsFalse()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var other = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);
            provider.AddSource(EmotionLayer, trigger);

            Assert.IsFalse(provider.RemoveSource(EmotionLayer, other), "未登録 source は false");
            Assert.IsFalse(provider.RemoveSource("overlay", trigger), "別レイヤーの組は false");
            Assert.IsFalse(provider.RemoveSource(EmotionLayer, null), "null は false");
        }

        [Test]
        public void AddSource_NullSourceOrEmptyLayer_Ignored()
        {
            var profile = BuildProfile();
            var trigger = new FakeTriggerSource(profile);
            var provider = new Layer2ActiveExpressionProvider(profile);

            provider.AddSource(EmotionLayer, null);
            provider.AddSource(string.Empty, trigger);
            provider.AddSource(null, trigger);
            trigger.TriggerOn("smile");

            Assert.IsFalse(provider.TryGetTopActiveExpression(EmotionLayer).HasValue);
            Assert.IsFalse(provider.RemoveSource(string.Empty, trigger));
        }
    }
}
