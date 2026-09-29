using System;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// Google の Test size（Small / Medium / Large）。
    /// テストが依存してよい実行環境の範囲を表し、NUnit の Category と Timeout に対応付ける。
    /// 詳細な定義と判定基準は <c>docs/testing.md</c> を参照。
    /// </summary>
    public enum TestSize
    {
        /// <summary>
        /// EditMode で同期実行。シーンロード、MonoBehaviour ライフサイクル、UnityWebRequest、PlayerPrefs、
        /// ファイル I/O、Resources / AssetDatabase、WaitForSeconds、Time.time への直接依存を禁止。
        /// </summary>
        Small = 0,

        /// <summary>
        /// PlayMode（エディタ上）または EditMode でローカル資源を使うテスト。シーン、コルーチン、フレーム待ち、
        /// Physics、ローカルアセット読み込み、ループバック UDP は可。外部ネットワーク通信は禁止。
        /// </summary>
        Medium = 1,

        /// <summary>
        /// 実機ビルド、または実サーバー・実デバイス等の外部システム接続を含むテスト。
        /// </summary>
        Large = 2,
    }

    /// <summary>
    /// <see cref="TestSize"/> と NUnit Category 名・既定 Timeout の対応表。
    /// CI の <c>-testCategory</c> フィルタはここで定義する Category 名を使う。
    /// </summary>
    public static class TestSizes
    {
        public const string SmallCategory = "Small";
        public const string MediumCategory = "Medium";
        public const string LargeCategory = "Large";

        /// <summary>Small の既定 Timeout（ミリ秒）。Google の推奨上限 60 秒に合わせる。</summary>
        public const int SmallTimeoutMilliseconds = 60_000;

        /// <summary>Medium の既定 Timeout（ミリ秒）。Google の推奨上限 300 秒に合わせる。</summary>
        public const int MediumTimeoutMilliseconds = 300_000;

        /// <summary>Large の既定 Timeout（ミリ秒）。Google の推奨上限 900 秒に合わせる。</summary>
        public const int LargeTimeoutMilliseconds = 900_000;

        /// <summary>全サイズを宣言順に列挙する（Small, Medium, Large）。</summary>
        public static readonly TestSize[] All = { TestSize.Small, TestSize.Medium, TestSize.Large };

        /// <summary>サイズに対応する NUnit Category 名を返す。</summary>
        public static string ToCategory(TestSize size)
        {
            switch (size)
            {
                case TestSize.Small: return SmallCategory;
                case TestSize.Medium: return MediumCategory;
                case TestSize.Large: return LargeCategory;
                default: throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        /// <summary>サイズの既定 Timeout（ミリ秒）を返す。</summary>
        public static int DefaultTimeoutMilliseconds(TestSize size)
        {
            switch (size)
            {
                case TestSize.Small: return SmallTimeoutMilliseconds;
                case TestSize.Medium: return MediumTimeoutMilliseconds;
                case TestSize.Large: return LargeTimeoutMilliseconds;
                default: throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        /// <summary>
        /// Category 名がサイズ名（大文字小文字区別あり）なら <paramref name="size"/> に変換して true を返す。
        /// サイズ以外の Category（例: "Performance"）は false。
        /// </summary>
        public static bool TryParseCategory(string category, out TestSize size)
        {
            switch (category)
            {
                case SmallCategory: size = TestSize.Small; return true;
                case MediumCategory: size = TestSize.Medium; return true;
                case LargeCategory: size = TestSize.Large; return true;
                default: size = default; return false;
            }
        }
    }
}
