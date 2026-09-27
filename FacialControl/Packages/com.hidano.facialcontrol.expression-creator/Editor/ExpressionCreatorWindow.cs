using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Sampling;

namespace Hidano.FacialControl.ExpressionCreator
{
    /// <summary>
    /// Expression 作成支援ツール。
    /// BlendShape スライダーをリアルタイムプレビューしながら AnimationClip にベイクする。
    /// 既存 AnimationClip を割り当てると <see cref="IExpressionAnimationClipSampler.SampleSnapshot"/>
    /// 経由でスライダー値が復元される。
    /// <para>
    /// Clip の選択は「登録済み Expression から編集」「AnimationClip を作成・編集」の 2 タブから開始する。
    /// 後者のタブでは既存 Clip の編集と新規 Clip の作成をボタンで選択する。
    /// </para>
    /// </summary>
    public class ExpressionCreatorWindow : EditorWindow
    {
        private const string WindowTitle = "Expression 作成";
        private const float MinWindowWidth = 700f;
        private const float MinWindowHeight = 500f;
        private const int PreviewSize = 256;
        // PNG 書き出し（単発保存 / 全 Expression 一括）の画像サイズ。画面上のプレビューとは独立。
        private const int ExportImageSize = 512;
        private const float BakeButtonMinWidth = 140f;
        // 通常ボタン(約20px)の2倍の高さ。潰れ対策。
        private const float BakeButtonHeight = 40f;
        // スライダー表示は SkinnedMeshRenderer Inspector に合わせた 0..100。内部値は正規化 0..1。
        private const float BlendShapeDisplayScale = 100f;
        private const string RendererFilterAllLabel = "すべて";

        private const string SavePreviewButtonName = "expression-creator-save-preview-png-button";
        private const string CreateNewClipButtonName = "expression-creator-create-new-clip-button";
        private const string TrackTargetFieldName = "expression-creator-track-target-field";
        private const string ClipTabViewName = "expression-creator-clip-tab-view";
        private const string RegisteredTabName = "expression-creator-registered-tab";
        private const string ClipEditTabName = "expression-creator-clip-edit-tab";
        private const string RegisteredClipHelpBoxName = "expression-creator-registered-clip-help";
        private const string EditProjectClipButtonName = "expression-creator-edit-project-clip-button";
        private const string RegisteredClipDropdownName = "expression-creator-registered-clip-dropdown";
        private const string ClipRowName = "expression-creator-clip-row";
        private const string RendererFilterDropdownName = "expression-creator-renderer-filter-dropdown";
        private const string MissingBlendShapeWarningName = "expression-creator-missing-blendshape-warning";
        private const string DeleteMissingBlendShapeButtonName = "expression-creator-delete-missing-blendshape-button";
        private const string ExportAllButtonName = "expression-creator-export-all-button";
        private const string ExportAllContainerName = "expression-creator-export-all-container";

        private const string BlendShapePropertyPrefix = "blendShape.";

        /// <summary>
        /// 全 Expression 書き出しの前回書き出し先フォルダを記憶する EditorPrefs キー。
        /// プロジェクトデータに残すほどではないマシンローカルの利便設定のため EditorPrefs を使う。
        /// </summary>
        private const string LastExportFolderPrefsKey
            = "Hidano.FacialControl.ExpressionCreatorWindow.LastExportFolder";

        // モデル参照
        private GameObject _targetObject;
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;
        private ObjectField _modelField;
        private ObjectField _trackTargetField;
        private Transform _trackTarget;
        private string[] _availableBlendShapeNames = Array.Empty<string>();
        private HelpBox _blendShapeHelpBox;

        // プレビュー
        private PreviewRenderWrapper _previewWrapper;
        private IMGUIContainer _previewContainer;

        // BlendShape 管理
        private List<BlendShapeEntry> _blendShapeEntries = new List<BlendShapeEntry>();
        private ScrollView _blendShapeListView;
        private TextField _blendShapeSearchField;
        private string _blendShapeSearchText = "";
        private DropdownField _rendererFilterDropdown;
        private readonly List<int> _rendererFilterRendererIndices = new List<int>();
        private int _rendererFilterIndex = -1;
        private Label _missingBlendShapeWarningLabel;
        private Button _deleteMissingBlendShapeButton;
        private readonly List<(string rendererPath, string blendShapeName)> _missingBlendShapeKeys
            = new List<(string rendererPath, string blendShapeName)>();

        // ベイク先 AnimationClip
        private ObjectField _clipField;
        private AnimationClip _targetClip;
        private VisualElement _clipRow;
        private HelpBox _registeredClipHelpBox;
        private DropdownField _registeredClipDropdown;
        private readonly List<(string label, AnimationClip clip)> _registeredClipChoices
            = new List<(string label, AnimationClip clip)>();

        // 全 Expression プレビュー書き出し
        private VisualElement _exportAllContainer;
        private Button _exportAllButton;

        // 依存
        private IExpressionAnimationClipSampler _sampler;
        // 引数はダイアログのデフォルトファイル名
        private Func<string, string> _savePreviewPathProvider;
        private Func<int, int, Texture2D> _previewTextureCapture;
        private Action<string, byte[]> _pngFileWriter;
        private Func<string> _createClipPathProvider;
        private Action<AnimationClip, string> _clipAssetCreator;
        private Func<string, AnimationClip> _clipAssetLoader;
        private Action _assetDatabaseSaveAssets;
        private Func<string> _exportFolderProvider;

