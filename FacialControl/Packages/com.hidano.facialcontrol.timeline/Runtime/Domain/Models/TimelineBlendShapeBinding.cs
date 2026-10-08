namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// 値提供型 Value トラックの軸 1 本分の BlendShape 対応（名前と記録時の index）。Unity 型を含まない（D14）。
    /// </summary>
    public readonly struct TimelineBlendShapeBinding
    {
        public TimelineBlendShapeBinding(string name, int recordedIndex)
        {
            Name = name ?? string.Empty;
            RecordedIndex = recordedIndex;
        }

        /// <summary>BlendShape 名（Export で解決できなかったときは空文字）。</summary>
        public string Name { get; }

        /// <summary>記録時の FacialController の BlendShape index。</summary>
        public int RecordedIndex { get; }
    }
}
