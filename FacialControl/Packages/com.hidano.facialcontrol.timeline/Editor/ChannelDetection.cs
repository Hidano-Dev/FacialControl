using Hidano.FacialControl.Timeline.Tracks;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// REC Export が値チャネルの種別（Analog / Gaze / ValueProvider）を決めた理由（Req 10.1 / 10.3）。
    /// </summary>
    public enum ChannelDetectionReason
    {
        /// <summary>Profile の GazeChannel の明示 source id（左 / 右）と完全一致した。</summary>
        ExplicitGazeSourceId = 0,

        /// <summary>source id が Gaze の規約形で、そのチャネル id が Profile の GazeChannels にある。</summary>
        ConventionGazeChannel = 1,

        /// <summary>Profile の binding が gaze source を宣言している（slug + チャネル id、またはワイルドカード slug）。</summary>
        GazeProviderDeclaration = 2,

        /// <summary>Gaze 候補だが 2 軸でないサンプルを含むため Analog にした。</summary>
        NonTwoAxisSamples = 3,

        /// <summary>Gaze の手がかりが無いため既定の Analog にした。</summary>
        DefaultAnalog = 4,

        /// <summary>呼び出し側の種別上書きを使った（プログラム・テスト用途）。</summary>
        Overridden = 5,

        /// <summary>値提供型の記録（kind 7 / 8）。BlendShape は REC に記録された録画時の名前で保存する。</summary>
        ValueProviderNamed = 6,

        /// <summary>値提供型の記録（kind 7 / 8）。REC に BlendShape 名が無いため BlendShape は記録時の index で保存する。</summary>
        ValueProviderIndexed = 7,
    }

    /// <summary>
    /// REC の source 1 つ分のチャネル検出結果（Analog イベント、または寄与 BlendShape のある値提供型レコードを持つ source）。
    /// </summary>
    public readonly struct ChannelDetection
    {
        public ChannelDetection(string sourceId, FacialValueChannelKind kind, ChannelDetectionReason reason, int axisCount)
        {
            SourceId = sourceId ?? string.Empty;
            Kind = kind;
            Reason = reason;
            AxisCount = axisCount;
        }

        /// <summary>REC の source id（Export 後の ChannelSubId と同じ）。</summary>
        public string SourceId { get; }

        public FacialValueChannelKind Kind { get; }

        public ChannelDetectionReason Reason { get; }

        /// <summary>サンプルの最大軸数。値提供型は寄与した BlendShape の数。</summary>
        public int AxisCount { get; }
    }
}