        [MenuItem("Tools/FacialControl/Expression 作成", false, 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<ExpressionCreatorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        }

        private void OnEnable()
        {
            _sampler = new AnimationClipExpressionSampler();
            _previewWrapper = new PreviewRenderWrapper();
            saveChangesMessage = "ベイクされていない編集内容があります。AnimationClip にベイクして保存しますか？";
            ConfigureSavePreviewDependencies();
            ConfigureCreateClipDependencies();
            ConfigureExportAllDependencies();
        }

        private void OnDisable()
        {
            _previewWrapper?.Dispose();
            _previewWrapper = null;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            var mainContainer = new VisualElement();
            mainContainer.style.flexDirection = FlexDirection.Row;
            mainContainer.style.flexGrow = 1;
            // min-content 高さでベイク行を window 外へ押し出さないよう、縮小を許可する
            mainContainer.style.minHeight = 0;
            root.Add(mainContainer);

            // ========================================
            // 左パネル: プレビュー + モデル選択
            // ========================================
            var leftPanel = new VisualElement();
            leftPanel.name = "expression-creator-left-panel";
            leftPanel.style.width = PreviewSize + 16;
            leftPanel.style.minWidth = PreviewSize + 16;
            leftPanel.style.paddingLeft = 4;
            leftPanel.style.paddingRight = 4;
            leftPanel.style.paddingTop = 4;
            mainContainer.Add(leftPanel);

            _modelField = new ObjectField("モデル")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "BlendShape プレビュー対象のモデル。シーン上に FacialController があれば自動解決される。"
            };
            _modelField.RegisterValueChangedCallback(OnModelChanged);
            leftPanel.Add(_modelField);

            _trackTargetField = new ObjectField("トラッキング対象")
            {
                name = TrackTargetFieldName,
                objectType = typeof(Transform),
                allowSceneObjects = true,
                tooltip = "プレビューカメラが注視するジョイント。モデル設定時に Humanoid の Head ボーン → "
                    + "head / neck 名のジョイントの順で自動解決される。誤検出時は手動で差し替え可能。"
            };
            _trackTargetField.RegisterValueChangedCallback(OnTrackTargetChanged);
            leftPanel.Add(_trackTargetField);

            _blendShapeHelpBox = new HelpBox(
                "モデルを設定するか、シーン上に FacialController を配置すると BlendShape スライダーが表示されます。",
                HelpBoxMessageType.Info);
            _blendShapeHelpBox.style.marginTop = 4;
            leftPanel.Add(_blendShapeHelpBox);

            _previewContainer = new IMGUIContainer(OnPreviewGUI);
            _previewContainer.style.width = PreviewSize;
            _previewContainer.style.height = PreviewSize;
            _previewContainer.style.marginTop = 4;
            _previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            leftPanel.Add(_previewContainer);

            var cameraResetButton = new Button(OnCameraReset) { text = "カメラリセット" };
            cameraResetButton.AddToClassList(FacialControlStyles.ActionButton);
            cameraResetButton.style.marginTop = 4;
            leftPanel.Add(cameraResetButton);

            var savePreviewButton = new Button(OnSavePreviewClicked) { text = "プレビューを PNG として保存" };
            savePreviewButton.name = SavePreviewButtonName;
            savePreviewButton.AddToClassList(FacialControlStyles.ActionButton);
            savePreviewButton.style.marginTop = 4;
            leftPanel.Add(savePreviewButton);

            // 全 Expression プレビュー PNG 書き出し。
            // 無効化時もホバーで理由を提示できるよう、コンテナ側にも tooltip を持たせる。
            _exportAllContainer = new VisualElement();
            _exportAllContainer.name = ExportAllContainerName;
            leftPanel.Add(_exportAllContainer);

            _exportAllButton = new Button(OnExportAllExpressionPreviewsClicked)
            {
                text = "全 Expression プレビューを PNG 書き出し"
            };
            _exportAllButton.name = ExportAllButtonName;
            _exportAllButton.AddToClassList(FacialControlStyles.ActionButton);
            _exportAllButton.style.marginTop = 4;
            _exportAllContainer.Add(_exportAllButton);

            var resetButton = new Button(OnResetBlendShapes) { text = "全スライダーリセット" };
            resetButton.AddToClassList(FacialControlStyles.ActionButton);
            resetButton.style.marginTop = 4;
            leftPanel.Add(resetButton);

            // ========================================
            // 右パネル: AnimationClip + BlendShape スライダー
            // ========================================
            var rightPanel = new VisualElement();
            rightPanel.style.flexGrow = 1;
            rightPanel.style.paddingLeft = 4;
            rightPanel.style.paddingRight = 4;
            rightPanel.style.paddingTop = 4;
            mainContainer.Add(rightPanel);

            // Clip 選択（2 タブ: 登録済み Expression から編集 / AnimationClip を作成・編集）
            var clipTabView = new TabView { name = ClipTabViewName };

            var registeredTab = new Tab("登録済み Expression から編集") { name = RegisteredTabName };
            clipTabView.Add(registeredTab);

            // 登録済み Expression の Clip 選択ドロップダウン（登録が 1 件以上ある場合のみ表示）
            _registeredClipDropdown = new DropdownField("登録済み Expression");
            _registeredClipDropdown.name = RegisteredClipDropdownName;
            _registeredClipDropdown.style.marginTop = 4;
            _registeredClipDropdown.RegisterValueChangedCallback(
                _ => ApplyRegisteredClipSelection(_registeredClipDropdown.index));
            registeredTab.contentContainer.Add(_registeredClipDropdown);

            _registeredClipHelpBox = new HelpBox(
                "登録済みの Clip がありません。FacialController の FacialCharacterProfileSO に "
                    + "AnimationClip 付きの Expression を登録してください。",
                HelpBoxMessageType.Info);
            _registeredClipHelpBox.name = RegisteredClipHelpBoxName;
            _registeredClipHelpBox.style.marginTop = 4;
            registeredTab.contentContainer.Add(_registeredClipHelpBox);

            var clipEditTab = new Tab("AnimationClip を作成・編集") { name = ClipEditTabName };
            clipTabView.Add(clipEditTab);

            var clipEditButtonRow = new VisualElement();
            clipEditButtonRow.style.flexDirection = FlexDirection.Row;
            clipEditButtonRow.style.flexWrap = Wrap.Wrap;
            clipEditButtonRow.style.marginTop = 4;
            clipEditTab.contentContainer.Add(clipEditButtonRow);

            var editProjectClipButton = new Button(OnEditProjectClipClicked)
            {
                text = "既存 Clip を編集",
                tooltip = "Project 内の既存 AnimationClip をスロットに割り当てて編集します。"
            };
            editProjectClipButton.name = EditProjectClipButtonName;
            editProjectClipButton.AddToClassList(FacialControlStyles.ActionButton);
            clipEditButtonRow.Add(editProjectClipButton);

            var createNewClipButton = new Button(OnCreateNewClipClicked)
            {
                text = "新規 Clip を作成",
                tooltip = "新規 AnimationClip アセットを作成してスロットに割り当てます。"
            };
            createNewClipButton.name = CreateNewClipButtonName;
            createNewClipButton.AddToClassList(FacialControlStyles.ActionButton);
            createNewClipButton.style.marginLeft = 4;
            clipEditButtonRow.Add(createNewClipButton);

            // タブ切り替え時に登録済み Clip の選択肢を最新化する
            clipTabView.activeTabChanged += (_, next) =>
            {
                if (next == registeredTab)
                    RefreshRegisteredClipChoices();
            };

            rightPanel.Add(clipTabView);

            // ベイク先 Clip スロット（いずれかのモードが選択されるまで非表示）
            _clipField = new ObjectField("AnimationClip")
            {
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
                tooltip = "ベイク対象の AnimationClip。割り当てると現在の値がスライダーに復元される。"
            };
            _clipField.RegisterValueChangedCallback(OnClipFieldChanged);
            _clipField.style.flexGrow = 1;

            _clipRow = new VisualElement();
            _clipRow.name = ClipRowName;
            _clipRow.style.flexDirection = FlexDirection.Row;
            _clipRow.style.alignItems = Align.Center;
            _clipRow.style.marginTop = 4;
            _clipRow.style.display = DisplayStyle.None;
            _clipRow.Add(_clipField);
            rightPanel.Add(_clipRow);

            // SkinnedMeshRenderer フィルタ
            _rendererFilterDropdown = new DropdownField("SkinnedMeshRenderer");
            _rendererFilterDropdown.name = RendererFilterDropdownName;
            _rendererFilterDropdown.choices = new List<string> { RendererFilterAllLabel };
            _rendererFilterDropdown.SetValueWithoutNotify(RendererFilterAllLabel);
            _rendererFilterDropdown.style.marginTop = 8;
            _rendererFilterDropdown.RegisterValueChangedCallback(
                _ => ApplyRendererFilter(_rendererFilterDropdown.index));
            rightPanel.Add(_rendererFilterDropdown);

            // BlendShape 検索
            _blendShapeSearchField = new TextField("BlendShape 検索");
            _blendShapeSearchField.RegisterValueChangedCallback(OnBlendShapeSearchChanged);
            _blendShapeSearchField.style.marginTop = 2;
            rightPanel.Add(_blendShapeSearchField);

            // 設定中モデルに存在しない BlendShape の警告（黄色）
            _missingBlendShapeWarningLabel = new Label();
            _missingBlendShapeWarningLabel.name = MissingBlendShapeWarningName;
            _missingBlendShapeWarningLabel.style.color = new Color(1f, 0.85f, 0.2f, 1f);
            _missingBlendShapeWarningLabel.style.whiteSpace = WhiteSpace.Normal;
            _missingBlendShapeWarningLabel.style.marginTop = 4;
            _missingBlendShapeWarningLabel.style.display = DisplayStyle.None;
            rightPanel.Add(_missingBlendShapeWarningLabel);

            // 存在しない BlendShape のカーブを Clip から一括削除するボタン（警告表示中のみ表示）
            _deleteMissingBlendShapeButton = new Button(OnDeleteMissingBlendShapesClicked)
            {
                text = "存在しない BlendShape を Clip から一括削除",
                tooltip = "設定中のモデルに存在しない BlendShape のカーブを AnimationClip から削除します。"
            };
            _deleteMissingBlendShapeButton.name = DeleteMissingBlendShapeButtonName;
            _deleteMissingBlendShapeButton.AddToClassList(FacialControlStyles.ActionButton);
            _deleteMissingBlendShapeButton.style.marginTop = 2;
            _deleteMissingBlendShapeButton.style.alignSelf = Align.FlexStart;
            _deleteMissingBlendShapeButton.style.display = DisplayStyle.None;
            rightPanel.Add(_deleteMissingBlendShapeButton);

            // BlendShape スライダーリスト
            _blendShapeListView = new ScrollView(ScrollViewMode.Vertical);
            _blendShapeListView.style.flexGrow = 1;
            _blendShapeListView.style.marginTop = 4;
            rightPanel.Add(_blendShapeListView);

            // ========================================
            // 下部: ベイクボタン + ステータス
            // ========================================
            var bottomSection = new VisualElement();
            bottomSection.style.flexDirection = FlexDirection.Row;
            bottomSection.style.flexShrink = 0f;
            bottomSection.style.paddingLeft = 4;
            bottomSection.style.paddingRight = 4;
            bottomSection.style.paddingBottom = 4;
            bottomSection.style.paddingTop = 4;
            bottomSection.style.justifyContent = Justify.FlexEnd;

            var bakeButton = new Button(OnBakeClicked) { text = "AnimationClip にベイク" };
            bakeButton.name = "expression-creator-bake-button";
            bakeButton.AddToClassList(FacialControlStyles.ActionButton);
            bakeButton.style.flexShrink = 0f;
            bakeButton.style.minWidth = BakeButtonMinWidth;
            bakeButton.style.height = BakeButtonHeight;
            bottomSection.Add(bakeButton);

            root.Add(bottomSection);

            RefreshRegisteredClipChoices();
            RefreshExportAllButtonState();
            TryAutoResolveModelFromScene();

            // domain reload 直後は CreateGUI 時点でシーンオブジェクトを解決できないことがあるため、
            // 未解決の場合は 1 フレーム遅らせて再試行する。
            if (_targetObject == null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                        TryAutoResolveModelFromScene();
                };
            }
        }

