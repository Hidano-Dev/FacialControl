using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialTimelineReceiver"/> の Inspector（UI Toolkit）。上書きフィールド・領域別の診断（重大度 + 件名 + 直し方）・
    /// Play 中のセッション状態・操作ボタン 3 つを表示する（D7）。
    /// </summary>
    /// <remarks>
    /// <para>Edit の評価は <see cref="FacialTimelineReceiverEditEvaluator"/>（UI 非依存）に任せ、本クラスは結果を VisualElement に写すだけ。
    /// 表示判定は <see cref="FacialTimelineReceiverInspectorModel"/>。デバウンスや再ベイクは持たない（Watcher の役割）。</para>
    /// <para>表示更新の購読（Undo / hierarchyChanged / ObjectChangeEvents / Watcher の BakeUpdated / 診断の Changed / playModeStateChanged）は
    /// このインスタンスが所有し、<see cref="CreateInspectorGUI"/> で登録、root の DetachFromPanelEvent と OnDisable で解除する
    /// （二重解除は no-op。各 handler の先頭で target の破棄を確認する）。再描画は 100 ms 後に合流させる。</para>
    /// </remarks>
    [CustomEditor(typeof(FacialTimelineReceiver))]
    public sealed class FacialTimelineReceiverInspector : UnityEditor.Editor
    {
        internal const string AssignBindingsButtonName = "facial-timeline-assign-bindings";
        internal const string RebakeButtonName = "facial-timeline-rebake";
        internal const string RemoveLegacyButtonName = "facial-timeline-remove-legacy";
        internal const string DiagnosticsContainerName = "facial-timeline-diagnostics";
        internal const string SessionContainerName = "facial-timeline-session";

        private const long RefreshDelayMs = 100;
        private const string AutoRebakeNote = "（自動再ベイク中）";

        private readonly AutoRebakeRequestGate _autoRebakeGate = new AutoRebakeRequestGate();

        private VisualElement _root;
        private VisualElement _diagnosticsContainer;
        private VisualElement _sessionContainer;
        private Label _overrideDiffersLabel;
        private Label _statusLabel;
        private Button _assignButton;
        private Button _rebakeButton;
        private Button _removeLegacyButton;
        private IVisualElementScheduledItem _scheduledRefresh;

        private bool _pendingEvaluate;
        private bool _evaluating;
        private ReceiverEditEvaluation _lastEvaluation;

        private bool _undoSubscribed;
        private bool _hierarchySubscribed;
        private bool _objectChangeSubscribed;
        private bool _playModeSubscribed;
        private TimelineEditChangeWatcher _subscribedWatcher;
        private FacialTimelineDiagnostics _subscribedDiagnostics;

        /// <summary>テスト用: 現在登録している購読の数。</summary>
        internal int SubscriptionCount =>
            (_undoSubscribed ? 1 : 0)
            + (_hierarchySubscribed ? 1 : 0)
            + (_objectChangeSubscribed ? 1 : 0)
            + (_playModeSubscribed ? 1 : 0)
            + (_subscribedWatcher != null ? 1 : 0)
            + (_subscribedDiagnostics != null ? 1 : 0);

        public override VisualElement CreateInspectorGUI()
        {
            Unsubscribe();
            _root = new VisualElement();
            _scheduledRefresh = null;

            var receiver = target as FacialTimelineReceiver;
            if (receiver == null)
            {
                return _root;
            }

            BuildOverrideSection();
            BuildDiagnosticsSection();
            BuildSessionSection();
            BuildButtons();

            _root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            Subscribe(receiver);

            _pendingEvaluate = true;
            RefreshNow();
            return _root;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>全購読を解除する。二重呼び出しは no-op。</summary>
        internal void Unsubscribe()
        {
            if (_undoSubscribed)
            {
                Undo.undoRedoPerformed -= OnUndoRedoPerformed;
                _undoSubscribed = false;
            }

            if (_hierarchySubscribed)
            {
                EditorApplication.hierarchyChanged -= OnHierarchyChanged;
                _hierarchySubscribed = false;
            }

            if (_objectChangeSubscribed)
            {
                ObjectChangeEvents.changesPublished -= OnChangesPublished;
                _objectChangeSubscribed = false;
            }

            if (_playModeSubscribed)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                _playModeSubscribed = false;
            }

            if (_subscribedWatcher != null)
            {
                _subscribedWatcher.BakeUpdated -= OnBakeUpdated;
                _subscribedWatcher = null;
            }

            if (_subscribedDiagnostics != null)
            {
                _subscribedDiagnostics.Changed -= OnDiagnosticsChanged;
                _subscribedDiagnostics = null;
            }

            _scheduledRefresh?.Pause();
        }

        /// <summary>保留中の評価を行い、表示を作り直す（予約された再描画の本体。テストからも呼ぶ）。</summary>
        internal void RefreshNow()
        {
            if (_root == null || target == null || !(target is FacialTimelineReceiver receiver))
            {
                return;
            }

            bool playing = EditorApplication.isPlaying;
            if (!playing && _pendingEvaluate)
            {
                _pendingEvaluate = false;
                _evaluating = true;
                try
                {
                    _lastEvaluation = FacialTimelineReceiverEditEvaluator.Evaluate(
                        receiver,
                        TimelineEditorServices.ChangeWatcher,
                        requestAutoRebake: true,
                        _autoRebakeGate);
                }
                finally
                {
                    _evaluating = false;
                }
            }

            Render(receiver, playing);
        }

        // ================================================================
        // 構築
        // ================================================================

        private void BuildOverrideSection()
        {
            var section = new VisualElement();
            section.style.marginBottom = 6;
            section.Add(new PropertyField(serializedObject.FindProperty("director"), "Director（上書き・任意）"));
            section.Add(new PropertyField(serializedObject.FindProperty("bakeAsset"), "Bake（上書き・任意）"));
            _overrideDiffersLabel = new Label("この Bake はトラックの Bake 参照と異なります（BakeOverrideDiffers）。意図しない場合は空にしてください。");
            _overrideDiffersLabel.style.whiteSpace = WhiteSpace.Normal;
            _overrideDiffersLabel.style.color = new Color(0.95f, 0.75f, 0.2f);
            _overrideDiffersLabel.style.display = DisplayStyle.None;
            section.Add(_overrideDiffersLabel);
            _root.Add(section);
        }

        private void BuildDiagnosticsSection()
        {
            var header = new Label("診断");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 4;
            _root.Add(header);

            _diagnosticsContainer = new VisualElement { name = DiagnosticsContainerName };
            _root.Add(_diagnosticsContainer);
        }

        private void BuildSessionSection()
        {
            _sessionContainer = new VisualElement { name = SessionContainerName };
            _sessionContainer.style.marginTop = 6;
            _sessionContainer.style.display = DisplayStyle.None;
            _root.Add(_sessionContainer);
        }

        private void BuildButtons()
        {
            var buttons = new VisualElement();
            buttons.style.marginTop = 6;

            _assignButton = new Button(OnAssignBindingsClicked) { name = AssignBindingsButtonName, text = "トラック binding を今設定" };
            _rebakeButton = new Button(OnRebakeClicked) { name = RebakeButtonName, text = "今再ベイク" };
            _removeLegacyButton = new Button(OnRemoveLegacyClicked) { name = RemoveLegacyButtonName, text = "旧 timeline 宣言を削除" };
            buttons.Add(_assignButton);
            buttons.Add(_rebakeButton);
            buttons.Add(_removeLegacyButton);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            buttons.Add(_statusLabel);
            _root.Add(buttons);
        }

        // ================================================================
        // 描画
        // ================================================================

        private void Render(FacialTimelineReceiver receiver, bool playing)
        {
            FacialTimelineDiagnostics diagnostics = receiver.Diagnostics;
            _overrideDiffersLabel.style.display = FacialTimelineReceiverInspectorModel.ShowsOverrideDiffers(diagnostics)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            RenderDiagnostics(diagnostics, autoRebakePending: !playing && _lastEvaluation.AutoRebakePending);
            RenderSession(receiver, playing);

            _assignButton.SetEnabled(FacialTimelineReceiverInspectorModel.CanAssignTrackBindings(playing, _lastEvaluation));
            _rebakeButton.SetEnabled(FacialTimelineReceiverInspectorModel.CanRebake(playing, _lastEvaluation));
            _removeLegacyButton.SetEnabled(FacialTimelineReceiverInspectorModel.CanRemoveLegacyDeclarations(playing, diagnostics));
        }

        private void RenderDiagnostics(FacialTimelineDiagnostics diagnostics, bool autoRebakePending)
        {
            _diagnosticsContainer.Clear();
            IReadOnlyList<DiagnosticAreaGroup> groups = FacialTimelineReceiverInspectorModel.GroupByArea(diagnostics.Items);
            if (groups.Count == 0)
            {
                _diagnosticsContainer.Add(new Label("診断項目はありません。"));
                return;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                DiagnosticAreaGroup group = groups[g];
                string areaLabel = FacialTimelineReceiverInspectorModel.AreaLabel(group.Area);
                var foldout = new Foldout
                {
                    text = group.IsHealthy ? areaLabel + ": 問題なし" : areaLabel,
                    value = !group.IsHealthy,
                };
                for (int i = 0; i < group.Items.Count; i++)
                {
                    foldout.Add(BuildItemRow(group.Items[i], autoRebakePending));
                }

                _diagnosticsContainer.Add(foldout);
            }
        }

        private static VisualElement BuildItemRow(TimelineDiagnosticItem item, bool autoRebakePending)
        {
            var row = new VisualElement();
            row.style.marginBottom = 2;

            var headline = new VisualElement();
            headline.style.flexDirection = FlexDirection.Row;
            var icon = new Image
            {
                image = EditorGUIUtility.IconContent(FacialTimelineReceiverInspectorModel.SeverityIconName(item.Severity)).image,
            };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 4;
            headline.Add(icon);

            string title = string.IsNullOrEmpty(item.Subject) ? item.Code.ToString() : item.Code + " — " + item.Subject;
            if (FacialTimelineReceiverInspectorModel.ShowsAutoRebakeNote(item.Code, autoRebakePending))
            {
                title += " " + AutoRebakeNote;
            }

            var titleLabel = new Label(title);
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.flexShrink = 1;
            headline.Add(titleLabel);
            row.Add(headline);

            if (!string.IsNullOrEmpty(item.Detail))
            {
                var detail = new Label(item.Detail);
                detail.style.whiteSpace = WhiteSpace.Normal;
                detail.style.marginLeft = 20;
                detail.style.opacity = 0.8f;
                row.Add(detail);
            }

            return row;
        }

        private void RenderSession(FacialTimelineReceiver receiver, bool playing)
        {
            _sessionContainer.Clear();
            _sessionContainer.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!playing)
            {
                return;
            }

            var header = new Label("再生セッション");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            _sessionContainer.Add(header);
            _sessionContainer.Add(new Label("状態: " + FacialTimelineReceiverInspectorModel.SessionStateLabel(receiver.SessionState)));

            IReadOnlyList<string> layers = receiver.ConnectedLayerNames;
            _sessionContainer.Add(new Label("接続レイヤー: " + (layers.Count == 0 ? "なし" : string.Join(", ", layers))));

            IReadOnlyList<TimelineTakeoverEntry> entries = receiver.TakeoverEntries;
            if (entries.Count == 0)
            {
                _sessionContainer.Add(new Label("乗っ取り: なし"));
                return;
            }

            _sessionContainer.Add(new Label("乗っ取り:"));
            for (int i = 0; i < entries.Count; i++)
            {
                TimelineTakeoverEntry entry = entries[i];
                var line = new Label($"  {entry.ChannelSubId} ({entry.Kind}) — {(entry.IsAttached ? "接続中" : "未接続")} / {entry.Status}");
                _sessionContainer.Add(line);
            }
        }

        // ================================================================
        // ボタン
        // ================================================================

        private void OnAssignBindingsClicked()
        {
            if (!(target is FacialTimelineReceiver receiver) || receiver == null)
            {
                return;
            }

            PlayableDirector director = _lastEvaluation.Director;
            TimelineAsset timeline = _lastEvaluation.Timeline;
            if (director == null || timeline == null)
            {
                SetStatus("Director または TimelineAsset が見つからないため設定できません。");
                return;
            }

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(director, timeline, receiver, EditorTrackBindingWriter.Instance);
            SetStatus($"トラック binding を {report.Assigned} 本設定しました（他オブジェクトを指す {report.BoundToOther.Count} 本は変更していません）。");
            RequestRefresh(evaluate: true);
        }

        private void OnRebakeClicked()
        {
            TimelineAsset timeline = _lastEvaluation.Timeline;
            if (timeline == null)
            {
                SetStatus("TimelineAsset が見つからないため再ベイクできません。");
                return;
            }

            FacialCharacterProfileSO profileAsset = _lastEvaluation.ProfileAsset;
            RebakeOutcome outcome;
            string failureReason;
            if (profileAsset != null)
            {
                outcome = TimelineBakeDirtyWatcher.RebakeNow(timeline, profileAsset, out FacialTimelineBakeAsset _, out failureReason);
            }
            else
            {
                outcome = TimelineBakeDirtyWatcher.RebakeNow(timeline, out FacialTimelineBakeAsset _, out failureReason);
            }

            SetStatus(outcome == RebakeOutcome.Failed
                ? "再ベイクに失敗しました（前回の Bake を保持します）: " + failureReason
                : "再ベイク結果: " + outcome);
            _autoRebakeGate.Reset();
            RequestRefresh(evaluate: true);
        }

        private void OnRemoveLegacyClicked()
        {
            FacialCharacterProfileSO profileAsset = _lastEvaluation.ProfileAsset;
            if (profileAsset == null)
            {
                SetStatus("Profile SO が見つからないため削除できません。");
                return;
            }

            int removed = LegacyTimelineDeclarationCleaner.RemoveStateDeclarations(
                profileAsset,
                LegacyTimelineDeclarationCleaner.ResolveSlug(profileAsset));
            SetStatus($"旧 timeline 宣言を {removed} 件削除しました（Undo で戻せます）。");
            RequestRefresh(evaluate: true);
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message ?? string.Empty;
            }
        }

        // ================================================================
        // 購読と再描画の合流
        // ================================================================

        private void Subscribe(FacialTimelineReceiver receiver)
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            _undoSubscribed = true;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            _hierarchySubscribed = true;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            _objectChangeSubscribed = true;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _playModeSubscribed = true;

            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            if (watcher != null)
            {
                watcher.BakeUpdated += OnBakeUpdated;
                _subscribedWatcher = watcher;
            }

            FacialTimelineDiagnostics diagnostics = receiver.Diagnostics;
            diagnostics.Changed += OnDiagnosticsChanged;
            _subscribedDiagnostics = diagnostics;
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            Unsubscribe();
        }

        /// <summary>再描画を 100 ms 後に予約する（予約中なら合流する）。</summary>
        private void RequestRefresh(bool evaluate)
        {
            if (_root == null || target == null)
            {
                return;
            }

            _pendingEvaluate |= evaluate;
            if (_scheduledRefresh == null)
            {
                _scheduledRefresh = _root.schedule.Execute(RefreshNow);
                _scheduledRefresh.ExecuteLater(RefreshDelayMs);
                return;
            }

            if (!_scheduledRefresh.isActive)
            {
                _scheduledRefresh.ExecuteLater(RefreshDelayMs);
            }
        }

        private void OnUndoRedoPerformed()
        {
            if (target == null)
            {
                return;
            }

            RequestRefresh(evaluate: true);
        }

        private void OnHierarchyChanged()
        {
            if (target == null)
            {
                return;
            }

            RequestRefresh(evaluate: true);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (target == null)
            {
                return;
            }

            RequestRefresh(evaluate: change == PlayModeStateChange.EnteredEditMode);
        }

        private void OnBakeUpdated(TimelineAsset timeline, FacialTimelineBakeAsset bake, TimelineDirtyReason reason)
        {
            if (target == null)
            {
                return;
            }

            RequestRefresh(evaluate: true);
        }

        private void OnDiagnosticsChanged()
        {
            // 自分の Edit 評価による変更は評価後にまとめて描くので無視する。
            if (_evaluating || target == null)
            {
                return;
            }

            RequestRefresh(evaluate: false);
        }

        private void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!(target is FacialTimelineReceiver receiver) || receiver == null)
            {
                return;
            }

            for (int i = 0; i < stream.length; i++)
            {
                int instanceId;
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs component);
                        instanceId = component.instanceId;
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out ChangeAssetObjectPropertiesEventArgs asset);
                        instanceId = asset.instanceId;
                        break;
                    default:
                        continue;
                }

                if (IsRelevant(receiver, EditorUtility.InstanceIDToObject(instanceId)))
                {
                    RequestRefresh(evaluate: true);
                    return;
                }
            }
        }

        /// <summary>Director・Profile SO・Receiver（とその GameObject のコンポーネント）・Timeline 関連の変更だけを拾う。</summary>
        private bool IsRelevant(FacialTimelineReceiver receiver, Object changed)
        {
            switch (changed)
            {
                case null:
                    return false;
                case FacialTimelineReceiver _:
                case PlayableDirector _:
                case FacialController _:
                case FacialCharacterProfileSO _:
                case TimelineAsset _:
                case FacialTimelineBakeAsset _:
                    return true;
                case TrackAsset track:
                    return track is IFacialTimelineBakeHolder;
                case GameObject gameObject:
                    return gameObject == receiver.gameObject;
                case Component component:
                    return component.gameObject == receiver.gameObject;
                default:
                    return ReferenceEquals(changed, _lastEvaluation.ProfileAsset);
            }
        }
    }
}
