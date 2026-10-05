using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Diagnostics
{
    /// <summary>
    /// Edit / Play 開始前に静的に判定できる診断を評価し、領域単位で診断状態へ置換する。
    /// </summary>
    /// <remarks>
    /// <para>置換する領域: Director / TrackBinding / Bake / Profile / ProfileBinding / LayerMatch / Placement。
    /// セッション側の領域（LayerConnection / Analog / Gaze / Session）には触れない。</para>
    /// <para>Console 出力は行わない（出すかどうかは呼び出し側が警告ゲートで判断する）。</para>
    /// <para>確保は評価時に閉じる（毎フレーム呼ばない）。</para>
    /// </remarks>
    public static class TimelineDiagnosticsEvaluator
    {
        private const string DirectorMissingDetail =
            "PlayableDirector が見つかりません。FacialTimelineReceiver と同じ GameObject（または親）に PlayableDirector を置くか、Receiver の Director 欄に指定してください。";
        private const string DirectorAmbiguousDetail =
            "この Receiver を binding している PlayableDirector が複数あります。Receiver の Director 欄で使う Director を指定してください。";
        private const string TimelineNotBoundDetail =
            "PlayableDirector に TimelineAsset がセットされていません。Director の Playable 欄に Export した TimelineAsset をセットしてください。";
        private const string AutoAssignedDetail = "Facial トラックの binding に Receiver を自動設定しました。";
        private const string ForeignDetail =
            "このトラックの binding は別のオブジェクトを指しているため変更しません。この Receiver で再生する場合は binding を外してください。";
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
        private const string BindingMissingDetail =
            "Profile の AdapterBindings に Timeline binding がありません。Profile Inspector で Timeline binding を追加してください。";
        private const string BindingSlugInvalidDetail =
            "Timeline binding の Slug が不正です。英小文字・数字・ハイフンなどの有効な slug（既定 timeline）にしてください。";
        private const string TrackLayerUnmatchedDetail =
            "トラック名が Profile のレイヤー名と一致しないため、このトラックは再生されません。トラック名をレイヤー名に合わせてください。";
        private const string ReceiverNotOnControllerDetail =
            "FacialTimelineReceiver を FacialController と同じ GameObject に置いてください。";
        private const string ControllerMissingDetail =
            "FacialController が見つかりません。FacialTimelineReceiver と同じ GameObject に FacialController を置いてください。";
        private const string ControllerNotInitializedDetail =
            "FacialController はまだ初期化されていません（Play 開始後に初期化されます）。";

        private const string DefaultBindingSlug = "timeline";

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

            EvaluateTrackBinding(context, items);
            Flush(target, TimelineDiagnosticArea.TrackBinding, items);

            EvaluateBakeAndProfile(context, items, out List<TimelineDiagnosticItem> profileItems);
            Flush(target, TimelineDiagnosticArea.Bake, items);
            Flush(target, TimelineDiagnosticArea.Profile, profileItems);

            EvaluateProfileBinding(context, items);
            Flush(target, TimelineDiagnosticArea.ProfileBinding, items);

            EvaluateLayerMatch(context, items);
            Flush(target, TimelineDiagnosticArea.LayerMatch, items);

            EvaluatePlacement(receiver, context, items);
            Flush(target, TimelineDiagnosticArea.Placement, items);
        }

        private static void EvaluateDirector(in TimelineStaticEvaluationContext context, List<TimelineDiagnosticItem> items)
        {
            if (context.Director == null)
            {
                if (context.DirectorStatus == DirectorResolveStatus.Ambiguous)
                {
                    items.Add(Error(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorAmbiguous, string.Empty, DirectorAmbiguousDetail));
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

        private static void EvaluateTrackBinding(in TimelineStaticEvaluationContext context, List<TimelineDiagnosticItem> items)
        {
            if (!context.HasTrackBindings)
            {
                return;
            }

            TrackBindingReport report = context.TrackBindings;
            if (report.Assigned > 0)
            {
                string subject = context.Director != null ? context.Director.name : string.Empty;
                items.Add(Info(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.TrackBindingAutoAssigned, subject, AutoAssignedDetail));
            }

            IReadOnlyList<TrackAsset> foreign = report.BoundToOther;
            for (int i = 0; i < foreign.Count; i++)
            {
                string subject = foreign[i] != null ? foreign[i].name : string.Empty;
                items.Add(Warning(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.TrackBindingForeign, subject, ForeignDetail));
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

        private static void EvaluateProfileBinding(in TimelineStaticEvaluationContext context, List<TimelineDiagnosticItem> items)
        {
            FacialCharacterProfileSO source = context.ProfileSource;
            string subject = source != null ? source.name : string.Empty;
            TimelineAdapterBinding binding = FindTimelineBinding(source);
            if (binding == null)
            {
                items.Add(Error(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingMissing, subject, BindingMissingDetail));
                return;
            }

            string slugText = string.IsNullOrWhiteSpace(binding.Slug) ? DefaultBindingSlug : binding.Slug;
            if (!AdapterSlug.TryParse(slugText, out _))
            {
                items.Add(Error(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingSlugInvalid, slugText, BindingSlugInvalidDetail));
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

            if (controller.gameObject != receiver.gameObject)
            {
                items.Add(Error(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ReceiverNotOnControllerObject, receiver.name, ReceiverNotOnControllerDetail));
            }

            if (!controller.IsInitialized)
            {
                items.Add(Info(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ControllerNotInitialized, controller.name, ControllerNotInitializedDetail));
            }
        }

        private static TimelineAdapterBinding FindTimelineBinding(FacialCharacterProfileSO source)
        {
            if (source == null || source.AdapterBindings == null)
            {
                return null;
            }

            IReadOnlyList<AdapterBindingBase> bindings = source.AdapterBindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] is TimelineAdapterBinding timelineBinding)
                {
                    return timelineBinding;
                }
            }

            return null;
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
