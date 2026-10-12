using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    public readonly struct ResolvedGazeInputSources
    {
        public IAnalogInputSource LeftSource { get; }
        public IAnalogInputSource RightSource { get; }
        public string ProviderSlug { get; }
        public string SelectedSlug => ProviderSlug;
        public string LeftSourceId { get; }
        public string RightSourceId { get; }

        public ResolvedGazeInputSources(IAnalogInputSource leftSource, IAnalogInputSource rightSource, string providerSlug, string leftSourceId, string rightSourceId)
        {
            LeftSource = leftSource;
            RightSource = rightSource;
            ProviderSlug = providerSlug;
            LeftSourceId = leftSourceId;
            RightSourceId = rightSourceId;
        }
    }

    /// <summary>GazeChannel を起点に入力源を解決する旧 resolver 後継。</summary>
    /// <remarks>
    /// 目線メニューの設定はどの目線制御 (InputSystem / iFacialMocap / OSC 受信など) にも同じく効かせるため、
    /// 入力源は規約 id (<c>{slug}:{channelId}</c>・<c>.left</c> / <c>.right</c>) だけで解決する (HID-191)。
    /// 複数の binding が同じチャネルを提供する場合は slug の Ordinal 順で
    /// 先頭のものを使い、警告を (チャネル id, slug) ごとに 1 回出す。
    /// </remarks>
    public static class GazeChannelResolver
    {
        public static bool TryResolve(
            GazeChannel channel,
            IInputSourceRegistry registry,
            out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            if (channel == null || registry == null || string.IsNullOrWhiteSpace(channel.id))
            {
                return false;
            }

            return TryResolveSidePair(channel.id, registry, out resolved)
                || TryResolveShared(channel.id, registry, out resolved);
        }

        private static bool TryResolveSidePair(string channelId, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            string selectedSlug = null;
            IAnalogInputSource left = null, right = null;
            string leftId = null, rightId = null;
            var ids = registry.RegisteredIds;
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!GazeSourceIdConvention.TryParse(ids[i], out string slug, out string parsedChannel, out GazeSide side)
                    || !string.Equals(parsedChannel, channelId, StringComparison.Ordinal)
                    || side == GazeSide.Shared
                    || !TryGetAnalog(registry, ids[i], out IAnalogInputSource source)) continue;

                if (selectedSlug == null || string.CompareOrdinal(slug, selectedSlug) < 0)
                {
                    if (selectedSlug != null) LogConflict(channelId, slug);
                    selectedSlug = slug;
                    left = right = null;
                    leftId = rightId = null;
                }
                else if (!string.Equals(slug, selectedSlug, StringComparison.Ordinal))
                {
                    LogConflict(channelId, selectedSlug);
                    continue;
                }

                if (side == GazeSide.Left) { left = source; leftId = ids[i]; }
                else { right = source; rightId = ids[i]; }
            }

            if (left == null && right == null) return false;
            left ??= right; right ??= left; leftId ??= rightId; rightId ??= leftId;
            resolved = new ResolvedGazeInputSources(left, right, selectedSlug, leftId, rightId);
            return true;
        }

        private static bool TryResolveShared(string channelId, IInputSourceRegistry registry, out ResolvedGazeInputSources resolved)
        {
            resolved = default;
            string selectedSlug = null, selectedId = null;
            IAnalogInputSource source = null;
            var ids = registry.RegisteredIds;
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!GazeSourceIdConvention.TryParse(ids[i], out string slug, out string parsedChannel, out GazeSide side)
                    || side != GazeSide.Shared
                    || !string.Equals(parsedChannel, channelId, StringComparison.Ordinal)
                    || !TryGetAnalog(registry, ids[i], out IAnalogInputSource candidate)) continue;
                if (selectedSlug == null || string.CompareOrdinal(slug, selectedSlug) < 0)
                {
                    if (selectedSlug != null) LogConflict(channelId, slug);
                    selectedSlug = slug; selectedId = ids[i]; source = candidate;
                }
                else if (!string.Equals(slug, selectedSlug, StringComparison.Ordinal)) LogConflict(channelId, selectedSlug);
            }
            if (source == null) return false;
            resolved = new ResolvedGazeInputSources(source, source, selectedSlug, selectedId, selectedId);
            return true;
        }

        private static bool TryGetAnalog(IInputSourceRegistry registry, string id, out IAnalogInputSource source)
        {
            source = null;
            return !string.IsNullOrEmpty(id) && registry.TryResolve(id, out IInputSource input) && (source = input as IAnalogInputSource) != null;
        }

        // 競合の警告は (チャネル id, 採用 slug) ごとに 1 回だけ出す。TryResolve は出力 observer がいると毎フレーム
        // 呼ばれるため (FacialController.BuildGazeSnapshotSpan)、毎回の警告・文字列生成を避ける。値タプルの照合は確保しない。
        private static readonly HashSet<(string ChannelId, string Slug)> s_warnedConflicts =
            new HashSet<(string ChannelId, string Slug)>();

        private static void LogConflict(string channelId, string slug)
        {
            if (!s_warnedConflicts.Add((channelId, slug)))
            {
                return;
            }

            Debug.LogWarning($"[FacialControl] Gaze channel '{channelId}' is provided by multiple binding slugs; selected '{slug}' by Ordinal lexicographic order.");
        }
    }
}
