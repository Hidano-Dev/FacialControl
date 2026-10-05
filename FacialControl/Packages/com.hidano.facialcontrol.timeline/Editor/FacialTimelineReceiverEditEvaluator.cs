using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>Receiver Inspector の Edit 評価の結果（表示とボタン判定に使う。診断そのものは Receiver の Diagnostics にある）。</summary>
    internal readonly struct ReceiverEditEvaluation
    {
        public ReceiverEditEvaluation(
            PlayableDirector director,
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            bool hasDirector,
            bool hasTimeline,
            bool timelineSaved,
            int unboundTrackCount,
            bool hasLegacyStateDeclarations,
            bool autoRebakePending)
        {
            Director = director;
            Timeline = timeline;
            ProfileAsset = profileAsset;
            HasDirector = hasDirector;
            HasTimeline = hasTimeline;
            TimelineSaved = timelineSaved;
            UnboundTrackCount = unboundTrackCount;
            HasLegacyStateDeclarations = hasLegacyStateDeclarations;
            AutoRebakePending = autoRebakePending;
        }

        /// <summary>解決した Director（無ければ null）。</summary>
        public PlayableDirector Director { get; }

        /// <summary>Director にバインドされた TimelineAsset（無ければ null）。</summary>
        public TimelineAsset Timeline { get; }

        /// <summary>評価に使った Profile SO（無ければ null）。</summary>
        public FacialCharacterProfileSO ProfileAsset { get; }

        public bool HasDirector { get; }

        public bool HasTimeline { get; }

        /// <summary>Timeline がアセットとして保存済みか（未保存なら自動ベイク・再ベイクの対象外）。</summary>
        public bool TimelineSaved { get; }

        /// <summary>generic binding が未設定の Facial トラック数（「トラック binding を今設定」の対象）。</summary>
        public int UnboundTrackCount { get; }

        /// <summary>Profile に旧 <c>{slug}:*:state</c> 宣言が残っているか。</summary>
        public bool HasLegacyStateDeclarations { get; }

        /// <summary>Watcher に再ベイクが予約されているか（「自動再ベイク中」の併記に使う）。</summary>
        public bool AutoRebakePending { get; }
    }

    /// <summary>
    /// 自動再ベイク要求を (Timeline, 理由) ごとに 1 回へ絞るゲート。その理由の診断が消えたら再び通す。
    /// </summary>
    internal sealed class AutoRebakeRequestGate
    {
        private readonly HashSet<(int timelineId, TimelineDirtyReason reason)> _requested =
            new HashSet<(int timelineId, TimelineDirtyReason reason)>();

        public bool TryRequest(TimelineAsset timeline, TimelineDirtyReason reason)
        {
            return timeline != null && _requested.Add((timeline.GetInstanceID(), reason));
        }

        /// <summary>現在の評価で出ている理由（無ければ hasReason=false）以外の記録を消す。</summary>
        public void RetainOnly(bool hasReason, TimelineDirtyReason reason)
        {
            _requested.RemoveWhere(key => !hasReason || key.reason != reason);
        }

        public void Reset()
        {
            _requested.Clear();
        }
    }

    /// <summary>
    /// Edit の Track binding 自動設定を「Inspector セッション × (Director, TimelineAsset)」ごとに 1 回へ絞るゲート（D7）。
    /// Director や TimelineAsset が変われば再び 1 回通す。自動設定を Undo した後の再評価で書き直さないようにする。
    /// </summary>
    internal sealed class TrackBindingAutoAssignGate
    {
        private readonly HashSet<(int directorId, int timelineId)> _entered = new HashSet<(int directorId, int timelineId)>();

        public bool TryEnter(PlayableDirector director, TimelineAsset timeline)
        {
            return director != null && timeline != null && _entered.Add((director.GetInstanceID(), timeline.GetInstanceID()));
        }
    }

    /// <summary>
    /// Receiver Inspector の Edit 評価（UI に依存しない）。Profile を <see cref="TimelineProfileSource"/> で解決して Receiver の静的診断を評価し、
    /// Profile SO の旧宣言（<see cref="LegacyTimelineDeclarationCleaner.Scan"/>）を LayerConnection 領域に写し、
    /// 自動修復できる診断（Bake 参照の不整合 / 旧形式 / Profile 不一致）を Watcher へ MarkDirty する。
    /// </summary>
    /// <remarks>
    /// デバウンスや再ベイクは持たない（Watcher の役割）。Director の binding は未設定の Facial トラックがあるときだけ
    /// <see cref="EditorTrackBindingWriter"/> で書く（未設定が 0 件なら書かない）。Inspector からは Undo / Redo 起点の評価では書かず、
    /// 自動設定は <see cref="TrackBindingAutoAssignGate"/> で Inspector × (Director, TimelineAsset) ごとに 1 回にする。
    /// </remarks>
    internal static class FacialTimelineReceiverEditEvaluator
    {
        private const string LegacyStateDetailFormat =
            "Layer.inputSources から '{0}' を削除してください。Receiver Inspector の『旧 timeline 宣言を削除』で除去できます。";
        private const string DeclaredValueSinkDetail =
            "Layer.inputSources の宣言（値 sink）で接続されるため、宣言の weight が優先されます。";
        private const string UnsavedTimelineDetail =
            "TimelineAsset が保存されていないため自動ベイクの対象外です。TimelineAsset を保存すると自動ベイクが有効になります。";

        /// <param name="receiver">評価対象。</param>
        /// <param name="watcher">自動再ベイクの予約先（null なら予約しない）。</param>
        /// <param name="requestAutoRebake">
        /// false なら MarkDirty しない（再ベイク完了通知による再評価で、解消しない不一致が再ベイクを繰り返さないようにする）。
        /// </param>
        /// <param name="gate">
        /// 同じ (Timeline, 理由) の自動再ベイク要求を、その診断が解消するまで 1 回に絞る（null なら毎回要求する）。
        /// 再ベイクで解消しない不一致（Profile を解決できない等）で「再ベイク完了 → 再評価 → 再要求」が回り続けないようにする。
        /// </param>
        /// <param name="allowTrackBindingWrite">
        /// false なら Track binding を書かず結果の記録だけ行う（Undo / Redo 起点の評価。書くと Undo を打ち消し Redo 履歴を消すため）。
        /// </param>
        /// <param name="assignGate">自動設定を (Director, TimelineAsset) ごとに 1 回へ絞る（null なら毎回、未設定があれば書く）。</param>
        public static ReceiverEditEvaluation Evaluate(
            FacialTimelineReceiver receiver,
            TimelineEditChangeWatcher watcher,
            bool requestAutoRebake,
            AutoRebakeRequestGate gate = null,
            bool allowTrackBindingWrite = true,
            TrackBindingAutoAssignGate assignGate = null)
        {
            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            PlayableDirector director = TimelineTrackBindingResolver.ResolveDirector(receiver, receiver.DirectorOverride, out _);
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            FacialController controller = receiver.GetComponentInParent<FacialController>(true);
            FacialCharacterProfileSO profileAsset = controller != null ? controller.CharacterSO : null;
            if (profileAsset == null && timeline != null)
            {
                TimelineProfileSource.TryResolveProfileAssetForTimeline(timeline, out profileAsset);
            }

            EnsureTrackBindings(receiver, director, timeline, allowTrackBindingWrite, assignGate);

            bool hasProfile = TryResolveProfile(profileAsset, out FacialProfile profile);
            receiver.EvaluateStaticDiagnostics(profile, hasProfile);

            FacialTimelineDiagnostics diagnostics = receiver.Diagnostics;
            bool hasLegacy = WriteDeclarationArea(diagnostics, profileAsset);

            bool saved = timeline != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(timeline));
            if (watcher != null && timeline != null && profileAsset != null && saved)
            {
                watcher.TrackProfile(timeline, profileAsset);
            }

            bool hasAutoRebakeReason = TryGetAutoRebakeReason(diagnostics, out TimelineDirtyReason reason);
            gate?.RetainOnly(hasAutoRebakeReason, reason);

            if (timeline != null && hasAutoRebakeReason)
            {
                bool unsaved = !saved;
                if (requestAutoRebake && watcher != null && (gate == null || gate.TryRequest(timeline, reason)))
                {
                    unsaved = watcher.MarkDirty(timeline, reason) == MarkDirtyResult.UnsavedTimeline;
                }

                if (unsaved)
                {
                    AppendToArea(diagnostics, new TimelineDiagnosticItem(
                        TimelineDiagnosticArea.Bake,
                        TimelineDiagnosticCode.UnsavedTimeline,
                        TimelineDiagnosticSeverity.Info,
                        timeline.name,
                        UnsavedTimelineDetail));
                }
            }

            bool pending = watcher != null && timeline != null && watcher.IsPending(timeline);
            return new ReceiverEditEvaluation(
                director,
                timeline,
                profileAsset,
                director != null,
                timeline != null,
                saved,
                CountUnboundTracks(director, timeline),
                hasLegacy,
                pending);
        }

        /// <summary>
        /// Director の Facial トラックのうち binding が null のものにだけ Receiver を設定し（Req 1.6 / D7。Undo + SetDirty 付き）、
        /// 結果を Receiver に記録する（TrackBinding 領域の AutoAssigned / Foreign の元）。未設定が 0 件なら何も書かないため、
        /// Inspector を表示しただけでシーンが dirty になることはない。他オブジェクトを指す binding は触らない。
        /// </summary>
        /// <remarks>
        /// 自動設定した直後の再評価（binding 変更の通知で起きる）では設定数が 0 になるため、同じ Director について
        /// 直前に記録した設定数を引き継いで AutoAssigned の表示が一瞬で消えないようにする。
        /// 書き込みを許さない評価（Undo / Redo 起点、またはゲートで 1 回を使い切った後）は binding を書かずに現状だけを記録し、
        /// 未設定のトラックが残っていれば AutoAssigned は出さない（「トラック binding を今設定」ボタンで設定できる）。
        /// </remarks>
        private static void EnsureTrackBindings(
            FacialTimelineReceiver receiver,
            PlayableDirector director,
            TimelineAsset timeline,
            bool allowWrite,
            TrackBindingAutoAssignGate assignGate)
        {
            if (director == null || timeline == null || UnityEngine.Application.isPlaying || EditorUtility.IsPersistent(director))
            {
                return;
            }

            bool write = allowWrite && (assignGate == null || assignGate.TryEnter(director, timeline));
            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(
                director, timeline, receiver, write ? EditorTrackBindingWriter.Instance : (ITrackBindingWriter)DryRunTrackBindingWriter.Instance);
            if (!write)
            {
                // 書かなかった分は「未設定のまま」なので設定数に数えない。
                bool hasUnbound = report.Assigned > 0;
                report = new TrackBindingReport(0, report.AlreadyBound, report.BoundToOther);
                if (hasUnbound)
                {
                    receiver.RecordTrackBindingReport(director, report);
                    return;
                }
            }

            if (report.Assigned == 0
                && receiver.TryGetTrackBindingReport(director, out TrackBindingReport previous)
                && previous.Assigned > 0)
            {
                report = new TrackBindingReport(previous.Assigned, report.AlreadyBound, report.BoundToOther);
            }

            receiver.RecordTrackBindingReport(director, report);
        }

        /// <summary>何も書かない書込口（書き込みを許さない評価で、現状の binding を数えるためだけに使う）。</summary>
        private sealed class DryRunTrackBindingWriter : ITrackBindingWriter
        {
            public static readonly DryRunTrackBindingWriter Instance = new DryRunTrackBindingWriter();

            public void SetGenericBinding(PlayableDirector director, TrackAsset track, UnityEngine.Object value)
            {
            }
        }

        private static bool TryResolveProfile(FacialCharacterProfileSO profileAsset, out FacialProfile profile)
        {
            profile = default;
            if (profileAsset == null)
            {
                return false;
            }

            try
            {
                profile = TimelineProfileSource.Resolve(profileAsset);
                return true;
            }
            catch (Exception)
            {
                // 読込不能な Profile は「Profile 無し」として評価する（Profile 領域が空になり、ProfileBinding 等の他領域は評価される）。
                return false;
            }
        }

        /// <summary>Profile SO の Timeline 宣言を LayerConnection 領域に写す。旧 state 宣言があれば true。</summary>
        private static bool WriteDeclarationArea(FacialTimelineDiagnostics diagnostics, FacialCharacterProfileSO profileAsset)
        {
            var items = new List<TimelineDiagnosticItem>();
            bool hasLegacy = false;
            if (profileAsset != null)
            {
                IReadOnlyList<LegacyTimelineDeclaration> declarations =
                    LegacyTimelineDeclarationCleaner.Scan(profileAsset, LegacyTimelineDeclarationCleaner.ResolveSlug(profileAsset));
                for (int i = 0; i < declarations.Count; i++)
                {
                    LegacyTimelineDeclaration declaration = declarations[i];
                    if (declaration.IsStateDeclaration)
                    {
                        hasLegacy = true;
                        items.Add(new TimelineDiagnosticItem(
                            TimelineDiagnosticArea.LayerConnection,
                            TimelineDiagnosticCode.LegacyStateDeclaration,
                            TimelineDiagnosticSeverity.Error,
                            declaration.LayerName + " / " + declaration.DeclaredId,
                            string.Format(LegacyStateDetailFormat, declaration.DeclaredId)));
                    }
                    else
                    {
                        items.Add(new TimelineDiagnosticItem(
                            TimelineDiagnosticArea.LayerConnection,
                            TimelineDiagnosticCode.LayerConnectionSkippedDeclared,
                            TimelineDiagnosticSeverity.Info,
                            declaration.LayerName,
                            DeclaredValueSinkDetail));
                    }
                }
            }

            diagnostics.ReplaceArea(TimelineDiagnosticArea.LayerConnection, items.ToArray());
            return hasLegacy;
        }

        private static bool TryGetAutoRebakeReason(FacialTimelineDiagnostics diagnostics, out TimelineDirtyReason reason)
        {
            if (diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict)
                || diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport))
            {
                reason = TimelineDirtyReason.BakeReferenceInconsistent;
                return true;
            }

            if (diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch))
            {
                reason = TimelineDirtyReason.ProfileMismatch;
                return true;
            }

            reason = default;
            return false;
        }

        private static void AppendToArea(FacialTimelineDiagnostics diagnostics, TimelineDiagnosticItem item)
        {
            var items = new List<TimelineDiagnosticItem>();
            IReadOnlyList<TimelineDiagnosticItem> current = diagnostics.Items;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i].Area == item.Area)
                {
                    items.Add(current[i]);
                }
            }

            items.Add(item);
            diagnostics.ReplaceArea(item.Area, items.ToArray());
        }

        private static int CountUnboundTracks(PlayableDirector director, TimelineAsset timeline)
        {
            if (director == null || timeline == null)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (director.GetGenericBinding(tracks[i]) == null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
