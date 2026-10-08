using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Scanning
{
    /// <summary>
    /// <see cref="TimelineAssetScanner.Scan"/> の結果。Domain へ渡す DTO 列と、同じ index で並ぶ実トラック列を持つ。
    /// </summary>
    public readonly struct TimelineScanResult
    {
        private static readonly TimelineTrackDescriptor[] EmptyDescriptors = Array.Empty<TimelineTrackDescriptor>();
        private static readonly TrackAsset[] EmptyTrackAssets = Array.Empty<TrackAsset>();

        private readonly IReadOnlyList<TimelineTrackDescriptor> _tracks;
        private readonly IReadOnlyList<TrackAsset> _trackAssets;

        public TimelineScanResult(IReadOnlyList<TimelineTrackDescriptor> tracks, IReadOnlyList<TrackAsset> trackAssets)
        {
            _tracks = tracks;
            _trackAssets = trackAssets;
        }

        /// <summary>Domain へ渡す Unity 非依存の記述子列。</summary>
        public IReadOnlyList<TimelineTrackDescriptor> Tracks => _tracks ?? EmptyDescriptors;

        /// <summary>Adapters 専用。<c>Tracks[i]</c> に対応する実トラック。</summary>
        public IReadOnlyList<TrackAsset> TrackAssets => _trackAssets ?? EmptyTrackAssets;

        public bool HasFacialTracks => Tracks.Count > 0;
    }

    /// <summary>
    /// TimelineAsset を Unity 非依存のトラック記述子列（<see cref="TimelineTrackDescriptor"/>）と実トラック列に写す（D14）。
    /// <c>Unity.Timeline</c> の走査を本クラスに閉じ、Domain の導出 / 診断は記述子列だけを受ける。
    /// </summary>
    /// <remarks>
    /// <para><c>GetOutputTracks()</c> の順に root の Facial トラックを並べ、各 root の直後にその子孫の Facial トラックを
    /// <c>IsChild = true</c> / <c>ParentIndex = 直接の親の index</c> で並べる。Facial 以外のトラック（と、その配下）は含めない。</para>
    /// <para>アセットは変更しない。確保は呼び出し時に閉じる（セッション開始 / 診断評価でのみ呼ぶ）。</para>
    /// </remarks>
    public static class TimelineAssetScanner
    {
        public static TimelineScanResult Empty => default;

        public static TimelineScanResult Scan(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                return Empty;
            }

            var descriptors = new List<TimelineTrackDescriptor>();
            var trackAssets = new List<TrackAsset>();
            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                if (!IsFacialTrack(rootTrack))
                {
                    continue;
                }

                AppendTrack(rootTrack, isChild: false, parentIndex: -1, descriptors, trackAssets);
            }

            if (descriptors.Count == 0)
            {
                return Empty;
            }

            return new TimelineScanResult(descriptors.ToArray(), trackAssets.ToArray());
        }

        /// <summary>
        /// レイヤー weight トラック（<see cref="FacialLayerWeightTrack"/>）を出力トラック順に集める。
        /// レイヤー weight は Bake・チャネル導出の対象ではないため <see cref="Scan"/> の結果には含めない。
        /// </summary>
        public static IReadOnlyList<FacialLayerWeightTrack> CollectLayerWeightTracks(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                return Array.Empty<FacialLayerWeightTrack>();
            }

            List<FacialLayerWeightTrack> tracks = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialLayerWeightTrack weightTrack)
                {
                    tracks ??= new List<FacialLayerWeightTrack>();
                    tracks.Add(weightTrack);
                }
            }

            return tracks != null ? tracks.ToArray() : Array.Empty<FacialLayerWeightTrack>();
        }

        private static void AppendTrack(
            TrackAsset track,
            bool isChild,
            int parentIndex,
            List<TimelineTrackDescriptor> descriptors,
            List<TrackAsset> trackAssets)
        {
            int index = descriptors.Count;
            descriptors.Add(CreateDescriptor(track, index, isChild, parentIndex));
            trackAssets.Add(track);

            foreach (TrackAsset childTrack in track.GetChildTracks())
            {
                if (!IsFacialTrack(childTrack))
                {
                    continue;
                }

                AppendTrack(childTrack, isChild: true, parentIndex: index, descriptors, trackAssets);
            }
        }

        private static TimelineTrackDescriptor CreateDescriptor(TrackAsset track, int index, bool isChild, int parentIndex)
        {
            FacialTimelineBakeAsset bake = (track as IFacialTimelineBakeHolder)?.Bake;
            bool hasBake = bake != null;
            int bakeInstanceId = hasBake ? bake.GetInstanceID() : 0;

            if (track is FacialValueTrack valueTrack)
            {
                return new TimelineTrackDescriptor(
                    index,
                    TimelineTrackKind.Value,
                    track.name,
                    valueTrack.ChannelSubId,
                    valueTrack.ChannelKind,
                    ComputeMaxAxisCount(valueTrack),
                    hasBake,
                    bakeInstanceId,
                    isChild,
                    parentIndex,
                    valueTrack.ChannelKind == FacialValueChannelKind.ValueProvider
                        ? CollectBlendShapeBindings(valueTrack)
                        : null);
            }

            return new TimelineTrackDescriptor(
                index,
                TimelineTrackKind.Expression,
                track.name,
                string.Empty,
                default,
                0,
                hasBake,
                bakeInstanceId,
                isChild,
                parentIndex);
        }

        private static int ComputeMaxAxisCount(FacialValueTrack track)
        {
            int max = 0;
            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.asset is FacialValueClip valueClip && valueClip.Axes != null && valueClip.Axes.Length > max)
                {
                    max = valueClip.Axes.Length;
                }
            }

            return max;
        }

        /// <summary>値提供型トラックの全 Clip の軸を (BlendShape 名, 記録時 index) の和集合として初出順に集める。</summary>
        private static TimelineBlendShapeBinding[] CollectBlendShapeBindings(FacialValueTrack track)
        {
            var bindings = new List<TimelineBlendShapeBinding>();
            var seen = new HashSet<(string, int)>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip) || valueClip.Axes == null)
                {
                    continue;
                }

                string[] names = valueClip.BlendShapeNames;
                int[] indices = valueClip.BlendShapeIndices;
                for (int axis = 0; axis < valueClip.Axes.Length; axis++)
                {
                    string name = names != null && axis < names.Length ? names[axis] ?? string.Empty : string.Empty;
                    int index = indices != null && axis < indices.Length ? indices[axis] : axis;
                    if (seen.Add((name, index)))
                    {
                        bindings.Add(new TimelineBlendShapeBinding(name, index));
                    }
                }
            }

            return bindings.ToArray();
        }

        private static bool IsFacialTrack(TrackAsset track)
        {
            return track is FacialExpressionTrack || track is FacialValueTrack;
        }
    }
}
