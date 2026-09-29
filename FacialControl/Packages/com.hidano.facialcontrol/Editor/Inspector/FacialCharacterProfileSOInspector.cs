using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Editor.Windows.Routing;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;

namespace Hidano.FacialControl.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSO"/> 系アセットの汎用 UI Toolkit カスタム Inspector。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入力方式 (InputSystem / OSC / ARKit 等) には依存しない汎用 UI を提供する。
    /// Layers / Expressions / Reference Model / Debug 表示と自動保存（profile.json）を担当する。
    /// </para>
    /// <para>
    /// 派生クラスは <c>OnResolveDerivedSerializedProperties</c> / <c>OnBuildPreLayersSections</c> /
    /// <c>FlushAutoExport</c> をオーバーライドして入力源固有 UI や保存処理を追加する。
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(FacialCharacterProfileSO), editorForChildClasses: true)]
    public class FacialCharacterProfileSOInspector : UnityEditor.Editor
    {
        // ====================================================================
        // VisualElement の name 定数（汎用部分）
        // ====================================================================

        public const string LayersFoldoutName = "facial-character-layers-foldout";
        public const string BaseExpressionFoldoutName = "facial-character-base-expression-foldout";
        public const string BaseExpressionClipFieldName = "facial-character-base-expression-clip-field";
        public const string BaseExpressionUnsetHelpName = "facial-character-base-expression-unset-help";
        public const string GazeConfigsFoldoutName = "facial-character-gaze-configs-foldout";
        public const string DebugFoldoutName = "facial-character-debug-foldout";
        /// <summary>
        /// Adapter Bindings セクションのルート要素名。折りたたみは廃止したため
        /// <see cref="Foldout"/> ではなく素の <see cref="VisualElement"/> に付く（名前は互換のため据え置き）。
        /// </summary>
        public const string AdapterBindingsFoldoutName = "facial-character-adapter-bindings-foldout";

        public const string TabViewName = "facial-character-tabview";
        public const string TabExpressionLibraryName = "facial-character-tab-expression-library";
        public const string TabLayersName = "facial-character-tab-layers";
        public const string TabBaseExpressionName = "facial-character-tab-base-expression";
        public const string TabGazeName = "facial-character-tab-gaze";
        public const string TabAdapterBindingsName = "facial-character-tab-adapter-bindings";
        public const string TabDebugName = "facial-character-tab-debug";
        public const string TabExpressionsName = TabExpressionLibraryName;

        public const string SlotsDeclarationFoldoutName = "facial-character-slots-declaration-foldout";
        public const string SlotsInitPhonemeButtonName = "slots-init-phoneme-button";
        public const string DefaultOverlaysFoldoutName = "facial-character-default-overlays-foldout";
        public const string ExpressionLibraryFoldoutName = "facial-character-expression-library-foldout";
        public const string ExpressionLibraryAddButtonName = "facial-character-expression-library-add-button";
        public const string ExpressionThumbnailRegenerateButtonName = "facial-character-expression-thumbnail-regenerate-button";
        public const string RoutingEditorOpenButtonName = "facial-character-routing-editor-open-button";
        public const string ExpressionOverlaysSectionName = "expression-row-overlays-section";
        public const string ExpressionPhonemeOverlaysFoldoutName = "expression-row-phoneme-overlays-foldout";
        public const string ExpressionPhonemeOverlaysSummaryName = "expression-row-phoneme-overlays-summary";
        public const string ExpressionPhonemeOverlayUndeclaredSlotHelpName = "expression-row-phoneme-overlay-undeclared-slot-help";
        public const string DefaultOverlaySlotDropdownName = "default-overlay-slot-dropdown";
        public const string DefaultOverlayAnimationClipFieldName = "default-overlay-animation-clip-field";
        public const string DefaultOverlayUndeclaredSlotHelpName = "default-overlay-undeclared-slot-help";
        public const string ExpressionOverlayStateDropdownName = "expression-overlay-state-dropdown";
        public const string ExpressionOverlayAnimationClipFieldName = "expression-overlay-animation-clip-field";
        public const string ExpressionOverlayUndeclaredSlotHelpName = "expression-overlay-undeclared-slot-help";

        /// <summary>「目線」タブの表示文字列（参照モデル変更時にアスタリスクを付与する基準値）。</summary>
        public const string GazeTabBaseLabel = "目線";

        /// <summary>「目線」タブに付与する「未確認」マーカー（参照モデルが変わった直後に表示）。</summary>
        public const string GazeTabAttentionMarker = " *";

        public const string ReferenceModelDirectFieldName = "facial-character-reference-model-field";

        public const string SaveStatusBarName = "facial-character-save-status-bar";
        public const string SaveStatusLabelName = "facial-character-save-status";
        public const string ExpressionsValidationHelpName = "facial-character-expressions-validation";
        public const string DebugExpressionIdMappingTitleName = "debug-expression-id-mapping-title";
        public const string DebugExpressionIdMappingName = "debug-expression-id-mapping";
        public const string DebugExpressionIdMappingRowName = "debug-expression-id-mapping-row";
        public const string DebugExpressionIdMappingNameCellName = "debug-expression-id-mapping-name";
        public const string DebugExpressionIdMappingExpressionIdCellName = "debug-expression-id-mapping-expression-id";
        public const string DebugExpressionIdMappingKindCellName = "debug-expression-id-mapping-kind";
        public const string DebugExpressionIdMappingLayerCellName = "debug-expression-id-mapping-layer";

        public const string ReferenceModelFoldoutName = "facial-character-reference-model-foldout";

        public const string ExpressionRowNameFieldName = "expression-row-name-field";
        public const string ExpressionRowLayerDropdownName = "expression-row-layer-dropdown";
        public const string ExpressionRowClipFieldName = "expression-row-clip-field";
        public const string ExpressionRowRendererSummaryName = "expression-row-renderer-summary";
        public const string ExpressionRowValidationHelpName = "expression-row-validation-help";
        public const string ExpressionRowTransitionDurationFieldName = "expression-row-transition-duration-field";
        public const string GazeInputSourceDropdownName = "gaze-input-source-dropdown";
        public const string GazeLegacyHelpName = "facial-character-gaze-legacy-help";
        public const string GazeLegacyClearButtonName = "facial-character-gaze-legacy-clear-button";
        public const string GazeChannelsListName = "facial-character-gaze-channels-list";
        public const string GazeChannelRowName = "facial-character-gaze-channel-row";
        public const string GazeChannelIdFieldName = "facial-character-gaze-channel-id";
        public const string GazeChannelProviderDropdownName = "facial-character-gaze-channel-provider";
        public const string GazeChannelAdvancedFoldoutName = "facial-character-gaze-channel-advanced";
        public const string GazeChannelAddButtonName = "facial-character-gaze-channel-add-button";
        public const string GazeChannelIdValidationName = "facial-character-gaze-channel-id-validation";
        public const string GazeChannelDistinctToggleName = "facial-character-gaze-channel-distinct";
        public const string GazeChannelSourceIdLeftName = "facial-character-gaze-channel-source-left";
        public const string GazeChannelSourceIdRightName = "facial-character-gaze-channel-source-right";
        public const string GazeChannelLeftBonePathName = "facial-character-gaze-channel-left-bone";
        public const string GazeChannelRightBonePathName = "facial-character-gaze-channel-right-bone";
        public const string GazeChannelAutoAssignButtonName = "facial-character-gaze-channel-auto-assign";
        public const string GazeChannelBoneResolutionHelpName = "facial-character-gaze-channel-bone-resolution";
        public const string GazeConfigRemoveButtonName = "gaze-config-remove-button";
        public const string GazeConfigLookUpAngleFieldName = "gaze-config-look-up-angle";
        public const string GazeConfigLookDownAngleFieldName = "gaze-config-look-down-angle";
        public const string GazeConfigOuterYawAngleFieldName = "gaze-config-outer-yaw-angle";
        public const string GazeConfigInnerYawAngleFieldName = "gaze-config-inner-yaw-angle";

        // ====================================================================
        // 共通スタイル定数
        // ====================================================================

        protected const int HelpBoxFontSize = 12;
        protected const int SectionFoldoutFontSize = 13;

        // ====================================================================
        // SerializedProperty（汎用部分）
        // ====================================================================

        protected SerializedProperty _layersProperty;
        protected SerializedProperty _expressionsProperty;
        protected SerializedProperty _baseExpressionProperty;
        protected SerializedProperty _schemaVersionProperty;
        protected SerializedProperty _adapterBindingsProperty;
        protected SerializedProperty _slotsProperty;
        protected SerializedProperty _defaultOverlaysProperty;
        protected SerializedProperty _gazeChannelsProperty;

#if UNITY_EDITOR
        protected SerializedProperty _referenceModelProperty;
#endif

        // ====================================================================
        // VisualElement キャッシュ
        // ====================================================================

        private Label _debugSchemaVersionLabel;
        private Label _debugLayerCountLabel;
        private Label _debugExpressionCountLabel;
        private Label _debugJsonPathLabel;
        private VisualElement _debugExpressionIdMappingContainer;
        private HelpBox _expressionsValidationHelp;
        private Label _saveStatusLabel;
        private VisualElement _layersContainer;
        private VisualElement _expressionLibraryContainer;
        private Tab _gazeTab;
        // CreateInspectorGUI が返すルート要素。overlay 編集を bind 更新サイクルと衝突させないよう
        // 次ティックへ遅延実行する際の schedule / panel 判定に使用する。
        private VisualElement _rootElement;
        private readonly List<DropdownField> _slotDropdowns = new List<DropdownField>();
        private readonly List<ExpressionLayerDropdownField> _expressionLayerDropdowns = new List<ExpressionLayerDropdownField>();

        // ====================================================================
        // 候補リスト
        // ====================================================================

        protected readonly List<string> _layerNameChoices = new List<string>();
        protected readonly List<string> _slotNameChoices = new List<string>();
        private static readonly IPhonemeSlotInitializer s_phonemeSlotInitializer = new PhonemeSlotInitializer();

        protected IExpressionAnimationClipSampler _sampler;
        private bool _autoSavePending;

        // Expression 行のサムネイル生成。CreateInspectorGUI で生成し、OnDisable で破棄する
        // （プレビュー用の一時オブジェクトとテクスチャを Inspector より長く残さない）。
        private ExpressionThumbnailService _thumbnailService;

        // panel attach 時に次ティックへ遅延した overlay 編集（ApplyDefaultOverlayClipCore /
        // ApplyExpressionOverlayClipCore 等）を保持する。遅延ティックが来る前に Play 突入すると、
        // ドメインリロードで panel が破棄され schedule.Execute が実行されず、編集が確定しないまま失われる
        // （アサイン直後に Play すると Default Overlays の clip が外れる不具合の根因）。
        // ExitingEditMode / OnDisable でこのリストをフラッシュして確実に SerializedProperty へ確定する。
        private readonly List<Action> _pendingOverlayEdits = new List<Action>();

        // SessionState 永続化対象の Foldout（開閉状態を OnDisable で一括保存する）。
        private readonly List<(Foldout foldout, string key)> _persistedFoldouts =
            new List<(Foldout foldout, string key)>();
#if UNITY_EDITOR
        private GameObject _lastReferenceModel;
        private bool _isAutoAssigningGazeBones;
#endif

        // ====================================================================
        // Editor lifecycle
        // ====================================================================

        protected virtual void OnEnable()
        {
            // Play 突入直前に保留中の overlay 編集を確定するため playModeStateChanged を購読する。
            // 二重登録を避けてから登録する。
            EditorApplication.playModeStateChanged -= OnPlayModeStateChangedFlushOverlayEdits;
            EditorApplication.playModeStateChanged += OnPlayModeStateChangedFlushOverlayEdits;
        }

        protected virtual void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChangedFlushOverlayEdits;

            DisposeThumbnailService();

            // 破棄前に Foldout の開閉状態を保存し、再構築時に直前の表示状態を再現できるようにする。
            SaveFoldoutViewStates();

            // Inspector 破棄 / 別オブジェクト選択時にも保留中の overlay 編集を取りこぼさない。
            FlushPendingOverlayEdits();

            // 予約済みの自動保存も破棄前に同期確定する。破棄後に delayCall で FlushAutoSave が
            // 発火しても target が null となり何も保存されず、suppress/override 編集がメモリ上の
            // SO にだけ残って .asset / profile.json が古いまま放置される
            // （編集直後に別オブジェクトを選択すると保存が失われる不具合の根因）。
            if (_autoSavePending)
            {
                EditorApplication.delayCall -= FlushAutoSave;
                FlushAutoSave();
            }
        }

        /// <summary>
        /// Edit モード終了（= Play 突入直前）に、次ティックへ遅延された overlay 編集を確定し、
        /// 予約済みの自動保存を即フラッシュする。<c>schedule.Execute</c> はドメインリロードによる
        /// panel 破棄で実行されないため、ここでフラッシュしないとアサイン直後の clip が
        /// .asset / profile.json に保存されず外れる。
        /// </summary>
        private void OnPlayModeStateChangedFlushOverlayEdits(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;

            FlushPendingOverlayEdits();
            if (_autoSavePending)
            {
                FlushAutoSave();
            }
        }

        /// <summary>
        /// <see cref="RunOverlayEditDeferredOrImmediate"/> で次ティックへ遅延した overlay 編集
        /// （<see cref="_pendingOverlayEdits"/>）を即時に実行して確定する。
        /// 各編集コアは内部で overlayIndex / slot を再検証するため、二重実行されても安全。
        /// </summary>
        private void FlushPendingOverlayEdits()
        {
            if (_pendingOverlayEdits.Count == 0) return;

            // 実行中に再度遅延が積まれても安全なよう、スナップショットを取り Clear してから実行する。
            var pending = _pendingOverlayEdits.ToArray();
            _pendingOverlayEdits.Clear();
            for (int i = 0; i < pending.Length; i++)
            {
                if (this == null || target == null || serializedObject == null) return;
                pending[i]?.Invoke();
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            ResolveSerializedProperties();
            OnResolveDerivedSerializedProperties();
            _gazeChannelsProperty = serializedObject.FindProperty("_gazeChannels");
            _sampler = new AnimationClipExpressionSampler();
            // 前回の CreateInspectorGUI で作った行の割り当て・生成待ちを持ち越さないよう作り直す。
            DisposeThumbnailService();
            EnsureThumbnailService();

            var root = new VisualElement();
            _rootElement = root;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // BuildLayersSection は内部で MaskField の choices を _layerNameChoices から構築する。
            // そのため先に RefreshLayerNameChoices() を呼んで候補を確定させてから section を構築する。
            RefreshLayerNameChoices();
            RefreshSlotNameChoices();
            _slotDropdowns.Clear();
            _persistedFoldouts.Clear();

            BuildSaveStatusBar(root);

            // 参照モデルはタブの上に常時表示する（モデルタブを廃止してアクセス頻度を上げる）。
            BuildReferenceModelDirect(root);

            // 6 タブ構成: 表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug
            // レイヤー定義とベース表情を独立タブへ分離し、以降の overlay UI 拡張点を確保する。
            var tabView = new TabView { name = TabViewName };
            tabView.style.flexGrow = 1f;

            var expressionLibraryTab = new Tab("表情ライブラリ") { name = TabExpressionLibraryName };
            BuildSlotsDeclarationSection(expressionLibraryTab.contentContainer);
            BuildDefaultOverlaysSection(expressionLibraryTab.contentContainer);
            BuildExpressionLibrarySection(expressionLibraryTab.contentContainer);
            tabView.Add(expressionLibraryTab);

            var layersTab = new Tab("レイヤー") { name = TabLayersName };
            BuildLayersSection(layersTab.contentContainer);
            tabView.Add(layersTab);

            var baseExpressionTab = new Tab("ベース表情") { name = TabBaseExpressionName };
            BuildBaseExpressionSection(baseExpressionTab.contentContainer);
            tabView.Add(baseExpressionTab);

            _gazeTab = new Tab(GazeTabBaseLabel) { name = TabGazeName };
            BuildGazeConfigsSection(_gazeTab.contentContainer);
            // 目線タブのヘッダーをクリックした瞬間にアスタリスクを消す（参照モデル変更後の確認注意）。
            // Tab 直下のクリックでは header subelement に届かないことがあるため、capture 段階で受ける。
            _gazeTab.RegisterCallback<PointerDownEvent>(_ => ClearGazeTabAttention(), TrickleDown.TrickleDown);
            tabView.Add(_gazeTab);

            var adapterTab = new Tab("Adapter Bindings") { name = TabAdapterBindingsName };
            OnBuildPreLayersSections(adapterTab.contentContainer);
            BuildAdapterBindingsSection(adapterTab.contentContainer);
            tabView.Add(adapterTab);

            var debugTab = new Tab("Debug") { name = TabDebugName };
            BuildDebugSection(debugTab.contentContainer);
            tabView.Add(debugTab);

            root.Add(tabView);

            // domain reload / asset 再読み込みで Inspector が再構築されても、直前に選択していた
            // タブを復元する（既定は先頭の「表情ライブラリ」に戻ってしまう）。
            PersistSelectedTab(tabView, new List<Tab>
            {
                expressionLibraryTab,
                layersTab,
                baseExpressionTab,
                _gazeTab,
                adapterTab,
                debugTab,
            });

            UpdateValidation();
            UpdateDebugLabels();
            RebuildExpressionIdMapping();

            // 自動保存: SerializedObject の変更を監視
            root.TrackSerializedObjectValue(serializedObject, _ => OnSerializedObjectChanged());
            if (_slotsProperty != null)
            {
                root.TrackPropertyValue(_slotsProperty, _ => OnSlotsPropertyChanged());
            }
            if (_layersProperty != null)
            {
                root.TrackPropertyValue(_layersProperty, _ => OnLayersPropertyChanged());
            }

#if UNITY_EDITOR
            TrackReferenceModelChanges(root);
#endif

            return root;
        }

        private void ResolveSerializedProperties()
        {
            _layersProperty = serializedObject.FindProperty("_layers");
            _expressionsProperty = serializedObject.FindProperty("_expressions");
            _baseExpressionProperty = serializedObject.FindProperty("_baseExpression");
            _schemaVersionProperty = serializedObject.FindProperty("_schemaVersion");
            _adapterBindingsProperty = serializedObject.FindProperty("_adapterBindings");
            _slotsProperty = serializedObject.FindProperty("_slots");
            _defaultOverlaysProperty = serializedObject.FindProperty("_defaultOverlays");

#if UNITY_EDITOR
            _referenceModelProperty = serializedObject.FindProperty("_referenceModel");
#endif
        }

        // ====================================================================
        // 派生クラス向け hook
        // ====================================================================

        /// <summary>派生クラスで追加のシリアライズプロパティを解決する。</summary>
        protected virtual void OnResolveDerivedSerializedProperties() { }

        /// <summary>Layers セクションの直前に追加するセクション群（入力源固有 UI）。</summary>
        protected virtual void OnBuildPreLayersSections(VisualElement root) { }

        /// <summary>
        /// 自動保存時の永続化処理。既定では profile.json + AnimationClip サンプリングのみ。
        /// 派生は <c>base.FlushAutoExport(...)</c> を呼び出してから追加処理（例: analog_bindings.json）を行う。
        /// </summary>
        protected virtual void FlushAutoExport(
            FacialCharacterProfileSO so, IExpressionAnimationClipSampler sampler)
        {
            FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);
            FacialCharacterProfileExporter.ExportProfileJson(so);
        }

        // ====================================================================
        // 自動保存
        // ====================================================================

        private void BuildSaveStatusBar(VisualElement root)
        {
            var bar = new VisualElement
            {
                name = SaveStatusBarName,
            };
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 6;
            bar.style.paddingRight = 6;
            bar.style.paddingTop = 4;
            bar.style.paddingBottom = 4;
            bar.style.marginBottom = 6;
            bar.style.borderBottomColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.45f));
            bar.style.borderBottomWidth = 1;

            _saveStatusLabel = new Label("変更は自動的に保存されます")
            {
                name = SaveStatusLabelName,
            };
            _saveStatusLabel.style.flexGrow = 1f;
            _saveStatusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _saveStatusLabel.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
            bar.Add(_saveStatusLabel);

            // ルーティング編集はレイヤー・配線・スロットを横断する操作のため、
            // タブ内ではなく全タブ共通バーに常時表示する。
            // エディタ本体は com.hidano.facialcontrol.routing-editor にあり、
            // 未導入（ハンドラ未登録）ならボタン自体を出さない。
            if (RoutingEditorLauncher.IsAvailable && target is ScriptableObject routingProfile)
            {
                var routingButton = new Button(() => RoutingEditorLauncher.Open(routingProfile))
                {
                    name = RoutingEditorOpenButtonName,
                    text = "ルーティングを編集",
                };
                routingButton.AddToClassList(FacialControlStyles.ActionButton);
                bar.Add(routingButton);
            }

            root.Add(bar);
        }

        private void OnSerializedObjectChanged()
        {
            if (target == null) return;

            EditorUtility.SetDirty(target);
            UpdateValidation();
            UpdateDebugLabels();
            RebuildExpressionIdMapping();
            RefreshLayerNameChoices();

            ScheduleAutoSave();
        }

        /// <summary>
        /// 自動保存（profile.json エクスポート + アセット保存）を次の delayCall で実行するよう予約する。
        /// </summary>
        /// <remarks>
        /// SerializedProperty を経由せず managed モデルを直接書き換えるハンドラ
        /// （Overlay の Suppress/Override 切替や clip 割当など）は <c>serializedObject.Update()</c> しか
        /// 呼ばないため、<c>TrackSerializedObjectValue</c> による自動保存監視が発火しない。
        /// それらのハンドラは本メソッドを明示的に呼び、保存を確実に予約する必要がある。
        /// </remarks>
        private void ScheduleAutoSave()
        {
            if (_autoSavePending) return;

            _autoSavePending = true;
            EditorApplication.delayCall += FlushAutoSave;
            if (_saveStatusLabel != null)
            {
                _saveStatusLabel.text = "保存中…";
            }
        }

        private void FlushAutoSave()
        {
            _autoSavePending = false;
            if (target == null) return;

            try
            {
                if (target is FacialCharacterProfileSO profileSO)
                {
                    FlushAutoExport(profileSO, _sampler ?? new AnimationClipExpressionSampler());
                }
                AssetDatabase.SaveAssetIfDirty(target);

                if (_saveStatusLabel != null)
                {
                    _saveStatusLabel.text = $"保存しました ({DateTime.Now:HH:mm:ss})";
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"FacialCharacterProfileSOInspector: 自動保存に失敗しました: {ex.Message}");
                if (_saveStatusLabel != null)
                {
                    _saveStatusLabel.text = "保存エラー(コンソール参照)";
                }
            }
        }

        // ====================================================================
        // 共通ヘルパー: HelpBox / Foldout の見栄え調整
        // ====================================================================

        protected static HelpBox MakeHelpBox(string text, HelpBoxMessageType messageType = HelpBoxMessageType.Info)
        {
            var box = new HelpBox(text, messageType);
            box.style.fontSize = HelpBoxFontSize;
            box.style.marginTop = 2;
            box.style.marginBottom = 4;
            return box;
        }

        protected Foldout MakeSectionFoldout(string name, string text, bool open)
        {
            var foldout = new Foldout
            {
                name = name,
                text = text,
                value = open,
            };
            foldout.style.unityFontStyleAndWeight = FontStyle.Normal;
            foldout.style.fontSize = SectionFoldoutFontSize;
            PersistFoldoutOpenState(foldout, "section." + name, open);
            return foldout;
        }

        // ====================================================================
        // 表示状態（選択タブ / Foldout 開閉）の SessionState 永続化
        // ====================================================================

        /// <summary>
        /// Foldout の開閉状態を SessionState に保存・復元する。domain reload や asset 再読み込みで
        /// Inspector が再構築されても直前の展開状態を再現する（Editor 再起動でリセット）。
        /// </summary>
        private void PersistFoldoutOpenState(Foldout foldout, string keySuffix, bool defaultOpen)
        {
            string key = GetSessionStateKey(keySuffix);
            foldout.value = SessionState.GetBool(key, defaultOpen);
            foldout.RegisterValueChangedCallback(evt =>
            {
                // 内包する Toggle / 子 Foldout の ChangeEvent<bool> も bubble してくるため、
                // この Foldout 自身の開閉のみを保存する。
                if (evt.target == foldout)
                {
                    SessionState.SetBool(key, evt.newValue);
                }
            });
            // ChangeEvent は panel 未接続時にディスパッチされないため、破棄時の一括保存
            // （SaveFoldoutViewStates）でも確実に拾えるよう参照を保持する。
            _persistedFoldouts.Add((foldout, key));
        }

        /// <summary>
        /// 追跡中の全 Foldout の開閉状態を SessionState へ一括保存する。
        /// Inspector 破棄（OnDisable）時に呼び、ChangeEvent が届かない経路の変更も取りこぼさない。
        /// UI 再構築で同一キーの Foldout が重複しても、後から登録された（=最新の）値で上書きされる。
        /// </summary>
        private void SaveFoldoutViewStates()
        {
            for (int i = 0; i < _persistedFoldouts.Count; i++)
            {
                var (foldout, key) = _persistedFoldouts[i];
                if (foldout != null)
                {
                    SessionState.SetBool(key, foldout.value);
                }
            }
        }

        /// <summary>
        /// 選択中タブを SessionState に保存し、Inspector 再構築時に復元する。
        /// </summary>
        private void PersistSelectedTab(TabView tabView, List<Tab> tabs)
        {
            string key = GetSessionStateKey("tab");
            int savedIndex = SessionState.GetInt(key, 0);
            if (savedIndex > 0 && savedIndex < tabs.Count)
            {
                tabView.activeTab = tabs[savedIndex];
            }

            tabView.activeTabChanged += (_, next) =>
            {
                int index = tabs.IndexOf(next);
                if (index >= 0)
                {
                    SessionState.SetInt(key, index);
                }
            };
        }

        private string GetSessionStateKey(string suffix)
        {
            int id = target != null ? target.GetInstanceID() : 0;
            return $"Hidano.FacialControl.FacialCharacterProfileSOInspector.{id}.{suffix}";
        }

        /// <summary>展開状態の保存キー用に expression の id を返す（未解決時は index ベース）。</summary>
        private string GetExpressionIdAt(int exprIndex)
        {
            if (_expressionsProperty != null
                && exprIndex >= 0
                && exprIndex < _expressionsProperty.arraySize)
            {
                var idProp = _expressionsProperty
                    .GetArrayElementAtIndex(exprIndex)
                    .FindPropertyRelative("id");
                if (idProp != null && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    return idProp.stringValue;
                }
            }

            return $"index-{exprIndex}";
        }

        // ====================================================================
        // Section: Adapter Bindings（[SerializeReference] AdapterBindingBase list）
        // ====================================================================

        private void BuildAdapterBindingsSection(VisualElement root)
        {
            if (_adapterBindingsProperty == null) return;

            // タブ直下に 1 つしかないセクションのため折りたたみは設けず、内容を直接並べる。
            var section = new VisualElement { name = AdapterBindingsFoldoutName };
            section.Add(MakeHelpBox(
                "入力源（OSC / Input System / ARKit など）の Adapter Binding を登録します。"
                + "Add ボタンから利用可能な binding を追加し、各 binding は SO 内に直接保存されます。"));

            var listView = new AdapterBindingsListView(_adapterBindingsProperty);
            // Adapter Binding 追加時に Layer が自動追加された場合は、Layers セクションも再描画する。
            listView.OnLayersAutoModified += () =>
            {
                RefreshLayerNameChoices();
                RebuildLayersUI();
                UpdateValidation();
            };
            listView.Bind(serializedObject);
            section.Add(listView);

            root.Add(section);
        }

        // ====================================================================
        // Section: ベース表情
        // ====================================================================

        private void BuildBaseExpressionSection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(BaseExpressionFoldoutName, "ベース表情", open: true);

            if (_baseExpressionProperty == null)
            {
                foldout.Add(MakeHelpBox("ベース表情の保存先が見つかりません。", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            var clipProp = _baseExpressionProperty.FindPropertyRelative("animationClip");
            if (clipProp == null)
            {
                foldout.Add(MakeHelpBox("ベース表情の AnimationClip 保存先が見つかりません。", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            var clipField = new ObjectField("AnimationClip")
            {
                name = BaseExpressionClipFieldName,
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
            };
            clipField.BindProperty(clipProp);
            foldout.Add(clipField);

            var unsetHelp = MakeHelpBox(
                "ベース表情は、常時固定表情キャラや衣装固定 BlendShape など、"
                + "どのレイヤーも contribute しない BlendShape に残す初期値です。"
                + "AnimationClip を割り当てると自動保存時に cachedSnapshot が更新されます。");
            unsetHelp.name = BaseExpressionUnsetHelpName;
            SetBaseExpressionUnsetHelpVisibility(unsetHelp, clipProp.objectReferenceValue as AnimationClip);
            clipField.RegisterValueChangedCallback(evt =>
            {
                SetBaseExpressionUnsetHelpVisibility(unsetHelp, evt.newValue as AnimationClip);
            });
            foldout.Add(unsetHelp);

            root.Add(foldout);
        }

        private static void SetBaseExpressionUnsetHelpVisibility(HelpBox unsetHelp, AnimationClip clip)
        {
            if (unsetHelp == null) return;
            unsetHelp.style.display = clip == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ====================================================================
        // Section: 表情ライブラリ / Slots 宣言
        // ====================================================================

        private void BuildSlotsDeclarationSection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(SlotsDeclarationFoldoutName, "Slots 宣言", open: true);
            foldout.Add(MakeHelpBox(
                "Overlay が参照する slot 識別子を宣言します。"
                + "ここで定義した名前が Default Overlays と Expression Overlays の候補になります。"));

            if (_slotsProperty == null)
            {
                foldout.Add(MakeHelpBox("_slots の保存先が見つかりません。", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            var listView = BuildArrayListView(
                _slotsProperty,
                28f,
                () => new VisualElement(),
                BindSlotDeclarationRow);

            listView.itemsAdded += indices =>
            {
                serializedObject.Update();
                foreach (int index in indices)
                {
                    if (index < 0 || index >= _slotsProperty.arraySize) continue;
                    var slotProp = _slotsProperty.GetArrayElementAtIndex(index);
                    if (slotProp != null && string.IsNullOrWhiteSpace(slotProp.stringValue))
                    {
                        slotProp.stringValue = GenerateUniqueSlotName();
                    }
                }
                serializedObject.ApplyModifiedProperties();
                OnSlotsPropertyChanged();
                listView.Rebuild();
            };
            listView.itemsRemoved += _ => OnSlotsPropertyChanged();

            var initPhonemeButton = new Button(() =>
            {
                AddMissingPhonemeSlots();
                var indexProxy = new List<int>();
                for (int i = 0; i < _slotsProperty.arraySize; i++)
                {
                    indexProxy.Add(i);
                }

                listView.itemsSource = indexProxy;
                listView.Rebuild();
            })
            {
                name = SlotsInitPhonemeButtonName,
                text = "Phoneme slots を初期化 (a/i/u/e/o)",
            };
            initPhonemeButton.AddToClassList(FacialControlStyles.ActionButton);
            initPhonemeButton.style.alignSelf = Align.FlexStart;
            initPhonemeButton.style.marginTop = 4;
            initPhonemeButton.style.marginBottom = 4;
            foldout.Add(initPhonemeButton);

            foldout.Add(listView);
            root.Add(foldout);
        }

        private void AddMissingPhonemeSlots()
        {
            if (_slotsProperty == null) return;

            bool changed = s_phonemeSlotInitializer.EnsureReservedSlots(serializedObject);

            if (!changed) return;

            EditorUtility.SetDirty(target);
            OnSlotsPropertyChanged();
        }

        private static bool ContainsSlot(SerializedProperty slotsProperty, string slot)
        {
            if (slotsProperty == null || string.IsNullOrEmpty(slot))
            {
                return false;
            }

            for (int i = 0; i < slotsProperty.arraySize; i++)
            {
                var element = slotsProperty.GetArrayElementAtIndex(i);
                if (element != null && string.Equals(element.stringValue, slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void BindSlotDeclarationRow(VisualElement row, int slotIndex)
        {
            row.Clear();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            if (_slotsProperty == null || slotIndex < 0 || slotIndex >= _slotsProperty.arraySize)
            {
                row.Add(MakeHelpBox("Slot 行を解決できません。", HelpBoxMessageType.Warning));
                return;
            }

            var slotProp = _slotsProperty.GetArrayElementAtIndex(slotIndex);
            var slotField = new TextField("Slot")
            {
                tooltip = "overlaySlot / DefaultOverlays / Expression.Overlays から参照する識別子です。",
            };
            slotField.style.flexGrow = 1f;
            slotField.SetValueWithoutNotify(slotProp != null ? slotProp.stringValue ?? string.Empty : string.Empty);
            slotField.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                if (_slotsProperty != null && slotIndex >= 0 && slotIndex < _slotsProperty.arraySize)
                {
                    var p = _slotsProperty.GetArrayElementAtIndex(slotIndex);
                    if (p != null)
                    {
                        p.stringValue = evt.newValue ?? string.Empty;
                        serializedObject.ApplyModifiedProperties();
                        OnSlotsPropertyChanged();
                    }
                }
            });
            row.Add(slotField);
        }

        private void BuildDefaultOverlaysSection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(DefaultOverlaysFoldoutName, "Default Overlays", open: true);
            foldout.Add(MakeHelpBox(
                "slot ごとの既定 overlay clip を設定します。"
                + "slot 候補は Slots 宣言から生成されます。"));

            if (_defaultOverlaysProperty == null)
            {
                foldout.Add(MakeHelpBox("_defaultOverlays の保存先が見つかりません。", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            var rows = new VisualElement();
            rows.style.flexDirection = FlexDirection.Column;
            RebuildDefaultOverlayRows(rows);
            foldout.Add(rows);

            var addButton = new Button(() =>
            {
                serializedObject.Update();
                int newIndex = _defaultOverlaysProperty.arraySize;
                _defaultOverlaysProperty.InsertArrayElementAtIndex(newIndex);
                serializedObject.ApplyModifiedProperties();
                InitializeDefaultOverlayRow(newIndex);
                serializedObject.Update();
                RebuildDefaultOverlayRows(rows);
                RefreshAllSlotDropdownChoices();
            })
            {
                text = "+ Default Overlay を追加",
            };
            addButton.style.alignSelf = Align.FlexStart;
            addButton.style.marginTop = 4;
            foldout.Add(addButton);
            root.Add(foldout);
        }

        private void RebuildDefaultOverlayRows(VisualElement rows)
        {
            if (rows == null) return;
            rows.Clear();
            if (_defaultOverlaysProperty == null) return;

            serializedObject.Update();
            for (int i = 0; i < _defaultOverlaysProperty.arraySize; i++)
            {
                int overlayIndex = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;
                BindDefaultOverlayRow(row, overlayIndex);

                var removeButton = new Button(() =>
                {
                    serializedObject.Update();
                    if (overlayIndex >= 0 && overlayIndex < _defaultOverlaysProperty.arraySize)
                    {
                        _defaultOverlaysProperty.DeleteArrayElementAtIndex(overlayIndex);
                        serializedObject.ApplyModifiedProperties();
                        RebuildDefaultOverlayRows(rows);
                        RefreshAllSlotDropdownChoices();
                    }
                })
                {
                    text = "削除",
                };
                removeButton.style.marginLeft = 4;
                row.Add(removeButton);
                rows.Add(row);
            }
        }

        private void BindDefaultOverlayRow(VisualElement row, int overlayIndex)
        {
            row.Clear();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            if (_defaultOverlaysProperty == null
                || overlayIndex < 0
                || overlayIndex >= _defaultOverlaysProperty.arraySize)
            {
                row.Add(MakeHelpBox("Default Overlay 行を解決できません。", HelpBoxMessageType.Warning));
                return;
            }

            NormalizeDefaultOverlayBinding(overlayIndex);

            var bindingProp = _defaultOverlaysProperty.GetArrayElementAtIndex(overlayIndex);
            var slotProp = bindingProp.FindPropertyRelative("slot");
            var clipProp = bindingProp.FindPropertyRelative("animationClip");

            string currentSlot = slotProp != null ? slotProp.stringValue ?? string.Empty : string.Empty;
            bool declared = IsDeclaredSlot(currentSlot);

            if (!declared)
            {
                var help = MakeHelpBox(
                    $"Undeclared slot '{currentSlot}' is referenced by Default Overlays.",
                    HelpBoxMessageType.Warning);
                help.name = DefaultOverlayUndeclaredSlotHelpName;
                help.style.maxWidth = 260;
                row.Add(help);
            }

            var slotDropdown = new DropdownField("Slot")
            {
                name = DefaultOverlaySlotDropdownName,
            };
            slotDropdown.style.minWidth = 160;
            slotDropdown.style.flexShrink = 0;
            RegisterSlotDropdown(slotDropdown, currentSlot);
            slotDropdown.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                if (_defaultOverlaysProperty != null
                    && overlayIndex >= 0
                    && overlayIndex < _defaultOverlaysProperty.arraySize)
                {
                    var current = _defaultOverlaysProperty.GetArrayElementAtIndex(overlayIndex);
                    var p = current.FindPropertyRelative("slot");
                    if (p != null)
                    {
                        p.stringValue = evt.newValue ?? string.Empty;
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            });
            row.Add(slotDropdown);

            // AnimationClip スロットは内部ラベルを持たせず、Slot dropdown のすぐ脇に表示する
            // （ラベル込みで flexGrow すると欄が右端まで寄って見つけづらい）。
            var clipField = new OverlayAnimationClipObjectField(null)
            {
                name = DefaultOverlayAnimationClipFieldName,
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
                tooltip = "この slot の既定 overlay AnimationClip",
            };
            clipField.style.flexGrow = 1f;
            clipField.style.marginLeft = 4;
            clipField.style.display = DisplayStyle.Flex;
            if (clipProp != null)
            {
                clipField.SetValueWithoutNotify(clipProp.objectReferenceValue);
                clipField.OnValueAssigned = clip => ApplyDefaultOverlayClip(overlayIndex, clip);
            }
            else
            {
                clipField.SetEnabled(false);
            }
            row.Add(clipField);
        }

        private void InitializeDefaultOverlayRow(int overlayIndex)
        {
            var binding = EnsureDefaultOverlayBinding(overlayIndex);
            if (binding == null) return;

            if (string.IsNullOrWhiteSpace(binding.slot) && _slotNameChoices.Count > 0)
            {
                binding.slot = _slotNameChoices[0];
            }

            binding.suppress = false;
            binding.animationClip = null;
            binding.cachedSnapshot = OverlaySlotBindingSerializable.CreateEmptySnapshot();
            EditorUtility.SetDirty(target);
        }

        private void ApplyDefaultOverlayClip(int overlayIndex, AnimationClip clip)
        {
            // 実機 Inspector では bind 更新サイクルとの衝突（Assertion failed）を避けるため
            // 次ティックへ遅延実行する。テスト等の Panel 未 attach 環境では同期実行される。
            RunOverlayEditDeferredOrImmediate(
                () => ApplyDefaultOverlayClipCore(overlayIndex, clip));
        }

        /// <summary>
        /// Default overlay の AnimationClip を SerializedProperty 経由で確定する
        /// （managed 直書き + Update のみによる巻き戻り回避。ApplyExpressionOverlayStateCore のコメント参照）。
        /// 遅延実行されうるため overlayIndex は本メソッド内で再検証する。
        /// </summary>
        private void ApplyDefaultOverlayClipCore(int overlayIndex, AnimationClip clip)
        {
            serializedObject.Update();
            if (_defaultOverlaysProperty == null
                || overlayIndex < 0
                || overlayIndex >= _defaultOverlaysProperty.arraySize)
            {
                return;
            }

            var bindingProp = _defaultOverlaysProperty.GetArrayElementAtIndex(overlayIndex);
            if (bindingProp == null) return;

            int undoGroup = BeginUndoGroup("Change Default Overlay Clip");

            var suppressProp = bindingProp.FindPropertyRelative("suppress");
            var clipProp = bindingProp.FindPropertyRelative("animationClip");
            var snapshotProp = bindingProp.FindPropertyRelative("cachedSnapshot");

            if (suppressProp != null) suppressProp.boolValue = false;
            if (clipProp != null) clipProp.objectReferenceValue = clip;
            if (clip == null)
            {
                ClearOverlaySnapshotProperty(snapshotProp);
            }

            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);
            EditorUtility.SetDirty(target);
            ScheduleAutoSave();
        }

        private void NormalizeDefaultOverlayBinding(int overlayIndex)
        {
            var binding = EnsureDefaultOverlayBinding(overlayIndex);
            if (binding == null) return;

            bool changed = binding.suppress;
            binding.suppress = false;
            if (binding.animationClip == null && !OverlaySlotBindingSerializable.IsSnapshotEmpty(binding.cachedSnapshot))
            {
                binding.cachedSnapshot = OverlaySlotBindingSerializable.CreateEmptySnapshot();
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(target);
                serializedObject.Update();
            }
        }

        private OverlaySlotBindingSerializable GetDefaultOverlayBinding(int overlayIndex)
        {
            var profileSO = target as FacialCharacterProfileSO;
            if (profileSO == null || profileSO.DefaultOverlays == null) return null;
            if (overlayIndex < 0 || overlayIndex >= profileSO.DefaultOverlays.Count) return null;
            return profileSO.DefaultOverlays[overlayIndex];
        }

        private OverlaySlotBindingSerializable EnsureDefaultOverlayBinding(int overlayIndex)
        {
            var profileSO = target as FacialCharacterProfileSO;
            if (profileSO == null || profileSO.DefaultOverlays == null) return null;
            if (overlayIndex < 0 || overlayIndex >= profileSO.DefaultOverlays.Count) return null;

            if (profileSO.DefaultOverlays[overlayIndex] == null)
            {
                profileSO.DefaultOverlays[overlayIndex] = new OverlaySlotBindingSerializable();
            }

            return profileSO.DefaultOverlays[overlayIndex];
        }

        private void BuildExpressionLibrarySection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(ExpressionLibraryFoldoutName, "Expression List", open: true);

            if (_expressionsProperty == null)
            {
                foldout.Add(MakeHelpBox("_expressions storage was not found.", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            _expressionLibraryContainer = new VisualElement();
            _expressionLibraryContainer.style.flexDirection = FlexDirection.Column;
            foldout.Add(_expressionLibraryContainer);

            RebuildExpressionLibraryUI();

            var addExpressionButton = new Button(() =>
            {
                AddExpressionForLayer(ResolveDefaultExpressionLayer());
            })
            {
                name = ExpressionLibraryAddButtonName,
                text = "+ Expression",
            };
            var regenerateThumbnailsButton = new Button(() => EnsureThumbnailService()?.RegenerateAll())
            {
                name = ExpressionThumbnailRegenerateButtonName,
                text = "サムネイルを再生成",
                tooltip = "Expression のサムネイルをキャッシュから消して作り直します。",
            };
            regenerateThumbnailsButton.style.marginLeft = 6;

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4;
            buttonRow.Add(addExpressionButton);
            buttonRow.Add(regenerateThumbnailsButton);
            foldout.Add(buttonRow);

            root.Add(foldout);
        }

        /// <summary>
        /// サムネイル生成サービスを返す（無ければ作る）。OnDisable 後（Editor 破棄後）に UI から呼ばれた場合は
        /// 作らずに null を返す（破棄済み Editor が EditorApplication.update を購読し続けないように）。
        /// </summary>
        private ExpressionThumbnailService EnsureThumbnailService()
        {
            if (this == null || target == null) return null;

            if (_thumbnailService == null)
            {
                _thumbnailService = ExpressionThumbnailService.CreateDefault(
                    _sampler ?? new AnimationClipExpressionSampler());
            }
            return _thumbnailService;
        }

        private void DisposeThumbnailService()
        {
            if (_thumbnailService == null) return;

            _thumbnailService.Dispose();
            _thumbnailService = null;
        }

        private void RebuildExpressionLibraryUI()
        {
            if (_expressionLibraryContainer == null) return;

            _expressionLibraryContainer.Clear();
            _thumbnailService?.ClearBindings();
            if (_expressionsProperty == null) return;

            serializedObject.Update();
            if (_expressionsProperty.arraySize == 0)
            {
                var empty = new Label("No Expressions.");
                empty.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
                _expressionLibraryContainer.Add(empty);
                return;
            }

            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                _expressionLibraryContainer.Add(BuildExpressionRow(i));
            }
        }

        private string ResolveDefaultExpressionLayer()
        {
            const string preferredLayer = "emotion";

            RefreshLayerNameChoices();
            for (int i = 0; i < _layerNameChoices.Count; i++)
            {
                if (string.Equals(_layerNameChoices[i], preferredLayer, StringComparison.Ordinal))
                {
                    return preferredLayer;
                }
            }

            return _layerNameChoices.Count > 0 ? _layerNameChoices[0] : string.Empty;
        }

        // ====================================================================
        // Section: GazeConfigs
        // ====================================================================

        private void BuildGazeConfigsSection(VisualElement root)
        {
            var section = MakeSectionFoldout(GazeConfigsFoldoutName, "Gaze", open: true);
            var profile = target as FacialCharacterProfileSO;
            bool legacy = profile != null && profile.HasLegacyGazeConfigs;
            var help = MakeHelpBox("旧 gaze_configs を検出しました。目線チャネルを確認し、旧データをクリアしてください。", HelpBoxMessageType.Warning);
            help.name = GazeLegacyHelpName;
            help.style.display = legacy ? DisplayStyle.Flex : DisplayStyle.None;
            section.Add(help);
            var clear = new Button(() => ClearLegacyGazeConfigs(help)) { name = GazeLegacyClearButtonName, text = "旧データをクリア" };
            clear.style.display = legacy ? DisplayStyle.Flex : DisplayStyle.None;
            clear.style.alignSelf = Align.FlexStart;
            section.Add(clear);

            var channels = new VisualElement { name = GazeChannelsListName };
            channels.style.flexDirection = FlexDirection.Column;
            section.Add(channels);
            RebuildGazeChannelsUI(channels);
            var advanced = new Foldout { name = GazeChannelAdvancedFoldoutName, text = "上級設定", value = false };
            var add = new Button(() => AddGazeChannel(channels)) { name = GazeChannelAddButtonName, text = "チャネルを追加" };
            advanced.Add(add);
            section.Add(advanced);
            root.Add(section);
        }

        private void BuildGazeProviderDropdowns(VisualElement root)
        {
            if (_gazeChannelsProperty == null || !_gazeChannelsProperty.isArray) return;

            var bindings = new List<AdapterBindingBase>();
            SerializedProperty bindingsProperty = serializedObject.FindProperty("_adapterBindings");
            if (bindingsProperty != null && bindingsProperty.isArray)
            {
                for (int i = 0; i < bindingsProperty.arraySize; i++)
                {
                    AdapterBindingBase binding = bindingsProperty.GetArrayElementAtIndex(i).managedReferenceValue as AdapterBindingBase;
                    if (binding != null) bindings.Add(binding);
                }
            }

            var enumerator = new GazeProviderEnumerator();
            for (int i = 0; i < _gazeChannelsProperty.arraySize; i++)
            {
                int channelIndex = i;
                SerializedProperty channelProperty = _gazeChannelsProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProperty = channelProperty.FindPropertyRelative("id");
                SerializedProperty providerProperty = channelProperty.FindPropertyRelative("providerSlug");
                if (idProperty == null || providerProperty == null) continue;

                IReadOnlyList<GazeProviderOption> options = enumerator.Enumerate(bindings, idProperty.stringValue);
                var choices = new List<string>(options.Count);
                int selectedIndex = 0;
                for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
                {
                    choices.Add(options[optionIndex].DisplayName);
                    if (string.Equals(options[optionIndex].Slug, providerProperty.stringValue, StringComparison.Ordinal))
                        selectedIndex = optionIndex;
                }

                var dropdown = new DropdownField("入力ソース", choices, selectedIndex)
                {
                    name = channelIndex == 0
                        ? GazeInputSourceDropdownName
                        : GazeInputSourceDropdownName + "-" + channelIndex,
                };
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    int index = choices.IndexOf(evt.newValue);
                    if (index < 0 || index >= options.Count || serializedObject == null) return;
                    serializedObject.Update();
                    SerializedProperty current = serializedObject.FindProperty("_gazeChannels")
                        ?.GetArrayElementAtIndex(channelIndex)?.FindPropertyRelative("providerSlug");
                    if (current == null) return;
                    Undo.RecordObject(target, "Gaze 入力ソース変更");
                    current.stringValue = options[index].Slug;
                    serializedObject.ApplyModifiedProperties();
                });
                root.Add(dropdown);
            }
        }

        private void RebuildGazeChannelsUI(VisualElement container)
        {
            if (container == null || _gazeChannelsProperty == null || !_gazeChannelsProperty.isArray) return;
            container.Clear();
            serializedObject.Update();
            for (int i = 0; i < _gazeChannelsProperty.arraySize; i++)
                container.Add(BuildGazeChannelRow(i, container));
        }

        private VisualElement BuildGazeChannelRow(int index, VisualElement container)
        {
            var row = new VisualElement { name = GazeChannelRowName + "-" + index };
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 8;
            var channel = _gazeChannelsProperty.GetArrayElementAtIndex(index);
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            var id = channel.FindPropertyRelative("id");
            var idField = new TextField(index == 0 ? "既定チャネル (gaze)" : "チャネル ID") { name = GazeChannelIdFieldName + "-" + index };
            idField.style.flexGrow = 1f;
            idField.BindProperty(id);
            if (index == 0) idField.SetEnabled(false);
            else idField.RegisterValueChangedCallback(_ => UpdateGazeChannelIdValidation(row));
            header.Add(idField);
            if (index > 0)
            {
                var remove = new Button(() => RemoveGazeChannel(index, container)) { name = GazeConfigRemoveButtonName + "-" + index, text = "削除" };
                header.Add(remove);
            }
            row.Add(header);
            AddGazeProviderDropdown(row, index, channel);
            AddGazeChannelBoneResolutionControls(row, index, channel);
            AddGazeChannelProperty(row, channel, "leftEyeBonePath", "左目ボーン", GazeChannelLeftBonePathName);
            AddGazeChannelProperty(row, channel, "rightEyeBonePath", "右目ボーン", GazeChannelRightBonePathName);
            AddGazeChannelProperty(row, channel, "lookUpAngle", "上方向角度", GazeConfigLookUpAngleFieldName);
            AddGazeChannelProperty(row, channel, "lookDownAngle", "下方向角度", GazeConfigLookDownAngleFieldName);
            AddGazeChannelProperty(row, channel, "outerYawAngle", "外側角度", GazeConfigOuterYawAngleFieldName);
            AddGazeChannelProperty(row, channel, "innerYawAngle", "内側角度", GazeConfigInnerYawAngleFieldName);
            var advanced = new Foldout { name = GazeChannelAdvancedFoldoutName + "-" + index, text = "上級設定", value = false };
            AddGazeChannelProperty(advanced, channel, "useDistinctLeftRight", "左右を個別指定", GazeChannelDistinctToggleName);
            AddGazeChannelProperty(advanced, channel, "sourceIdLeft", "左入力 source ID", GazeChannelSourceIdLeftName);
            AddGazeChannelProperty(advanced, channel, "sourceIdRight", "右入力 source ID", GazeChannelSourceIdRightName);
            AddGazeChannelProperty(advanced, channel, "leftEyeInitialRotation", "左初期回転", null);
            AddGazeChannelProperty(advanced, channel, "rightEyeInitialRotation", "右初期回転", null);
            AddGazeChannelProperty(advanced, channel, "leftEyeYawAxisLocal", "左 yaw 軸", null);
            AddGazeChannelProperty(advanced, channel, "leftEyePitchAxisLocal", "左 pitch 軸", null);
            AddGazeChannelProperty(advanced, channel, "rightEyeYawAxisLocal", "右 yaw 軸", null);
            AddGazeChannelProperty(advanced, channel, "rightEyePitchAxisLocal", "右 pitch 軸", null);
            row.Add(advanced);
            UpdateGazeChannelIdValidation(row);
            return row;
        }

        private void AddGazeChannelBoneResolutionControls(VisualElement row, int index, SerializedProperty channel)
        {
            var button = new Button(() => ResolveGazeChannelFromReferenceModel(index))
            {
                name = GazeChannelAutoAssignButtonName + "-" + index,
                text = "参照モデルから目ボーンを自動解決",
                tooltip = "空欄の目ボーンだけを参照モデルから解決します。"
            };
            button.SetEnabled(HasReferenceModel());
            row.Add(button);
            var help = MakeHelpBox(string.Empty, HelpBoxMessageType.Warning);
            help.name = GazeChannelBoneResolutionHelpName + "-" + index;
            UpdateGazeChannelBoneResolutionHelp(help, channel);
            row.Add(help);
        }

        private void ResolveGazeChannelFromReferenceModel(int channelIndex)
        {
            if (_gazeChannelsProperty == null || channelIndex < 0 || channelIndex >= _gazeChannelsProperty.arraySize)
                return;
            serializedObject.Update();
            if (!EditorUtility.DisplayDialog("目ボーンを再解決", "既存の目ボーン設定を参照モデルで上書きしますか？", "再解決", "キャンセル"))
                return;
            Undo.RecordObject(target, "目ボーンを参照モデルから再解決");
            AutoAssignGazeBonesForChannel(_gazeChannelsProperty.GetArrayElementAtIndex(channelIndex), overwrite: true);
            serializedObject.ApplyModifiedProperties();
            RebuildGazeChannelsUI(_rootElement?.Q<VisualElement>(GazeChannelsListName));
            UpdateValidation();
        }

        private void AutoAssignGazeBonesForChannels()
        {
            if (_isAutoAssigningGazeBones || _gazeChannelsProperty == null || !HasReferenceModel()) return;
            _isAutoAssigningGazeBones = true;
            try
            {
                serializedObject.Update();
                Undo.RecordObject(target, "参照モデル変更時の目ボーン自動解決");
                for (int i = 0; i < _gazeChannelsProperty.arraySize; i++)
                    AutoAssignGazeBonesForChannel(_gazeChannelsProperty.GetArrayElementAtIndex(i));
                serializedObject.ApplyModifiedProperties();
                RebuildGazeChannelsUI(_rootElement?.Q<VisualElement>(GazeChannelsListName));
                UpdateValidation();
            }
            finally { _isAutoAssigningGazeBones = false; }
        }

        private void AutoAssignGazeBonesForChannel(SerializedProperty channel, bool overwrite = false)
        {
            if (channel == null) return;
            AutoAssignGazeBonesFromReferenceModel(
                channel.FindPropertyRelative("leftEyeBonePath"), channel.FindPropertyRelative("leftEyeInitialRotation"),
                channel.FindPropertyRelative("rightEyeBonePath"), channel.FindPropertyRelative("rightEyeInitialRotation"),
                channel.FindPropertyRelative("leftEyeYawAxisLocal"), channel.FindPropertyRelative("leftEyePitchAxisLocal"),
                channel.FindPropertyRelative("rightEyeYawAxisLocal"), channel.FindPropertyRelative("rightEyePitchAxisLocal"), overwrite);
        }

        private static void UpdateGazeChannelBoneResolutionHelp(HelpBox help, SerializedProperty channel)
        {
            if (help == null || channel == null) return;
            bool leftMissing = string.IsNullOrWhiteSpace(channel.FindPropertyRelative("leftEyeBonePath")?.stringValue);
            bool rightMissing = string.IsNullOrWhiteSpace(channel.FindPropertyRelative("rightEyeBonePath")?.stringValue);
            bool missing = leftMissing || rightMissing;
            help.text = missing ? "目ボーンを解決できない側があります。Humanoid マッピングを確認するか、ボーン path を手動入力してください。" : string.Empty;
            help.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddGazeProviderDropdown(VisualElement row, int index, SerializedProperty channel)
        {
            var bindings = new List<AdapterBindingBase>();
            var bp = serializedObject.FindProperty("_adapterBindings");
            if (bp != null && bp.isArray)
                for (int i = 0; i < bp.arraySize; i++)
                    if (bp.GetArrayElementAtIndex(i).managedReferenceValue is AdapterBindingBase b) bindings.Add(b);
            string channelId = channel.FindPropertyRelative("id")?.stringValue ?? string.Empty;
            var options = new GazeProviderEnumerator().Enumerate(bindings, channelId);
            var choices = new List<string>();
            int selected = 0;
            string current = channel.FindPropertyRelative("providerSlug")?.stringValue ?? string.Empty;
            for (int i = 0; i < options.Count; i++) { choices.Add(options[i].DisplayName); if (options[i].Slug == current) selected = i; }
            var dropdown = new DropdownField("入力ソース", choices, selected) { name = GazeChannelProviderDropdownName + "-" + index };
            dropdown.RegisterValueChangedCallback(e =>
            {
                int selectedIndex = choices.IndexOf(e.newValue);
                if (selectedIndex < 0) return;
                serializedObject.Update();
                var p = serializedObject.FindProperty("_gazeChannels").GetArrayElementAtIndex(index).FindPropertyRelative("providerSlug");
                Undo.RecordObject(target, "Gaze 入力ソース変更");
                p.stringValue = options[selectedIndex].Slug;
                serializedObject.ApplyModifiedProperties();
            });
            row.Add(dropdown);
        }

        private static void AddGazeChannelProperty(VisualElement parent, SerializedProperty channel, string propertyName, string label, string elementName)
        {
            var property = channel.FindPropertyRelative(propertyName);
            if (property == null) return;
            IBindable bindable = property.propertyType == SerializedPropertyType.Boolean ? (IBindable)new Toggle(label) :
                property.propertyType == SerializedPropertyType.Float ? new FloatField(label) :
                property.propertyType == SerializedPropertyType.Vector3 ? new Vector3Field(label) : new TextField(label);
            var field = bindable as VisualElement;
            if (!string.IsNullOrEmpty(elementName)) field.name = elementName;
            bindable.BindProperty(property);
            parent.Add(field);
        }

        private void UpdateGazeChannelIdValidation(VisualElement row)
        {
            if (row == null || _gazeChannelsProperty == null) return;
            var field = row.Q<TextField>(GazeChannelIdFieldName + "-" + row.name.Substring(row.name.LastIndexOf('-') + 1));
            if (field == null) return;
            string id = field.value ?? string.Empty;
            bool valid = GazeSourceIdConvention.IsValidChannelId(id);
            int current = _gazeChannelsProperty.arraySize;
            for (int i = 1; valid && i < current; i++)
            {
                var p = _gazeChannelsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("id");
                if (p != null && p.stringValue == id && p.stringValue != string.Empty && !row.name.EndsWith("-" + i, StringComparison.Ordinal)) valid = false;
            }
            var help = row.Q<HelpBox>(GazeChannelIdValidationName);
            if (help == null) { help = MakeHelpBox(string.Empty, HelpBoxMessageType.Error); help.name = GazeChannelIdValidationName; row.Add(help); }
            help.text = valid ? string.Empty : "チャネル ID は有効な形式で、重複しない値を指定してください。";
            help.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void AddGazeChannel(VisualElement container)
        {
            serializedObject.Update();
            int index = _gazeChannelsProperty.arraySize;
            _gazeChannelsProperty.InsertArrayElementAtIndex(index);
            var p = _gazeChannelsProperty.GetArrayElementAtIndex(index);
            p.FindPropertyRelative("id").stringValue = "channel-" + index;
            p.FindPropertyRelative("providerSlug").stringValue = string.Empty;
            Undo.RecordObject(target, "Gaze チャネル追加");
            serializedObject.ApplyModifiedProperties();
            RebuildGazeChannelsUI(container);
        }

        private void RemoveGazeChannel(int index, VisualElement container)
        {
            if (index <= 0 || _gazeChannelsProperty == null || index >= _gazeChannelsProperty.arraySize) return;
            serializedObject.Update();
            Undo.RecordObject(target, "Gaze チャネル削除");
            _gazeChannelsProperty.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildGazeChannelsUI(container);
        }

        private void ClearLegacyGazeConfigs(HelpBox help)
        {
            serializedObject.Update();
            var legacy = serializedObject.FindProperty("_legacyGazeConfigs");
            if (legacy != null && legacy.isArray)
            {
                Undo.RecordObject(target, "旧 Gaze データをクリア");
                legacy.ClearArray();
                serializedObject.ApplyModifiedProperties();
            }
            if (help != null) help.style.display = DisplayStyle.None;
            _rootElement?.Q<Button>(GazeLegacyClearButtonName)?.SetEnabled(false);
        }

        private void TrackReferenceModelChanges(VisualElement root)
        {
            if (root == null || _referenceModelProperty == null) return;

            _lastReferenceModel = GetReferenceModel();
            root.TrackPropertyValue(_referenceModelProperty, _ => OnReferenceModelPropertyChanged());
        }

        private void OnReferenceModelPropertyChanged()
        {
            var previousReferenceModel = _lastReferenceModel;
            var currentReferenceModel = GetReferenceModel();
            _lastReferenceModel = currentReferenceModel;

            if (currentReferenceModel != previousReferenceModel)
            {
                _thumbnailService?.RebindReferenceModel(currentReferenceModel);
            }


            // 参照モデルが切り替わった/設定されたら、目線タブ名にアスタリスクを付けて
            // 「ボーンパスを確認しろ」というユーザー注意を促す。
            // 自動入力はしない（前回値が残ると未設定との区別が付かなくなるため。
            // ユーザーが明示的に GazeConfig 行の「参照モデルから自動設定」ボタンで埋める想定）。
            if (currentReferenceModel != null && currentReferenceModel != previousReferenceModel)
            {
                AutoAssignGazeBonesForChannels();
            }
        }
        private void MarkGazeTabNeedsAttention()
        {
            if (_gazeTab == null) return;
            if (!string.Equals(_gazeTab.label, GazeTabBaseLabel + GazeTabAttentionMarker, StringComparison.Ordinal))
            {
                _gazeTab.label = GazeTabBaseLabel + GazeTabAttentionMarker;
            }
        }

        private void ClearGazeTabAttention()
        {
            if (_gazeTab == null) return;
            if (!string.Equals(_gazeTab.label, GazeTabBaseLabel, StringComparison.Ordinal))
            {
                _gazeTab.label = GazeTabBaseLabel;
            }
        }

        /// <summary>
        /// 参照モデルから目線ボーン名・初期角度・回転軸を <paramref name="cfg"/> に書き込む。
        /// </summary>
        /// <param name="resetRangesToDefaults">
        /// 可動範囲（lookUp / lookDown / outerYaw / innerYaw）を既定値へ戻すか。
        /// 新規作成した GazeConfig の初期化時のみ true。既存 GazeConfig の再解決では
        /// ユーザーが数値入力した調整値を失わないよう false を指定する。
        /// </param>
        private bool HasReferenceModel()
        {
            return GetReferenceModel() != null;
        }

        private GameObject GetReferenceModel()
        {
#if UNITY_EDITOR
            return _referenceModelProperty != null
                ? _referenceModelProperty.objectReferenceValue as GameObject
                : null;
#else
            return null;
#endif
        }

        private static bool IsEmptyString(SerializedProperty prop)
        {
            return prop == null || string.IsNullOrEmpty(prop.stringValue);
        }

        private void RunOverlayEditDeferredOrImmediate(Action editCore)
        {
            editCore?.Invoke();
        }

        private static int BeginUndoGroup(string groupName)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(groupName);
            return Undo.GetCurrentGroup();
        }

        private void ApplyModifiedPropertiesAndCollapseUndo(int undoGroup)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static string ReadStringProperty(SerializedProperty owner, string propertyName)
        {
            var prop = owner.FindPropertyRelative(propertyName);
            return prop != null ? prop.stringValue : string.Empty;
        }

        // ====================================================================
        // Section: レイヤー (Layers + その配下の Expressions)
        // ====================================================================

        private void BuildLayersSection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(LayersFoldoutName, "レイヤーと表情", open: true);

            foldout.Add(MakeHelpBox(
                "表情を重ねる優先順位レイヤーを定義します。"
                + "各レイヤーには所属する表情を登録し、レイヤー全体で他レイヤーへの上書き範囲を指定します。"));

            _expressionsValidationHelp = new HelpBox(string.Empty, HelpBoxMessageType.Error)
            {
                name = ExpressionsValidationHelpName,
            };
            _expressionsValidationHelp.style.fontSize = HelpBoxFontSize;
            _expressionsValidationHelp.style.display = DisplayStyle.None;
            foldout.Add(_expressionsValidationHelp);

            _layersContainer = new VisualElement();
            _layersContainer.style.flexDirection = FlexDirection.Column;
            foldout.Add(_layersContainer);

            var addLayerButton = new Button(AddLayer)
            {
                text = "+ レイヤーを追加",
            };
            addLayerButton.style.alignSelf = Align.FlexStart;
            addLayerButton.style.marginTop = 4;
            foldout.Add(addLayerButton);

            RebuildLayersUI();

            root.Add(foldout);
        }

        private void AddLayer()
        {
            serializedObject.Update();
            int newIndex = _layersProperty.arraySize;
            _layersProperty.InsertArrayElementAtIndex(newIndex);
            var layerProp = _layersProperty.GetArrayElementAtIndex(newIndex);
            var nameProp = layerProp.FindPropertyRelative("name");
            if (nameProp != null && string.IsNullOrEmpty(nameProp.stringValue))
            {
                nameProp.stringValue = "new-layer";
            }
            var priorityProp = layerProp.FindPropertyRelative("priority");
            if (priorityProp != null) priorityProp.intValue = newIndex;
            serializedObject.ApplyModifiedProperties();
            RefreshLayerNameChoices();
            RebuildLayersUI();
            RebuildExpressionLibraryUI();
            UpdateValidation();
        }

        private void RebuildLayersUI()
        {
            if (_layersContainer == null || _layersProperty == null) return;
            _layersContainer.Clear();

            serializedObject.Update();
            int layerCount = _layersProperty.arraySize;
            for (int i = 0; i < layerCount; i++)
            {
                int layerIndex = i;
                var layerCard = BuildLayerCard(layerIndex);
                _layersContainer.Add(layerCard);
            }
        }

        private VisualElement BuildLayerCard(int layerIndex)
        {
            var layerProp = _layersProperty.GetArrayElementAtIndex(layerIndex);
            var nameProp = layerProp.FindPropertyRelative("name");
            var priorityProp = layerProp.FindPropertyRelative("priority");
            var exclusionModeProp = layerProp.FindPropertyRelative("exclusionMode");
            var inputSourcesProp = layerProp.FindPropertyRelative("inputSources");

            string layerName = nameProp != null ? nameProp.stringValue : $"layer{layerIndex}";

            var card = new Foldout
            {
                text = string.IsNullOrEmpty(layerName) ? $"レイヤー [{layerIndex}]" : layerName,
                value = true,
            };
            card.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.6f, 0.9f));
            card.style.borderLeftWidth = 3;
            card.style.paddingLeft = 6;
            card.style.marginBottom = 8;

            // 名前
            if (nameProp != null)
            {
                var nameField = new TextField("名前");
                nameField.SetValueWithoutNotify(nameProp.stringValue ?? string.Empty);
                // 1 文字ごとに RebuildLayersUI を呼ぶとフォーカスが外れるため、
                // SerializedProperty の更新だけ value change で行い、候補リスト同期と
                // UI 再構築はフォーカスアウト時にまとめて実施する。
                nameField.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    var p = _layersProperty.GetArrayElementAtIndex(layerIndex).FindPropertyRelative("name");
                    if (p != null)
                    {
                        p.stringValue = evt.newValue ?? string.Empty;
                        serializedObject.ApplyModifiedProperties();
                    }
                });
                nameField.RegisterCallback<BlurEvent>(_ =>
                {
                    // レイヤー名は他レイヤーの「上書き対象」候補にも影響するため全体を再構築する
                    RefreshLayerNameChoices();
                    RebuildLayersUI();
                    RefreshAllExpressionLayerDropdownChoices();
                });
                card.Add(nameField);
            }

            if (priorityProp != null)
            {
                var priorityField = new IntegerField("優先度");
                priorityField.SetValueWithoutNotify(priorityProp.intValue);
                priorityField.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    var p = _layersProperty.GetArrayElementAtIndex(layerIndex).FindPropertyRelative("priority");
                    if (p != null) { p.intValue = evt.newValue; serializedObject.ApplyModifiedProperties(); }
                });
                card.Add(priorityField);
            }

            if (exclusionModeProp != null)
            {
                var initialMode = (ExclusionMode)exclusionModeProp.enumValueIndex;
                var modeField = new EnumField("排他モード", initialMode);
                var modeHelp = MakeHelpBox(GetExclusionModeDescription(initialMode));
                modeField.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    var p = _layersProperty.GetArrayElementAtIndex(layerIndex).FindPropertyRelative("exclusionMode");
                    if (p != null) { p.enumValueIndex = (int)(ExclusionMode)evt.newValue; serializedObject.ApplyModifiedProperties(); }
                    modeHelp.text = GetExclusionModeDescription((ExclusionMode)evt.newValue);
                });
                card.Add(modeField);
                card.Add(modeHelp);
            }

            // LayerOverrideMask: このレイヤーがアクティブな間に上書きする他レイヤー
            // 「排他モード」と一緒に管理する設定なので、入力源より上に配置する。
            var maskHelp = MakeHelpBox(
                "このレイヤーがアクティブな間、合成時に上書き対象とするレイヤーを選択します。"
                + "Nothing を選んだ場合はこのレイヤーは他レイヤーに何も上書きしません。");
            card.Add(maskHelp);

            var maskContainer = new VisualElement { name = $"layer-mask-container-{layerIndex}" };
            card.Add(maskContainer);
            RebuildLayerOverrideMaskFieldInto(maskContainer, layerIndex);

            if (inputSourcesProp != null)
            {
                var inputSourcesField = new PropertyField(inputSourcesProp, "入力源");
                inputSourcesField.Bind(serializedObject);
                card.Add(inputSourcesField);
            }

            // レイヤー削除
            var removeLayerButton = new Button(() => RemoveLayer(layerIndex))
            {
                text = "このレイヤーを削除",
            };
            removeLayerButton.style.alignSelf = Align.FlexEnd;
            removeLayerButton.style.marginTop = 8;
            card.Add(removeLayerButton);

            return card;
        }

        private static string GetExclusionModeDescription(ExclusionMode mode)
        {
            switch (mode)
            {
                case ExclusionMode.LastWins:
                    return "後勝ち: 同レイヤー内で別の表情がトリガされると、現在の表情から新しい表情へ遷移時間でクロスフェードします。";
                case ExclusionMode.Blend:
                    return "ブレンド: 同レイヤー内で同時にアクティブな表情のウェイトを加算し、最終値を 0〜1 にクランプします。";
                default:
                    return string.Empty;
            }
        }

        private void RebuildLayerOverrideMaskFieldInto(VisualElement container, int layerIndex)
        {
            var layerProp = _layersProperty.GetArrayElementAtIndex(layerIndex);
            var maskProp = layerProp.FindPropertyRelative("layerOverrideMask");
            if (maskProp == null) return;

            var maskField = new MaskField("上書きするレイヤー");
            var orderedNames = new List<string>(_layerNameChoices);
            maskField.choices = orderedNames;
            int maskValue = ReadMaskValueFromSerializedList(maskProp, orderedNames);
            maskField.SetValueWithoutNotify(maskValue);
            maskField.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                var p = _layersProperty.GetArrayElementAtIndex(layerIndex).FindPropertyRelative("layerOverrideMask");
                if (p != null)
                {
                    WriteMaskValueToSerializedList(p, evt.newValue, orderedNames);
                    serializedObject.ApplyModifiedProperties();
                    UpdateValidation();
                }
            });
            container.Add(maskField);
        }

        private void RemoveLayer(int layerIndex)
        {
            if (!EditorUtility.DisplayDialog(
                "レイヤーを削除", "このレイヤーと、所属する全表情を削除します。よろしいですか？", "削除", "キャンセル"))
            {
                return;
            }

            serializedObject.Update();
            if (layerIndex < 0 || layerIndex >= _layersProperty.arraySize) return;

            int undoGroup = BeginUndoGroup("Remove Layer with Expressions");
            string layerName = string.Empty;
            var layerProp = _layersProperty.GetArrayElementAtIndex(layerIndex);
            var nameProp = layerProp.FindPropertyRelative("name");
            if (nameProp != null) layerName = nameProp.stringValue ?? string.Empty;

            // 所属する表情を削除
            for (int i = _expressionsProperty.arraySize - 1; i >= 0; i--)
            {
                var exprElem = _expressionsProperty.GetArrayElementAtIndex(i);
                var exprLayerProp = exprElem.FindPropertyRelative("layer");
                if (exprLayerProp != null
                    && string.Equals(exprLayerProp.stringValue, layerName, StringComparison.Ordinal))
                {
                    var idProp = exprElem.FindPropertyRelative("id");
                    if (idProp != null)
                    {
                    }
                    _expressionsProperty.DeleteArrayElementAtIndex(i);
                }
            }

            _layersProperty.DeleteArrayElementAtIndex(layerIndex);
            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);

            RefreshLayerNameChoices();
            RebuildLayersUI();
            RebuildExpressionLibraryUI();
            UpdateValidation();
        }

        private void AddExpressionForLayer(string layerName)
        {
            serializedObject.Update();
            int newIndex = _expressionsProperty.arraySize;
            _expressionsProperty.InsertArrayElementAtIndex(newIndex);
            var entryProp = _expressionsProperty.GetArrayElementAtIndex(newIndex);

            var idProp = entryProp.FindPropertyRelative("id");
            var nameProp = entryProp.FindPropertyRelative("name");
            var layerProp = entryProp.FindPropertyRelative("layer");

            string newId = Guid.NewGuid().ToString("N");
            if (idProp != null) idProp.stringValue = newId;
            if (nameProp != null) nameProp.stringValue = "新規表情";
            if (layerProp != null) layerProp.stringValue = layerName ?? string.Empty;

            serializedObject.ApplyModifiedProperties();

            RebuildLayersUI();
            RebuildExpressionLibraryUI();
            RebuildExpressionIdMapping();
            UpdateValidation();
        }

        private void ChangeExpressionLayer(int exprIndex, string layerName)
        {
            if (_expressionsProperty == null) return;

            serializedObject.Update();
            if (exprIndex < 0 || exprIndex >= _expressionsProperty.arraySize) return;

            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var layerProp = entryProp.FindPropertyRelative("layer");
            if (layerProp == null) return;

            string nextLayer = layerName ?? string.Empty;
            if (string.Equals(layerProp.stringValue, nextLayer, StringComparison.Ordinal)) return;

            int undoGroup = BeginUndoGroup("Change Expression Layer");
            layerProp.stringValue = nextLayer;
            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);

            RebuildLayersUI();
            RebuildExpressionLibraryUI();
            RebuildExpressionIdMapping();
            UpdateValidation();
        }

        // ====================================================================
        // Expression 行
        // ====================================================================

        private VisualElement BuildExpressionRow(int exprIndex)
        {
            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var idProp = entryProp.FindPropertyRelative("id");
            var nameProp = entryProp.FindPropertyRelative("name");
            var layerProp = entryProp.FindPropertyRelative("layer");
            var transitionDurationProp = entryProp.FindPropertyRelative("transitionDuration");

            // Id 自動採番
            if (idProp != null && string.IsNullOrEmpty(idProp.stringValue))
            {
                idProp.stringValue = Guid.NewGuid().ToString("N");
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 8;
            row.style.paddingLeft = 6;
            row.style.borderLeftColor = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            row.style.borderLeftWidth = 2;

            // ヘッダー行: 左にサムネイル、右に削除ボタン。「目線操作」トグルは AnimationClip スロット直下に移動した。
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.FlexStart;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.marginBottom = 4;

            var thumbnailView = new ExpressionThumbnailView();
            headerRow.Add(thumbnailView);
            var clipPropForThumbnail = entryProp.FindPropertyRelative("animationClip");
            BindExpressionThumbnail(
                thumbnailView,
                clipPropForThumbnail != null ? clipPropForThumbnail.objectReferenceValue as AnimationClip : null);

            var removeButton = new Button(() => RemoveExpression(exprIndex))
            {
                text = "削除",
            };
            removeButton.style.marginLeft = 6;
            headerRow.Add(removeButton);

            row.Add(headerRow);

            // 名前
            var nameField = new TextField("名前")
            {
                name = ExpressionRowNameFieldName,
            };
            if (nameProp != null)
            {
                nameField.SetValueWithoutNotify(nameProp.stringValue ?? string.Empty);
                nameField.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    var p = _expressionsProperty.GetArrayElementAtIndex(exprIndex).FindPropertyRelative("name");
                    if (p != null)
                    {
                        p.stringValue = evt.newValue ?? string.Empty;
                        serializedObject.ApplyModifiedProperties();
                        RebuildExpressionIdMapping();
                    }
                });
            }
            row.Add(nameField);

            var layerDropdown = new ExpressionLayerDropdownField
            {
                label = "Layer",
                name = ExpressionRowLayerDropdownName,
            };
            layerDropdown.style.minWidth = 180;
            if (layerProp != null)
            {
                string currentLayer = layerProp.stringValue ?? string.Empty;
                RegisterExpressionLayerDropdown(layerDropdown, currentLayer);
                layerDropdown.OnValueAssigned = value =>
                {
                    ChangeExpressionLayer(exprIndex, value ?? string.Empty);
                };
            }
            else
            {
                layerDropdown.SetEnabled(false);
            }
            row.Add(layerDropdown);

            // 遷移時間。目線操作では概念がないため非表示にする (データは互換目的で保持)。
            var transitionDurationField = new Slider("遷移時間 (秒)", 0f, 1f)
            {
                name = ExpressionRowTransitionDurationFieldName,
                showInputField = true,
            };
            if (transitionDurationProp != null)
            {
                transitionDurationField.BindProperty(transitionDurationProp);
            }
            row.Add(transitionDurationField);

            // GazeConfig は専用セクションで opt-in 編集するため、Expression 行では共通 clip のみ表示する。
            // BuildAnimationClipFields は AnimationClip スロットの直下に「目線操作」Toggle を配置する。
            BuildAnimationClipFields(row, exprIndex);

            var overlaysProp = entryProp.FindPropertyRelative("overlays");
            row.Add(BuildOverlaysSectionForExpression(overlaysProp, exprIndex));

            // Validation
            var validationHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning)
            {
                name = ExpressionRowValidationHelpName,
            };
            validationHelp.style.fontSize = HelpBoxFontSize;
            validationHelp.style.display = DisplayStyle.None;
            row.Add(validationHelp);

            UpdateRowValidation(row, exprIndex);

            return row;
        }

        private void BindExpressionThumbnail(ExpressionThumbnailView view, AnimationClip clip)
        {
            if (view == null) return;

            var service = EnsureThumbnailService();
            if (service == null)
            {
                view.ShowStatus(ExpressionThumbnailView.FailedMessage);
                return;
            }

            service.Bind(view, GetReferenceModel(), clip);
        }

        private VisualElement BuildOverlaysSectionForExpression(SerializedProperty overlaysProp, int exprIndex)
        {
            var section = new Foldout
            {
                name = ExpressionOverlaysSectionName,
                text = "Overlays",
                value = true,
            };
            section.style.marginTop = 6;
            PersistFoldoutOpenState(
                section, $"expression.{GetExpressionIdAt(exprIndex)}.overlays", defaultOpen: true);

            if (overlaysProp == null)
            {
                section.Add(MakeHelpBox("Expression.Overlays の保存先が見つかりません。", HelpBoxMessageType.Warning));
                return section;
            }

            var slots = CollectOverlaySlotsForExpression(overlaysProp);
            if (slots.Count == 0)
            {
                var empty = new Label("宣言済み slot はありません。");
                empty.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
                section.Add(empty);
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (!PhonemeOverlaySlots.IsReserved(slots[i]))
                {
                    section.Add(BuildExpressionOverlayRow(overlaysProp, exprIndex, slots[i]));
                }
            }

            section.Add(BuildPhonemeOverlaysFoldout(overlaysProp, exprIndex));
            return section;
        }

        private VisualElement BuildPhonemeOverlaysFoldout(SerializedProperty overlaysProp, int exprIndex)
        {
            var foldout = new Foldout
            {
                name = ExpressionPhonemeOverlaysFoldoutName,
                text = "Phoneme Overlays",
                value = false,
            };
            foldout.style.marginTop = 6;
            PersistFoldoutOpenState(
                foldout, $"expression.{GetExpressionIdAt(exprIndex)}.phoneme-overlays", defaultOpen: false);

            var summary = new Label(BuildPhonemeOverlaySummaryText(overlaysProp, exprIndex))
            {
                name = ExpressionPhonemeOverlaysSummaryName,
            };
            summary.style.marginBottom = 4;
            summary.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
            foldout.Add(summary);

            foreach (var slot in PhonemeOverlaySlots.ReservedNames)
            {
                if (IsDeclaredSlot(slot))
                {
                    foldout.Add(BuildExpressionOverlayRow(overlaysProp, exprIndex, slot));
                    continue;
                }

                var help = MakeHelpBox(
                    $"Slots section に '{slot}' を追加すると編集できます。",
                    HelpBoxMessageType.Info);
                help.name = ExpressionPhonemeOverlayUndeclaredSlotHelpName;
                foldout.Add(help);
            }

            return foldout;
        }

        private string BuildPhonemeOverlaySummaryText(SerializedProperty overlaysProp, int exprIndex)
        {
            int declared = 0;
            int overrides = 0;
            int suppress = 0;

            foreach (var slot in PhonemeOverlaySlots.ReservedNames)
            {
                if (!IsDeclaredSlot(slot))
                {
                    continue;
                }

                declared++;
                int bindingIndex = FindOverlayBindingIndex(overlaysProp, slot);
                SerializedProperty bindingProp = bindingIndex >= 0
                    ? overlaysProp.GetArrayElementAtIndex(bindingIndex)
                    : null;
                var state = ReadExpressionOverlayBindingState(exprIndex, slot, bindingProp);
                if (state == OverlaySlotBindingState.Override)
                {
                    overrides++;
                }
                else if (state == OverlaySlotBindingState.Suppress)
                {
                    suppress++;
                }
            }

            return $"{declared}/5 declared (override={overrides}, suppress={suppress})";
        }

        private VisualElement BuildExpressionOverlayRow(
            SerializedProperty overlaysProp,
            int exprIndex,
            string slot)
        {
            int bindingIndex = FindOverlayBindingIndex(overlaysProp, slot);
            SerializedProperty bindingProp = bindingIndex >= 0
                ? overlaysProp.GetArrayElementAtIndex(bindingIndex)
                : null;
            var state = ReadExpressionOverlayBindingState(exprIndex, slot, bindingProp);
            bool declared = IsDeclaredSlot(slot);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginTop = 4;
            row.style.marginBottom = 4;
            row.style.paddingLeft = 6;

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;

            var slotLabel = new Label(string.IsNullOrEmpty(slot) ? "(未設定 slot)" : slot);
            slotLabel.style.minWidth = 120;
            slotLabel.style.whiteSpace = WhiteSpace.Normal;
            line.Add(slotLabel);

            var stateDropdown = new OverlayStateDropdownField
            {
                name = ExpressionOverlayStateDropdownName,
                choices = new List<string>(OverlayStateChoices),
            };
            stateDropdown.SetValueWithoutNotify(ToOverlayStateChoice(state));
            stateDropdown.style.minWidth = 110;
            stateDropdown.style.flexShrink = 0;
            line.Add(stateDropdown);

            // AnimationClip スロットは内部ラベルを持たせず、状態 dropdown のすぐ脇に表示する
            // （ラベル込みで flexGrow すると欄が右端まで寄って見つけづらい）。
            var clipField = new OverlayAnimationClipObjectField(null)
            {
                name = ExpressionOverlayAnimationClipFieldName,
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
                tooltip = "Override 時に適用する AnimationClip",
            };
            clipField.style.flexGrow = 1f;
            clipField.style.marginLeft = 4;
            clipField.style.display = state == OverlaySlotBindingState.Override
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (bindingProp != null)
            {
                var clipProp = bindingProp.FindPropertyRelative("animationClip");
                if (clipProp != null)
                {
                    clipField.SetValueWithoutNotify(clipProp.objectReferenceValue);
                }
            }
            line.Add(clipField);

            stateDropdown.OnValueAssigned = value =>
            {
                var newState = FromOverlayStateChoice(value);
                ApplyExpressionOverlayState(exprIndex, slot, newState, clipField);
                clipField.style.display = newState == OverlaySlotBindingState.Override
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            };
            clipField.OnValueAssigned = clip => ApplyExpressionOverlayClip(exprIndex, slot, clip);

            row.Add(line);

            if (!declared)
            {
                var help = MakeHelpBox(
                    $"未宣言 slot '{slot}' を参照しています。Slots 宣言に追加するか、この overlay を削除してください。",
                    HelpBoxMessageType.Warning);
                help.name = ExpressionOverlayUndeclaredSlotHelpName;
                row.Add(help);
            }

            return row;
        }

        private OverlaySlotBindingState ReadExpressionOverlayBindingState(
            int exprIndex,
            string slot,
            SerializedProperty bindingProp)
        {
            var binding = GetExpressionOverlayBinding(exprIndex, slot);
            if (binding != null)
            {
                return binding.GetState();
            }

            return ReadOverlayBindingState(bindingProp);
        }

        private List<string> CollectOverlaySlotsForExpression(SerializedProperty overlaysProp)
        {
            var slots = new List<string>(_slotNameChoices.Count + (overlaysProp != null ? overlaysProp.arraySize : 0));
            for (int i = 0; i < _slotNameChoices.Count; i++)
            {
                if (!slots.Contains(_slotNameChoices[i])) slots.Add(_slotNameChoices[i]);
            }

            if (overlaysProp == null) return slots;

            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                var bindingProp = overlaysProp.GetArrayElementAtIndex(i);
                var slotProp = bindingProp.FindPropertyRelative("slot");
                var slot = slotProp != null ? slotProp.stringValue ?? string.Empty : string.Empty;
                if (!slots.Contains(slot)) slots.Add(slot);
            }

            return slots;
        }

        private static OverlaySlotBindingState ReadOverlayBindingState(SerializedProperty bindingProp)
        {
            if (bindingProp == null) return OverlaySlotBindingState.DefaultFallback;

            var suppressProp = bindingProp.FindPropertyRelative("suppress");
            if (suppressProp != null && suppressProp.boolValue)
            {
                return OverlaySlotBindingState.Suppress;
            }

            var clipProp = bindingProp.FindPropertyRelative("animationClip");
            if (clipProp != null && clipProp.objectReferenceValue != null)
            {
                return OverlaySlotBindingState.Override;
            }

            return OverlaySlotBindingState.DefaultFallback;
        }

        /// <summary>Overlay 状態 dropdown の choices（表示順固定）。</summary>
        private static readonly string[] OverlayStateChoices = { "Default", "Suppress", "Override" };

        private static string ToOverlayStateChoice(OverlaySlotBindingState state)
        {
            switch (state)
            {
                case OverlaySlotBindingState.Suppress:
                    return "Suppress";
                case OverlaySlotBindingState.Override:
                    return "Override";
                default:
                    return "Default";
            }
        }

        private static OverlaySlotBindingState FromOverlayStateChoice(string choice)
        {
            switch (choice)
            {
                case "Suppress":
                    return OverlaySlotBindingState.Suppress;
                case "Override":
                    return OverlaySlotBindingState.Override;
                default:
                    return OverlaySlotBindingState.DefaultFallback;
            }
        }

        private void ApplyExpressionOverlayState(
            int exprIndex,
            string slot,
            OverlaySlotBindingState state,
            ObjectField clipField)
        {
            // 実機 Inspector では bind 更新サイクルとの衝突（Assertion failed）を避けるため
            // 次ティックへ遅延実行する。テスト等の Panel 未 attach 環境では同期実行される。
            RunOverlayEditDeferredOrImmediate(
                () => ApplyExpressionOverlayStateCore(exprIndex, slot, state, clipField));
        }

        /// <summary>
        /// Expression overlay の状態（Suppress / Override / DefaultFallback）を SerializedProperty 経由で確定する。
        /// </summary>
        /// <remarks>
        /// SerializedProperty 経由で編集し ApplyModifiedProperties() で確定する。
        /// managed モデルを直接書き換えて serializedObject.Update() のみで終えると、
        /// bound な ListView / ObjectField が保持する SerializedObject の内部キャッシュが
        /// 巻き戻り前の値（例: suppress=0）のまま残り、Domain Reload 直前の最終
        /// ApplyModifiedProperties で managed の新値を古い値で上書きしてしまう
        /// （Suppress 設定が Play 突入で .asset 上 1→0 に戻る不具合の根因）。
        /// 遅延実行されうるため exprIndex / slot は本メソッド内で再検証する。
        /// </remarks>
        private void ApplyExpressionOverlayStateCore(
            int exprIndex,
            string slot,
            OverlaySlotBindingState state,
            ObjectField clipField)
        {
            serializedObject.Update();
            var bindingProp = GetOrCreateExpressionOverlayBindingProperty(exprIndex, slot);
            if (bindingProp == null) return;

            int undoGroup = BeginUndoGroup("Change Expression Overlay State");

            var suppressProp = bindingProp.FindPropertyRelative("suppress");
            var clipProp = bindingProp.FindPropertyRelative("animationClip");
            var snapshotProp = bindingProp.FindPropertyRelative("cachedSnapshot");

            switch (state)
            {
                case OverlaySlotBindingState.Suppress:
                    if (suppressProp != null) suppressProp.boolValue = true;
                    if (clipProp != null) clipProp.objectReferenceValue = null;
                    ClearOverlaySnapshotProperty(snapshotProp);
                    if (clipField != null) clipField.SetValueWithoutNotify(null);
                    break;
                case OverlaySlotBindingState.Override:
                    if (suppressProp != null) suppressProp.boolValue = false;
                    break;
                default:
                    if (suppressProp != null) suppressProp.boolValue = false;
                    if (clipProp != null) clipProp.objectReferenceValue = null;
                    ClearOverlaySnapshotProperty(snapshotProp);
                    if (clipField != null) clipField.SetValueWithoutNotify(null);
                    break;
            }

            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);
            EditorUtility.SetDirty(target);
            ScheduleAutoSave();
        }

        private void ApplyExpressionOverlayClip(int exprIndex, string slot, AnimationClip clip)
        {
            // 実機 Inspector では bind 更新サイクルとの衝突（Assertion failed）を避けるため
            // 次ティックへ遅延実行する。テスト等の Panel 未 attach 環境では同期実行される。
            RunOverlayEditDeferredOrImmediate(
                () => ApplyExpressionOverlayClipCore(exprIndex, slot, clip));
        }

        /// <summary>
        /// Expression overlay の AnimationClip を SerializedProperty 経由で確定する
        /// （managed 直書き + Update のみによる巻き戻り回避。ApplyExpressionOverlayStateCore のコメント参照）。
        /// 遅延実行されうるため exprIndex / slot は本メソッド内で再検証する。
        /// </summary>
        private void ApplyExpressionOverlayClipCore(int exprIndex, string slot, AnimationClip clip)
        {
            serializedObject.Update();
            var bindingProp = GetOrCreateExpressionOverlayBindingProperty(exprIndex, slot);
            if (bindingProp == null) return;

            int undoGroup = BeginUndoGroup("Change Expression Overlay Clip");

            var suppressProp = bindingProp.FindPropertyRelative("suppress");
            var clipProp = bindingProp.FindPropertyRelative("animationClip");
            var snapshotProp = bindingProp.FindPropertyRelative("cachedSnapshot");

            if (suppressProp != null) suppressProp.boolValue = false;
            if (clipProp != null) clipProp.objectReferenceValue = clip;
            if (clip == null)
            {
                ClearOverlaySnapshotProperty(snapshotProp);
            }

            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);

            // cachedSnapshot のベイクは AnimationClip サンプリングを伴い SerializedProperty では
            // 表現しづらいため、確定後に managed モデルへ反映してから再度確定する。
            if (clip != null && target is FacialCharacterProfileSO profileSO)
            {
                serializedObject.Update();
                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(
                    profileSO,
                    _sampler ?? new AnimationClipExpressionSampler());
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(target);
            ScheduleAutoSave();
        }

        /// <summary>
        /// overlay snapshot SerializedProperty を空（blendShapes/bones/rendererPaths を 0 件）にする。
        /// </summary>
        private static void ClearOverlaySnapshotProperty(SerializedProperty snapshotProp)
        {
            if (snapshotProp == null) return;

            var blendShapesProp = snapshotProp.FindPropertyRelative("blendShapes");
            if (blendShapesProp != null && blendShapesProp.isArray) blendShapesProp.ClearArray();

            var bonesProp = snapshotProp.FindPropertyRelative("bones");
            if (bonesProp != null && bonesProp.isArray) bonesProp.ClearArray();

            var rendererPathsProp = snapshotProp.FindPropertyRelative("rendererPaths");
            if (rendererPathsProp != null && rendererPathsProp.isArray) rendererPathsProp.ClearArray();
        }

        /// <summary>
        /// 指定 expression / slot の overlay binding を表す SerializedProperty を取得する。
        /// 存在しない場合は overlays 配列へ新規要素を <c>InsertArrayElementAtIndex</c> で追加し
        /// slot を設定してから返す（配列構造変更も SerializedObject 経由で確定する）。
        /// 戻り値の編集後は呼び出し側で <c>ApplyModifiedProperties()</c> を必ず実行すること。
        /// </summary>
        private SerializedProperty GetOrCreateExpressionOverlayBindingProperty(int exprIndex, string slot)
        {
            if (_expressionsProperty == null) return null;
            if (exprIndex < 0 || exprIndex >= _expressionsProperty.arraySize) return null;

            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var overlaysProp = entryProp.FindPropertyRelative("overlays");
            if (overlaysProp == null || !overlaysProp.isArray) return null;

            int existingIndex = FindOverlayBindingIndex(overlaysProp, slot);
            if (existingIndex >= 0)
            {
                return overlaysProp.GetArrayElementAtIndex(existingIndex);
            }

            int newIndex = overlaysProp.arraySize;
            overlaysProp.InsertArrayElementAtIndex(newIndex);
            var newBindingProp = overlaysProp.GetArrayElementAtIndex(newIndex);

            // InsertArrayElementAtIndex は直前要素を複製するため、各フィールドを明示的に初期化する。
            var slotProp = newBindingProp.FindPropertyRelative("slot");
            if (slotProp != null) slotProp.stringValue = slot;
            var suppressProp = newBindingProp.FindPropertyRelative("suppress");
            if (suppressProp != null) suppressProp.boolValue = false;
            var clipProp = newBindingProp.FindPropertyRelative("animationClip");
            if (clipProp != null) clipProp.objectReferenceValue = null;
            ClearOverlaySnapshotProperty(newBindingProp.FindPropertyRelative("cachedSnapshot"));

            return newBindingProp;
        }

        private OverlaySlotBindingSerializable GetExpressionOverlayBinding(int exprIndex, string slot)
        {
            var profileSO = target as FacialCharacterProfileSO;
            if (profileSO == null || profileSO.Expressions == null) return null;
            if (exprIndex < 0 || exprIndex >= profileSO.Expressions.Count) return null;

            var expression = profileSO.Expressions[exprIndex];
            if (expression == null || expression.overlays == null) return null;

            for (int i = 0; i < expression.overlays.Count; i++)
            {
                var binding = expression.overlays[i];
                if (binding != null && string.Equals(binding.slot, slot, StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            return null;
        }

        private void BuildAnimationClipFields(VisualElement row, int exprIndex)
        {
            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var clipProp = entryProp.FindPropertyRelative("animationClip");

            var clipField = new ExpressionClipObjectField
            {
                name = ExpressionRowClipFieldName,
                label = "AnimationClip",
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
            };
            if (clipProp != null)
            {
                clipField.BindProperty(clipProp);
                clipField.RegisterCallback<ChangeEvent<UnityEngine.Object>>(evt =>
                {
                    OnClipChanged(exprIndex, evt.newValue as AnimationClip, row);
                });
                clipField.OnValueAssigned = newValue =>
                {
                    OnClipChanged(exprIndex, newValue as AnimationClip, row);
                };
                clipField.RefreshDisplayLabel();
            }
            row.Add(clipField);

            var rendererSummary = new ListView
            {
                name = ExpressionRowRendererSummaryName,
                fixedItemHeight = 18f,
                showBorder = true,
                selectionType = SelectionType.None,
                showAddRemoveFooter = false,
                showFoldoutHeader = true,
                headerTitle = "影響する SkinnedMeshRenderer",
                reorderable = false,
            };
            rendererSummary.SetEnabled(false);
            rendererSummary.style.minHeight = 60f;
            row.Add(rendererSummary);

            RefreshRendererSummary(row, clipProp != null ? clipProp.objectReferenceValue as AnimationClip : null);
        }

        private void RemoveExpression(int exprIndex)
        {
            serializedObject.Update();
            if (exprIndex < 0 || exprIndex >= _expressionsProperty.arraySize) return;

            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var idProp = entryProp.FindPropertyRelative("id");
            string id = idProp != null ? idProp.stringValue : string.Empty;

            int undoGroup = BeginUndoGroup("Remove Expression");
            _expressionsProperty.DeleteArrayElementAtIndex(exprIndex);
            ApplyModifiedPropertiesAndCollapseUndo(undoGroup);

            RebuildLayersUI();
            RebuildExpressionLibraryUI();
            RebuildExpressionIdMapping();
            UpdateValidation();
        }

        // ====================================================================
        // GazeConfig ヘルパー
        // ====================================================================

        // ====================================================================
        // 目線ボーン: 参照モデルからの自動設定
        // ====================================================================

        /// <summary>
        /// 参照モデル (<c>_referenceModel</c>) の Animator から左右目ボーンを解決し、
        /// 対応する SerializedProperty にボーン名と現在の localEulerAngles を書き込む。
        /// Humanoid Avatar 優先、不在時は名前検索でフォールバック。
        /// </summary>
        private void AutoAssignGazeBonesFromReferenceModel(
            SerializedProperty leftBonePathProp,
            SerializedProperty leftInitRotProp,
            SerializedProperty rightBonePathProp,
            SerializedProperty rightInitRotProp,
            SerializedProperty leftYawAxisProp,
            SerializedProperty leftPitchAxisProp,
            SerializedProperty rightYawAxisProp,
            SerializedProperty rightPitchAxisProp,
            bool overwrite = false)
        {
            if (_referenceModelProperty == null)
            {
                Debug.LogWarning("[FacialCharacterProfileSOInspector] 参照モデル property が解決できません。");
                return;
            }

            var referenceModel = _referenceModelProperty.objectReferenceValue as GameObject;
            if (referenceModel == null)
            {
                Debug.LogWarning(
                    "[FacialCharacterProfileSOInspector] 参照モデルが未割り当てです。"
                    + " Inspector の「参照モデル」セクションで GameObject を割り当ててから再実行してください。");
                return;
            }

            var animator = referenceModel.GetComponentInChildren<Animator>(includeInactive: true);
            if (animator == null)
                Debug.LogWarning("[FacialControl] 参照モデルに Animator がないため、参照モデル root 起点で目ボーン path を保存します。");

            Transform leftEye = null;
            Transform rightEye = null;

            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

            if (leftEye == null) leftEye = FindEyeTransformByName(referenceModel.transform, isLeft: true);
            if (rightEye == null) rightEye = FindEyeTransformByName(referenceModel.transform, isLeft: false);

            serializedObject.Update();

            int assigned = 0;
            if (leftEye != null)
            {
                string path = GetBonePath(animator, referenceModel.transform, leftEye);
                if ((overwrite || string.IsNullOrWhiteSpace(leftBonePathProp?.stringValue)) && !string.IsNullOrEmpty(path))
                {
                    leftBonePathProp.stringValue = path;
                    if (leftInitRotProp != null) leftInitRotProp.vector3Value = leftEye.localEulerAngles;
                    AssignParentLocalAxes(leftEye, leftYawAxisProp, leftPitchAxisProp);
                    assigned++;
                }
            }
            else
            {
                Debug.LogWarning("[FacialCharacterProfileSOInspector] 左目ボーンを参照モデルから解決できませんでした。Humanoid マッピング・命名規則を確認してください。");
            }

            if (rightEye != null)
            {
                string path = GetBonePath(animator, referenceModel.transform, rightEye);
                if ((overwrite || string.IsNullOrWhiteSpace(rightBonePathProp?.stringValue)) && !string.IsNullOrEmpty(path))
                {
                    rightBonePathProp.stringValue = path;
                    if (rightInitRotProp != null) rightInitRotProp.vector3Value = rightEye.localEulerAngles;
                    AssignParentLocalAxes(rightEye, rightYawAxisProp, rightPitchAxisProp);
                    assigned++;
                }
            }
            else
            {
                Debug.LogWarning("[FacialCharacterProfileSOInspector] 右目ボーンを参照モデルから解決できませんでした。Humanoid マッピング・命名規則を確認してください。");
            }

            if (assigned > 0)
            {
                serializedObject.ApplyModifiedProperties();
                Debug.Log(
                    $"[FacialCharacterProfileSOInspector] 参照モデル '{referenceModel.name}' から目ボーンを自動設定しました"
                    + $" (Left: {(leftEye != null ? leftEye.name : "(skip)")}, Right: {(rightEye != null ? rightEye.name : "(skip)")}).");
            }
        }

        private static string GetBonePath(Animator animator, Transform referenceRoot, Transform bone)
        {
            if (bone == null) return null;
            var names = new List<string>();
            var current = bone;
            var origin = animator != null ? animator.transform : referenceRoot;
            while (current != null && current != origin)
            {
                names.Add(current.name);
                current = current.parent;
            }
            if (current != origin) return null;
            names.Reverse();
            return string.Join("/", names);
        }

        private static void AssignParentLocalAxes(
            Transform eyeBone,
            SerializedProperty yawAxisProp,
            SerializedProperty pitchAxisProp)
        {
            if (eyeBone == null) return;

            Vector3 yawAxisLocal = Vector3.up;
            Vector3 pitchAxisLocal = Vector3.right;

            var parent = eyeBone.parent;
            if (parent != null)
            {
                yawAxisLocal = parent.InverseTransformDirection(Vector3.up);
                pitchAxisLocal = parent.InverseTransformDirection(Vector3.right);
            }

            if (yawAxisLocal.sqrMagnitude > 1e-8f) yawAxisLocal = yawAxisLocal.normalized;
            if (pitchAxisLocal.sqrMagnitude > 1e-8f) pitchAxisLocal = pitchAxisLocal.normalized;

            if (yawAxisProp != null) yawAxisProp.vector3Value = yawAxisLocal;
            if (pitchAxisProp != null) pitchAxisProp.vector3Value = pitchAxisLocal;
        }

        private static Transform FindEyeTransformByName(Transform root, bool isLeft)
        {
            if (root == null) return null;
            var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);

            string exact = isLeft ? "LeftEye" : "RightEye";
            string sidePrefix = isLeft ? "l_" : "r_";
            string sideKeyword = isLeft ? "left" : "right";

            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (string.Equals(t.name, exact, StringComparison.OrdinalIgnoreCase)) return t;
            }
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (t.name.StartsWith(sidePrefix, StringComparison.OrdinalIgnoreCase)
                    && t.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                var n = t.name;
                if (n.IndexOf(sideKeyword, StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }

        // ====================================================================
        // Section: 参照モデル（タブ外、常時表示）
        // ====================================================================

        private void BuildReferenceModelDirect(VisualElement root)
        {
#if UNITY_EDITOR
            if (_referenceModelProperty == null) return;

            var refModelField = new PropertyField(_referenceModelProperty, "参照モデル")
            {
                name = ReferenceModelDirectFieldName,
                tooltip = "BlendShape 名やボーン名の取得元となるモデル。"
                    + "AnimationClip の RendererPath 検証にも利用されます。",
            };
            refModelField.style.marginBottom = 6;
            root.Add(refModelField);
#endif
        }

        // ====================================================================
        // Section: デバッグ情報
        // ====================================================================

        private void BuildDebugSection(VisualElement root)
        {
            var foldout = MakeSectionFoldout(DebugFoldoutName, "状態", open: false);

            _debugSchemaVersionLabel = new Label();
            _debugSchemaVersionLabel.AddToClassList(FacialControlStyles.InfoLabel);
            foldout.Add(_debugSchemaVersionLabel);

            _debugLayerCountLabel = new Label();
            _debugLayerCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            foldout.Add(_debugLayerCountLabel);

            _debugExpressionCountLabel = new Label();
            _debugExpressionCountLabel.AddToClassList(FacialControlStyles.InfoLabel);
            foldout.Add(_debugExpressionCountLabel);

            _debugJsonPathLabel = new Label();
            _debugJsonPathLabel.AddToClassList(FacialControlStyles.InfoLabel);
            _debugJsonPathLabel.style.whiteSpace = WhiteSpace.Normal;
            foldout.Add(_debugJsonPathLabel);

            var mappingTitle = new Label("Expression ID マッピング")
            {
                name = DebugExpressionIdMappingTitleName,
            };
            mappingTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mappingTitle.style.marginTop = 6;
            foldout.Add(mappingTitle);

            _debugExpressionIdMappingContainer = new VisualElement
            {
                name = DebugExpressionIdMappingName,
            };
            _debugExpressionIdMappingContainer.style.flexDirection = FlexDirection.Column;
            foldout.Add(_debugExpressionIdMappingContainer);

            RebuildExpressionIdMapping();

            root.Add(foldout);
        }

        private void UpdateDebugLabels()
        {
            var so = target as FacialCharacterProfileSO;
            if (so == null) return;

            if (_debugSchemaVersionLabel != null)
            {
                _debugSchemaVersionLabel.text = $"スキーマバージョン: {(string.IsNullOrEmpty(so.SchemaVersion) ? "(未設定)" : so.SchemaVersion)}";
            }
            if (_debugLayerCountLabel != null)
            {
                _debugLayerCountLabel.text = $"レイヤー数: {(so.Layers != null ? so.Layers.Count : 0)}";
            }
            if (_debugExpressionCountLabel != null)
            {
                _debugExpressionCountLabel.text = $"表情数: {(so.Expressions != null ? so.Expressions.Count : 0)}";
            }
            if (_debugJsonPathLabel != null)
            {
                var assetName = so.CharacterAssetName;
                var path = string.IsNullOrEmpty(assetName)
                    ? "(SO 名未設定)"
                    : Path.Combine(
                        UnityEngine.Application.streamingAssetsPath,
                        FacialCharacterProfileSO.StreamingAssetsRootFolder,
                        assetName,
                        FacialCharacterProfileSO.ProfileJsonFileName);
                _debugJsonPathLabel.text = $"JSON 出力先: {path}";
            }
        }

        private void RebuildExpressionIdMapping()
        {
            if (_debugExpressionIdMappingContainer == null) return;

            _debugExpressionIdMappingContainer.Clear();
            _debugExpressionIdMappingContainer.Add(BuildExpressionIdMappingHeaderRow());

            if (_expressionsProperty == null) return;

            serializedObject.Update();
            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                var expr = _expressionsProperty.GetArrayElementAtIndex(i);
                _debugExpressionIdMappingContainer.Add(BuildExpressionIdMappingRow(expr));
            }
        }

        private VisualElement BuildExpressionIdMappingHeaderRow()
        {
            var row = BuildExpressionIdMappingRowContainer();
            row.Add(BuildExpressionIdMappingCell("name", null, bold: true));
            row.Add(BuildExpressionIdMappingCell("expressionId", null, bold: true));
            row.Add(BuildExpressionIdMappingCell("種別", null, bold: true));
            row.Add(BuildExpressionIdMappingCell("layer", null, bold: true));
            return row;
        }

        private VisualElement BuildExpressionIdMappingRow(SerializedProperty expr)
        {
            string kindLabel = "通常";

            var row = BuildExpressionIdMappingRowContainer();
            row.Add(BuildExpressionIdMappingCell(
                ReadStringProperty(expr, "name"),
                DebugExpressionIdMappingNameCellName));
            row.Add(BuildExpressionIdMappingCell(
                ReadStringProperty(expr, "id"),
                DebugExpressionIdMappingExpressionIdCellName));
            row.Add(BuildExpressionIdMappingCell(kindLabel, DebugExpressionIdMappingKindCellName));
            row.Add(BuildExpressionIdMappingCell(
                ReadStringProperty(expr, "layer"),
                DebugExpressionIdMappingLayerCellName));
            return row;
        }

        private static VisualElement BuildExpressionIdMappingRowContainer()
        {
            var row = new VisualElement
            {
                name = DebugExpressionIdMappingRowName,
            };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            return row;
        }

        private static Label BuildExpressionIdMappingCell(string text, string name, bool bold = false)
        {
            var label = new Label(text ?? string.Empty)
            {
                name = name,
            };
            label.style.minWidth = 96;
            label.style.flexGrow = 1f;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            return label;
        }

        // ====================================================================
        // Validation
        // ====================================================================

        private void UpdateValidation()
        {
            if (_expressionsProperty == null) return;
            serializedObject.Update();

            var errors = new List<string>();

            // 重複 Id 検出
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                var elem = _expressionsProperty.GetArrayElementAtIndex(i);
                var idProp = elem.FindPropertyRelative("id");
                var id = idProp != null ? idProp.stringValue : string.Empty;
                if (string.IsNullOrEmpty(id)) continue;
                if (!seenIds.Add(id))
                {
                    errors.Add($"表情 ID '{id}' が重複しています。");
                }
            }

            // layerOverrideMask が空（= Nothing）はユーザーの明示的な選択として
            // 「このレイヤーは他レイヤーに何も上書きしない」と扱うため、警告にしない。

            // AnimationClip null タリー (通常表情のみ。目線操作は AnimationClip 任意)
            int nullClipCount = 0;
            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                var elem = _expressionsProperty.GetArrayElementAtIndex(i);
                var clipP = elem.FindPropertyRelative("animationClip");
                if (clipP != null && clipP.objectReferenceValue == null) nullClipCount++;
            }
            if (nullClipCount > 0)
            {
                errors.Add($"AnimationClip が未割当の通常表情が {nullClipCount} 件あります。");
            }

            if (_expressionsValidationHelp != null)
            {
                if (errors.Count == 0)
                {
                    _expressionsValidationHelp.text = string.Empty;
                    _expressionsValidationHelp.style.display = DisplayStyle.None;
                }
                else
                {
                    _expressionsValidationHelp.text = string.Join("\n", errors);
                    _expressionsValidationHelp.style.display = DisplayStyle.Flex;
                }
            }

            // 各行 validation
            if (_expressionLibraryContainer != null)
            {
                UpdateAllRowValidations();
            }
        }

        private void UpdateAllRowValidations()
        {
            if (_expressionLibraryContainer == null || _expressionsProperty == null) return;

            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                if (i < _expressionLibraryContainer.childCount)
                {
                    UpdateRowValidation(_expressionLibraryContainer[i], i);
                }
            }
        }

        private void UpdateRowValidation(VisualElement rowElement, int exprIndex)
        {
            if (rowElement == null || _expressionsProperty == null) return;
            var help = rowElement.Q<HelpBox>(ExpressionRowValidationHelpName);
            if (help == null) return;
            var entryProp = _expressionsProperty.GetArrayElementAtIndex(exprIndex);
            var clipProp = entryProp.FindPropertyRelative("animationClip");
            var messages = new List<string>();
            if (clipProp == null || clipProp.objectReferenceValue == null)
                messages.Add("AnimationClip が未割り当てです。");
            var mismatchMessage = BuildRendererPathMismatchMessage(clipProp != null ? clipProp.objectReferenceValue as AnimationClip : null);
            if (!string.IsNullOrEmpty(mismatchMessage)) messages.Add(mismatchMessage);
            help.text = string.Join("\n", messages);
            help.style.display = messages.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }
        private void OnClipChanged(int index, AnimationClip newClip, VisualElement rowElement)
        {
            if (_expressionsProperty == null
                || index < 0
                || index >= _expressionsProperty.arraySize)
            {
                return;
            }

            serializedObject.Update();
            var entryProp = _expressionsProperty.GetArrayElementAtIndex(index);
            var clipProp = entryProp.FindPropertyRelative("animationClip");
            var nameProp = entryProp.FindPropertyRelative("name");

            if (clipProp != null) clipProp.objectReferenceValue = newClip;

            if (newClip != null && nameProp != null && string.IsNullOrEmpty(nameProp.stringValue))
            {
                nameProp.stringValue = ResolveAnimationClipFileName(newClip);
            }
            serializedObject.ApplyModifiedProperties();

            RefreshRendererSummary(rowElement, newClip);
            if (rowElement != null)
            {
                BindExpressionThumbnail(rowElement.Q<ExpressionThumbnailView>(ExpressionThumbnailView.ElementName), newClip);

                var nameField = rowElement.Q<TextField>(ExpressionRowNameFieldName);
                if (nameField != null && nameProp != null)
                {
                    nameField.SetValueWithoutNotify(nameProp.stringValue ?? string.Empty);
                }
                var clipField = rowElement.Q<ExpressionClipObjectField>(ExpressionRowClipFieldName);
                if (clipField != null) clipField.RefreshDisplayLabel();

                UpdateRowValidation(rowElement, index);
            }
        }

        private static string ResolveAnimationClipFileName(AnimationClip clip)
        {
            if (clip == null) return string.Empty;
            var path = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            return clip.name;
        }

        private void RefreshRendererSummary(VisualElement rowElement, AnimationClip clip)
        {
            if (rowElement == null) return;
            var summary = rowElement.Q<ListView>(ExpressionRowRendererSummaryName);
            if (summary == null) return;

            List<string> rendererPaths = new List<string>();
            if (clip != null && _sampler != null)
            {
                try
                {
                    var clipSummary = _sampler.SampleSummary(clip);
                    if (clipSummary.RendererPaths != null)
                    {
                        for (int i = 0; i < clipSummary.RendererPaths.Count; i++)
                        {
                            rendererPaths.Add(clipSummary.RendererPaths[i]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"FacialCharacterProfileSOInspector: SampleSummary に失敗: {ex.Message}");
                }
            }
            summary.itemsSource = rendererPaths;
            summary.makeItem = () => new Label();
            summary.bindItem = (el, i) =>
            {
                if (el is Label label && i >= 0 && i < rendererPaths.Count)
                {
                    label.text = rendererPaths[i];
                }
            };
            summary.Rebuild();

            if (clip == null)
            {
                summary.headerTitle = "影響する SkinnedMeshRenderer (AnimationClip 未割当)";
            }
            else
            {
                summary.headerTitle = $"影響する SkinnedMeshRenderer ({rendererPaths.Count} 件)";
            }
        }

        // ====================================================================
        // 共通 ListView ビルダ（派生クラスからも利用）
        // ====================================================================

        protected ListView BuildArrayListView(
            SerializedProperty arrayProperty,
            float itemHeight,
            Func<VisualElement> makeItem,
            Action<VisualElement, int> bindItem)
        {
            var indexProxy = new List<int>();
            for (int i = 0; i < arrayProperty.arraySize; i++) indexProxy.Add(i);

            var listView = new ListView
            {
                fixedItemHeight = itemHeight,
                itemsSource = indexProxy,
                showAddRemoveFooter = true,
                showBorder = true,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                showFoldoutHeader = false,
                makeItem = makeItem,
                bindItem = bindItem,
            };
            listView.style.marginTop = 4;
            listView.style.minHeight = 80f;

            listView.itemsAdded += indices =>
            {
                serializedObject.Update();
                int addCount = 0;
                foreach (var _ in indices) addCount++;
                arrayProperty.arraySize += addCount;
                serializedObject.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arrayProperty);
                listView.Rebuild();
            };
            listView.itemsRemoved += indices =>
            {
                serializedObject.Update();
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    var removeIndex = sorted[i];
                    if (removeIndex >= 0 && removeIndex < arrayProperty.arraySize)
                    {
                        arrayProperty.DeleteArrayElementAtIndex(removeIndex);
                    }
                }
                serializedObject.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arrayProperty);
                listView.Rebuild();
            };

            return listView;
        }

        private static void RebuildIndexProxy(List<int> indexProxy, SerializedProperty arrayProperty)
        {
            indexProxy.Clear();
            for (int i = 0; i < arrayProperty.arraySize; i++) indexProxy.Add(i);
        }

        // ====================================================================
        // 候補キャッシュ更新
        // ====================================================================

        protected void RefreshLayerNameChoices()
        {
            _layerNameChoices.Clear();
            if (_layersProperty == null) return;
            for (int i = 0; i < _layersProperty.arraySize; i++)
            {
                var elem = _layersProperty.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var name = nameProp != null ? nameProp.stringValue : null;
                if (!string.IsNullOrEmpty(name)) _layerNameChoices.Add(name);
            }
        }

        protected void RefreshSlotNameChoices()
        {
            _slotNameChoices.Clear();
            if (_slotsProperty == null) return;

            serializedObject.Update();
            for (int i = 0; i < _slotsProperty.arraySize; i++)
            {
                var elem = _slotsProperty.GetArrayElementAtIndex(i);
                var name = elem != null ? elem.stringValue : null;
                if (!string.IsNullOrEmpty(name) && !_slotNameChoices.Contains(name))
                {
                    _slotNameChoices.Add(name);
                }
            }
        }

        private List<string> BuildLayerDropdownChoices(string currentValue)
        {
            var choices = new List<string>(_layerNameChoices.Count + 1);
            for (int i = 0; i < _layerNameChoices.Count; i++)
            {
                if (!choices.Contains(_layerNameChoices[i]))
                {
                    choices.Add(_layerNameChoices[i]);
                }
            }

            if (!string.IsNullOrEmpty(currentValue) && !choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            return choices;
        }

        private void OnLayersPropertyChanged()
        {
            RefreshLayerNameChoices();
            RefreshAllExpressionLayerDropdownChoices();
        }

        private void RegisterExpressionLayerDropdown(ExpressionLayerDropdownField dropdown, string currentValue)
        {
            if (dropdown == null) return;
            ApplyExpressionLayerDropdownChoices(dropdown, currentValue);
            if (!_expressionLayerDropdowns.Contains(dropdown))
            {
                _expressionLayerDropdowns.Add(dropdown);
            }
        }

        private void RefreshAllExpressionLayerDropdownChoices()
        {
            for (int i = _expressionLayerDropdowns.Count - 1; i >= 0; i--)
            {
                var dropdown = _expressionLayerDropdowns[i];
                if (dropdown == null || dropdown.parent == null)
                {
                    _expressionLayerDropdowns.RemoveAt(i);
                    continue;
                }

                ApplyExpressionLayerDropdownChoices(dropdown, dropdown.value);
            }
        }

        private void ApplyExpressionLayerDropdownChoices(ExpressionLayerDropdownField dropdown, string currentValue)
        {
            var choices = BuildLayerDropdownChoices(currentValue);
            dropdown.choices = choices;
            dropdown.SetValueWithoutNotify(currentValue ?? string.Empty);
            dropdown.SetEnabled(choices.Count > 0);
        }

        private void OnSlotsPropertyChanged()
        {
            RefreshSlotNameChoices();
            RefreshAllSlotDropdownChoices();
        }

        private void RegisterSlotDropdown(DropdownField dropdown, string currentValue)
        {
            if (dropdown == null) return;
            ApplySlotDropdownChoices(dropdown, currentValue);
            if (!_slotDropdowns.Contains(dropdown))
            {
                _slotDropdowns.Add(dropdown);
            }
        }

        private void RefreshAllSlotDropdownChoices()
        {
            for (int i = _slotDropdowns.Count - 1; i >= 0; i--)
            {
                var dropdown = _slotDropdowns[i];
                if (dropdown == null || dropdown.parent == null)
                {
                    _slotDropdowns.RemoveAt(i);
                    continue;
                }

                ApplySlotDropdownChoices(dropdown, dropdown.value);
            }
        }

        private void ApplySlotDropdownChoices(DropdownField dropdown, string currentValue)
        {
            var choices = BuildSlotDropdownChoices(currentValue);
            dropdown.choices = choices;

            string value = currentValue ?? string.Empty;
            if (choices.Count > 0 && !choices.Contains(value))
            {
                value = choices[0];
            }
            dropdown.SetValueWithoutNotify(value);
            dropdown.SetEnabled(choices.Count > 0);
        }

        private List<string> BuildSlotDropdownChoices(string currentValue)
        {
            var choices = new List<string>(_slotNameChoices.Count + 1);
            for (int i = 0; i < _slotNameChoices.Count; i++)
            {
                if (!choices.Contains(_slotNameChoices[i]))
                {
                    choices.Add(_slotNameChoices[i]);
                }
            }

            if (!string.IsNullOrEmpty(currentValue) && !choices.Contains(currentValue))
            {
                choices.Add(currentValue);
            }

            return choices;
        }

        private string GenerateUniqueSlotName()
        {
            const string baseName = "slot";
            if (!_slotNameChoices.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + i;
                if (!_slotNameChoices.Contains(candidate))
                {
                    return candidate;
                }
            }

            return Guid.NewGuid().ToString("N");
        }

        private bool IsDeclaredSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return false;
            for (int i = 0; i < _slotNameChoices.Count; i++)
            {
                if (string.Equals(_slotNameChoices[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int FindOverlayBindingIndex(SerializedProperty overlaysProp, string slot)
        {
            if (overlaysProp == null || !overlaysProp.isArray) return -1;

            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                var binding = overlaysProp.GetArrayElementAtIndex(i);
                var slotProp = binding.FindPropertyRelative("slot");
                var candidate = slotProp != null ? slotProp.stringValue ?? string.Empty : string.Empty;
                if (string.Equals(candidate, slot ?? string.Empty, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        protected List<string> CollectExpressionIds()
        {
            var ids = new List<string>();
            if (_expressionsProperty == null) return ids;
            for (int i = 0; i < _expressionsProperty.arraySize; i++)
            {
                var elem = _expressionsProperty.GetArrayElementAtIndex(i);
                var idProp = elem.FindPropertyRelative("id");
                if (idProp != null && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    ids.Add(idProp.stringValue);
                }
            }
            return ids;
        }

        protected static List<string> BuildSafeChoices(IReadOnlyList<string> baseChoices, string currentValue)
        {
            var result = new List<string>(baseChoices.Count + 2);
            result.Add(string.Empty);
            for (int i = 0; i < baseChoices.Count; i++)
            {
                if (!result.Contains(baseChoices[i])) result.Add(baseChoices[i]);
            }
            if (!string.IsNullOrEmpty(currentValue) && !result.Contains(currentValue))
            {
                result.Add(currentValue);
            }
            return result;
        }

        // ====================================================================
        // Mask <-> List<string> 変換
        // ====================================================================

        protected static int ReadMaskValueFromSerializedList(SerializedProperty listProp, IReadOnlyList<string> orderedLayerNames)
        {
            if (listProp == null || !listProp.isArray || orderedLayerNames == null || orderedLayerNames.Count == 0) return 0;
            int result = 0;
            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var name = elem.stringValue;
                if (string.IsNullOrEmpty(name)) continue;
                for (int b = 0; b < maxBits; b++)
                {
                    if (string.Equals(orderedLayerNames[b], name, StringComparison.Ordinal))
                    {
                        result |= 1 << b;
                        break;
                    }
                }
            }
            return result;
        }

        protected static void WriteMaskValueToSerializedList(SerializedProperty listProp, int maskValue, IReadOnlyList<string> orderedLayerNames)
        {
            if (listProp == null || !listProp.isArray) return;
            listProp.ClearArray();
            if (orderedLayerNames == null || orderedLayerNames.Count == 0 || maskValue == 0) return;
            int maxBits = orderedLayerNames.Count > 32 ? 32 : orderedLayerNames.Count;
            for (int b = 0; b < maxBits; b++)
            {
                if ((maskValue & (1 << b)) != 0)
                {
                    listProp.InsertArrayElementAtIndex(listProp.arraySize);
                    var elem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
                    elem.stringValue = orderedLayerNames[b];
                }
            }
        }

        // ====================================================================
        // RendererPath mismatch 判定 (AnimationClip)
        // ====================================================================

        private string BuildRendererPathMismatchMessage(AnimationClip clip)
        {
            if (clip == null || _sampler == null) return null;
            var profileSO = target as FacialCharacterProfileSO;
            var referenceModel = profileSO != null ? profileSO.ReferenceModel : null;
            if (referenceModel == null) return null;

            HashSet<string> modelPaths;
            try { modelPaths = CollectReferenceModelRendererPaths(referenceModel); }
            catch (Exception) { return null; }
            if (modelPaths.Count == 0)
            {
                return $"参照モデル '{referenceModel.name}' に SkinnedMeshRenderer が見つかりません。";
            }

            List<string> rendererPaths;
            try
            {
                var summary = _sampler.SampleSummary(clip);
                rendererPaths = summary.RendererPaths != null ? new List<string>(summary.RendererPaths) : new List<string>();
            }
            catch (Exception) { return null; }

            var invalid = new List<string>();
            for (int i = 0; i < rendererPaths.Count; i++)
            {
                var path = rendererPaths[i] ?? string.Empty;
                if (!modelPaths.Contains(path)) invalid.Add(string.IsNullOrEmpty(path) ? "(ルート)" : path);
            }
            if (invalid.Count == 0) return null;

            return $"AnimationClip の RendererPath [{string.Join(", ", invalid)}] が参照モデル内の SkinnedMeshRenderer と一致しません。"
                + $" 参照モデル候補: [{string.Join(", ", modelPaths)}]";
        }

        private static HashSet<string> CollectReferenceModelRendererPaths(GameObject model)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (model == null) return result;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0) continue;
                var path = AnimationUtility.CalculateTransformPath(smr.transform, model.transform) ?? string.Empty;
                result.Add(path);
            }
            return result;
        }

        // ====================================================================
        // AnimationClip 表示用 ObjectField
        // ====================================================================

        private sealed class OverlayStateDropdownField : DropdownField
        {
            public Action<string> OnValueAssigned;

            public override string value
            {
                get => base.value;
                set
                {
                    base.value = value;
                    OnValueAssigned?.Invoke(value);
                }
            }
        }

        private sealed class OverlayAnimationClipObjectField : ObjectField
        {
            public Action<AnimationClip> OnValueAssigned;

            public OverlayAnimationClipObjectField(string label)
                : base(label)
            {
            }

            public override UnityEngine.Object value
            {
                get => base.value;
                set
                {
                    var previous = base.value;
                    base.value = value;
                    if (!ReferenceEquals(previous, value))
                    {
                        OnValueAssigned?.Invoke(value as AnimationClip);
                    }
                }
            }
        }

        private sealed class ExpressionLayerDropdownField : DropdownField
        {
            public Action<string> OnValueAssigned;

            public override string value
            {
                get => base.value;
                set
                {
                    var previous = base.value;
                    base.value = value;
                    if (!string.Equals(previous, value, StringComparison.Ordinal))
                    {
                        OnValueAssigned?.Invoke(value);
                    }
                }
            }
        }

        /// <summary>
        /// AnimationClip 名ではなく、プロジェクトのファイル名（拡張子なし）を表示するカスタム ObjectField。
        /// AnimationClip を複製・編集した場合に object 名がファイル名と一致しなくなる Unity の挙動を回避する。
        /// </summary>
        protected sealed class ExpressionClipObjectField : ObjectField
        {
            public Action<UnityEngine.Object> OnValueAssigned;

            public override void SetValueWithoutNotify(UnityEngine.Object newValue)
            {
                var previous = value;
                base.SetValueWithoutNotify(newValue);
                if (!ReferenceEquals(previous, newValue))
                {
                    MarkDirtyRepaint();
                    OnValueAssigned?.Invoke(newValue);
                }
                RefreshDisplayLabel();
            }

            public void RefreshDisplayLabel()
            {
                var clip = value as AnimationClip;
                var labelToSet = ResolveDisplayLabel(clip);
                if (string.IsNullOrEmpty(labelToSet)) return;

                // ObjectField 内部の表示テキスト要素を ".unity-object-field-display__label" で取得して上書き
                var displayLabel = this.Q<Label>(className: "unity-object-field-display__label");
                if (displayLabel != null)
                {
                    displayLabel.text = labelToSet;
                }
            }

            private static string ResolveDisplayLabel(AnimationClip clip)
            {
                if (clip == null) return string.Empty;
                var path = AssetDatabase.GetAssetPath(clip);
                if (string.IsNullOrEmpty(path)) return clip.name;
                var fileName = Path.GetFileNameWithoutExtension(path);
                return string.IsNullOrEmpty(fileName) ? clip.name : fileName;
            }
        }
    }
}
