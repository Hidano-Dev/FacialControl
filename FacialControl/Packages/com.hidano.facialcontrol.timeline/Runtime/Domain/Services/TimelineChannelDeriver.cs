using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>
    /// Scanner が写したトラック記述子列と Profile から、レイヤー / チャネルを導出する純粋関数（D1 / D14）。
    /// </summary>
    /// <remarks>
    /// <para>レイヤー候補は root の Expression トラックのみ（子トラックは親レイヤーへ畳まれているため除外）。</para>
    /// <para>チャネルは root の Value トラックのみ。ChannelSubId は REC の source id をそのまま保持し、
    /// 重複は先勝ちで後続を不正として記録する。軸数 0・空の ChannelSubId も不正。</para>
    /// <para>確保は呼び出し時（セッション開始 / 診断評価）に閉じる。Unity オブジェクトには触れない。</para>
    /// </remarks>
    public static class TimelineChannelDeriver
    {
        /// <param name="tracks">トラック記述子列（空可）。</param>
        /// <param name="profile">Profile。<c>default</c> の場合はレイヤー一致なしとして全 root Expression トラックを未一致にする。</param>
        /// <exception cref="ArgumentNullException"><paramref name="tracks"/> が null。</exception>
        public static TimelineDerivation Derive(IReadOnlyList<TimelineTrackDescriptor> tracks, FacialProfile profile)
        {
            if (tracks == null)
            {
                throw new ArgumentNullException(nameof(tracks));
            }

            var layers = new List<TimelineLayerDescriptor>();
            var unmatchedTrackNames = new List<string>();
            var channels = new List<TimelineChannelDescriptor>();
            var invalidChannelSubIds = new List<string>();
            var acceptedChannelSubIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < tracks.Count; i++)
            {
                TimelineTrackDescriptor track = tracks[i];
                if (track.IsChild)
                {
                    continue;
                }

                if (track.Kind == TimelineTrackKind.Expression)
                {
                    int layerIndex = FindLayerIndex(profile, track.Name);
                    if (layerIndex >= 0)
                    {
                        layers.Add(new TimelineLayerDescriptor(track.Name, layerIndex, track.TrackIndex));
                    }
                    else
                    {
                        unmatchedTrackNames.Add(track.Name);
                    }

                    continue;
                }

                if (track.Kind == TimelineTrackKind.Value)
                {
                    string subId = track.ChannelSubId;
                    if (string.IsNullOrEmpty(subId)
                        || track.MaxAxisCount <= 0
                        || !acceptedChannelSubIds.Add(subId))
                    {
                        invalidChannelSubIds.Add(subId);
                        continue;
                    }

                    channels.Add(new TimelineChannelDescriptor(
                        subId, track.ChannelKind, track.MaxAxisCount, track.TrackIndex, track.BlendShapeBindings));
                }
            }

            layers.Sort(CompareByTrackIndex);

            return new TimelineDerivation(
                layers,
                unmatchedTrackNames,
                channels,
                invalidChannelSubIds,
                hasFacialTracks: tracks.Count > 0);
        }

        private static int FindLayerIndex(FacialProfile profile, string layerName)
        {
            ReadOnlySpan<LayerDefinition> profileLayers = profile.Layers.Span;
            for (int i = 0; i < profileLayers.Length; i++)
            {
                if (string.Equals(profileLayers[i].Name, layerName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompareByTrackIndex(TimelineLayerDescriptor left, TimelineLayerDescriptor right)
        {
            return left.TrackIndex.CompareTo(right.TrackIndex);
        }
    }
}
