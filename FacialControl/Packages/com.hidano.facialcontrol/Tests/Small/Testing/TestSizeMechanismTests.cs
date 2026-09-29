using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.Small.Testing
{
    /// <summary>
    /// 設計上の不変条件（test-policy 区分 C）: サイズ属性が NUnit の Category / Timeout として実際に反映され、
    /// Small の Fake ガードが働くこと。ここが壊れると CI の <c>-testCategory</c> 絞り込みが空振りするため、
    /// メカニズム自体を最小限のテストで守る。
    /// </summary>
    [SmallTest]
    public sealed class TestSizeMechanismTests : SizedTestFixture
    {
        [Test]
        public void SmallTest_OnFixture_ResolvesCurrentTestSizeToSmall()
        {
            Assert.That(CurrentTestSize, Is.EqualTo(TestSize.Small));
        }

        [Test]
        public void Register_SmallWithFake_ReturnsDependency()
        {
            var registry = new TestDependencyRegistry(TestSize.Small);
            var fake = new ManualTimeProvider();

            ITimeProvider registered = registry.Register<ITimeProvider>(fake);

            Assert.That(registered, Is.SameAs(fake));
            Assert.That(registry.Entries, Has.Count.EqualTo(1));
            Assert.That(registry.Entries[0].Role, Is.EqualTo(nameof(ITimeProvider)));
        }

        [Test]
        public void Register_SmallWithNonFake_FailsAssertion()
        {
            var registry = new TestDependencyRegistry(TestSize.Small);

            var ex = Assert.Throws<AssertionException>(() => registry.Register(new RealLikeTimeProvider(), "ITimeProvider"));

            Assert.That(ex.Message, Does.Contain("ITimeProvider").And.Contain(nameof(RealLikeTimeProvider)));
            Assert.That(registry.Entries, Is.Empty);
        }

        [Test]
        public void Register_MediumWithNonFake_Succeeds()
        {
            var registry = new TestDependencyRegistry(TestSize.Medium);

            Assert.DoesNotThrow(() => registry.Register(new RealLikeTimeProvider()));
            Assert.That(registry.Entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void SharedFakes_ImplementFakeDependencyMarker()
        {
            Assert.That(TestDependencyRegistry.IsFake(new ManualTimeProvider()), Is.True);
            Assert.That(TestDependencyRegistry.IsFake(new InMemorySaveStorage()), Is.True);
            Assert.That(TestDependencyRegistry.IsFake(new FakeDatagramSender()), Is.True);
        }

        [Test]
        public void CollectDeclaredSizes_FixtureWithoutSize_ReturnsEmpty()
        {
            var sizes = TestSizeResolver.CollectDeclaredSizes(
                typeof(UndeclaredSample).GetMethod(nameof(UndeclaredSample.Probe)), typeof(UndeclaredSample));

            Assert.That(sizes, Is.Empty);
        }

        [Test]
        public void CollectDeclaredSizes_ClassAndMethodConflict_ReturnsBothSizes()
        {
            var sizes = TestSizeResolver.CollectDeclaredSizes(
                typeof(ConflictingSample).GetMethod(nameof(ConflictingSample.Probe)), typeof(ConflictingSample));

            Assert.That(sizes, Is.EquivalentTo(new[] { TestSize.Small, TestSize.Large }));
        }

        [Test]
        public void CollectDeclaredSizes_PlainCategoryAttribute_IsRecognized()
        {
            var sizes = TestSizeResolver.CollectDeclaredSizes(null, typeof(PlainCategorySample));

            Assert.That(sizes, Is.EquivalentTo(new[] { TestSize.Large }));
        }

        [Test]
        public void TestSizes_DefaultTimeouts_FollowSizeOrder()
        {
            Assert.That(TestSizes.DefaultTimeoutMilliseconds(TestSize.Small), Is.LessThan(TestSizes.DefaultTimeoutMilliseconds(TestSize.Medium)));
            Assert.That(TestSizes.DefaultTimeoutMilliseconds(TestSize.Medium), Is.LessThan(TestSizes.DefaultTimeoutMilliseconds(TestSize.Large)));
            Assert.That(TestSizes.TryParseCategory("Performance", out _), Is.False);
        }

        /// <summary>Fake マーカーを持たない ITimeProvider 実装（本番実装の代役）。</summary>
        private sealed class RealLikeTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }

        /// <summary>サイズ未宣言のサンプル。NUnit にテストとして拾われないよう [Test] は付けない。</summary>
        private sealed class UndeclaredSample
        {
            public void Probe()
            {
            }
        }

        /// <summary>
        /// クラスとメソッドで異なるサイズを宣言したサンプル。メソッド側は素の Category で "Large" を与える
        /// （定数連結にしているのは、Tests/Small 配下で Medium / Large のリテラル宣言を禁止する静的チェック
        /// scripts/check-test-sizes.ps1 に誤検出させないため）。
        /// </summary>
        [SmallTest]
        private sealed class ConflictingSample
        {
            private const string ConflictingCategoryName = "Lar" + "ge";

            [Category(ConflictingCategoryName)]
            public void Probe()
            {
            }
        }

        [Category(TestSizes.LargeCategory)]
        private sealed class PlainCategorySample
        {
        }
    }

    /// <summary>
    /// メソッド単位でサイズを宣言した場合に Category / Timeout プロパティがそのテストに載ることを確認する
    /// （fixture 側にはサイズを付けない。同じテストに 2 つのサイズが宣言されると SetUp が失敗する）。
    /// </summary>
    public sealed class TestSizeMethodLevelDeclarationTests : SizedTestFixture
    {
        [Test]
        [SmallTest]
        public void SmallTest_OnMethod_AddsSmallCategoryAndDefaultTimeoutProperties()
        {
            var properties = TestContext.CurrentContext.Test.Properties;

            Assert.That(properties[NUnit.Framework.Internal.PropertyNames.Category], Has.Member(TestSizes.SmallCategory));
            Assert.That(properties[NUnit.Framework.Internal.PropertyNames.Timeout], Has.Member(TestSizes.SmallTimeoutMilliseconds));
            Assert.That(CurrentTestSize, Is.EqualTo(TestSize.Small));
        }

        [Test]
        [SmallTest(timeoutMilliseconds: 5_000)]
        public void SmallTest_OnMethodWithCustomTimeout_UsesGivenTimeout()
        {
            var properties = TestContext.CurrentContext.Test.Properties;

            Assert.That(properties[NUnit.Framework.Internal.PropertyNames.Timeout], Has.Member(5_000));
        }
    }
}
