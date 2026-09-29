using NUnit.Framework;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// サイズ宣言を前提とするテスト fixture の共通基底。
    /// SetUp でカテゴリ（<see cref="SmallTestAttribute"/> 等）を読み取り、Small のテストに Fake 以外の依存が注入されていれば失敗させる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使い方: fixture クラスに <c>[SmallTest]</c> / <c>[MediumTest]</c> / <c>[LargeTest]</c> を付け、この型を継承する。
    /// 時刻・保存・通信などの依存は <see cref="RegisterDependencies"/> をオーバーライドするか、
    /// 自身の SetUp から <see cref="Dependencies"/>.<c>Register</c> で登録する
    /// （NUnit は基底クラスの SetUp を先に実行するため、派生の SetUp では <see cref="Dependencies"/> が使える）。
    /// </para>
    /// <para>
    /// SetUp メソッド名は派生クラスの <c>SetUp()</c> と衝突しないよう固有名にしている。
    /// </para>
    /// </remarks>
    public abstract class SizedTestFixture
    {
        /// <summary>現在のテストに宣言されたサイズ。SetUp 以降に有効。</summary>
        protected TestSize CurrentTestSize { get; private set; }

        /// <summary>現在のテストの依存登録簿。SetUp 以降に有効。</summary>
        protected TestDependencyRegistry Dependencies { get; private set; }

        [SetUp]
        public void SizedTestFixtureSetUp()
        {
            var sizes = TestSizeResolver.CollectFromCurrentTest(GetType());
            if (sizes.Count == 0)
            {
                Assert.Fail(
                    $"テスト '{TestContext.CurrentContext.Test.FullName}' にサイズが宣言されていません。" +
                    " fixture クラスまたはテストメソッドに [SmallTest] / [MediumTest] / [LargeTest] を付けてください。");
            }

            if (sizes.Count > 1)
            {
                Assert.Fail(
                    $"テスト '{TestContext.CurrentContext.Test.FullName}' に複数のサイズが宣言されています: {TestSizeResolver.Describe(sizes)}。" +
                    " サイズは 1 つだけ宣言してください。");
            }

            foreach (var size in sizes)
            {
                CurrentTestSize = size;
            }

            Dependencies = new TestDependencyRegistry(CurrentTestSize);
            RegisterDependencies(Dependencies);
        }

        [TearDown]
        public void SizedTestFixtureTearDown()
        {
            Dependencies = null;
        }

        /// <summary>
        /// テストが使う外部依存を登録する。Small で Fake 以外を登録すると SetUp の時点で失敗する。
        /// 既定では何も登録しない。
        /// </summary>
        protected virtual void RegisterDependencies(TestDependencyRegistry registry)
        {
        }

        /// <summary>
        /// 依存を登録してそのまま返す糖衣。<c>_clock = UseDependency(new ManualTimeProvider());</c> のように使う。
        /// </summary>
        protected T UseDependency<T>(T dependency, string role = null) where T : class
        {
            return Dependencies.Register(dependency, role);
        }
    }
}
