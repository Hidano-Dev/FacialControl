using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Assets
{
    /// <summary>
    /// <see cref="FacialTimelineBakeLocator.Locate"/> の判定状態（D5）。
    /// </summary>
    public enum BakeLocateStatus
    {
        /// <summary>全 Facial トラックが同一の非 null 参照を指す。</summary>
        Found = 0,

        /// <summary>上書き Bake が指定され、それを採用した（トラック参照の検証結果は <see cref="BakeLocateResult.OverrideDiffers"/> に出る）。</summary>
        OverrideUsed = 1,

        /// <summary>Facial トラックが無い（または Timeline が null）。</summary>
        Missing = 2,

        /// <summary>全 Facial トラックの参照が null（Bake 参照導入前の旧 Export）。</summary>
        LegacyExport = 3,

        /// <summary>異なる参照の混在、または一部トラックのみ参照が欠落している。</summary>
        Conflict = 4,
    }

    /// <summary>
    /// Bake 解決の結果。同じ入力に対して決定的（走査順に依存しない）。
    /// </summary>
    public readonly struct BakeLocateResult
    {
        public BakeLocateResult(
            BakeLocateStatus status,
            FacialTimelineBakeAsset bake,
            FacialTimelineBakeAsset trackBake,
            int holderCount,
            int nullHolderCount,
            int distinctReferenceCount,
            bool overrideDiffers)
        {
            Status = status;
            Bake = bake;
            TrackBake = trackBake;
            HolderCount = holderCount;
            NullHolderCount = nullHolderCount;
            DistinctReferenceCount = distinctReferenceCount;
            OverrideDiffers = overrideDiffers;
        }

        public BakeLocateStatus Status { get; }

        /// <summary>採用した Bake。Missing / LegacyExport / Conflict では null、OverrideUsed では上書き Bake。</summary>
        public FacialTimelineBakeAsset Bake { get; }

        /// <summary>全トラックが同一の非 null 参照を指すときのその参照。それ以外は null。</summary>
        public FacialTimelineBakeAsset TrackBake { get; }

        /// <summary>走査した Facial トラック数（root + 子）。</summary>
        public int HolderCount { get; }

        /// <summary>参照が null のトラック数。</summary>
        public int NullHolderCount { get; }

        /// <summary>非 null 参照の種類数。</summary>
        public int DistinctReferenceCount { get; }

        /// <summary>OverrideUsed かつ上書き Bake がいずれかのトラック参照と異なる（参照欠落を含む）。</summary>
        public bool OverrideDiffers { get; }
    }

    /// <summary>
    /// Editor API を使わずに TimelineAsset から Bake を解決する（D5）。全 Facial トラック（root + 子）の
    /// <see cref="IFacialTimelineBakeHolder"/> を検査し、全トラックが同一の非 null 参照を指すときだけ <see cref="BakeLocateStatus.Found"/> を返す。
    /// </summary>
    /// <remarks>アセットは変更しない。呼び出しはセッション開始 / 診断評価時のみ（確保は呼び出し時に閉じる）。</remarks>
    public static class FacialTimelineBakeLocator
    {
        public static BakeLocateResult Locate(TimelineAsset timeline, FacialTimelineBakeAsset overrideBake)
        {
            TimelineScanResult scan = TimelineAssetScanner.Scan(timeline);
            IReadOnlyList<TrackAsset> tracks = scan.TrackAssets;

            int holderCount = 0;
            int nullHolderCount = 0;
            var distinct = new List<FacialTimelineBakeAsset>(1);
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!(tracks[i] is IFacialTimelineBakeHolder holder))
                {
                    continue;
                }

                holderCount++;
                FacialTimelineBakeAsset bake = holder.Bake;
                if (bake == null)
                {
                    nullHolderCount++;
                    continue;
                }

                if (!ContainsReference(distinct, bake))
                {
                    distinct.Add(bake);
                }
            }

            int distinctCount = distinct.Count;
            FacialTimelineBakeAsset trackBake = distinctCount == 1 && nullHolderCount == 0 ? distinct[0] : null;

            if (overrideBake != null)
            {
                bool differs = nullHolderCount > 0
                    || (distinctCount > 0 && (distinctCount > 1 || !ReferenceEquals(distinct[0], overrideBake)));
                return new BakeLocateResult(
                    BakeLocateStatus.OverrideUsed,
                    overrideBake,
                    trackBake,
                    holderCount,
                    nullHolderCount,
                    distinctCount,
                    differs);
            }

            BakeLocateStatus status;
            if (holderCount == 0)
            {
                status = BakeLocateStatus.Missing;
            }
            else if (nullHolderCount == holderCount)
            {
                status = BakeLocateStatus.LegacyExport;
            }
            else if (trackBake != null)
            {
                status = BakeLocateStatus.Found;
            }
            else
            {
                status = BakeLocateStatus.Conflict;
            }

            return new BakeLocateResult(
                status,
                status == BakeLocateStatus.Found ? trackBake : null,
                trackBake,
                holderCount,
                nullHolderCount,
                distinctCount,
                overrideDiffers: false);
        }

        private static bool ContainsReference(List<FacialTimelineBakeAsset> list, FacialTimelineBakeAsset bake)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], bake))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
