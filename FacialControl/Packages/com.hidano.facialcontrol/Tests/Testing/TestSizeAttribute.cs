using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// テストサイズを宣言する属性の基底。NUnit の Category（"Small" / "Medium" / "Large"）と
    /// サイズ既定の Timeout を 1 つの属性で付与する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TimeoutAttribute"/> を継承することで、Timeout の適用（<c>IApplyToContext</c>）は
    /// NUnit / Unity Test Framework 標準の実装にそのまま委ねる。Category は
    /// <see cref="CategoryAttribute"/> と同じく <c>Properties["Category"]</c> に追加するため、
    /// Unity CLI の <c>-testCategory</c> フィルタや Test Runner ウィンドウの Category 絞り込みがそのまま使える。
    /// </para>
    /// <para>
    /// メソッドとクラスの両方に付けられる。クラスに付けた場合は fixture 内の全テストに適用される
    /// （Category は NUnit のフィルタが親を辿るため、Timeout は実行コンテキストの継承により伝播する）。
    /// 同じテストに複数のサイズを宣言してはならない（<see cref="TestSizeResolver"/> が違反として検出する）。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public abstract class TestSizeAttribute : TimeoutAttribute
    {
        protected TestSizeAttribute(TestSize size, int timeoutMilliseconds)
            : base(timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "Timeout はミリ秒の正の値で指定する。");
            }

            Size = size;
            TimeoutMilliseconds = timeoutMilliseconds;
        }

        /// <summary>宣言したサイズ。</summary>
        public TestSize Size { get; }

        /// <summary>適用する Timeout（ミリ秒）。</summary>
        public int TimeoutMilliseconds { get; }

        /// <summary>付与する Category 名。</summary>
        public string Category => TestSizes.ToCategory(Size);

        /// <summary>
        /// Timeout と Category のプロパティをテストに付与する。
        /// </summary>
        /// <remarks>
        /// <see cref="PropertyAttribute"/> の既定実装は「属性の型名から Attribute を除いた名前」をキーにするため
        /// （派生型では "SmallTest" 等になる）、基底は呼ばず <see cref="PropertyNames.Timeout"/> を明示的に設定する。
        /// Timeout の実行時適用（<c>IApplyToContext</c>）は <see cref="TimeoutAttribute"/> の実装がそのまま働く。
        /// </remarks>
        public override void ApplyToTest(Test test)
        {
            test.Properties.Set(PropertyNames.Timeout, TimeoutMilliseconds);
            test.Properties.Add(PropertyNames.Category, Category);
        }
    }

    /// <summary>
    /// Small テスト。EditMode で同期実行し、I/O・ネットワーク・エンジンライフサイクル・実時間に依存しない。
    /// 既定 Timeout は <see cref="TestSizes.SmallTimeoutMilliseconds"/>。
    /// </summary>
    public sealed class SmallTestAttribute : TestSizeAttribute
    {
        public SmallTestAttribute() : this(TestSizes.SmallTimeoutMilliseconds)
        {
        }

        /// <param name="timeoutMilliseconds">既定より短い Timeout が必要な場合に指定する（ミリ秒）。</param>
        public SmallTestAttribute(int timeoutMilliseconds) : base(TestSize.Small, timeoutMilliseconds)
        {
        }
    }

    /// <summary>
    /// Medium テスト。PlayMode やローカル資源（ファイル、AssetDatabase、ループバック UDP、MonoBehaviour）を使う。
    /// 既定 Timeout は <see cref="TestSizes.MediumTimeoutMilliseconds"/>。
    /// </summary>
    public sealed class MediumTestAttribute : TestSizeAttribute
    {
        public MediumTestAttribute() : this(TestSizes.MediumTimeoutMilliseconds)
        {
        }

        public MediumTestAttribute(int timeoutMilliseconds) : base(TestSize.Medium, timeoutMilliseconds)
        {
        }
    }

    /// <summary>
    /// Large テスト。実機ビルドや外部システム接続を含む。既定 Timeout は <see cref="TestSizes.LargeTimeoutMilliseconds"/>。
    /// </summary>
    public sealed class LargeTestAttribute : TestSizeAttribute
    {
        public LargeTestAttribute() : this(TestSizes.LargeTimeoutMilliseconds)
        {
        }

        public LargeTestAttribute(int timeoutMilliseconds) : base(TestSize.Large, timeoutMilliseconds)
        {
        }
    }
}