        // ========================================
        // モデル選択
        // ========================================

        private void OnModelChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            ApplyModelChange(evt.newValue as GameObject);
        }

        /// <summary>
        /// モデル変更を適用する。panel 未接続でも動作するよう ChangeEvent に依存しない。
        /// </summary>
        private void ApplyModelChange(GameObject model)
        {
            _targetObject = model;
            CollectBlendShapes();
            RefreshBlendShapeNameChoices();
            RefreshRendererFilterChoices();
            AutoResolveTrackTarget();
            RebuildBlendShapeList();
            SetupPreview();
            RestoreSliderValuesFromTargetClip();
            RefreshRegisteredClipChoices();
            RefreshExportAllButtonState();
        }

        private void AutoResolveTrackTarget()
        {
            _trackTarget = _targetObject != null ? FaceTrackTargetResolver.Resolve(_targetObject) : null;
            _trackTargetField?.SetValueWithoutNotify(_trackTarget);
        }

        private void OnTrackTargetChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _trackTarget = evt.newValue as Transform;
            SetupPreview();
        }

        /// <summary>
        /// トラッキング対象をモデルルートからの相対 Transform パスへ変換する。
        /// 未設定・モデル階層外の場合は null（従来のカメラ配置にフォールバック）。
        /// </summary>
        private string ResolveTrackTargetPath()
        {
            if (_targetObject == null || _trackTarget == null)
                return null;

            if (!_trackTarget.IsChildOf(_targetObject.transform))
                return null;

            return AnimationUtility.CalculateTransformPath(_trackTarget, _targetObject.transform);
        }

        private void RefreshBlendShapeNameChoices()
        {
            _availableBlendShapeNames = BlendShapeNameProvider.GetBlendShapeNames(_targetObject);

            if (_blendShapeHelpBox != null)
            {
                _blendShapeHelpBox.style.display = _availableBlendShapeNames.Length == 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private string[] AvailableBlendShapeNames => _availableBlendShapeNames ?? Array.Empty<string>();

        /// <summary>
        /// 対象モデル配下の全 SkinnedMeshRenderer から BlendShape を収集し、
        /// 各エントリに Transform 階層パス（AnimationClip binding.path 用）を付与する。
        /// </summary>
        private void CollectBlendShapes()
        {
            _blendShapeEntries.Clear();

            if (_targetObject == null)
            {
                _skinnedMeshRenderers = null;
                return;
            }

            _skinnedMeshRenderers = _targetObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (int r = 0; r < _skinnedMeshRenderers.Length; r++)
            {
                var smr = _skinnedMeshRenderers[r];
                if (smr.sharedMesh == null)
                    continue;

                var rendererPath = AnimationUtility.CalculateTransformPath(
                    smr.transform, _targetObject.transform);

                int count = smr.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    var shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    _blendShapeEntries.Add(new BlendShapeEntry
                    {
                        RendererName = smr.name,
                        RendererIndex = r,
                        RendererPath = rendererPath ?? string.Empty,
                        BlendShapeName = shapeName,
                        BlendShapeIndex = i,
                        Value = 0f
                    });
                }
            }
        }

        private void TryAutoResolveModelFromScene()
        {
            if (_modelField == null || _targetObject != null)
                return;

            var controller = UnityEngine.Object.FindFirstObjectByType<FacialController>();
            if (controller != null)
            {
                _modelField.SetValueWithoutNotify(controller.gameObject);
                ApplyModelChange(controller.gameObject);
            }
        }

        private FacialCharacterProfileSO ResolveCharacterSO()
        {
            if (_targetObject == null)
                return null;

            var controller = _targetObject.GetComponentInChildren<FacialController>(true);
            return controller != null ? controller.CharacterSO : null;
        }

        // ========================================
        // BlendShape スライダー UI
        // ========================================

        private void OnBlendShapeSearchChanged(ChangeEvent<string> evt)
        {
            _blendShapeSearchText = evt.newValue ?? "";
            RebuildBlendShapeList();
        }

        /// <summary>
        /// SkinnedMeshRenderer フィルタの選択肢を再構築する。
        /// 先頭は「すべて」、以降は BlendShape を持つレンダラーの階層パス。
        /// </summary>
        private void RefreshRendererFilterChoices()
        {
            _rendererFilterRendererIndices.Clear();
            _rendererFilterIndex = -1;

            var labels = new List<string> { RendererFilterAllLabel };
            var seen = new HashSet<int>();
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                if (!seen.Add(entry.RendererIndex))
                    continue;

                labels.Add(string.IsNullOrEmpty(entry.RendererPath)
                    ? entry.RendererName
                    : entry.RendererPath);
                _rendererFilterRendererIndices.Add(entry.RendererIndex);
            }

            if (_rendererFilterDropdown != null)
            {
                _rendererFilterDropdown.choices = labels;
                _rendererFilterDropdown.SetValueWithoutNotify(RendererFilterAllLabel);
            }
        }

        private void ApplyRendererFilter(int dropdownIndex)
        {
            int mappedIndex = dropdownIndex - 1;
            _rendererFilterIndex = mappedIndex >= 0 && mappedIndex < _rendererFilterRendererIndices.Count
                ? _rendererFilterRendererIndices[mappedIndex]
                : -1;
            RebuildBlendShapeList();
        }

        private void RebuildBlendShapeList()
        {
            if (_blendShapeListView == null)
                return;

            _blendShapeListView.Clear();

            if (_blendShapeEntries.Count == 0)
            {
                var message = AvailableBlendShapeNames.Length == 0
                    ? "モデルを選択してください。"
                    : "選択中のモデルに BlendShape が見つかりませんでした。";
                var emptyLabel = new Label(message);
                emptyLabel.AddToClassList(FacialControlStyles.InfoLabel);
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.marginTop = 20;
                _blendShapeListView.Add(emptyLabel);
                return;
            }

            string currentRenderer = null;

            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];

                if (_rendererFilterIndex >= 0 && entry.RendererIndex != _rendererFilterIndex)
                    continue;

                if (!string.IsNullOrEmpty(_blendShapeSearchText)
                    && entry.BlendShapeName.IndexOf(_blendShapeSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (currentRenderer != entry.RendererName)
                {
                    currentRenderer = entry.RendererName;
                    var header = new Label(entry.RendererName);
                    header.style.marginTop = 8;
                    header.style.marginBottom = 2;
                    _blendShapeListView.Add(header);
                }

                var row = CreateBlendShapeSliderRow(i);
                _blendShapeListView.Add(row);
            }
        }

        private VisualElement CreateBlendShapeSliderRow(int entryIndex)
        {
            var entry = _blendShapeEntries[entryIndex];

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 1;

            var nameLabel = new Label(entry.BlendShapeName);
            nameLabel.style.width = 160;
            nameLabel.style.minWidth = 100;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            // 表示スケールは SkinnedMeshRenderer Inspector と同じ 0..100。内部値は正規化 0..1。
            var slider = new Slider(0f, BlendShapeDisplayScale);
            slider.value = entry.Value * BlendShapeDisplayScale;
            slider.style.flexGrow = 1;
            slider.style.minWidth = 80;

            var valueField = new FloatField();
            valueField.value = entry.Value * BlendShapeDisplayScale;
            valueField.style.width = 55;
            valueField.style.marginLeft = 4;

            int capturedIndex = entryIndex;

            slider.RegisterValueChangedCallback(evt =>
            {
                _blendShapeEntries[capturedIndex].Value = evt.newValue / BlendShapeDisplayScale;
                valueField.SetValueWithoutNotify(evt.newValue);
                ApplyBlendShapeToPreview(capturedIndex);
                MarkUnsavedChanges();
            });

            valueField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, 0f, BlendShapeDisplayScale);
                _blendShapeEntries[capturedIndex].Value = clamped / BlendShapeDisplayScale;
                slider.SetValueWithoutNotify(clamped);
                valueField.SetValueWithoutNotify(clamped);
                ApplyBlendShapeToPreview(capturedIndex);
                MarkUnsavedChanges();
            });

            row.Add(slider);
            row.Add(valueField);

            return row;
        }

        private void OnResetBlendShapes()
        {
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                _blendShapeEntries[i].Value = 0f;
            }

            RebuildBlendShapeList();
            ApplyAllBlendShapesToPreview();

            if (_blendShapeEntries.Count > 0)
                MarkUnsavedChanges();
        }

        private void OnCameraReset()
        {
            _previewWrapper.ResetCamera();
            _previewContainer.MarkDirtyRepaint();
        }

        private void OnSavePreviewClicked()
        {
            // どの Expression の画像か判別できるファイル名を組み立てられないため、
            // Expression（AnimationClip）未選択時は保存せず警告する。
            if (_targetClip == null)
            {
                Debug.LogWarning(
                    "[ExpressionCreatorWindow] Expression が選択されていないため、プレビュー PNG を保存できません。"
                        + "「登録済み Expression から編集」タブで Expression を選択するか、AnimationClip を設定してください。");
                return;
            }

            ConfigureSavePreviewDependencies();

            var path = _savePreviewPathProvider(BuildSavePreviewDefaultFileName());
            if (string.IsNullOrEmpty(path))
                return;

            Texture2D texture = null;
            try
            {
                texture = _previewTextureCapture(ExportImageSize, ExportImageSize);
                if (texture == null)
                {
                    ShowStatus("PNG 保存用のプレビュー画像を取得できませんでした。", isError: true);
                    return;
                }

                var pngBytes = texture.EncodeToPNG();
                _pngFileWriter(path, pngBytes);
                ShowStatus($"プレビュー PNG を保存しました: {path}", isError: false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionCreatorWindow] プレビュー PNG 保存エラー: {ex}");
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ========================================
        // 全 Expression プレビュー PNG 書き出し
        // ========================================

        /// <summary>
        /// 全 Expression 書き出しが実行できない理由を返す。実行可能な場合は null。
        /// </summary>
        private string GetExportAllDisabledReason()
        {
            if (_targetObject == null)
                return "モデルが設定されていません。FacialController を持つモデルを設定してください。";

            var controller = _targetObject.GetComponentInChildren<FacialController>(true);
            if (controller == null)
                return "モデルに FacialController コンポーネントが見つかりません。";

            var so = controller.CharacterSO;
            if (so == null)
                return "FacialController に FacialCharacterProfileSO が設定されていません。";

            var expressions = so.Expressions;
            if (expressions != null)
            {
                for (int i = 0; i < expressions.Count; i++)
                {
                    if (expressions[i] != null && expressions[i].animationClip != null)
                        return null;
                }
            }

            return "FacialCharacterProfileSO に AnimationClip 付きの Expression が登録されていません。";
        }

        private void RefreshExportAllButtonState()
        {
            if (_exportAllButton == null)
                return;

            var reason = GetExportAllDisabledReason();
            _exportAllButton.SetEnabled(reason == null);

            var tooltip = reason ?? "FacialCharacterProfileSO の全 Expression のプレビューを PNG として書き出します。";
            _exportAllButton.tooltip = tooltip;
            if (_exportAllContainer != null)
                _exportAllContainer.tooltip = tooltip;
        }

        private void OnExportAllExpressionPreviewsClicked()
        {
            var reason = GetExportAllDisabledReason();
            if (reason != null)
            {
                ShowStatus(reason, isError: true);
                return;
            }

            ConfigureSavePreviewDependencies();
            ConfigureExportAllDependencies();

            var folder = _exportFolderProvider();
            if (string.IsNullOrEmpty(folder))
                return;

            var so = ResolveCharacterSO();
            var expressions = so.Expressions;

            // 書き出しのためにスライダー値を一時的に上書きするので退避する
            var backupValues = new float[_blendShapeEntries.Count];
            for (int i = 0; i < _blendShapeEntries.Count; i++)
                backupValues[i] = _blendShapeEntries[i].Value;

            try
            {
                int exported = 0;
                var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // GetExportAllDisabledReason で _targetObject 非 null は確認済み
                var modelName = _targetObject.name;
                var timestamp = BuildExportTimestamp();

                for (int e = 0; e < expressions.Count; e++)
                {
                    var expression = expressions[e];
                    if (expression == null || expression.animationClip == null)
                        continue;

                    var values = ExpressionClipBakery.LoadBlendShapeValues(expression.animationClip, _sampler);
                    ApplyClipValuesToEntries(values);

                    ApplyAllBlendShapesToPreview();

                    Texture2D texture = null;
                    try
                    {
                        texture = _previewTextureCapture(ExportImageSize, ExportImageSize);
                        if (texture == null)
                        {
                            ShowStatus("PNG 書き出し用のプレビュー画像を取得できませんでした。", isError: true);
                            return;
                        }

                        var fileName = BuildExportFileName(modelName, expression, timestamp, usedFileNames);
                        _pngFileWriter(Path.Combine(folder, fileName), texture.EncodeToPNG());
                        exported++;
                    }
                    finally
                    {
                        if (texture != null)
                            UnityEngine.Object.DestroyImmediate(texture);
                    }
                }

                ShowStatus($"{exported} 件の Expression プレビューを PNG 書き出ししました: {folder}", isError: false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionCreatorWindow] Expression プレビュー書き出しエラー: {ex}");
            }
            finally
            {
                for (int i = 0; i < _blendShapeEntries.Count; i++)
                    _blendShapeEntries[i].Value = backupValues[i];
                RebuildBlendShapeList();
                ApplyAllBlendShapesToPreview();
            }
        }

        /// <summary>
        /// 単発プレビュー保存ダイアログのデフォルトファイル名
        /// 「{モデル名}_{Expression 名}_{yyyyMMdd-HHmm}.png」を組み立てる。
        /// Expression 名は編集中の Clip が登録済み Expression のものであればその名前、
        /// そうでなければ Clip 名。<see cref="_targetClip"/> 非 null が呼び出し前提。
        /// </summary>
        private string BuildSavePreviewDefaultFileName()
        {
            var expressionName = ResolveExpressionNameForTargetClip();
            if (string.IsNullOrWhiteSpace(expressionName))
                expressionName = _targetClip.name;
            if (string.IsNullOrWhiteSpace(expressionName))
                expressionName = "expression";

            var timestamp = BuildExportTimestamp();
            var baseName = _targetObject != null
                ? $"{_targetObject.name}_{expressionName}_{timestamp}"
                : $"{expressionName}_{timestamp}";
            return SanitizeFileName(baseName) + ".png";
        }

        private static string BuildExportTimestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmm");
        }

        /// <summary>
        /// <see cref="_targetClip"/> が登録済み Expression の Clip であれば、その Expression 名を返す。
        /// 該当が無ければ null。
        /// </summary>
        private string ResolveExpressionNameForTargetClip()
        {
            var so = ResolveCharacterSO();
            if (so == null || so.Expressions == null)
                return null;

            for (int i = 0; i < so.Expressions.Count; i++)
            {
                var expression = so.Expressions[i];
                if (expression != null
                    && expression.animationClip == _targetClip
                    && !string.IsNullOrWhiteSpace(expression.name))
                {
                    return expression.name;
                }
            }

            return null;
        }

        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
                name = name.Replace(invalidChars[i], '_');
            return name;
        }

        /// <summary>
        /// 一括書き出しの PNG ファイル名「{モデル名}_{Expression 名}_{yyyyMMdd-HHmm}.png」を組み立てる。
        /// 同名衝突時は末尾に連番を付与する。
        /// </summary>
        private static string BuildExportFileName(
            string modelName,
            ExpressionSerializable expression,
            string timestamp,
            HashSet<string> usedFileNames)
        {
            var expressionName = !string.IsNullOrWhiteSpace(expression.name)
                ? expression.name
                : expression.animationClip.name;

            if (string.IsNullOrWhiteSpace(expressionName))
                expressionName = "expression";

            var baseName = SanitizeFileName($"{modelName}_{expressionName}_{timestamp}");

            var fileName = baseName + ".png";
            int suffix = 1;
            while (!usedFileNames.Add(fileName))
            {
                fileName = $"{baseName}_{suffix}.png";
                suffix++;
            }

            return fileName;
        }

        // ========================================
        // プレビュー
        // ========================================

        private void SetupPreview()
        {
            _previewWrapper.Setup(_targetObject, ResolveTrackTargetPath());

            if (_previewWrapper.IsInitialized)
            {
                _skinnedMeshRenderers = _previewWrapper.PreviewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            ApplyAllBlendShapesToPreview();
        }

        private void ApplyBlendShapeToPreview(int entryIndex)
        {
            if (!_previewWrapper.IsInitialized || _skinnedMeshRenderers == null)
                return;

            var entry = _blendShapeEntries[entryIndex];

            if (entry.RendererIndex >= 0 && entry.RendererIndex < _skinnedMeshRenderers.Length)
            {
                var smr = _skinnedMeshRenderers[entry.RendererIndex];
                if (smr != null && smr.sharedMesh != null)
                {
                    smr.SetBlendShapeWeight(entry.BlendShapeIndex, entry.Value * 100f);
                }
            }

            _previewContainer?.MarkDirtyRepaint();
        }

        private void ApplyAllBlendShapesToPreview()
        {
            if (!_previewWrapper.IsInitialized || _skinnedMeshRenderers == null)
                return;

            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                if (entry.RendererIndex >= 0 && entry.RendererIndex < _skinnedMeshRenderers.Length)
                {
                    var smr = _skinnedMeshRenderers[entry.RendererIndex];
                    if (smr != null && smr.sharedMesh != null)
                    {
                        smr.SetBlendShapeWeight(entry.BlendShapeIndex, entry.Value * 100f);
                    }
                }
            }

            _previewContainer?.MarkDirtyRepaint();
        }

        private void OnPreviewGUI()
        {
            if (!_previewWrapper.IsInitialized)
            {
                GUILayout.Label("モデルを選択してください。", EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
                return;
            }

            var rect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
                _previewContainer.CaptureMouse();
            else if (evt.type == EventType.MouseUp && _previewContainer.HasMouseCapture())
                _previewContainer.ReleaseMouse();

            if (_previewWrapper.HandleInput(rect))
            {
                _previewContainer.MarkDirtyRepaint();
                Repaint();
            }

            _previewWrapper.Render(rect);
        }

        // ========================================
        // AnimationClip 選択モード
        // ========================================

        private void OnClipFieldChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            _targetClip = evt.newValue as AnimationClip;
            RestoreSliderValuesFromTargetClip();
        }

        private void OnEditProjectClipClicked()
        {
            _clipRow.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// FacialCharacterProfileSO に登録された AnimationClip 付き Expression の選択肢を再構築する。
        /// 選択肢が 0 件の場合はドロップダウンの代わりに案内 HelpBox を表示する。
        /// </summary>
        private void RefreshRegisteredClipChoices()
        {
            _registeredClipChoices.Clear();

            var so = ResolveCharacterSO();
            if (so != null && so.Expressions != null)
            {
                for (int i = 0; i < so.Expressions.Count; i++)
                {
                    var expression = so.Expressions[i];
                    if (expression == null || expression.animationClip == null)
                        continue;

                    var displayName = string.IsNullOrWhiteSpace(expression.name)
                        ? expression.animationClip.name
                        : expression.name;
                    _registeredClipChoices.Add(
                        ($"{displayName} ({expression.animationClip.name})", expression.animationClip));
                }
            }

            if (_registeredClipDropdown != null)
            {
                var labels = new List<string>(_registeredClipChoices.Count);
                for (int i = 0; i < _registeredClipChoices.Count; i++)
                    labels.Add(_registeredClipChoices[i].label);
                _registeredClipDropdown.choices = labels;

                if (_registeredClipChoices.Count == 0)
                    _registeredClipDropdown.SetValueWithoutNotify(string.Empty);

                _registeredClipDropdown.style.display = _registeredClipChoices.Count > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_registeredClipHelpBox != null)
            {
                _registeredClipHelpBox.style.display = _registeredClipChoices.Count > 0
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        private void ApplyRegisteredClipSelection(int choiceIndex)
        {
            if (choiceIndex < 0 || choiceIndex >= _registeredClipChoices.Count)
                return;

            var clip = _registeredClipChoices[choiceIndex].clip;
            _clipField.SetValueWithoutNotify(clip);
            _clipRow.style.display = DisplayStyle.Flex;

            if (_targetClip != clip)
            {
                _targetClip = clip;
                RestoreSliderValuesFromTargetClip();
            }
        }

        private void OnCreateNewClipClicked()
        {
            ConfigureCreateClipDependencies();

            var path = _createClipPathProvider();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var clip = new AnimationClip();
                _clipAssetCreator(clip, path);
                _assetDatabaseSaveAssets();

                var loadedClip = _clipAssetLoader(path) ?? clip;
                _clipRow.style.display = DisplayStyle.Flex;
                _clipField.value = loadedClip;
                if (_targetClip != loadedClip)
                {
                    _targetClip = loadedClip;
                    RestoreSliderValuesFromTargetClip();
                }

                ShowStatus($"AnimationClip を作成しました: {path}", isError: false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionCreatorWindow] AnimationClip 作成エラー: {ex}");
            }
        }

        /// <summary>
        /// 現在の <see cref="_targetClip"/> から <see cref="IExpressionAnimationClipSampler"/> 経由で
        /// BlendShape 値を取得し、スライダーへ復元する。clip 未設定時は何もしない。
        /// 設定中モデルに存在しない BlendShape が Clip に含まれる場合は警告を表示する。
        /// </summary>
        private void RestoreSliderValuesFromTargetClip()
        {
            HideMissingBlendShapeWarning();

            if (_targetClip == null || _sampler == null || _blendShapeEntries.Count == 0)
                return;

            try
            {
                var values = ExpressionClipBakery.LoadBlendShapeValues(_targetClip, _sampler);
                ApplyClipValuesToEntries(values);

                UpdateMissingBlendShapeWarning(values);
                RebuildBlendShapeList();
                ApplyAllBlendShapesToPreview();
                ShowStatus($"AnimationClip を読み込みました: {_targetClip.name}", isError: false);
                hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionCreatorWindow] AnimationClip 読み込みエラー: {ex}");
            }
        }

        /// <summary>
        /// Clip から読み込んだ (RendererPath, BlendShapeName) → 値のマップをスライダーへ適用する。
        /// <para>
        /// RendererPath の完全一致を優先し、一致しない場合は BlendShape 名のみで解決する。
        /// これはランタイムの出力経路（<c>SkinnedMeshRendererBlendShapeWriter</c>）が
        /// RendererPath を参照せず BlendShape 名だけで SkinnedMeshRenderer を解決する仕様に合わせるため。
        /// Clip を別階層のモデルで作った場合でも、名前さえ合っていれば値が復元される。
        /// </para>
        /// </summary>
        private void ApplyClipValuesToEntries(
            Dictionary<(string rendererPath, string blendShapeName), float> clipValues)
        {
            var valuesByName = ExpressionClipBakery.BuildBlendShapeNameFallback(clipValues);

            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                var blendShapeName = entry.BlendShapeName ?? string.Empty;
                var key = (entry.RendererPath ?? string.Empty, blendShapeName);

                if (clipValues.TryGetValue(key, out var value)
                    || valuesByName.TryGetValue(blendShapeName, out value))
                {
                    entry.Value = Mathf.Clamp01(value);
                }
                else
                {
                    entry.Value = 0f;
                }
            }
        }

        /// <summary>
        /// Clip に含まれる BlendShape のうち設定中モデルに存在しないものを黄色の警告として表示し、
        /// 一括削除ボタン用に (RendererPath, BlendShapeName) のキーを保持する。
        /// RendererPath だけが違い BlendShape 名がモデルに存在するものは
        /// <see cref="ApplyClipValuesToEntries"/> が名前で解決するため、警告対象に含めない。
        /// </summary>
        private void UpdateMissingBlendShapeWarning(
            Dictionary<(string rendererPath, string blendShapeName), float> clipValues)
        {
            if (_missingBlendShapeWarningLabel == null)
                return;

            var known = new HashSet<(string, string)>();
            var knownNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var entry = _blendShapeEntries[i];
                known.Add((entry.RendererPath ?? string.Empty, entry.BlendShapeName ?? string.Empty));
                knownNames.Add(entry.BlendShapeName ?? string.Empty);
            }

            _missingBlendShapeKeys.Clear();
            List<string> missing = null;
            foreach (var kv in clipValues)
            {
                if (known.Contains(kv.Key))
                    continue;

                // RendererPath がズレていても BlendShape 名がモデルに存在すれば名前で解決されるため、
                // 「存在しない BlendShape」としては扱わない（一括削除の対象にもしない）。
                if (knownNames.Contains(kv.Key.blendShapeName ?? string.Empty))
                    continue;

                _missingBlendShapeKeys.Add(kv.Key);
                missing ??= new List<string>();
                missing.Add(string.IsNullOrEmpty(kv.Key.rendererPath)
                    ? kv.Key.blendShapeName
                    : $"{kv.Key.rendererPath}/{kv.Key.blendShapeName}");
            }

            if (missing == null)
            {
                HideMissingBlendShapeWarning();
                return;
            }

            missing.Sort(StringComparer.Ordinal);
            _missingBlendShapeWarningLabel.text =
                "設定中のモデルに存在しない BlendShape が Clip に含まれています: " + string.Join(", ", missing);
            _missingBlendShapeWarningLabel.style.display = DisplayStyle.Flex;

            if (_deleteMissingBlendShapeButton != null)
                _deleteMissingBlendShapeButton.style.display = DisplayStyle.Flex;
        }

        private void HideMissingBlendShapeWarning()
        {
            _missingBlendShapeKeys.Clear();

            if (_deleteMissingBlendShapeButton != null)
                _deleteMissingBlendShapeButton.style.display = DisplayStyle.None;

            if (_missingBlendShapeWarningLabel == null)
                return;

            _missingBlendShapeWarningLabel.text = string.Empty;
            _missingBlendShapeWarningLabel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 検出済みの「設定中モデルに存在しない BlendShape」のカーブを
        /// <see cref="_targetClip"/> から一括削除する。
        /// 削除後はスライダーを再読み込みし、警告と本ボタンは非表示に戻る。
        /// </summary>
        private void OnDeleteMissingBlendShapesClicked()
        {
            if (_targetClip == null || _missingBlendShapeKeys.Count == 0)
                return;

            var missingSet = new HashSet<(string, string)>(_missingBlendShapeKeys);

            Undo.RecordObject(_targetClip, "存在しない BlendShape カーブを削除");

            int removed = 0;
            var bindings = AnimationUtility.GetCurveBindings(_targetClip);
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || binding.propertyName == null
                    || !binding.propertyName.StartsWith(BlendShapePropertyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = (binding.path ?? string.Empty,
                    binding.propertyName.Substring(BlendShapePropertyPrefix.Length));
                if (!missingSet.Contains(key))
                    continue;

                AnimationUtility.SetEditorCurve(_targetClip, binding, null);
                removed++;
            }

            EditorUtility.SetDirty(_targetClip);
            AssetDatabase.SaveAssetIfDirty(_targetClip);

            RestoreSliderValuesFromTargetClip();
            ShowStatus($"存在しない BlendShape のカーブを {removed} 件削除しました: {_targetClip.name}", isError: false);
        }

        // ========================================
        // ベイク
        // ========================================

        private void OnBakeClicked()
        {
            if (_targetClip == null)
            {
                ShowStatus("ベイク対象の AnimationClip を割り当ててください。", isError: true);
                return;
            }

            if (_blendShapeEntries.Count == 0)
            {
                ShowStatus("BlendShape を持つモデルを選択してください。", isError: true);
                return;
            }

            try
            {
                BakeToTargetClip();
                ShowStatus($"AnimationClip にベイクしました: {_targetClip.name}", isError: false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionCreatorWindow] ベイクエラー: {ex}");
            }
        }

        private void BakeToTargetClip()
        {
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>(_blendShapeEntries.Count);
            for (int i = 0; i < _blendShapeEntries.Count; i++)
            {
                var e = _blendShapeEntries[i];
                if (e.Value > 0f)
                {
                    entries.Add(new ExpressionClipBakery.BlendShapeBakeEntry(
                        e.RendererPath, e.BlendShapeName, e.Value));
                }
            }

            var transitionDuration = Expression.DefaultTransitionDuration;
            var transitionCurvePreset = TransitionCurvePreset.Linear;

            Undo.RecordObject(_targetClip, "Expression ベイク");
            ExpressionClipBakery.Bake(_targetClip, entries, transitionDuration, transitionCurvePreset);
            EditorUtility.SetDirty(_targetClip);
            AssetDatabase.SaveAssetIfDirty(_targetClip);
            hasUnsavedChanges = false;
        }

        /// <summary>
        /// ウィンドウを閉じる際の未保存確認（<see cref="EditorWindow.hasUnsavedChanges"/>）から呼ばれる。
        /// ベイク先 Clip 未設定の場合は作成ダイアログを開き、キャンセル時は例外でクローズを中断する。
        /// </summary>
        public override void SaveChanges()
        {
            if (_blendShapeEntries.Count > 0)
            {
                if (_targetClip == null)
                {
                    ConfigureCreateClipDependencies();
                    var path = _createClipPathProvider();
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException("ベイク先 AnimationClip が未設定のため保存できませんでした。");

                    var clip = new AnimationClip();
                    _clipAssetCreator(clip, path);
                    _assetDatabaseSaveAssets();
                    _targetClip = _clipAssetLoader(path) ?? clip;
                    _clipField?.SetValueWithoutNotify(_targetClip);
                    if (_clipRow != null)
                        _clipRow.style.display = DisplayStyle.Flex;
                }

                BakeToTargetClip();
            }

            base.SaveChanges();
        }

        // ========================================
        // ヘルパー
        // ========================================

        private void MarkUnsavedChanges()
        {
            hasUnsavedChanges = true;
        }

        /// <summary>
        /// 実行結果・エラーを Console へ表示する。
        /// ウィンドウ下部のステータスラベルはウィンドウ幅で見切れるため廃止した。
        /// </summary>
        private static void ShowStatus(string message, bool isError)
        {
            if (isError)
                Debug.LogError($"[ExpressionCreatorWindow] {message}");
            else
                Debug.Log($"[ExpressionCreatorWindow] {message}");
        }

        private void ConfigureSavePreviewDependencies()
        {
            _savePreviewPathProvider ??= defaultFileName => EditorUtility.SaveFilePanel(
                "プレビューを PNG として保存",
                "",
                defaultFileName,
                "png");
            _previewTextureCapture ??= (width, height) => _previewWrapper?.CapturePreviewTexture(width, height);
            _pngFileWriter ??= File.WriteAllBytes;
        }

        private void ConfigureCreateClipDependencies()
        {
            _createClipPathProvider ??= () => EditorUtility.SaveFilePanelInProject(
                "新規 AnimationClip を作成",
                "NewExpression",
                "anim",
                "");
            _clipAssetCreator ??= AssetDatabase.CreateAsset;
            _clipAssetLoader ??= AssetDatabase.LoadAssetAtPath<AnimationClip>;
            _assetDatabaseSaveAssets ??= AssetDatabase.SaveAssets;
        }

        private void ConfigureExportAllDependencies()
        {
            _exportFolderProvider ??= () =>
            {
                var folder = EditorUtility.SaveFolderPanel(
                    "Expression プレビュー PNG の書き出し先",
                    LoadLastExportFolder(),
                    "");
                SaveLastExportFolder(folder);
                return folder;
            };
        }

        /// <summary>
        /// 前回の書き出し先フォルダを返す。未記録またはディレクトリが既に存在しない場合は
        /// 空文字（<see cref="EditorUtility.SaveFolderPanel"/> の既定位置）を返す。
        /// </summary>
        private static string LoadLastExportFolder()
        {
            var folder = EditorPrefs.GetString(LastExportFolderPrefsKey, string.Empty);
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : string.Empty;
        }

        private static void SaveLastExportFolder(string folder)
        {
            if (!string.IsNullOrEmpty(folder))
                EditorPrefs.SetString(LastExportFolderPrefsKey, folder);
        }

        private class BlendShapeEntry
        {
            public string RendererName;
            public int RendererIndex;
            public string RendererPath;
            public string BlendShapeName;
            public int BlendShapeIndex;
            public float Value;
        }
    }
}
