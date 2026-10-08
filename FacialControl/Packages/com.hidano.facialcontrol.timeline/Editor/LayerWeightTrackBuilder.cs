using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Session;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>REC のレイヤー weight の 1 サンプル（基準は t=0）。</summary>
    internal readonly struct RecordedLayerWeightSample
    {
        public RecordedLayerWeightSample(double timeSeconds, string layerName, float weight)
        {
            TimeSeconds = timeSeconds;
            LayerName = layerName ?? string.Empty;
            Weight = weight;
        }

        public double TimeSeconds { get; }

        public string LayerName { get; }

        public float Weight { get; }
    }

    /// <summary>
    /// REC のレイヤー weight サンプル列を、レイヤーごとのレイヤー weight トラック（階段カーブ）に組み立てる（HID-182）。
    /// </summary>
    /// <remarks>
    /// <para>REC 再生はサンプルを記録時刻に注入して次のサンプルまで保持するため、キーは階段（接線無限大）にする。
    /// 同時刻のサンプルは後勝ちで 1 キーにまとめる。</para>
    /// <para>全サンプルが宣言値 1 のレイヤーは Timeline の再生結果を変えないためトラックにしない。
    /// Clip は最初のサンプル時刻（基準があれば 0）から始まり、それより前は宣言値 1 で再生される。</para>
    /// </remarks>
    internal static class LayerWeightTrackBuilder
    {
        private const float StepTangent = float.PositiveInfinity;

        internal sealed class LayerTrack
        {
            public LayerTrack(string layerName, double clipStart, AnimationCurve curve)
            {
                LayerName = layerName;
                ClipStart = clipStart;
                Curve = curve;
            }

            public string LayerName { get; }

            /// <summary>Clip の開始時刻（Timeline 秒）。カーブの時刻はここからの相対秒。</summary>
            public double ClipStart { get; }

            public AnimationCurve Curve { get; }
        }

        /// <summary>サンプルは時刻の非減少順であること。戻り値はレイヤー名の昇順。</summary>
        public static List<LayerTrack> Build(IReadOnlyList<RecordedLayerWeightSample> samples)
        {
            var tracks = new List<LayerTrack>();
            if (samples == null || samples.Count == 0)
            {
                return tracks;
            }

            var samplesByLayer = new Dictionary<string, List<RecordedLayerWeightSample>>(StringComparer.Ordinal);
            for (int i = 0; i < samples.Count; i++)
            {
                RecordedLayerWeightSample sample = samples[i];
                if (string.IsNullOrEmpty(sample.LayerName))
                {
                    continue;
                }

                if (!samplesByLayer.TryGetValue(sample.LayerName, out List<RecordedLayerWeightSample> layerSamples))
                {
                    layerSamples = new List<RecordedLayerWeightSample>();
                    samplesByLayer.Add(sample.LayerName, layerSamples);
                }

                layerSamples.Add(sample);
            }

            foreach (KeyValuePair<string, List<RecordedLayerWeightSample>> pair in samplesByLayer)
            {
                if (IsAlwaysDeclared(pair.Value))
                {
                    continue;
                }

                double clipStart = pair.Value[0].TimeSeconds;
                var keys = new List<Keyframe>(pair.Value.Count);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    AddStepKey(keys, (float)(pair.Value[i].TimeSeconds - clipStart), Mathf.Clamp01(pair.Value[i].Weight));
                }

                tracks.Add(new LayerTrack(pair.Key, clipStart, new AnimationCurve(keys.ToArray())));
            }

            tracks.Sort((left, right) => string.CompareOrdinal(left.LayerName, right.LayerName));
            return tracks;
        }

        private static bool IsAlwaysDeclared(List<RecordedLayerWeightSample> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (Mathf.Clamp01(samples[i].Weight) != TimelineLayerWeightOverride.DeclaredLayerWeight)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddStepKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0)
            {
                Keyframe last = keys[keys.Count - 1];
                if (last.time == time)
                {
                    // 同時刻は後勝ち。置き換えで直前のキーと同値になったら冗長なので落とす。
                    keys.RemoveAt(keys.Count - 1);
                    if (keys.Count > 0 && keys[keys.Count - 1].value == value)
                    {
                        return;
                    }
                }
                else if (last.value == value)
                {
                    return;
                }
            }

            keys.Add(new Keyframe(time, value, StepTangent, StepTangent));
        }
    }
}
