using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// REC の値提供型レコード（<see cref="RecordedEventKind.ValueProviderSample"/>）を source id ごとに再生し直し、
    /// 値提供型 Value トラックの Clip データ（BlendShape ごとの値・寄与 mask・有効状態の階段カーブ）を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>状態の復元は <see cref="ValueProviderStateReplayer"/>（REC 再生の注入ソースと同じ規則）で行う。
    /// 同じ時刻のレコードはまとめて適用し、その時刻の最終状態を 1 キーにする。</para>
    /// <para>カーブは全キーの接線を無限大（階段）にする。REC 再生は次のレコードまで値を保持するため、
    /// 線形 / 滑らかな補間ではサンプル間の値が REC 再生と食い違う。値が変わらない時刻のキーは省く。</para>
    /// <para>BlendShape 名は呼び出し側が渡す参照名列（Profile の参照モデル）が記録の mask 長と矛盾しないときだけ使う。</para>
    /// </remarks>
    internal static class ValueProviderTrackBuilder
    {
        private const float StepTangent = float.PositiveInfinity;

        /// <summary>source 1 つ分の組み立て結果。</summary>
        internal sealed class SourceTrack
        {
            public string SourceId { get; set; }

            /// <summary>Clip の開始時刻（基準があれば 0、無ければ最初のレコードの時刻）。</summary>
            public double ClipStart { get; set; }

            /// <summary>軸ごとの記録時の BlendShape index（昇順）。</summary>
            public int[] RecordedIndices { get; set; }

            /// <summary>軸ごとの BlendShape 名（解決できなければ全て空文字）。</summary>
            public string[] BlendShapeNames { get; set; }

            public bool NamesResolved { get; set; }

            public AnimationCurve[] Values { get; set; }

            public AnimationCurve[] Contributes { get; set; }

            public AnimationCurve Validity { get; set; }

            /// <summary>形が合わず REC 再生でも拒否されるレコードの件数。</summary>
            public int RejectedRecords { get; set; }

            /// <summary>記録の mask バイト数（BlendShape 数の目安: (BlendShape 数 + 7) / 8）。</summary>
            public int MaskByteCount { get; set; }
        }

        /// <summary>
        /// 値提供型レコードを持つ source ごとに組み立てる（source id 昇順）。寄与した BlendShape が 1 つも無い source は含めない。
        /// </summary>
        /// <param name="referenceBlendShapeNames">参照モデルの BlendShape 名列（FacialController と同じ並び。無ければ null）。</param>
        public static List<SourceTrack> Build(IRecordedEventSequence sequence, IReadOnlyList<string> referenceBlendShapeNames)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            var eventsBySource = new Dictionary<string, List<RecordedEvent>>(StringComparer.Ordinal);
            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (evt.Kind != RecordedEventKind.ValueProviderSample)
                {
                    continue;
                }

                if (!eventsBySource.TryGetValue(evt.SourceId, out List<RecordedEvent> events))
                {
                    events = new List<RecordedEvent>();
                    eventsBySource.Add(evt.SourceId, events);
                }

                events.Add(evt);
            }

            var tracks = new List<SourceTrack>(eventsBySource.Count);
            foreach (KeyValuePair<string, List<RecordedEvent>> pair in eventsBySource)
            {
                SourceTrack track = BuildSource(pair.Key, pair.Value, referenceBlendShapeNames);
                if (track != null)
                {
                    tracks.Add(track);
                }
            }

            tracks.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return tracks;
        }

        private static SourceTrack BuildSource(string sourceId, List<RecordedEvent> events, IReadOnlyList<string> referenceNames)
        {
            int maskByteCount = 0;
            for (int i = 0; i < events.Count; i++)
            {
                maskByteCount = Math.Max(maskByteCount, events[i].MaskBytesSpan.Length);
            }

            if (maskByteCount == 0)
            {
                // mask を一度も載せていない（有効になったことが無い）source は寄与する BlendShape を持たない。
                return null;
            }

            int bitCount = maskByteCount * 8;
            var replayer = new ValueProviderStateReplayer(bitCount);
            var valueKeys = new List<Keyframe>[bitCount];
            var contributeKeys = new List<Keyframe>[bitCount];
            var everContributed = new bool[bitCount];
            for (int i = 0; i < bitCount; i++)
            {
                valueKeys[i] = new List<Keyframe>();
                contributeKeys[i] = new List<Keyframe>();
            }

            var validityKeys = new List<Keyframe>();
            double clipStart = events[0].TimeSeconds;
            int rejected = 0;
            int eventIndex = 0;
            while (eventIndex < events.Count)
            {
                double time = events[eventIndex].TimeSeconds;
                while (eventIndex < events.Count && events[eventIndex].TimeSeconds == time)
                {
                    RecordedEvent evt = events[eventIndex];
                    if (!replayer.Apply(evt.IsValid, evt.MaskBytesSpan, evt.AxesSpan))
                    {
                        rejected++;
                    }

                    eventIndex++;
                }

                float keyTime = (float)(time - clipStart);
                AddStepKey(validityKeys, keyTime, replayer.IsValid ? 1f : 0f);
                for (int bit = 0; bit < bitCount; bit++)
                {
                    bool contributes = replayer.Contributes(bit);
                    everContributed[bit] |= contributes;
                    AddStepKey(contributeKeys[bit], keyTime, contributes ? 1f : 0f);
                    AddStepKey(valueKeys[bit], keyTime, replayer.GetValue(bit));
                }
            }

            var indices = new List<int>();
            for (int bit = 0; bit < bitCount; bit++)
            {
                if (everContributed[bit])
                {
                    indices.Add(bit);
                }
            }

            if (indices.Count == 0)
            {
                return null;
            }

            bool namesResolved = CanUseNames(referenceNames, maskByteCount, indices);
            var track = new SourceTrack
            {
                SourceId = sourceId,
                ClipStart = clipStart,
                RecordedIndices = indices.ToArray(),
                BlendShapeNames = new string[indices.Count],
                NamesResolved = namesResolved,
                Values = new AnimationCurve[indices.Count],
                Contributes = new AnimationCurve[indices.Count],
                Validity = new AnimationCurve(validityKeys.ToArray()),
                RejectedRecords = rejected,
                MaskByteCount = maskByteCount,
            };

            for (int axis = 0; axis < indices.Count; axis++)
            {
                int bit = indices[axis];
                track.BlendShapeNames[axis] = namesResolved ? referenceNames[bit] ?? string.Empty : string.Empty;
                track.Values[axis] = new AnimationCurve(valueKeys[bit].ToArray());
                track.Contributes[axis] = new AnimationCurve(contributeKeys[bit].ToArray());
            }

            return track;
        }

        /// <summary>
        /// 参照名列が記録と矛盾しないか（mask バイト数が名前数から求まる値と一致し、寄与した index が全て名前の範囲内）。
        /// </summary>
        internal static bool CanUseNames(IReadOnlyList<string> referenceNames, int maskByteCount, IReadOnlyList<int> indices)
        {
            if (referenceNames == null || referenceNames.Count == 0 || (referenceNames.Count + 7) / 8 != maskByteCount)
            {
                return false;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] >= referenceNames.Count || string.IsNullOrEmpty(referenceNames[indices[i]]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddStepKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0 && BitConverter.SingleToInt32Bits(keys[keys.Count - 1].value) == BitConverter.SingleToInt32Bits(value))
            {
                return;
            }

            keys.Add(new Keyframe(time, value, StepTangent, StepTangent));
        }
    }
}
