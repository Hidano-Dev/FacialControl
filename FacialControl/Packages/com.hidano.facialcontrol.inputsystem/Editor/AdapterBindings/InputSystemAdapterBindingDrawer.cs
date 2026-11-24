using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
// UnityEngine.UIElements にも BindingMode 型があるため明示エイリアスで衝突を避ける。
using BindingMode = Hidano.FacialControl.InputSystem.Adapters.ScriptableObject.BindingMode;

namespace Hidano.FacialControl.InputSystem.Editor.AdapterBindings
{
    /// <summary>
    /// <see cref="InputSystemAdapterBinding"/> の inline UI を提供する UI Toolkit ベースの
    /// <see cref="PropertyDrawer"/>。
    /// </summary>
    /// <remarks>
    /// Trigger / Gaze / Analog の 3 リストを単一の
    /// 「キーバインディング」リスト (<c>_expressionBindings</c>) に統合し、各行で
    /// <see cref="BindingMode"/> を選択する形に集約する。
    /// <c>[CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]</c> を付与し、core の
    /// <see cref="UnityEditor.UIElements.PropertyField"/> から自動解決される。
    /// </remarks>
    [CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]
    public sealed class InputSystemAdapterBindingDrawer : PropertyDrawer
    {
        private const string SlugFieldName = "Slug";
        private const string InputActionAssetFieldName = "_inputActionAsset";
        private const string ActionMapNameFieldName = "_actionMapName";
        private const string ExpressionBindingsFieldName = "_expressionBindings";

        public const string RootClassName = "facial-control-input-system-adapter-binding";
        public const string ActionMapDropdownName = "input-system-binding-action-map-dropdown";
        public const string ExpressionBindingsListName = "input-system-binding-expression-bindings-list";
        public const string BindingModeFieldName = "input-system-binding-mode-field";
        public const string ActionDropdownName = "input-system-binding-action-dropdown";
        public const string GazeUseDistinctToggleName = "input-system-binding-gaze-use-distinct-toggle";
        public const string GazeLeftActionDropdownName = "input-system-binding-gaze-left-action-dropdown";
        public const string GazeRightActionDropdownName = "input-system-binding-gaze-right-action-dropdown";
        public const string ExpressionDropdownName = "input-system-binding-expression-dropdown";
        public const string TriggerModeFieldName = "input-system-binding-trigger-mode-field";
        public const string ModeMismatchHelpName = "input-system-binding-mode-mismatch-help";
        public const string OverlaySlotDropdownName = "input-system-binding-overlay-slot-dropdown";
        public const string OverlaySlotHelpName = "input-system-binding-overlay-slot-help";
        public const string ResponseCurveFieldName = "input-system-binding-response-curve-field";

        private const long OverlaySlotRefreshIntervalMs = 500;

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);

            var assetField = AddInputActionAssetField(root, property);
            var actionMapDropdown = AddActionMapDropdown(root, property);

            var expressionIndexProxy = new List<int>();
            var expressionBindingsList = AddExpressionBindingsList(
                root, property, expressionIndexProxy);

            assetField?.RegisterValueChangedCallback(_ =>
            {
                RefreshActionMapChoices(actionMapDropdown, property);
                ReassignItemsSource(expressionBindingsList, property, ExpressionBindingsFieldName, expressionIndexProxy);
            });
            actionMapDropdown?.RegisterValueChangedCallback(_ =>
            {
                ReassignItemsSource(expressionBindingsList, property, ExpressionBindingsFieldName, expressionIndexProxy);
            });

