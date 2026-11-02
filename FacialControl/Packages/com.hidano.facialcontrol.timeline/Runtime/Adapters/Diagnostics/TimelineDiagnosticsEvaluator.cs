using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Diagnostics
{
    /// <summary>
    /// Edit / Play 開始前に静的に判定できる診断を評価し、領域単位で診断状態へ置換する。
    /// </summary>
    /// <remarks>
    /// <para>置換する領域: Director / TrackBinding（トラック構造）/ Bake / Profile / LayerMatch / Placement。
    /// セッション側の領域（LayerConnection / Analog / Gaze / Session）には触れない。</para>
    /// <para>Console 出力は行わない（出すかどうかは呼び出し側が警告ゲートで判断する）。</para>
    /// <para>確保は評価時に閉じる（毎フレーム呼ばない）。</para>
    /// </remarks>
    public static class TimelineDiagnosticsEvaluator
    {
        private const string DirectorMissingDetail =
            "この FacialController を Bind した Facial Character トラックを持つ PlayableDirector が見つかりません。"
            + "Facial Character トラックの binding に FacialController をセットしてください。";
        private const string DirectorAmbiguousDetail =
            "この FacialController を Bind している PlayableDirector が複数あります。診断は最初に再生された Director で行います。";
        private const string TimelineNotBoundDetail =
            "PlayableDirector に TimelineAsset がセットされていません。Director の Playable 欄に Export した TimelineAsset をセットしてください。";
        private const string LegacyTrackStructureDetail =
            "Facial Character トラックの外にある Facial トラックは再生されません（親トラック導入前に書き出した旧構造）。"
            + "REC から TimelineAsset を書き出し直してください。";
        private const string MultipleCharacterTracksDetail =
            "1 つの Timeline に置ける Facial Character トラックは 1 本までです（Bake は Timeline 全体を 1 体として作るため）。"
            + "キャラごとに Timeline を分けてください。";
        private const string BakeMissingDetail =
            "TimelineAsset に Facial トラックが無いため、再生する Bake がありません。";
        private const string BakeLegacyExportDetail =
            "Bake 参照の無い旧形式の TimelineAsset です。Editor で Timeline を開いて保存するか再 Export してください。Receiver Inspector の『今再ベイク』でも修復できます。";
        private const string BakeConflictDetail =
            "Facial トラックの Bake 参照が一致していません。Editor で Timeline を開いて保存するか再 Export してください。Receiver Inspector の『今再ベイク』でも修復できます。";
        private const string BakeOverrideUsedDetail = "Receiver の Bake 欄で指定した Bake を使います。";
        private const string BakeOverrideDiffersDetail =
            "Receiver の Bake 欄で指定した Bake がトラックの Bake 参照と異なります。意図しない場合は Bake 欄を空にしてください。";
        private const string BakeFreshDetail = "Bake は最新です。";
        private const string BakeStaleDetail =
            "Timeline の内容が Bake 作成時から変わっています。Bake の値で再生を続けます。Edit に戻ると自動で再ベイクされます（今すぐ直す場合は Receiver Inspector の『今再ベイク』）。";
        private const string ProfileMatchedDetail = "Bake は現在の Profile から作られています。";
        private const string ProfileMismatchDetail =
            "Bake が現在の Profile と異なるスナップショットから作られています。Bake の値で再生を続けます。Edit に戻ると自動で再ベイクされます（今すぐ直す場合は Receiver Inspector の『今再ベイク』）。";
        private const string TrackLayerUnmatchedDetail =
            "トラック名が Profile のレイヤー名と一致しないため、このトラックは再生されません。トラック名をレイヤー名に合わせてください。";
        private const string ControllerMissingDetail =
            "FacialController が見つかりません。Facial Character トラックには FacialController を Bind してください。";
        private const string ControllerNotInitializedDetail =
            "FacialController はまだ初期化されていません（Play 開始後に初期化されます）。";

        public static void EvaluateStatic(
            FacialTimelineReceiver receiver,
            FacialTimelineDiagnostics target,
            TimelineStaticEvaluationContext context)
        {
            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var items = new List<TimelineDiagnosticItem>();

            EvaluateDirector(context, items);
            Flush(target, TimelineDiagnosticArea.Director, items);

            EvaluateTrackStructure(context.Timeline, items);
            Flush(target, TimelineDiagnosticArea.TrackBinding, items);

            EvaluateBakeAndProfile(context, items, out List<TimelineDiagnosticItem> profileItems);
            Flush(target, TimelineDiagnosticArea.Bake, items);
            Flush(target, TimelineDiagnosticArea.Profile, profileItems);

            EvaluateLayerMatch(context, items);
            Flush(target, TimelineDiagnosticArea.LayerMatch, items);

            EvaluatePlacement(receiver, context, items);
            Flush(target, TimelineDiagnosticArea.Placement, items);
        }

        /// <summary>
        /// TrackBinding 領域（トラック構造: 親トラックの外の旧構造トラック / 親トラックが複数）だけを評価して置換する。
        /// Error があれば true（再生セッションは開始しない）。
        /// </summary>
        public static bool EvaluateTrackStructureArea(FacialTimelineDiagnostics target, TimelineAsset timeline)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var items = new List<TimelineDiagnosticItem>();
            EvaluateTrackStructure(timeline, items);
            bool hasError = items.Count > 0;
            Flush(target, TimelineDiagnosticArea.TrackBinding, items);
            return hasError;
        }

        /// <summary>
        /// Bake / Profile 領域だけを評価して置換する（再生セッション開始時に Receiver が Bake 解決と Profile 照合に使う）。
        /// </summary>
        public static void EvaluateBakeAndProfileAreas(FacialTimelineDiagnostics target, TimelineStaticEvaluationContext context)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var items = new List<TimelineDiagnosticItem>();
            EvaluateBakeAndProfile(context, items, out List<TimelineDiagnosticItem> profileItems);
            Flush(target, TimelineDiagnosticArea.Bake, items);
            Flush(target, TimelineDiagnosticArea.Profile, profileItems);
        }

        /// <summary>
        /// LayerMatch 領域（Profile のレイヤー名と一致しないトラック）だけを評価して置換する。
        /// </summary>
        public static void EvaluateLayerMatchArea(FacialTimelineDiagnostics target, TimelineStaticEvaluationContext context)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var items = new List<TimelineDiagnosticItem>();
            EvaluateLayerMatch(context, items);
            Flush(target, TimelineDiagnosticArea.LayerMatch, items);
        }

        private static void EvaluateDirector(in TimelineStaticEvaluationContext context, List<TimelineDiagnosticItem> items)
        {
            if (context.Director == null)
            {
                if (context.DirectorStatus == DirectorResolveStatus.Ambiguous)
                {
                    items.Add(Warning(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorAmbiguous, string.Empty, DirectorAmbiguousDetail));
                }
                else
                {
                    items.Add(Error(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, string.Empty, DirectorMissingDetail));
                }

                return;
            }

            if (context.Timeline == null)
            {
                items.Add(Error(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.TimelineNotBound, context.Director.name, TimelineNotBoundDetail));
            }
        }

        private static void EvaluateTrackStructure(TimelineAsset timeline, List<TimelineDiagnosticItem> items)
        {
            if (timeline == null)
            {
                // Director 領域が原因を示す。
                return;
            }

            List<TrackAsset> outside = FacialCharacterTrackBinding.CollectTracksOutsideCharacter(timeline);
            for (int i = 0; i < outside.Count; i++)
            {
                string subject = outside[i] != null ? outside[i].name : string.Empty;
                items.Add(Error(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.LegacyTrackStructure, subject, LegacyTrackStructureDetail));
            }

            List<FacialCharacterTrack> characters = FacialCharacterTrackBinding.CollectCharacterTracks(timeline);
            if (characters.Count > 1)
            {
                items.Add(Error(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.MultipleCharacterTracks, timeline.name, MultipleCharacterTracksDetail));
            }
        }

        private static void EvaluateBakeAndProfile(
            in TimelineStaticEvaluationContext context,
            List<TimelineDiagnosticItem> bakeItems,
            out List<TimelineDiagnosticItem> profileItems)
        {
            profileItems = new List<TimelineDiagnosticItem>();
            if (context.Timeline == null)
            {
                // Director 領域が原因を示す。
                return;
            }

            BakeLocateResult located = context.Bake;
            string timelineName = context.Timeline.name;
            switch (located.Status)
            {
                case BakeLocateStatus.Missing:
                    bakeItems.Add(Warning(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeMissing, timelineName, BakeMissingDetail));
                    return;
                case BakeLocateStatus.LegacyExport:
                    bakeItems.Add(Error(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeLegacyExport, timelineName, BakeLegacyExportDetail));
                    return;
                case BakeLocateStatus.Conflict:
                    bakeItems.Add(Error(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeReferenceConflict, timelineName, BakeConflictDetail));
                    return;
                case BakeLocateStatus.OverrideUsed:
                    string overrideName = located.Bake != null ? located.Bake.name : string.Empty;
                    bakeItems.Add(Info(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeOverrideUsed, overrideName, BakeOverrideUsedDetail));
                    if (located.OverrideDiffers)
                    {
                        bakeItems.Add(Warning(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeOverrideDiffers, overrideName, BakeOverrideDiffersDetail));
                    }

                    break;
            }

            FacialTimelineBakeAsset bake = located.Bake;
            if (bake == null || !context.HasProfile)
            {
                return;
            }

            string profileSubject = context.ProfileSource != null ? context.ProfileSource.name : string.Empty;
            GazeChannel[] gazeChannels = FacialTimelineHashCalculator.ToGazeChannelArray(context.GazeChannels);
            FacialProfile profile = context.Profile;
            string actualProfileHash = FacialTimelineHashCalculator.ComputeProfileContentHashHex(profile, gazeChannels);
            string expectedProfileHash = context.ExpectedProfileContentHashHex;
            bool profileMatched = !string.IsNullOrEmpty(expectedProfileHash)
                && string.Equals(expectedProfileHash, actualProfileHash, StringComparison.Ordinal);
            if (!profileMatched)
            {
                profileItems.Add(Warning(TimelineDiagnosticArea.Profile, TimelineDiagnosticCode.ProfileMismatch, profileSubject, ProfileMismatchDetail));
                return;
            }

            profileItems.Add(Info(TimelineDiagnosticArea.Profile, TimelineDiagnosticCode.ProfileMatched, profileSubject, ProfileMatchedDetail));

            string actualSourceHash = FacialTimelineHashCalculator.ComputeHashHex(context.Timeline, profile, gazeChannels, bake.SampleRate);
            if (string.Equals(bake.SourceHashHex, actualSourceHash, StringComparison.Ordinal))
            {
                bakeItems.Add(Info(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeFresh, timelineName, BakeFreshDetail));
            }
            else
            {
                bakeItems.Add(Warning(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeStale, timelineName, BakeStaleDetail));
            }
        }

        private static void EvaluateLayerMatch(in TimelineStaticEvaluationContext context, List<TimelineDiagnosticItem> items)
        {
            if (context.Derivation == null)
            {
                return;
            }

            IReadOnlyList<string> unmatched = context.Derivation.UnmatchedTrackNames;
            for (int i = 0; i < unmatched.Count; i++)
            {
                items.Add(Warning(TimelineDiagnosticArea.LayerMatch, TimelineDiagnosticCode.TrackLayerUnmatched, unmatched[i], TrackLayerUnmatchedDetail));
            }
        }

        private static void EvaluatePlacement(
            FacialTimelineReceiver receiver,
            in TimelineStaticEvaluationContext context,
            List<TimelineDiagnosticItem> items)
        {
            FacialController controller = context.Controller;
            if (controller == null)
            {
                items.Add(Error(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ControllerMissing, receiver.name, ControllerMissingDetail));
                return;
            }

            if (!controller.IsInitialized)
            {
                items.Add(Info(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ControllerNotInitialized, controller.name, ControllerNotInitializedDetail));
            }
        }

        private static void Flush(FacialTimelineDiagnostics target, TimelineDiagnosticArea area, List<TimelineDiagnosticItem> items)
        {
            target.ReplaceArea(area, items.ToArray());
            items.Clear();
        }

        private static TimelineDiagnosticItem Info(TimelineDiagnosticArea area, TimelineDiagnosticCode code, string subject, string detail)
        {
            return new TimelineDiagnosticItem(area, code, TimelineDiagnosticSeverity.Info, subject, detail);
        }

        private static TimelineDiagnosticItem Warning(TimelineDiagnosticArea area, TimelineDiagnosticCode code, string subject, string detail)
        {
            return new TimelineDiagnosticItem(area, code, TimelineDiagnosticSeverity.Warning, subject, detail);
        }

        private static TimelineDiagnosticItem Error(TimelineDiagnosticArea area, TimelineDiagnosticCode code, string subject, string detail)
        {
            return new TimelineDiagnosticItem(area, code, TimelineDiagnosticSeverity.Error, subject, detail);
        }
    }
}
