using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Domain.Diagnostics
{
    /// <summary>
    /// Timeline 再生の診断状態モデル。Inspector とテストはログ文言ではなくこの状態値を参照する。
    /// </summary>
    /// <remarks>
    /// <para>項目は領域（<see cref="TimelineDiagnosticArea"/>）単位で置換し、常に領域の宣言順に並ぶ。</para>
    /// <para>置換・全消去のたびに <see cref="Revision"/> を進めて <see cref="Changed"/> を発火する。</para>
    /// <para>メインスレッド専用。シリアライズしない。</para>
    /// </remarks>
    public sealed class FacialTimelineDiagnostics
    {
        private readonly List<TimelineDiagnosticItem> _items = new List<TimelineDiagnosticItem>();

        public FacialTimelineDiagnostics()
        {
            Items = _items.AsReadOnly();
        }

        /// <summary>状態が変わったとき（領域置換・全消去）に発火する。</summary>
        public event Action Changed;

        /// <summary>現在の診断項目（領域の宣言順）。</summary>
        public IReadOnlyList<TimelineDiagnosticItem> Items { get; }

        /// <summary>置換・全消去のたびに 1 増える。</summary>
        public int Revision { get; private set; }

        /// <summary>
        /// 全体の重大度。項目の重大度の最大値で、<see cref="TimelineDiagnosticSeverity.Ok"/> を下限とする
        /// （空、または Info のみなら Ok）。
        /// </summary>
        public TimelineDiagnosticSeverity Overall
        {
            get
            {
                TimelineDiagnosticSeverity overall = TimelineDiagnosticSeverity.Ok;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Severity > overall)
                    {
                        overall = _items[i].Severity;
                    }
                }

                return overall;
            }
        }

        public bool HasErrors => Overall == TimelineDiagnosticSeverity.Error;

        public bool Contains(TimelineDiagnosticCode code)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Contains(TimelineDiagnosticCode code, string subject)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Code == code && string.Equals(_items[i].Subject, subject, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 指定領域の項目をすべて <paramref name="items"/> で置き換える（空なら領域を消す）。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="items"/> に別領域の項目が含まれる。状態は変更しない。</exception>
        internal void ReplaceArea(TimelineDiagnosticArea area, ReadOnlySpan<TimelineDiagnosticItem> items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Area != area)
                {
                    throw new ArgumentException(
                        $"All items must belong to area '{area}', but found '{items[i].Area}'.",
                        nameof(items));
                }
            }

            _items.RemoveAll(item => item.Area == area);

            int insertAt = 0;
            while (insertAt < _items.Count && _items[insertAt].Area < area)
            {
                insertAt++;
            }

            for (int i = 0; i < items.Length; i++)
            {
                _items.Insert(insertAt + i, items[i]);
            }

            NotifyChanged();
        }

        /// <summary>全項目を消去する。</summary>
        internal void Clear()
        {
            _items.Clear();
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Revision++;
            Changed?.Invoke();
        }
    }
}