            return root;
        }

        /// <summary>
        /// SerializedProperty の現在の array 状態から index proxy を再構築し、
        /// ListView の <see cref="BaseVerticalCollectionView.itemsSource"/> に再代入したうえで rows を refresh する。
        /// </summary>
        private static void ReassignItemsSource(
            ListView listView,
            SerializedProperty bindingProperty,
            string arrayFieldName,
            List<int> indexProxy)
        {
            if (listView == null) return;

            var arr = bindingProperty.FindPropertyRelative(arrayFieldName);
            RebuildIndexProxy(indexProxy, arr);
            listView.itemsSource = indexProxy;
            listView.RefreshItems();
        }

        // ----------------------------------------------------------------
        // Sections
        // ----------------------------------------------------------------

        private static ObjectField AddInputActionAssetField(VisualElement root, SerializedProperty property)
        {
            var assetProp = property.FindPropertyRelative(InputActionAssetFieldName);
            if (assetProp == null)
            {
                root.Add(new Label($"<missing field: {InputActionAssetFieldName}>"));
                return null;
            }

            var field = new ObjectField("InputActionAsset")
            {
                objectType = typeof(InputActionAsset),
                allowSceneObjects = false,
            };
            field.BindProperty(assetProp);
            root.Add(field);
            return field;
        }

        private static DropdownField AddActionMapDropdown(VisualElement root, SerializedProperty property)
        {
            var mapNameProp = property.FindPropertyRelative(ActionMapNameFieldName);
            if (mapNameProp == null)
            {
                root.Add(new Label($"<missing field: {ActionMapNameFieldName}>"));
                return null;
            }

            var dropdown = new DropdownField("ActionMap 名")
            {
                name = ActionMapDropdownName,
            };
            RefreshActionMapChoices(dropdown, property);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var so = property.serializedObject;
                so.Update();
                var prop = property.FindPropertyRelative(ActionMapNameFieldName);
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                }
            });
            root.Add(dropdown);
            return dropdown;
        }

        private ListView AddExpressionBindingsList(
            VisualElement root, SerializedProperty property, List<int> indexProxy)
        {
            var listProp = property.FindPropertyRelative(ExpressionBindingsFieldName);
            if (listProp == null)
            {
                root.Add(new Label($"<missing field: {ExpressionBindingsFieldName}>"));
                return null;
            }

            RebuildIndexProxy(indexProxy, listProp);

            // NOTE: itemsSource は SetViewController の "後" に設定する。
            // UnityCsReference の BaseVerticalCollectionView.SetViewController は
            // 旧 controller を Dispose して新 controller を SetView する実装で、
            // ListView.itemsSource ゲッターは viewController.itemsSource を直接返すため、
            // 初期化子で itemsSource を渡すと差し替え後に null 化されてしまい、
            // Add ボタンで EnsureItemSourceCanBeResized が例外を投げる。
            var listView = new ListView
            {
                name = ExpressionBindingsListName,
                // 行内は BindingMode によってフィールドの表示/非表示が切り替わるため、
                // 固定高だと非表示分の空白が残り縦に間延びする。DynamicHeight で実高に追従させる。
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                showAddRemoveFooter = true,
                showBorder = true,
                showFoldoutHeader = true,
                // 各行が複数フィールドのブロックになるため、行境界を視認しやすいよう交互背景を付ける。
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                headerTitle = "キーバインディング",
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                makeItem = CreateExpressionBindingRow,
                bindItem = (element, index) => BindExpressionBindingRow(element, index, property),
                // unbindItem: 行が再利用される際に古い RegisterValueChangedCallback を持ち越さないように
                // 子要素を毎回破棄する。bindItem 側は element.Clear() 済み前提で組み立てる。
                unbindItem = (element, _) => element.Clear(),
            };
            // minHeight を指定すると折りたたみ時にもその高さ分の空白が残るため設定しない。
            listView.style.marginTop = 4;
            listView.SetViewController(new SafeListViewController());
            listView.itemsSource = indexProxy;
            // ヘッダー Foldout の開閉状態を SessionState へ永続化し、Inspector 再構築時に復元する。
            ListViewFoldoutStatePersistence.Register(listView, listProp);

            listView.itemsAdded += indices =>
            {
                var so = property.serializedObject;
                so.Update();
                var arr = property.FindPropertyRelative(ExpressionBindingsFieldName);
                if (arr == null) return;
                int oldSize = arr.arraySize;
                int addCount = 0;
                foreach (var _ in indices) addCount++;
                arr.arraySize += addCount;
                // 新規エントリは Normal モード + 既定 Hold で初期化する (追加直後の意味のあるデフォルト)。
                for (int i = oldSize; i < arr.arraySize; i++)
                {
                    var entry = arr.GetArrayElementAtIndex(i);
                    var modeProp = entry.FindPropertyRelative("bindingMode");
                    if (modeProp != null) modeProp.enumValueIndex = (int)BindingMode.Normal;
                    var actionProp = entry.FindPropertyRelative("actionName");
                    if (actionProp != null) actionProp.stringValue = string.Empty;
                    var useDistinctProp = entry.FindPropertyRelative("useDistinctLeftRight");
                    if (useDistinctProp != null) useDistinctProp.boolValue = false;
                    var actionLeftProp = entry.FindPropertyRelative("actionNameLeft");
                    if (actionLeftProp != null) actionLeftProp.stringValue = string.Empty;
                    var actionRightProp = entry.FindPropertyRelative("actionNameRight");
                    if (actionRightProp != null) actionRightProp.stringValue = string.Empty;
                    var exprProp = entry.FindPropertyRelative("expressionId");
                    if (exprProp != null) exprProp.stringValue = string.Empty;
                    var triggerProp = entry.FindPropertyRelative("triggerMode");
                    if (triggerProp != null) triggerProp.enumValueIndex = (int)TriggerMode.Hold;
                }
                so.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arr);
                listView.ClearSelection();
                listView.Rebuild();
            };
            listView.itemsRemoved += indices =>
            {
                var so = property.serializedObject;
                so.Update();
                var arr = property.FindPropertyRelative(ExpressionBindingsFieldName);
                if (arr == null) return;
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    var removeIndex = sorted[i];
                    if (removeIndex >= 0 && removeIndex < arr.arraySize)
                    {
                        arr.DeleteArrayElementAtIndex(removeIndex);
                    }
                }
                so.ApplyModifiedProperties();
                RebuildIndexProxy(indexProxy, arr);
                listView.ClearSelection();
                listView.Rebuild();
            };

            root.Add(listView);
            return listView;
        }

        private static void RebuildIndexProxy(List<int> indexProxy, SerializedProperty arrayProperty)
        {
            indexProxy.Clear();
            if (arrayProperty == null || !arrayProperty.isArray) return;
            for (int i = 0; i < arrayProperty.arraySize; i++) indexProxy.Add(i);
        }

        // ----------------------------------------------------------------
        // Expression bindings row
        // ----------------------------------------------------------------

        private static VisualElement CreateExpressionBindingRow()
        {
            // makeItem は空の container だけを返し、内部要素は BindExpressionBindingRow で毎回再構築する。
            // 旧実装のように子要素を makeItem で作って bindItem で再 bind する形だと、再 bind の度に
            // RegisterValueChangedCallback がスタックして同 evt で複数回 ApplyModifiedProperties が走り、
            // SerializedProperty / arraySize が想定外に変動する → 別 row の bindItem が範囲外 index で
            // GetArrayElementAtIndex を呼んで ArgumentOutOfRangeException、という連鎖を起こしていた。
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 4;
            return container;
        }

        private static void BindExpressionBindingRow(
            VisualElement element, int index, SerializedProperty bindingProperty)
        {
            // unbindItem だけでは itemsRemoved → bindItem 直行のケースで element に旧子要素が残る場合があるため、
            // 防御的に bindItem 先頭でも Clear する。これにより RegisterValueChangedCallback の二重登録を防ぐ。
            element.Clear();

            var listProp = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
            if (listProp == null || index < 0 || index >= listProp.arraySize)
            {
                // index が範囲外 (削除直後にレンダリングがズレた等) の場合は何も bind しない。
                return;
            }

            var serializedObject = bindingProperty.serializedObject;
            serializedObject.Update();

            var entryProp = listProp.GetArrayElementAtIndex(index);
            var bindingModeProp = entryProp.FindPropertyRelative("bindingMode");
            var actionNameProp = entryProp.FindPropertyRelative("actionName");
            var useDistinctLeftRightProp = entryProp.FindPropertyRelative("useDistinctLeftRight");
            var actionNameLeftProp = entryProp.FindPropertyRelative("actionNameLeft");
            var actionNameRightProp = entryProp.FindPropertyRelative("actionNameRight");
            var expressionIdProp = entryProp.FindPropertyRelative("expressionId");
            var triggerModeProp = entryProp.FindPropertyRelative("triggerMode");
            var overlaySlotProp = entryProp.FindPropertyRelative("overlaySlot");
            var overlayTargetLayerProp = entryProp.FindPropertyRelative("overlayTargetLayer");

            // 1) Action dropdown
            var actionDropdown = new DropdownField("Action 名")
            {
                name = ActionDropdownName,
            };
            string actionNameValue = actionNameProp != null ? actionNameProp.stringValue ?? string.Empty : string.Empty;
            var actionChoices = CollectActionNames(bindingProperty);
            actionDropdown.choices = BuildSafeChoices(actionChoices, actionNameValue);
            actionDropdown.SetValueWithoutNotify(actionNameValue);

            var useDistinctLeftRightToggle = new Toggle("Gaze 左右別 Action")
            {
                name = GazeUseDistinctToggleName,
                tooltip = "Gaze モードのみ有効。OFF は Action 名を左右共通で使い、ON は左/右 Action 名を別々に使います。",
            };
            if (useDistinctLeftRightProp != null)
            {
                useDistinctLeftRightToggle.BindProperty(useDistinctLeftRightProp);
            }

            var actionLeftDropdown = new DropdownField("左 Action")
            {
                name = GazeLeftActionDropdownName,
            };
            string actionNameLeftValue = actionNameLeftProp != null
                ? actionNameLeftProp.stringValue ?? string.Empty
                : string.Empty;
            actionLeftDropdown.choices = BuildSafeChoices(actionChoices, actionNameLeftValue);
            actionLeftDropdown.SetValueWithoutNotify(actionNameLeftValue);

            var actionRightDropdown = new DropdownField("右 Action")
            {
                name = GazeRightActionDropdownName,
            };
            string actionNameRightValue = actionNameRightProp != null
                ? actionNameRightProp.stringValue ?? string.Empty
                : string.Empty;
            actionRightDropdown.choices = BuildSafeChoices(actionChoices, actionNameRightValue);
            actionRightDropdown.SetValueWithoutNotify(actionNameRightValue);

            // 2) Expression dropdown
            bool isGazeBinding = IsGazeBinding(entryProp, bindingModeProp);
            var expressionDropdown = new DropdownField(isGazeBinding ? "Gaze チャネル ID" : "表情 ID")
            {
                name = ExpressionDropdownName,
            };
            string expressionIdValue = expressionIdProp != null ? expressionIdProp.stringValue ?? string.Empty : string.Empty;
            var expressionChoices = isGazeBinding
                ? CollectGazeChannelIds(bindingProperty)
                : CollectExpressionIds(bindingProperty);
            expressionDropdown.choices = BuildSafeChoices(expressionChoices, expressionIdValue);
            expressionDropdown.SetValueWithoutNotify(expressionIdValue);
            if (!isGazeBinding)
            {
                expressionDropdown.formatListItemCallback =
                    id => FormatExpressionDropdownLabel(bindingProperty, id);
                expressionDropdown.formatSelectedValueCallback =
                    id => FormatExpressionDropdownLabel(bindingProperty, id);
            }
            else
            {
                expressionDropdown.formatListItemCallback = id => id;
                expressionDropdown.formatSelectedValueCallback = id => id;
            }
            expressionDropdown.RegisterValueChangedCallback(evt =>
            {
                var so = bindingProperty.serializedObject;
                so.Update();
                var list = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
                if (list == null || index < 0 || index >= list.arraySize) return;
                var prop = list.GetArrayElementAtIndex(index).FindPropertyRelative("expressionId");
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                }
            });

            // 3) BindingMode field
            var bindingModeField = new EnumField("動作モード", BindingMode.Normal)
            {
                name = BindingModeFieldName,
                tooltip = "Normal: 通常キー押下による Expression。"
                    + " Gaze: Vector2 入力で目線駆動。"
                    + " Analog: Scalar 連続値で expression weight を 0..1 駆動。",
            };
            if (bindingModeProp != null)
            {
                bindingModeField.BindProperty(bindingModeProp);
            }

            // 4) TriggerMode field
            var triggerModeField = new EnumField("トリガモード", TriggerMode.Hold)
            {
                name = TriggerModeFieldName,
                tooltip = "Normal モード時のみ有効。"
                    + " Hold: 押している間だけ ON。Toggle: 押すたびに ON/OFF が切替わる。",
            };
            if (triggerModeProp != null)
            {
                triggerModeField.BindProperty(triggerModeProp);
            }
            BindingMode currentMode = bindingModeProp != null
                ? (BindingMode)bindingModeProp.enumValueIndex
                : BindingMode.Normal;
            UpdateTriggerModeVisibility(triggerModeField, currentMode);

            // 4b) Overlay モード時のみ表示する overlaySlot / overlayTargetLayer。
            var overlaySlotField = CreateOverlaySlotDropdown(
                bindingProperty,
                index,
                overlaySlotProp);

            var overlayTargetLayerField = new PropertyField(overlayTargetLayerProp, "Overlay 対象レイヤー")
            {
                tooltip = "Overlay モード時のみ有効。Action 押し量で weight を更新する対象レイヤー名（typically \"overlay\"）。",
            };

            UpdateOverlayFieldVisibility(overlaySlotField, overlayTargetLayerField, currentMode);

            // 4c) Analog / Overlay モード時のみ表示する応答カーブ (HID-427)。
            var responseCurveProp = entryProp.FindPropertyRelative("responseCurve");
            var responseCurveField = new CurveField("応答カーブ")
            {
                name = ResponseCurveFieldName,
                tooltip = "Analog / Overlay モード時のみ有効。横軸が Action の値 (0..1)、縦軸が weight。"
                    + " 既定の直線は値をそのまま使う。下に膨らませると半押しでは弱く、奥まで押すと一気に強くなる。",
                ranges = new UnityEngine.Rect(0f, 0f, 1f, 1f),
            };
            if (responseCurveProp != null)
            {
                responseCurveField.BindProperty(responseCurveProp);
            }
            UpdateResponseCurveVisibility(responseCurveField, currentMode);

            UpdateGazeActionFieldState(
                actionDropdown,
                useDistinctLeftRightToggle,
                actionLeftDropdown,
                actionRightDropdown,
                currentMode,
                useDistinctLeftRightProp != null && useDistinctLeftRightProp.boolValue);

            element.Add(expressionDropdown);
            element.Add(bindingModeField);
            element.Add(useDistinctLeftRightToggle);
            element.Add(actionLeftDropdown);
            element.Add(actionRightDropdown);
            element.Add(actionDropdown);
            element.Add(triggerModeField);
            element.Add(overlaySlotField);
            element.Add(overlayTargetLayerField);
            element.Add(responseCurveField);

            // 5) Mode と Action の expectedControlType の不一致を検知するヒントボックス。
            //    Normal モードで連続値 Action (Axis/Vector2) を選んでいる、
            //    あるいは Analog モードで Button Action を選んでいるケースで警告を出す。
            var modeMismatchHelp = new HelpBox(string.Empty, HelpBoxMessageType.Warning)
            {
                name = ModeMismatchHelpName,
            };
            modeMismatchHelp.style.display = DisplayStyle.None;
            element.Add(modeMismatchHelp);

            // Action と BindingMode の整合チェックを実行 (初期表示 + 値変更時)。
            void RefreshModeMismatchHint()
            {
                BindingMode m = bindingModeProp != null
                    ? (BindingMode)bindingModeProp.enumValueIndex
                    : BindingMode.Normal;
                string aName = actionNameProp != null ? actionNameProp.stringValue ?? string.Empty : string.Empty;
                UpdateModeMismatchHint(modeMismatchHelp, bindingProperty, aName, m);
            }
            RefreshModeMismatchHint();

            // Action 変更時のコールバックは validation 更新を含むため、ここで一括登録する。
            actionDropdown.RegisterValueChangedCallback(evt =>
            {
                var so = bindingProperty.serializedObject;
                so.Update();
                var list = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
                if (list == null || index < 0 || index >= list.arraySize) return;
                var prop = list.GetArrayElementAtIndex(index).FindPropertyRelative("actionName");
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                }
                RefreshModeMismatchHint();
            });
            useDistinctLeftRightToggle.RegisterValueChangedCallback(evt =>
            {
                BindingMode m = bindingModeProp != null
                    ? (BindingMode)bindingModeProp.enumValueIndex
                    : BindingMode.Normal;
                UpdateGazeActionFieldState(
                    actionDropdown,
                    useDistinctLeftRightToggle,
                    actionLeftDropdown,
                    actionRightDropdown,
                    m,
                    evt.newValue);
                RefreshModeMismatchHint();
            });
            actionLeftDropdown.RegisterValueChangedCallback(evt =>
            {
                var so = bindingProperty.serializedObject;
                so.Update();
                var list = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
                if (list == null || index < 0 || index >= list.arraySize) return;
                var prop = list.GetArrayElementAtIndex(index).FindPropertyRelative("actionNameLeft");
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                }
            });
            actionRightDropdown.RegisterValueChangedCallback(evt =>
            {
                var so = bindingProperty.serializedObject;
                so.Update();
                var list = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
                if (list == null || index < 0 || index >= list.arraySize) return;
                var prop = list.GetArrayElementAtIndex(index).FindPropertyRelative("actionNameRight");
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                }
            });
            bindingModeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is BindingMode mode)
                {
                    UpdateTriggerModeVisibility(triggerModeField, mode);
                    UpdateOverlayFieldVisibility(overlaySlotField, overlayTargetLayerField, mode);
                    UpdateResponseCurveVisibility(responseCurveField, mode);
                    UpdateGazeActionFieldState(
                        actionDropdown,
                        useDistinctLeftRightToggle,
                        actionLeftDropdown,
                        actionRightDropdown,
                        mode,
                        useDistinctLeftRightToggle.value);
                }
                RefreshModeMismatchHint();
            });
        }

        private static void UpdateGazeActionFieldState(
            VisualElement actionField,
            VisualElement useDistinctLeftRightField,
            VisualElement leftActionField,
            VisualElement rightActionField,
            BindingMode mode,
            bool useDistinctLeftRight)
        {
            bool isGaze = mode == BindingMode.Gaze;
            if (actionField != null)
            {
                actionField.style.display = DisplayStyle.Flex;
                actionField.SetEnabled(!isGaze || !useDistinctLeftRight);
            }
            if (useDistinctLeftRightField != null)
            {
                useDistinctLeftRightField.style.display = isGaze
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            DisplayStyle distinctDisplay = isGaze && useDistinctLeftRight
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (leftActionField != null) leftActionField.style.display = distinctDisplay;
            if (rightActionField != null) rightActionField.style.display = distinctDisplay;
        }

        private static void UpdateResponseCurveVisibility(VisualElement responseCurveField, BindingMode mode)
        {
            if (responseCurveField == null) return;
            responseCurveField.style.display = mode == BindingMode.Analog || mode == BindingMode.Overlay
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private static void UpdateOverlayFieldVisibility(
            VisualElement overlaySlotField, VisualElement overlayTargetLayerField, BindingMode mode)
        {
            DisplayStyle d = mode == BindingMode.Overlay ? DisplayStyle.Flex : DisplayStyle.None;
            if (overlaySlotField != null) overlaySlotField.style.display = d;
            if (overlayTargetLayerField != null) overlayTargetLayerField.style.display = d;
        }

        private static VisualElement CreateOverlaySlotDropdown(
            SerializedProperty bindingProperty,
            int index,
            SerializedProperty overlaySlotProp)
        {
            var container = new VisualElement();

            var dropdown = new DropdownField("Overlay slot")
            {
                name = OverlaySlotDropdownName,
                tooltip = "Overlay モード時のみ有効。駆動する slot 識別子（例: blink）。",
            };
            var help = new HelpBox(string.Empty, HelpBoxMessageType.Warning)
            {
                name = OverlaySlotHelpName,
            };
            help.style.display = DisplayStyle.None;

            RefreshOverlaySlotChoices(dropdown, help, bindingProperty, overlaySlotProp);

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var so = bindingProperty.serializedObject;
                if (!IsSerializedObjectAlive(so)) return;
                so.Update();
                var prop = overlaySlotProp
                    ?? FindExpressionBindingEntryProperty(bindingProperty, index)
                        ?.FindPropertyRelative("overlaySlot");
                if (prop != null)
                {
                    prop.stringValue = evt.newValue ?? string.Empty;
                    so.ApplyModifiedProperties();
                    RefreshOverlaySlotChoices(dropdown, help, bindingProperty, prop);
                }
            });

            container.Add(dropdown);
            container.Add(help);
            IVisualElementScheduledItem scheduledRefresh = null;
            scheduledRefresh = container.schedule.Execute(() =>
            {
                if (!TickOverlaySlotRefresh(dropdown, help, bindingProperty, index))
                {
                    // Inspector 切替等で SerializedObject が破棄された後もタイマーだけが
                    // 生き残ると、ティックごとに破棄済みオブジェクトへアクセスしてしまうため停止する。
                    scheduledRefresh?.Pause();
                }
            });
            scheduledRefresh.Every(OverlaySlotRefreshIntervalMs);

            return container;
        }

        /// <summary>
        /// Overlay slot 候補の定期リフレッシュ 1 回分を実行する。
        /// </summary>
        /// <returns>継続可能なら true。SerializedObject が破棄済みなら false（呼び出し側でタイマー停止）。</returns>
        private static bool TickOverlaySlotRefresh(
            DropdownField dropdown,
            HelpBox help,
            SerializedProperty bindingProperty,
            int index)
        {
            var so = bindingProperty.serializedObject;
            if (!IsSerializedObjectAlive(so))
            {
                return false;
            }

            so.Update();
            var prop = FindExpressionBindingEntryProperty(bindingProperty, index)
                ?.FindPropertyRelative("overlaySlot");
            RefreshOverlaySlotChoices(dropdown, help, bindingProperty, prop);
            return true;
        }

        // SerializedObject に破棄済み判定の公開 API が無いため、native ポインタを直接確認する。
        private static readonly System.Reflection.FieldInfo SerializedObjectNativePtrField =
            typeof(SerializedObject).GetField(
                "m_NativeObjectPtr",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>
        /// <paramref name="so"/> が破棄されておらず、対象オブジェクトも生存しているかを返す。
        /// 破棄済みの SerializedObject へ <see cref="SerializedObject.Update"/> 等を呼ぶと
        /// NullReferenceException になるため、アクセス前に必ず確認する。
        /// </summary>
        private static bool IsSerializedObjectAlive(SerializedObject so)
        {
            if (so == null)
            {
                return false;
            }

            if (SerializedObjectNativePtrField != null)
            {
                if ((IntPtr)SerializedObjectNativePtrField.GetValue(so) == IntPtr.Zero)
                {
                    return false;
                }

                return so.targetObject != null;
            }

            // 内部フィールドが取得できない（Unity 側の変更）場合は例外ベースで判定する。
            try
            {
                return so.targetObject != null;
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch (ArgumentNullException)
            {
                return false;
            }
        }

        private static SerializedProperty FindExpressionBindingEntryProperty(
            SerializedProperty bindingProperty,
            int index)
        {
            var list = bindingProperty.FindPropertyRelative(ExpressionBindingsFieldName);
            if (list == null || index < 0 || index >= list.arraySize)
            {
                return null;
            }

            return list.GetArrayElementAtIndex(index);
        }

        private static void RefreshOverlaySlotChoices(
            DropdownField dropdown,
            HelpBox help,
            SerializedProperty bindingProperty,
            SerializedProperty overlaySlotProp)
        {
            if (dropdown == null) return;

            string current = overlaySlotProp != null ? overlaySlotProp.stringValue ?? string.Empty : string.Empty;
            bool canSelect = TryCollectOverlaySlots(bindingProperty, out var slotNames);
            var choices = BuildSafeChoices(slotNames, current);
            if (!StringListEquals(dropdown.choices, choices))
            {
                dropdown.choices = choices;
            }

            dropdown.SetValueWithoutNotify(current);
            dropdown.SetEnabled(canSelect && overlaySlotProp != null);

            if (help == null) return;
            if (canSelect && overlaySlotProp != null)
            {
                help.style.display = DisplayStyle.None;
            }
            else
            {
                help.text = "FacialCharacterProfileSO の Slots を先に宣言してください。";
                help.style.display = DisplayStyle.Flex;
            }
        }

        private static bool TryCollectOverlaySlots(
            SerializedProperty bindingProperty,
            out List<string> slotNames)
        {
            slotNames = new List<string>();
            var so = bindingProperty.serializedObject;
            if (!(so.targetObject is FacialCharacterProfileSO profileSo))
            {
                return false;
            }

            var slots = profileSo.Slots;
            if (slots == null || slots.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                string slot = slots[i];
                if (string.IsNullOrEmpty(slot)) continue;
                if (!slotNames.Contains(slot)) slotNames.Add(slot);
            }

            return slotNames.Count > 0;
        }

        /// <summary>
        /// Action の expectedControlType と BindingMode の組合せを評価し、
        /// 不一致を警告する HelpBox の表示状態を更新する。
        /// </summary>
        private static void UpdateModeMismatchHint(
            HelpBox helpBox,
            SerializedProperty bindingProperty,
            string actionName,
            BindingMode mode)
        {
            if (helpBox == null) return;

            // Action 未設定時はヒント無し（Action 自体の未設定エラーは別経路で扱う）。
            if (string.IsNullOrEmpty(actionName))
            {
                helpBox.style.display = DisplayStyle.None;
                return;
            }

            var assetProp = bindingProperty.FindPropertyRelative(InputActionAssetFieldName);
            var asset = assetProp != null ? assetProp.objectReferenceValue as InputActionAsset : null;
            if (asset == null)
            {
                helpBox.style.display = DisplayStyle.None;
                return;
            }

            var mapNameProp = bindingProperty.FindPropertyRelative(ActionMapNameFieldName);
            string mapName = mapNameProp != null ? mapNameProp.stringValue : null;
            if (string.IsNullOrEmpty(mapName))
            {
                helpBox.style.display = DisplayStyle.None;
                return;
            }

            var map = asset.FindActionMap(mapName);
            var action = map?.FindAction(actionName);
            string controlType = action != null ? action.expectedControlType ?? string.Empty : string.Empty;

            // expectedControlType が空 (= 未指定) の Action は判定不能。
            if (string.IsNullOrEmpty(controlType))
            {
                helpBox.style.display = DisplayStyle.None;
                return;
            }

            bool isButton = string.Equals(controlType, "Button", StringComparison.OrdinalIgnoreCase);
            bool isContinuous = !isButton; // Axis / Vector2 / Stick / Dpad 等は連続値とみなす。

            string message = null;
            if (mode == BindingMode.Normal && isContinuous)
            {
                message = $"動作モードが Normal ですが、Action '{actionName}' の expectedControlType は '{controlType}' (連続値) です。"
                    + " Analog または Gaze モードに切り替えるとアナログ入力を活用できます。";
            }
            else if (mode == BindingMode.Analog && isButton)
            {
                message = $"動作モードが Analog ですが、Action '{actionName}' の expectedControlType は 'Button' (ON/OFF) です。"
                    + " Action を Axis 系に変更するか、動作モードを Normal に戻してください。";
            }

            if (string.IsNullOrEmpty(message))
            {
                helpBox.style.display = DisplayStyle.None;
            }
            else
            {
                helpBox.text = message;
                helpBox.style.display = DisplayStyle.Flex;
            }
        }

        private static void UpdateTriggerModeVisibility(EnumField triggerModeField, BindingMode mode)
        {
            if (triggerModeField == null) return;
            triggerModeField.style.display = mode == BindingMode.Normal
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            var slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp == null)
            {
                root.Add(new Label($"<missing field: {SlugFieldName}>"));
                return;
            }
            root.Add(new AdapterBindingSlugField(slugProp, typeof(InputSystemAdapterBinding)));
        }

        private static void AddBoundField(
            VisualElement root, SerializedProperty property, string relativePath, string label)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child == null)
            {
                // 該当フィールドが見つからない場合でも他フィールドの描画を止めない。
                root.Add(new Label($"<missing field: {relativePath}>"));
                return;
            }

            var field = new PropertyField(child, label);
            root.Add(field);
        }

        private static void RefreshActionMapChoices(DropdownField dropdown, SerializedProperty property)
        {
            if (dropdown == null) return;
            var choices = new List<string>();

            var assetProp = property.FindPropertyRelative(InputActionAssetFieldName);
            var asset = assetProp != null ? assetProp.objectReferenceValue as InputActionAsset : null;
            if (asset != null)
            {
                foreach (var map in asset.actionMaps)
                {
                    if (!string.IsNullOrEmpty(map.name)) choices.Add(map.name);
                }
            }
            dropdown.choices = choices;

            var mapNameProp = property.FindPropertyRelative(ActionMapNameFieldName);
            var current = mapNameProp != null ? mapNameProp.stringValue : string.Empty;
            if (!string.IsNullOrEmpty(current) && choices.Contains(current))
            {
                dropdown.SetValueWithoutNotify(current);
            }
            else if (choices.Count > 0)
            {
                dropdown.SetValueWithoutNotify(choices[0]);
            }
            else
            {
                dropdown.SetValueWithoutNotify(string.Empty);
            }
        }

        private static List<string> CollectActionNames(SerializedProperty bindingProperty)
        {
            var result = new List<string>();
            var assetProp = bindingProperty.FindPropertyRelative(InputActionAssetFieldName);
            var asset = assetProp != null ? assetProp.objectReferenceValue as InputActionAsset : null;
            if (asset == null) return result;

            var mapNameProp = bindingProperty.FindPropertyRelative(ActionMapNameFieldName);
            var mapName = mapNameProp != null ? mapNameProp.stringValue : null;
            if (string.IsNullOrEmpty(mapName)) return result;

            var map = asset.FindActionMap(mapName);
            if (map == null) return result;

            foreach (var action in map.actions)
            {
                if (!string.IsNullOrEmpty(action.name)) result.Add(action.name);
            }
            return result;
        }

        private static List<string> CollectExpressionIds(SerializedProperty bindingProperty)
        {
            var result = new List<string>();
            var so = bindingProperty.serializedObject;
            if (!(so.targetObject is FacialCharacterProfileSO profileSo))
            {
                return result;
            }
            var mode = bindingProperty.FindPropertyRelative("bindingMode");
            if (mode != null && mode.enumValueIndex == (int)BindingMode.Gaze)
            {
                var channels = profileSo.GazeChannels;
                if (channels == null) return result;
                for (int i = 0; i < channels.Count; i++)
                {
                    var id = channels[i] != null ? channels[i].id : null;
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
                }
                return result;
            }

            var expressions = profileSo.Expressions;
            if (expressions == null) return result;
            for (int i = 0; i < expressions.Count; i++)
            {
                var id = expressions[i] != null ? expressions[i].id : null;
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// dropdown の表示ラベルを「表情名 (なければ id 短縮)」に整形する。
        /// 内部に保存される値は expressionId（hash 文字列）のままで触らない。
        /// </summary>
        private static string FormatExpressionDropdownLabel(
            SerializedProperty bindingProperty, string expressionId)
        {
            if (string.IsNullOrEmpty(expressionId))
            {
                return string.Empty;
            }

            var so = bindingProperty.serializedObject;
            if (!(so.targetObject is FacialCharacterProfileSO profileSo))
            {
                return expressionId;
            }

            var mode = bindingProperty.FindPropertyRelative("bindingMode");
            if (mode != null && mode.enumValueIndex == (int)BindingMode.Gaze)
            {
                return expressionId;
            }

            var expressions = profileSo.Expressions;
            if (expressions == null)
            {
                return expressionId;
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                var e = expressions[i];
                if (e == null) continue;
                if (!string.Equals(e.id, expressionId, StringComparison.Ordinal)) continue;

                if (!string.IsNullOrEmpty(e.name))
                {
                    return e.name;
                }
                break;
            }

            // 名前が空 / 候補から外れた id は判別性のため短縮表示。
            return expressionId.Length <= 8 ? expressionId : expressionId.Substring(0, 8) + "…";
        }

        private static List<string> CollectGazeChannelIds(SerializedProperty bindingProperty)
        {
            var result = new List<string>();
            var so = bindingProperty.serializedObject;
            if (!(so.targetObject is FacialCharacterProfileSO profileSo)) return result;
            var channels = profileSo.GazeChannels;
            if (channels == null) return result;
            for (int i = 0; i < channels.Count; i++)
            {
                var id = channels[i] != null ? channels[i].id : null;
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }

        private static bool IsGazeBinding(
            SerializedProperty entryProperty, SerializedProperty bindingModeProperty)
        {
            if (bindingModeProperty != null && bindingModeProperty.enumValueIndex == (int)BindingMode.Gaze)
            {
                return true;
            }

            var entry = entryProperty != null
                && entryProperty.propertyType == SerializedPropertyType.ManagedReference
                ? entryProperty.managedReferenceValue as ExpressionBindingEntry
                : null;
            return entry != null && entry.bindingMode == BindingMode.Gaze;
        }

        private static List<string> BuildSafeChoices(IReadOnlyList<string> baseChoices, string currentValue)
        {
            var result = new List<string>(baseChoices.Count + 2) { string.Empty };
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

        private static bool StringListEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
