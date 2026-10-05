using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;

namespace Hidano.FacialControl.Timeline.Editor.Inspector
{
    /// <summary>Inspector の Foldout 1 つ分（領域ごとの診断項目）。</summary>
    internal readonly struct DiagnosticAreaGroup
    {
        public DiagnosticAreaGroup(TimelineDiagnosticArea area, IReadOnlyList<TimelineDiagnosticItem> items, TimelineDiagnosticSeverity worst)
        {
            Area = area;
            Items = items;
            Worst = worst;
        }

        public TimelineDiagnosticArea Area { get; }

        public IReadOnlyList<TimelineDiagnosticItem> Items { get; }

        /// <summary>領域内の最大重大度（Info のみなら Ok）。</summary>
        public TimelineDiagnosticSeverity Worst { get; }

        /// <summary>Warning / Error を含まない（1 行に畳んで表示する）。</summary>
        public bool IsHealthy => Worst <= TimelineDiagnosticSeverity.Ok;
    }

    /// <summary>
    /// Receiver Inspector の表示判定（UI に依存しない純粋関数）。どの診断で何を表示し、どのボタンを有効にするかをここで決め、
    /// <see cref="FacialTimelineReceiverInspector"/> は結果を VisualElement に写すだけにする。
    /// </summary>
    internal static class FacialTimelineReceiverInspectorModel
    {
        /// <summary>診断項目を領域の宣言順にまとめる。</summary>
        public static IReadOnlyList<DiagnosticAreaGroup> GroupByArea(IReadOnlyList<TimelineDiagnosticItem> items)
        {
            var groups = new List<DiagnosticAreaGroup>();
            if (items == null || items.Count == 0)
            {
                return groups;
            }

            foreach (TimelineDiagnosticArea area in (TimelineDiagnosticArea[])Enum.GetValues(typeof(TimelineDiagnosticArea)))
            {
                List<TimelineDiagnosticItem> inArea = null;
                TimelineDiagnosticSeverity worst = TimelineDiagnosticSeverity.Ok;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Area != area)
                    {
                        continue;
                    }

                    inArea ??= new List<TimelineDiagnosticItem>();
                    inArea.Add(items[i]);
                    if (items[i].Severity > worst)
                    {
                        worst = items[i].Severity;
                    }
                }

                if (inArea != null)
                {
                    groups.Add(new DiagnosticAreaGroup(area, inArea, worst));
                }
            }

            return groups;
        }

        /// <summary>重大度アイコン（<c>EditorGUIUtility.IconContent</c> の名前）。</summary>
        public static string SeverityIconName(TimelineDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case TimelineDiagnosticSeverity.Error:
                    return "console.erroricon.sml";
                case TimelineDiagnosticSeverity.Warning:
                    return "console.warnicon.sml";
                case TimelineDiagnosticSeverity.Info:
                    return "console.infoicon.sml";
                default:
                    return "TestPassed";
            }
        }

        /// <summary>領域の表示名。</summary>
        public static string AreaLabel(TimelineDiagnosticArea area)
        {
            switch (area)
            {
                case TimelineDiagnosticArea.Director:
                    return "PlayableDirector";
                case TimelineDiagnosticArea.TrackBinding:
                    return "トラック binding";
                case TimelineDiagnosticArea.Bake:
                    return "Bake";
                case TimelineDiagnosticArea.Profile:
                    return "Profile 整合";
                case TimelineDiagnosticArea.ProfileBinding:
                    return "Timeline binding";
                case TimelineDiagnosticArea.LayerMatch:
                    return "トラック名とレイヤー名";
                case TimelineDiagnosticArea.LayerConnection:
                    return "レイヤー接続";
                case TimelineDiagnosticArea.Analog:
                    return "Analog";
                case TimelineDiagnosticArea.Gaze:
                    return "Gaze";
                case TimelineDiagnosticArea.Placement:
                    return "配置";
                case TimelineDiagnosticArea.Session:
                    return "再生セッション";
                default:
                    return area.ToString();
            }
        }

        /// <summary>Edit で自動再ベイクの契機になる診断か（Bake 参照の不整合 / 旧形式 / Profile 不一致）。</summary>
        public static bool IsAutoRebakeCode(TimelineDiagnosticCode code)
        {
            return code == TimelineDiagnosticCode.BakeReferenceConflict
                || code == TimelineDiagnosticCode.BakeLegacyExport
                || code == TimelineDiagnosticCode.ProfileMismatch;
        }

        /// <summary>行に「自動再ベイク中」を併記するか。</summary>
        public static bool ShowsAutoRebakeNote(TimelineDiagnosticCode code, bool autoRebakePending)
        {
            return autoRebakePending && IsAutoRebakeCode(code);
        }

        /// <summary>「旧 timeline 宣言を削除」を有効にするか（Edit で LegacyStateDeclaration があるときだけ）。</summary>
        public static bool CanRemoveLegacyDeclarations(bool isPlaying, FacialTimelineDiagnostics diagnostics)
        {
            return !isPlaying && diagnostics != null && diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration);
        }

        /// <summary>Bake 欄の隣に「トラックの Bake と異なる」を表示するか。</summary>
        public static bool ShowsOverrideDiffers(FacialTimelineDiagnostics diagnostics)
        {
            return diagnostics != null && diagnostics.Contains(TimelineDiagnosticCode.BakeOverrideDiffers);
        }

        /// <summary>「トラック binding を今設定」を有効にするか（Edit で binding 未設定の Facial トラックがあるとき）。</summary>
        public static bool CanAssignTrackBindings(bool isPlaying, ReceiverEditEvaluation evaluation)
        {
            return !isPlaying && evaluation.HasDirector && evaluation.HasTimeline && evaluation.UnboundTrackCount > 0;
        }

        /// <summary>「今再ベイク」を有効にするか（Edit で保存済みの Timeline があるとき）。</summary>
        public static bool CanRebake(bool isPlaying, ReceiverEditEvaluation evaluation)
        {
            return !isPlaying && evaluation.HasTimeline && evaluation.TimelineSaved;
        }

        /// <summary>セッション状態の表示名。</summary>
        public static string SessionStateLabel(TimelineSessionState state)
        {
            switch (state)
            {
                case TimelineSessionState.Idle:
                    return "Idle（再生セッションなし）";
                case TimelineSessionState.Pending:
                    return "Pending（FacialController の初期化待ち）";
                case TimelineSessionState.Active:
                    return "Active（再生中）";
                case TimelineSessionState.Failed:
                    return "Failed（診断の Error を直してください）";
                default:
                    return state.ToString();
            }
        }
    }
}
