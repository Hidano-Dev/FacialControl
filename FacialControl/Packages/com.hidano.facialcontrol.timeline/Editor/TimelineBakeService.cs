using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// <see cref="TimelineBakeService.IsStale(TimelineAsset, FacialCharacterProfileSO, FacialTimelineBakeAsset)"/> の結果。
    /// Profile 内容ハッシュ → Source ハッシュの順に比較し、最初に不一致になった種別を返す（D6）。
    /// </summary>
    public enum BakeStaleReason
    {
        /// <summary>Bake は最新（Profile 内容ハッシュと Source ハッシュの両方が一致）。</summary>
        None = 0,

        /// <summary>Profile 内容ハッシュが不一致（空文字・Bake 無しを含む）。</summary>
        ProfileChanged = 1,

        /// <summary>Profile 内容ハッシュは一致し、Timeline 構造（または sampleRate）による Source ハッシュのみ不一致。</summary>
        TimelineChanged = 2,
    }

    public static class TimelineBakeService
    {
        public static FacialTimelineBakeAsset Bake(
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            float sampleRate = FacialTimelineHashCalculator.DefaultSampleRate)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            return Bake(
                timeline,
                TimelineProfileSource.Resolve(profileAsset),
                FacialTimelineHashCalculator.ToGazeChannelArray(profileAsset.GazeChannels),
                sampleRate,
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profileAsset)));
        }

        /// <summary>
        /// Profile 単体から焼く（GazeChannels は空として Profile 内容ハッシュを計算する）。
        /// </summary>
        public static FacialTimelineBakeAsset Bake(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = FacialTimelineHashCalculator.DefaultSampleRate)
        {
            return Bake(timeline, profile, Array.Empty<GazeChannel>(), sampleRate, string.Empty);
        }

        /// <summary>
        /// 既存の Bake サブアセット <paramref name="target"/> を焼き直し、全 Facial トラックへ同じ参照を書く
        /// （<see cref="BakeReferenceWriter.Apply"/>）。通常のベイク経路（Exporter / 再ベイク）はこのメソッドを通る。
        /// </summary>
        public static void UpdateBakeAsset(
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            FacialTimelineBakeAsset target,
            float sampleRate = FacialTimelineHashCalculator.DefaultSampleRate)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            FacialTimelineBakeAsset baked = null;
            try
            {
                baked = Bake(timeline, profileAsset, sampleRate);
                CopyBakeData(baked, target);
                BakeReferenceWriter.Apply(timeline, target);
            }
            finally
            {
                if (baked != null)
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
        }

        private static FacialTimelineBakeAsset Bake(
            TimelineAsset timeline,
            FacialProfile profile,
            GazeChannel[] gazeChannels,
            float sampleRate,
            string profileAssetGuid)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (sampleRate <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "sampleRate must be greater than zero.");
            }

            var harness = new BakeSimulationHarness(profile, sampleRate);
            var expressionBakes = new List<ExpressionSourceBake>();
            var valueBakes = new List<ValueChannelBake>();
            var stateEvents = new List<TimelineStateEvent>();
            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                if (rootTrack is FacialExpressionTrack expressionTrack)
                {
                    expressionBakes.Add(harness.BakeExpressionTrack(expressionTrack));
                    stateEvents.AddRange(TimelineStateEventCollector.Collect(expressionTrack));
                }

                // 値提供型トラックは ValueBakes に焼かない。再生（Mixer）と Edit プレビューは Clip の階段カーブを直接評価し、
                // BlendShape ごとに sampleRate で再サンプルすると Bake が録画長 × BlendShape 数で肥大する（ValueBakes は再生に使わない）。
                // Clip の内容は Source ハッシュに含まれるため、編集すれば通常どおり再ベイク対象になる。
                if (rootTrack is FacialValueTrack valueTrack && valueTrack.ChannelKind != FacialValueChannelKind.ValueProvider)
                {
                    valueBakes.Add(BakeValueTrack(valueTrack, sampleRate));
                }
            }

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, gazeChannels, sampleRate);
            bake.ProfileContentHashHex = FacialTimelineHashCalculator.ComputeProfileContentHashHex(profile, gazeChannels);
            bake.ProfileAssetGuid = profileAssetGuid;
            bake.SampleRate = sampleRate;
            bake.ExpressionBakes = expressionBakes.ToArray();
            bake.ValueBakes = valueBakes.ToArray();
            bake.StateEvents = stateEvents.ToArray();
            return bake;
        }

        private static void CopyBakeData(FacialTimelineBakeAsset source, FacialTimelineBakeAsset target)
        {
            target.SourceHashHex = source.SourceHashHex;
            target.ProfileContentHashHex = source.ProfileContentHashHex;
            target.ProfileAssetGuid = source.ProfileAssetGuid;
            target.SampleRate = source.SampleRate;
            target.ExpressionBakes = source.ExpressionBakes;
            target.ValueBakes = source.ValueBakes;
            target.StateEvents = source.StateEvents;
        }

        /// <summary>
        /// Profile SO（Profile + GazeChannels）に対して Bake が古いかを返す。SO から焼いた Bake の鮮度判定に使う。
        /// </summary>
        public static BakeStaleReason IsStale(
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            FacialTimelineBakeAsset bake)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            return IsStale(
                timeline,
                TimelineProfileSource.Resolve(profileAsset),
                FacialTimelineHashCalculator.ToGazeChannelArray(profileAsset.GazeChannels),
                bake);
        }

        /// <summary>
        /// Profile 単体（GazeChannels は空）に対して Bake が古いかを返す。
        /// </summary>
        public static BakeStaleReason IsStale(
            TimelineAsset timeline,
            FacialProfile profile,
            FacialTimelineBakeAsset bake)
        {
            return IsStale(timeline, profile, Array.Empty<GazeChannel>(), bake);
        }

        private static BakeStaleReason IsStale(
            TimelineAsset timeline,
            FacialProfile profile,
            GazeChannel[] gazeChannels,
            FacialTimelineBakeAsset bake)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (bake == null)
            {
                return BakeStaleReason.ProfileChanged;
            }

            string expectedProfileHash = FacialTimelineHashCalculator.ComputeProfileContentHashHex(profile, gazeChannels);
            if (!string.Equals(expectedProfileHash, bake.ProfileContentHashHex, StringComparison.Ordinal))
            {
                return BakeStaleReason.ProfileChanged;
            }

            float sampleRate = bake.SampleRate > 0f
                ? bake.SampleRate
                : FacialTimelineHashCalculator.DefaultSampleRate;

            string expected = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, gazeChannels, sampleRate);
            return string.Equals(expected, bake.SourceHashHex, StringComparison.Ordinal)
                ? BakeStaleReason.None
                : BakeStaleReason.TimelineChanged;
        }

        private static ValueChannelBake BakeValueTrack(FacialValueTrack track, float sampleRate)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            ValueClipSample[] clips = CollectValueClips(track);
            int axisCount = ResolveAxisCount(clips);
            var curveKeys = new List<Keyframe>[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                curveKeys[i] = new List<Keyframe>();
            }

            if (axisCount > 0)
            {
                double sampleInterval = 1d / sampleRate;
                double lastBoundary = CaptureValueTrackSamples(clips, curveKeys, sampleInterval);
                if (lastBoundary <= 0d && curveKeys.Length > 0)
                {
                    CaptureValueState(clips, curveKeys, axisCount, 0d, 0d);
                }
            }

            var axes = new AnimationCurve[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                axes[i] = new AnimationCurve(curveKeys[i].ToArray());
            }

            return new ValueChannelBake
            {
                Sub = track.ChannelSubId,
                IsGaze = track.ChannelKind == FacialValueChannelKind.Gaze,
                Axes = axes,
            };
        }

        private static double CaptureValueTrackSamples(
            ValueClipSample[] clips,
            List<Keyframe>[] curveKeys,
            double sampleInterval)
        {
            const double boundaryTolerance = 1e-9d;
            double[] boundaries = CollectBoundaries(clips);
            double currentTime = 0d;
            int axisCount = curveKeys.Length;

            CaptureValueState(clips, curveKeys, axisCount, 0d, 0d);

            for (int boundaryIndex = 0; boundaryIndex < boundaries.Length; boundaryIndex++)
            {
                double boundaryTime = boundaries[boundaryIndex];
                while ((currentTime + sampleInterval) < (boundaryTime - boundaryTolerance))
                {
                    currentTime += sampleInterval;
                    CaptureValueState(clips, curveKeys, axisCount, currentTime, currentTime);
                }

                if (boundaryTime > 0d)
                {
                    double preBoundaryTime = Math.Max(0d, boundaryTime - Math.Min(sampleInterval * 0.25d, 1e-4d));
                    CaptureValueState(clips, curveKeys, axisCount, preBoundaryTime, boundaryTime);
                }

                currentTime = boundaryTime;
                CaptureValueState(clips, curveKeys, axisCount, currentTime, currentTime);
            }

            return boundaries.Length == 0 ? 0d : boundaries[boundaries.Length - 1];
        }

        private static void CaptureValueState(
            ValueClipSample[] clips,
            List<Keyframe>[] curveKeys,
            int axisCount,
            double sampleTime,
            double keyTime)
        {
            ValueClipSample? activeClip = FindActiveClip(clips, sampleTime);
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                float value = 0f;
                if (activeClip.HasValue)
                {
                    AnimationCurve curve = activeClip.Value.GetAxis(axisIndex);
                    if (curve != null)
                    {
                        float clipTime = (float)(sampleTime - activeClip.Value.StartTime);
                        value = curve.Evaluate(clipTime);
                    }
                }

                AddKey(curveKeys[axisIndex], (float)keyTime, value);
            }
        }

        private static ValueClipSample? FindActiveClip(ValueClipSample[] clips, double timeSeconds)
        {
            for (int i = clips.Length - 1; i >= 0; i--)
            {
                ValueClipSample clip = clips[i];
                if (timeSeconds < clip.StartTime || timeSeconds >= clip.EndTime)
                {
                    continue;
                }

                return clip;
            }

            return null;
        }

        private static double[] CollectBoundaries(ValueClipSample[] clips)
        {
            if (clips.Length == 0)
            {
                return Array.Empty<double>();
            }

            var boundaries = new SortedSet<double>();
            for (int i = 0; i < clips.Length; i++)
            {
                boundaries.Add(clips[i].StartTime);
                boundaries.Add(clips[i].EndTime);
            }

            var ordered = new double[boundaries.Count];
            boundaries.CopyTo(ordered);
            return ordered;
        }

        private static int ResolveAxisCount(ValueClipSample[] clips)
        {
            int axisCount = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                axisCount = Math.Max(axisCount, clips[i].AxisCount);
            }

            return axisCount;
        }

        private static ValueClipSample[] CollectValueClips(FacialValueTrack track)
        {
            var clips = new List<ValueClipSample>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip))
                {
                    continue;
                }

                clips.Add(new ValueClipSample(clip.start, clip.end, valueClip.Axes));
            }

            return clips.Count == 0 ? Array.Empty<ValueClipSample>() : clips.ToArray();
        }

        private static void AddKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0)
            {
                Keyframe last = keys[keys.Count - 1];
                if (Mathf.Abs(last.time - time) <= 1e-6f && Mathf.Abs(last.value - value) <= 1e-6f)
                {
                    return;
                }
            }

            keys.Add(new Keyframe(time, value));
        }

        private readonly struct ValueClipSample
        {
            private readonly AnimationCurve[] _axes;

            public ValueClipSample(double startTime, double endTime, AnimationCurve[] axes)
            {
                StartTime = startTime;
                EndTime = endTime;
                _axes = axes ?? Array.Empty<AnimationCurve>();
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public int AxisCount => _axes.Length;

            public AnimationCurve GetAxis(int axisIndex)
            {
                return (uint)axisIndex < (uint)_axes.Length ? _axes[axisIndex] : null;
            }
        }
    }
}
