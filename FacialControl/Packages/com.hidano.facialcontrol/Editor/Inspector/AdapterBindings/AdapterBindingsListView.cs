using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSO._adapterBindings"/>（<c>[SerializeReference]</c>
    /// <see cref="System.Collections.Generic.List{T}"/> of <see cref="AdapterBindingBase"/>）を
    /// 編集する UI Toolkit reorderable list。
    /// </summary>
    /// <remarks>
    /// <para>
    // / 2.4-2.7 / 3.3 / 3.5 / 3.6 / 7.1 / 12.2 / 12.3 を満たす。+ ボタン（種別選択メニュー）→
    /// <see cref="System.Activator.CreateInstance(System.Type)"/> + slug auto-populate、
    /// - ボタンで選択行削除、Reorder、null 要素の <see cref="MissingAdapterPlaceholderElement"/>、
    /// 同 SO 内 slug 重複時の error class + summary banner、PropertyDrawer 例外時の
    /// per-element fallback element を提供する。
    /// </para>
    /// <para>
    /// 行は大きめのカード表示で、中身を Adapter 単位の <see cref="Foldout"/> で包む。ヘッダーに表示名と
    /// slug を出し、開閉状態は <see cref="AdapterBindingFoldoutState"/> で要素単位に保持する。
    /// 追加/削除はリスト末尾の +/- フッター操作に加え、Foldout ヘッダーの削除ボタンからも行える。
    /// Drawer が <see cref="IAdapterBindingHeaderSummaryProvider"/> を実装していれば、ヘッダーに要約
    /// （OSC の IP・ポート等）も出し、binding の値の変更に追従させる。
    /// ヘッダーの有効トグルで binding を一時的に無効にでき（<see cref="AdapterBindingBase.Disabled"/>）、
    /// 無効の行はヘッダーをグレーアウトする。
    /// ヘッダーの ▲ / ▼ ボタンで並び順を入れ替えられる（Undo 対応）。並び順は Play 時の OnStart / Tick の
    /// 呼び出し順になり、複数の binding が同じ値を書き込む場合（同じレイヤーの overlay weight 等）は後ろが勝つ。
    /// </para>
    /// </remarks>
    public sealed class AdapterBindingsListView : VisualElement
    {
        public const string ErrorRowClassName = "facial-control-error";
        public const string FallbackRowClassName = "facial-control-fallback-row";
        public const string SummaryBannerName = "facial-control-adapter-bindings-summary-banner";
        public const string RowClassName = "facial-control-adapter-binding-row";
        public const string RowBoxClassName = "facial-control-adapter-binding-row-box";
        public const string BoxTitleClassName = "facial-control-adapter-binding-box-title";
        public const string BoxTitleLabelClassName = "facial-control-adapter-binding-box-title-label";
        public const string RootClassName = "facial-control-adapter-bindings-list-view";
        public const string RowFoldoutClassName = "facial-control-adapter-binding-foldout";
        public const string HeaderRemoveButtonClassName = "facial-control-adapter-binding-header-remove";
        public const string HeaderSummaryClassName = "facial-control-adapter-binding-header-summary";
        public const string HeaderEnabledToggleClassName = "facial-control-adapter-binding-header-enabled";
        public const string DisabledRowClassName = "facial-control-adapter-binding-disabled";
        public const string HeaderMoveUpButtonClassName = "facial-control-adapter-binding-header-move-up";
        public const string HeaderMoveDownButtonClassName = "facial-control-adapter-binding-header-move-down";

        public const string FooterAddButtonName = "facial-control-adapter-bindings-add";
        public const string FooterRemoveButtonName = "facial-control-adapter-bindings-remove";
        public const string FooterExpandAllButtonName = "facial-control-adapter-bindings-expand-all";
        public const string FooterCollapseAllButtonName = "facial-control-adapter-bindings-collapse-all";

        private const string AdapterBindingsFieldName = "_adapterBindings";

        private readonly SerializedProperty _listProperty;
        private readonly VisualElement _summaryBanner;
        private readonly VisualElement _rowsContainer;
        private readonly VisualElement _footer;
        private readonly Button _removeButton;
        private AdvancedDropdownState _addDropdownState;
        private int _selectedIndex = -1;
        // 選択は参照 ID で持ち、移動・削除・Undo で index がずれても同じ要素を指す（参照 ID の無い null 要素は index のみ）。
        private long _selectedReferenceId = ManagedReferenceUtility.RefIdNull;

        // 現在の行 Foldout と開閉状態の保存情報（Rebuild のたびに作り直す）。
        private readonly List<RowFoldout> _rowFoldouts = new List<RowFoldout>();

        private sealed class RowFoldout
        {
            public Foldout Foldout;
            public string Key;
            // 最後に SessionState と一致していた値。これと異なる行だけを後から保存し、
            // 別 Inspector が保存した新しい状態を古い View が上書きしないようにする。
            public bool SavedValue;
        }

        /// <summary>
        /// Adapter Binding 追加時に <see cref="IAdapterBindingDefaultLayer"/> 経由で
        /// プロファイルの <c>_layers</c> が自動的に書換えられた直後に呼ばれる通知。
        /// 親 Inspector に Layers セクションの再描画を依頼するためのフック。
        /// </summary>
        public event Action OnLayersAutoModified;

        public AdapterBindingsListView(SerializedProperty listProperty)
        {
            _listProperty = listProperty ?? throw new ArgumentNullException(nameof(listProperty));
            if (!_listProperty.isArray)
            {
                throw new ArgumentException(
                    "listProperty must be an array property bound to _adapterBindings.",
                    nameof(listProperty));
            }

            AddToClassList(RootClassName);

            _summaryBanner = new VisualElement { name = SummaryBannerName };
            _summaryBanner.style.display = DisplayStyle.None;
            Add(_summaryBanner);

            _rowsContainer = new VisualElement { name = "facial-control-adapter-bindings-rows" };
            Add(_rowsContainer);

            // +/- フッター。+ 押下で binding 種別 dropdown、- 押下で選択中の行を削除。
            _footer = new VisualElement();
            _footer.style.flexDirection = FlexDirection.Row;
            _footer.style.justifyContent = Justify.FlexEnd;
            _footer.style.marginTop = 4;

            var expandAllButton = new Button(() => SetAllExpanded(true))
            {
                name = FooterExpandAllButtonName,
                text = "すべて展開",
                tooltip = "すべての Adapter Binding を展開",
            };
            expandAllButton.style.marginRight = 2;
            _footer.Add(expandAllButton);

            var collapseAllButton = new Button(() => SetAllExpanded(false))
            {
                name = FooterCollapseAllButtonName,
                text = "すべて折り畳む",
                tooltip = "すべての Adapter Binding を折り畳む",
            };
            _footer.Add(collapseAllButton);

            var footerSpacer = new VisualElement();
            footerSpacer.style.flexGrow = 1;
            _footer.Add(footerSpacer);

            _removeButton = new Button(RemoveSelected)
            {
                name = FooterRemoveButtonName,
                text = "− 選択中を削除",
                tooltip = "選択中の Adapter Binding を削除",
            };
            _removeButton.style.minWidth = 120;
            _removeButton.style.marginRight = 4;
            _removeButton.SetEnabled(false);
            _footer.Add(_removeButton);

            var addButton = new Button(OpenAddDropdown)
            {
                name = FooterAddButtonName,
                text = "+ Adapter Binding を追加",
                tooltip = "Adapter Binding を追加",
            };
            addButton.style.minWidth = 180;
            _footer.Add(addButton);

            Add(_footer);

            // ChangeEvent は panel 未接続時にディスパッチされないため、detach 時にも
            // 未保存の開閉状態を保存して破棄直前の変更を取りこぼさない。
            // Undo / Redo で要素が戻っても行が古いままにならないよう、表示中は再構築する。
            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedoPerformed);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= OnUndoRedoPerformed;
                SaveFoldoutStates();
            });

            Rebuild();
        }

        // ------------------------------------------------------------------
        // Public API（テスト + Inspector 統合（task 5.6）から呼び出される）
        // ------------------------------------------------------------------

        /// <summary>
        /// <see cref="AdapterBindingDiscovery"/> 経由で取得した descriptor から具象を生成し
        /// list に追加する。slug は <see cref="AdapterSlug.FromDisplayName(string)"/> で
        /// auto-populate される。
        /// </summary>
        public void AddBindingFromDescriptor(AdapterBindingDescriptor descriptor)
        {
            if (descriptor.Type == null)
            {
                throw new ArgumentException("descriptor.Type must be non-null.", nameof(descriptor));
            }

            var instance = (AdapterBindingBase)Activator.CreateInstance(descriptor.Type);
            instance.Slug = AdapterSlug.FromDisplayName(descriptor.OriginalDisplayName).Value;
            if (instance is IAdapterBindingInitialDefaults initial)
            {
                initial.ApplyInitialDefaults();
            }

            // SerializeReference リストへの追加は SerializedProperty 経由で行う。
            // 直接 C# List に Add すると、SerializeReference の子フィールド
            // (Drawer 内の FindPropertyRelative("_settings") 等) が次の
            // ApplyModifiedProperties 完了まで SerializedObject に反映されず、
            // 初回 Rebuild の Drawer.CreatePropertyGUI が空の PropertyField を
            // 生成して「ObjectField のスロットが描画されない」現象が発生する。
            var so = _listProperty.serializedObject;
            so.Update();
            int newIndex = _listProperty.arraySize;
            _listProperty.InsertArrayElementAtIndex(newIndex);
            var elementProp = _listProperty.GetArrayElementAtIndex(newIndex);
            elementProp.managedReferenceValue = instance;
            so.ApplyModifiedProperties();

            // 自身用の Layer がプロファイルに無い場合のみ自動的に作成する
            // (例: uLipSync は専用 Layer を必要とするため Inspector 操作の手間を減らす)。
            bool layersModified = false;
            if (instance is IAdapterBindingDefaultLayer defaultLayer)
            {
                layersModified = EnsureDefaultLayerForBinding(defaultLayer);
            }

            CommitMutation();

            if (layersModified)
            {
                OnLayersAutoModified?.Invoke();
            }
        }

        private bool EnsureDefaultLayerForBinding(IAdapterBindingDefaultLayer defaultLayer)
        {
            var defaultInputSources = ResolveDefaultInputSources(defaultLayer);
            if (defaultInputSources.Count == 0) return false;

            var so = _listProperty.serializedObject;
            so.Update();
            var layersProperty = so.FindProperty("_layers");
            if (layersProperty == null || !layersProperty.isArray) return false;

            // 既に同じ入力源 ID を持つ Layer があれば何もしない (重複追加防止)。
            for (int i = 0; i < layersProperty.arraySize; i++)
            {
                if (LayerContainsAnyInputSourceId(layersProperty.GetArrayElementAtIndex(i), defaultInputSources))
                {
                    return false;
                }
            }

            int newIndex = layersProperty.arraySize;
            layersProperty.InsertArrayElementAtIndex(newIndex);
            var layerProp = layersProperty.GetArrayElementAtIndex(newIndex);

            string desiredName = string.IsNullOrEmpty(defaultLayer.DefaultLayerName)
                ? defaultInputSources[0].id
                : defaultLayer.DefaultLayerName;
            string uniqueName = ResolveUniqueLayerName(layersProperty, desiredName, excludeIndex: newIndex);

            var nameProp = layerProp.FindPropertyRelative("name");
            if (nameProp != null) nameProp.stringValue = uniqueName;

            var priorityProp = layerProp.FindPropertyRelative("priority");
            if (priorityProp != null) priorityProp.intValue = ResolveNextPriority(layersProperty, excludeIndex: newIndex);

            var modeProp = layerProp.FindPropertyRelative("exclusionMode");
            if (modeProp != null) modeProp.enumValueIndex = (int)defaultLayer.DefaultLayerExclusionMode;

            var inputSourcesProp = layerProp.FindPropertyRelative("inputSources");
            if (inputSourcesProp != null && inputSourcesProp.isArray)
            {
                inputSourcesProp.ClearArray();
                for (int i = 0; i < defaultInputSources.Count; i++)
                {
                    inputSourcesProp.InsertArrayElementAtIndex(i);
                    var declProp = inputSourcesProp.GetArrayElementAtIndex(i);
                    var idProp = declProp.FindPropertyRelative("id");
                    if (idProp != null) idProp.stringValue = defaultInputSources[i].id;
                    var weightProp = declProp.FindPropertyRelative("weight");
                    if (weightProp != null) weightProp.floatValue = defaultInputSources[i].weight;
                    var optionsProp = declProp.FindPropertyRelative("optionsJson");
                    if (optionsProp != null) optionsProp.stringValue = string.Empty;
                }
            }

            // layerOverrideMask は空 (= 何も上書きしない) を初期値にする。
            var maskProp = layerProp.FindPropertyRelative("layerOverrideMask");
            if (maskProp != null && maskProp.isArray)
            {
                maskProp.ClearArray();
            }

            so.ApplyModifiedProperties();
            return true;
        }

        private static List<(string id, float weight)> ResolveDefaultInputSources(
            IAdapterBindingDefaultLayer defaultLayer)
        {
            var result = new List<(string id, float weight)>();
            if (defaultLayer is not AdapterBindingBase binding)
            {
                return result;
            }

            var sourcePorts = new SourcePortEnumerator().Enumerate(binding, Array.Empty<string>());
            for (int i = 0; i < sourcePorts.Count; i++)
            {
                string canonicalId = sourcePorts[i].CanonicalId;
                if (string.IsNullOrEmpty(canonicalId))
                {
                    continue;
                }

                result.Add((canonicalId, ResolveDefaultInputWeight(defaultLayer, canonicalId)));
            }

            return result;
        }

        private static float ResolveDefaultInputWeight(
            IAdapterBindingDefaultLayer defaultLayer,
            string canonicalId)
        {
            if (defaultLayer is IAdapterBindingDefaultLayerInputs multipleInputs)
            {
                foreach ((string layerName, IEnumerable<(string id, float weight)> sources) in
                    EnumerateDefaultLayerSources(defaultLayer, multipleInputs))
                {
                    if (sources == null)
                    {
                        continue;
                    }

                    foreach ((string id, float weight) in sources)
                    {
                        if (string.Equals(id, canonicalId, StringComparison.Ordinal))
                        {
                            return weight;
                        }
                    }
                }
            }

            return string.Equals(defaultLayer.DefaultLayerInputSourceId, canonicalId, StringComparison.Ordinal)
                ? 1f
                : 1f;
        }

        private static IEnumerable<(string layerName, IEnumerable<(string id, float weight)> sources)>
            EnumerateDefaultLayerSources(
                IAdapterBindingDefaultLayer defaultLayer,
                IAdapterBindingDefaultLayerInputs multipleInputs)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string layerName in new[] { defaultLayer.DefaultLayerName, "overlay" })
            {
                if (string.IsNullOrEmpty(layerName) || !seen.Add(layerName))
                {
                    continue;
                }

                yield return (layerName, multipleInputs.GetDefaultLayerInputSources(layerName));
            }
        }

        private static bool LayerContainsAnyInputSourceId(
            SerializedProperty layerProp,
            IReadOnlyList<(string id, float weight)> inputSources)
        {
            if (inputSources == null || inputSources.Count == 0) return false;
            for (int i = 0; i < inputSources.Count; i++)
            {
                if (LayerContainsInputSourceId(layerProp, inputSources[i].id))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool LayerContainsInputSourceId(SerializedProperty layerProp, string inputSourceId)
        {
            if (layerProp == null || string.IsNullOrEmpty(inputSourceId)) return false;
            var inputSourcesProp = layerProp.FindPropertyRelative("inputSources");
            if (inputSourcesProp == null || !inputSourcesProp.isArray) return false;
            for (int i = 0; i < inputSourcesProp.arraySize; i++)
            {
                var declProp = inputSourcesProp.GetArrayElementAtIndex(i);
                var idProp = declProp.FindPropertyRelative("id");
                if (idProp != null && string.Equals(idProp.stringValue, inputSourceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ResolveUniqueLayerName(
            SerializedProperty layersProperty, string desired, int excludeIndex)
        {
            if (string.IsNullOrEmpty(desired)) desired = "layer";

            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layersProperty.arraySize; i++)
            {
                if (i == excludeIndex) continue;
                var nameProp = layersProperty.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                {
                    existing.Add(nameProp.stringValue);
                }
            }

            if (!existing.Contains(desired)) return desired;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = $"{desired}-{suffix}";
                if (!existing.Contains(candidate)) return candidate;
            }
            return desired + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        private static int ResolveNextPriority(SerializedProperty layersProperty, int excludeIndex)
        {
            int maxPriority = -1;
            bool anyFound = false;
            for (int i = 0; i < layersProperty.arraySize; i++)
            {
                if (i == excludeIndex) continue;
                var priorityProp = layersProperty.GetArrayElementAtIndex(i).FindPropertyRelative("priority");
                if (priorityProp == null) continue;
                if (!anyFound || priorityProp.intValue > maxPriority)
                {
                    maxPriority = priorityProp.intValue;
                    anyFound = true;
                }
            }

            if (!anyFound) return 0;
            return maxPriority + 1;
        }

        /// <summary>
        /// 指定 index の binding を list から削除し、SO を dirty 化する。
        /// </summary>
        public void RemoveBindingAt(int index)
        {
            var list = ResolveUnderlyingList();
            if (list == null) return;
            if (index < 0 || index >= list.Count) return;

            string removedKey = index < _listProperty.arraySize
                ? AdapterBindingFoldoutState.GetSessionStateKey(_listProperty.GetArrayElementAtIndex(index))
                : null;

            list.RemoveAt(index);
            if (_selectedReferenceId == ManagedReferenceUtility.RefIdNull && _selectedIndex >= list.Count)
            {
                _selectedIndex = list.Count - 1;
            }
            CommitMutation();

            // 削除した要素の開閉状態は不要になるため消す。
            AdapterBindingFoldoutState.Erase(removedKey);
        }

        /// <summary>
        /// 指定 index の binding の有効 / 無効を切り替える（<see cref="AdapterBindingBase.Disabled"/> を書き込む）。
        /// 設定値には触れず、ヘッダーのトグルと無効表示も合わせて更新する。型欠落の要素は対象外。
        /// </summary>
        public void SetBindingEnabled(int index, bool enabled)
        {
            _listProperty.serializedObject.Update();
            if (index < 0 || index >= _listProperty.arraySize) return;

            var element = _listProperty.GetArrayElementAtIndex(index);
            if (element.managedReferenceValue == null) return;
            var disabledProp = element.FindPropertyRelative(nameof(AdapterBindingBase.Disabled));
            if (disabledProp == null) return;

            disabledProp.boolValue = !enabled;
            _listProperty.serializedObject.ApplyModifiedProperties();

            if (index < _rowsContainer.childCount)
            {
                ApplyEnabledState(_rowsContainer[index], enabled);
            }
        }

        /// <summary>
        /// 指定 index の binding を <paramref name="destinationIndex"/> へ移動する。
        /// <see cref="SerializedProperty.MoveArrayElement"/> で書き込むため Undo で戻せ、
        /// 開閉状態は参照 ID 単位で保存しているので移動した要素に付いたままになる。
        /// 範囲外・同じ位置の指定は何もせず false を返す。
        /// </summary>
        public bool MoveBinding(int index, int destinationIndex)
        {
            var so = _listProperty.serializedObject;
            so.Update();
            int count = _listProperty.arraySize;
            if (index < 0 || index >= count) return false;
            if (destinationIndex < 0 || destinationIndex >= count) return false;
            if (index == destinationIndex) return false;

            if (!_listProperty.MoveArrayElement(index, destinationIndex)) return false;
            so.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("Adapter Binding を移動");

            // 参照 ID の無い null 要素を選択していた場合だけ、index で同じ要素へ追従させる
            // （参照 ID で選択している場合は Rebuild で引き直す）。
            if (_selectedReferenceId == ManagedReferenceUtility.RefIdNull && _selectedIndex >= 0)
            {
                _selectedIndex = FollowMovedIndex(_selectedIndex, index, destinationIndex);
            }

            CommitMutation();
            return true;
        }

        /// <summary>
        /// <paramref name="from"/> → <paramref name="to"/> の移動後に、元 <paramref name="current"/> にあった要素の index を返す
        /// （移動した要素は移動先へ、間の要素は 1 つずつずれる）。
        /// </summary>
        internal static int FollowMovedIndex(int current, int from, int to)
        {
            if (current == from) return to;
            if (from < to && current > from && current <= to) return current - 1;
            if (to < from && current >= to && current < from) return current + 1;
            return current;
        }

        /// <summary>
        /// 選択中の binding の index（未選択なら -1）。フッターの「− 選択中を削除」の対象。
        /// </summary>
        public int SelectedIndex => _selectedIndex;

        /// <summary>
        /// 指定 index の binding を選択する（行のクリックと同じ）。範囲外なら選択を外す。
        /// </summary>
        public void SelectBinding(int index)
        {
            if (index < 0 || index >= _listProperty.arraySize)
            {
                _selectedIndex = -1;
                _selectedReferenceId = ManagedReferenceUtility.RefIdNull;
            }
            else
            {
                _selectedIndex = index;
                long referenceId = _listProperty.GetArrayElementAtIndex(index).managedReferenceId;
                // 参照 ID が未確定の要素は null 要素と同じく index で持つ。
                _selectedReferenceId = referenceId == ManagedReferenceUtility.RefIdUnknown
                    ? ManagedReferenceUtility.RefIdNull
                    : referenceId;
            }

            ApplySelectionMarkers();
            UpdateRemoveButtonState();
        }

        /// <summary>
        /// すべての Adapter 行 Foldout を展開 / 折り畳みし、開閉状態を保存する。
        /// </summary>
        public void SetAllExpanded(bool expanded)
        {
            for (int i = 0; i < _rowFoldouts.Count; i++)
            {
                var row = _rowFoldouts[i];
                row.Foldout.value = expanded;
                AdapterBindingFoldoutState.Save(row.Key, expanded);
                row.SavedValue = expanded;
            }
        }

        // ------------------------------------------------------------------
        // 内部: rebuild / row 構築 / duplicate 検出
        // ------------------------------------------------------------------

        private void Rebuild()
        {
            // 行の作り直しで ChangeEvent を経ずに捨てられる開閉状態を先に保存する。
            SaveFoldoutStates();
            SyncSelectedIndex();
            _rowFoldouts.Clear();
            _rowsContainer.Clear();

            int count = _listProperty.arraySize;
            for (int i = 0; i < count; i++)
            {
                var row = BuildRow(i);
                _rowsContainer.Add(row);
            }

            ApplySelectionMarkers();
            ApplyDuplicateSlugMarkers();
            UpdateRemoveButtonState();
        }

        private VisualElement BuildRow(int index)
        {
            var row = new VisualElement { name = $"adapter-binding-row-{index}" };
            row.AddToClassList(RowClassName);
            row.AddToClassList(RowBoxClassName);
            // 大きめのカード表示。クリックで選択して - フッターでの削除対象にする。
            row.style.marginTop = 2;
            row.style.marginBottom = 4;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.borderTopWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderRightWidth = 1;

            int capturedIndex = index;
            row.RegisterCallback<PointerDownEvent>(_ => SelectBinding(capturedIndex));

            var prop = _listProperty.GetArrayElementAtIndex(index);
            object value = prop.managedReferenceValue;

            if (value == null)
            {
                // 型が解決できない要素も参照 ID は残るため、キーを取れればその単位で保存する
                // （本当に null の要素はキーが null になり、常に展開で開始する）。
                var missingFoldout = BuildRowFoldout(
                    index, "Missing Adapter Binding", prop);
                row.Add(missingFoldout);
                // 削除はヘッダーの − ボタンに集約する（placeholder 側の Remove は出さない）。
                var placeholder = new MissingAdapterPlaceholderElement(
                    prop.managedReferenceFullTypename,
                    onRemove: null);
                missingFoldout.Add(placeholder);
                return row;
            }

            var bindingType = value.GetType();
            var drawer = TryCreateCustomDrawer(bindingType);

            // Foldout ヘッダーに binding type の表示名と slug を出す。
            string displayName = GetDisplayName(bindingType);
            var slugProp = prop.FindPropertyRelative(nameof(AdapterBindingBase.Slug));
            var foldout = BuildRowFoldout(
                index,
                BuildHeaderText(displayName, slugProp?.stringValue),
                prop);
            if (slugProp != null)
            {
                // slug を編集したらヘッダー表示も追従させる。
                foldout.TrackPropertyValue(slugProp, p => foldout.text = BuildHeaderText(displayName, p.stringValue));
            }
            if (drawer is IAdapterBindingHeaderSummaryProvider summaryProvider)
            {
                AddHeaderSummary(foldout, index, prop, summaryProvider, bindingType);
            }
            row.Add(foldout);
            AddHeaderEnabledToggle(row, foldout, index, prop);

            if (drawer != null)
            {
                try
                {
                    var element = drawer.CreatePropertyGUI(prop);
                    if (element == null)
                    {
                        foldout.Add(new PropertyField(prop));
                    }
                    else
                    {
                        foldout.Add(element);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[FacialControl] PropertyDrawer for '{bindingType.FullName}' threw {ex.GetType().Name}: " +
                        $"{ex.Message}. A fallback element is shown for this row .");
                    foldout.Add(BuildFallbackElement(bindingType, index));
                    return row;
                }
            }
            else
            {
                foldout.Add(BuildDefaultBody(prop));
            }

            return row;
        }

        /// <summary>
        /// Adapter 行の Foldout を組み立てる。ヘッダーに削除ボタンを置き、開閉状態を
        /// <see cref="AdapterBindingFoldoutState"/> で要素単位に保存・復元する。
        /// </summary>
        private Foldout BuildRowFoldout(int index, string headerText, SerializedProperty elementProperty)
        {
            string sessionStateKey = AdapterBindingFoldoutState.GetSessionStateKey(elementProperty);
            long referenceId = elementProperty.managedReferenceId;
            bool open = AdapterBindingFoldoutState.Load(sessionStateKey);

            var foldout = new Foldout
            {
                name = $"adapter-binding-foldout-{index}",
                text = headerText,
                value = open,
            };
            foldout.AddToClassList(RowFoldoutClassName);
            // 行カード内で drawer の内容を従来どおり左端に揃える。
            foldout.contentContainer.style.marginLeft = 0;

            var headerLabel = foldout.Q<Label>(className: Foldout.textUssClassName);
            if (headerLabel != null)
            {
                headerLabel.AddToClassList(BoxTitleLabelClassName);
                headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                headerLabel.style.flexGrow = 1;
            }

            var headerInput = foldout.Q(className: Foldout.inputUssClassName);
            if (headerInput != null)
            {
                var removeButton = new Button(() => ConfirmAndRemove(foldout.text, referenceId, index))
                {
                    text = "−",
                    tooltip = "この Adapter Binding を削除",
                };
                removeButton.AddToClassList(HeaderRemoveButtonClassName);
                // ヘッダーのクリックで開閉・行選択が同時に起きないよう、押下をここで止める。
                removeButton.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                removeButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

                int count = _listProperty.arraySize;
                headerInput.Add(BuildMoveButton(
                    "▲", "この Adapter Binding を 1 つ上へ移動（Play 時の起動・更新順が変わる）",
                    HeaderMoveUpButtonClassName, referenceId, index, -1, enabled: index > 0));
                headerInput.Add(BuildMoveButton(
                    "▼", "この Adapter Binding を 1 つ下へ移動（Play 時の起動・更新順が変わる）",
                    HeaderMoveDownButtonClassName, referenceId, index, 1, enabled: index < count - 1));
                headerInput.Add(removeButton);
            }

            var rowFoldout = new RowFoldout { Foldout = foldout, Key = sessionStateKey, SavedValue = open };
            foldout.RegisterValueChangedCallback(evt =>
            {
                // 内包する Toggle / 子 Foldout の ChangeEvent<bool> も bubble してくるため、
                // この Foldout 自身の開閉のみを保存する。
                if (evt.target == foldout)
                {
                    AdapterBindingFoldoutState.Save(sessionStateKey, evt.newValue);
                    rowFoldout.SavedValue = evt.newValue;
                }
            });

            _rowFoldouts.Add(rowFoldout);
            return foldout;
        }

        /// <summary>
        /// ヘッダーの ▲ / ▼ ボタン。押下時に組み立て時の参照 ID から現在の index を引き直して移動する
        /// （Undo 等で行と要素の対応がずれていても別の要素を動かさない）。先頭の ▲・末尾の ▼ は無効化する。
        /// </summary>
        private Button BuildMoveButton(
            string text,
            string tooltip,
            string className,
            long referenceId,
            int builtIndex,
            int offset,
            bool enabled)
        {
            var button = new Button(() =>
            {
                int index = ResolveCurrentIndex(referenceId, builtIndex);
                if (index < 0 || !MoveBinding(index, index + offset))
                {
                    // 対応する要素が見つからない・端にいる（行が古い）場合は動かさずに表示を最新化する。
                    Rebuild();
                }
            })
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList(className);
            button.SetEnabled(enabled);
            // ヘッダーのクリックで開閉・行選択が同時に起きないよう、押下をここで止める。
            button.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            button.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            return button;
        }

        /// <summary>
        /// Foldout ヘッダーの表示名の手前に有効 / 無効トグルを置く。折り畳んだままでも切り替えられ、
        /// 無効の行はヘッダーをグレーアウトする。
        /// </summary>
        private void AddHeaderEnabledToggle(
            VisualElement row,
            Foldout foldout,
            int index,
            SerializedProperty elementProperty)
        {
            var headerInput = foldout.Q(className: Foldout.inputUssClassName);
            var disabledProp = elementProperty.FindPropertyRelative(nameof(AdapterBindingBase.Disabled));
            if (headerInput == null || disabledProp == null) return;

            var toggle = new Toggle
            {
                name = $"adapter-binding-enabled-{index}",
                tooltip = "この Adapter Binding を有効にする（無効の間は Play で起動しない。設定は残る）",
            };
            toggle.AddToClassList(HeaderEnabledToggleClassName);
            toggle.style.marginRight = 4;
            toggle.SetValueWithoutNotify(!disabledProp.boolValue);
            // ヘッダーのクリックで開閉・行選択が同時に起きないよう、押下をここで止める。
            toggle.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            toggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            long referenceId = elementProperty.managedReferenceId;
            toggle.RegisterValueChangedCallback(evt =>
            {
                // Foldout の開閉保存（ChangeEvent<bool> の bubble）に拾わせない。
                evt.StopPropagation();
                // 組み立て時の参照 ID から現在の index を引き直す（行と要素の対応がずれていても別の要素を書き換えない）。
                int currentIndex = ResolveCurrentIndex(referenceId, index);
                if (currentIndex < 0) return;
                SetBindingEnabled(currentIndex, evt.newValue);
                ApplyEnabledState(row, evt.newValue);
            });

            var headerLabel = foldout.Q<Label>(className: Foldout.textUssClassName);
            int insertAt = headerLabel != null && headerLabel.parent == headerInput
                ? headerInput.IndexOf(headerLabel)
                : 0;
            headerInput.Insert(insertAt, toggle);

            ApplyEnabledState(row, !disabledProp.boolValue);
            // body 側の PropertyField 等から書き換えられた場合も追従させる（Undo / Redo は行ごと作り直す）。
            toggle.TrackSerializedObjectValue(
                elementProperty.serializedObject,
                _ =>
                {
                    if (toggle.panel == null || elementProperty.serializedObject.targetObject == null) return;
                    bool enabled;
                    try
                    {
                        enabled = !disabledProp.boolValue;
                    }
                    catch (Exception)
                    {
                        // 要素の追加・削除の直後は行の作り直し前に古い要素を指していることがある（作り直した行が正しい値を出す）。
                        return;
                    }
                    toggle.SetValueWithoutNotify(enabled);
                    ApplyEnabledState(row, enabled);
                });
        }

        /// <summary>
        /// 行の無効表示（クラスとヘッダーのグレーアウト）とトグルの値を合わせる。
        /// </summary>
        private static void ApplyEnabledState(VisualElement row, bool enabled)
        {
            var toggle = row.Q<Toggle>(className: HeaderEnabledToggleClassName);
            toggle?.SetValueWithoutNotify(enabled);

            if (enabled)
            {
                row.RemoveFromClassList(DisabledRowClassName);
            }
            else
            {
                row.AddToClassList(DisabledRowClassName);
            }

            var foldout = row.Q<Foldout>(className: RowFoldoutClassName);
            var headerLabel = foldout?.Q<Label>(className: Foldout.textUssClassName);
            if (headerLabel != null)
            {
                headerLabel.style.opacity = enabled ? 1f : 0.5f;
            }
            var summary = foldout?.Q<Label>(className: HeaderSummaryClassName);
            if (summary != null)
            {
                summary.style.opacity = enabled ? 0.75f : 0.4f;
            }
        }

        /// <summary>
        /// Foldout ヘッダーの移動・削除ボタンの手前に要約ラベルを置き、プロファイルの値が変わるたびに取り直す。
        /// </summary>
        private static void AddHeaderSummary(
            Foldout foldout,
            int index,
            SerializedProperty elementProperty,
            IAdapterBindingHeaderSummaryProvider provider,
            Type bindingType)
        {
            var headerInput = foldout.Q(className: Foldout.inputUssClassName);
            if (headerInput == null) return;

            var label = new Label { name = $"adapter-binding-header-summary-{index}" };
            label.AddToClassList(HeaderSummaryClassName);
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            label.style.marginRight = 6;
            label.style.opacity = 0.75f;

            var firstButton = headerInput.Q<Button>(className: HeaderMoveUpButtonClassName)
                ?? headerInput.Q<Button>(className: HeaderRemoveButtonClassName);
            int insertAt = firstButton != null ? headerInput.IndexOf(firstButton) : headerInput.childCount;
            headerInput.Insert(insertAt, label);

            ApplyHeaderSummary(label, elementProperty, provider, bindingType, logErrors: true);
            // SerializeReference 要素への TrackPropertyValue は子プロパティ（_port や _endpoints 内の
            // host / port / enabled）の変更を通知しないことがあるため、SerializedObject 全体の変更で取り直す。
            // 要約の組み立ては軽く、Inspector の編集頻度でしか走らない。
            label.TrackSerializedObjectValue(
                elementProperty.serializedObject,
                _ =>
                {
                    // 要素の追加・削除・Undo の直後は行の作り直し前に古い要素を指していることがあるため、
                    // 追跡からの再計算では失敗を警告せずに読み飛ばす（作り直した行が正しい値を出す）。
                    if (label.panel == null || elementProperty.serializedObject.targetObject == null) return;
                    ApplyHeaderSummary(label, elementProperty, provider, bindingType, logErrors: false);
                });
        }

        private static void ApplyHeaderSummary(
            Label label,
            SerializedProperty elementProperty,
            IAdapterBindingHeaderSummaryProvider provider,
            Type bindingType,
            bool logErrors)
        {
            AdapterBindingHeaderSummary summary;
            try
            {
                summary = provider.GetHeaderSummary(elementProperty);
            }
            catch (Exception ex)
            {
                // 要約は表示だけの補助なので、失敗しても行の描画は止めない。
                if (!logErrors) return;
                Debug.LogWarning(
                    $"[FacialControl] Header summary for '{bindingType.FullName}' threw {ex.GetType().Name}: {ex.Message}");
                summary = AdapterBindingHeaderSummary.None;
            }

            label.text = summary.Text ?? string.Empty;
            label.tooltip = summary.Tooltip ?? string.Empty;
            label.style.display = summary.IsEmpty ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// ヘッダーの − ボタン用。確認のうえ、組み立て時の参照 ID から現在の index を引き直して削除する
        /// （Undo 等で行と要素の対応がずれていても別の要素を消さない）。
        /// </summary>
        private void ConfirmAndRemove(string headerText, long referenceId, int builtIndex)
        {
            if (!EditorUtility.DisplayDialog(
                    "Adapter Binding を削除",
                    $"「{headerText}」を削除しますか？",
                    "削除",
                    "キャンセル"))
            {
                return;
            }

            int index = ResolveCurrentIndex(referenceId, builtIndex);
            if (index < 0)
            {
                // 対応する要素が見つからない（行が古い）場合は消さずに表示を最新化する。
                Rebuild();
                return;
            }

            RemoveBindingAt(index);
        }

        /// <summary>
        /// 行の組み立て時の参照 ID から現在の index を引き直す（SerializedObject を最新化してから引く）。
        /// 参照 ID の無い null 要素は組み立て時の index を使う。見つからない・範囲外なら -1。
        /// </summary>
        private int ResolveCurrentIndex(long referenceId, int builtIndex)
        {
            _listProperty.serializedObject.Update();
            int index = referenceId != ManagedReferenceUtility.RefIdNull
                ? FindIndexByReferenceId(referenceId)
                : builtIndex;
            return index >= 0 && index < _listProperty.arraySize ? index : -1;
        }

        /// <summary>
        /// 参照 ID で持っている選択を現在の index へ引き直す（削除された要素なら選択を外す）。
        /// </summary>
        private void SyncSelectedIndex()
        {
            if (_selectedReferenceId != ManagedReferenceUtility.RefIdNull)
            {
                _selectedIndex = FindIndexByReferenceId(_selectedReferenceId);
                if (_selectedIndex < 0) _selectedReferenceId = ManagedReferenceUtility.RefIdNull;
            }
            else if (_selectedIndex >= _listProperty.arraySize)
            {
                _selectedIndex = _listProperty.arraySize - 1;
            }
        }

        private int FindIndexByReferenceId(long referenceId)
        {
            for (int i = 0; i < _listProperty.arraySize; i++)
            {
                if (_listProperty.GetArrayElementAtIndex(i).managedReferenceId == referenceId)
                {
                    return i;
                }
            }
            return -1;
        }

        private void OnUndoRedoPerformed()
        {
            if (_listProperty.serializedObject.targetObject == null) return;
            _listProperty.serializedObject.Update();
            ClearIndexOnlySelection();
            Rebuild();
        }

        /// <summary>
        /// 参照 ID の無い null 要素の選択は Undo / Redo で同じ要素を追えないため、別の Adapter を
        /// 削除対象にしないよう選択を外す（参照 ID で持つ選択はそのまま Rebuild で引き直す）。
        /// </summary>
        internal void ClearIndexOnlySelection()
        {
            if (_selectedReferenceId == ManagedReferenceUtility.RefIdNull)
            {
                _selectedIndex = -1;
            }
        }

        private static string BuildHeaderText(string displayName, string slug)
        {
            return string.IsNullOrEmpty(slug) ? displayName : $"{displayName}  ({slug})";
        }

        private void SaveFoldoutStates()
        {
            for (int i = 0; i < _rowFoldouts.Count; i++)
            {
                var row = _rowFoldouts[i];
                if (row.Foldout.value == row.SavedValue) continue;
                AdapterBindingFoldoutState.Save(row.Key, row.Foldout.value);
                row.SavedValue = row.Foldout.value;
            }
        }

        private void ApplySelectionMarkers()
        {
            int count = _rowsContainer.childCount;
            for (int i = 0; i < count; i++)
            {
                var row = _rowsContainer[i];
                if (i == _selectedIndex)
                {
                    row.style.borderTopColor = new StyleColor(new Color(0.45f, 0.7f, 0.95f));
                    row.style.borderBottomColor = new StyleColor(new Color(0.45f, 0.7f, 0.95f));
                    row.style.borderLeftColor = new StyleColor(new Color(0.45f, 0.7f, 0.95f));
                    row.style.borderRightColor = new StyleColor(new Color(0.45f, 0.7f, 0.95f));
                }
                else
                {
                    var dim = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 0.5f));
                    row.style.borderTopColor = dim;
                    row.style.borderBottomColor = dim;
                    row.style.borderLeftColor = dim;
                    row.style.borderRightColor = dim;
                }
            }
        }

        private void UpdateRemoveButtonState()
        {
            int count = _listProperty.arraySize;
            bool enabled = _selectedIndex >= 0 && _selectedIndex < count;
            _removeButton.SetEnabled(enabled);
        }

        private void RemoveSelected()
        {
            if (_selectedIndex < 0) return;
            int target = _selectedIndex;
            RemoveBindingAt(target);
        }

        private static string GetDisplayName(Type bindingType)
        {
            if (bindingType == null) return "<unknown>";
            var attr = bindingType.GetCustomAttribute<FacialAdapterBindingAttribute>(inherit: false);
            if (attr != null && !string.IsNullOrWhiteSpace(attr.DisplayName))
            {
                return attr.DisplayName;
            }
            return bindingType.Name;
        }

        /// <summary>
        /// Drawer の無い binding の本文。子プロパティを並べ、ヘッダーのトグルと重複する
        /// <see cref="AdapterBindingBase.Disabled"/> だけは出さない。
        /// </summary>
        private static VisualElement BuildDefaultBody(SerializedProperty elementProperty)
        {
            var body = new VisualElement();
            SerializedProperty iterator = elementProperty.Copy();
            SerializedProperty end = elementProperty.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.name == nameof(AdapterBindingBase.Disabled)) continue;
                body.Add(new PropertyField(iterator.Copy()));
            }
            return body;
        }

        private VisualElement BuildFallbackElement(Type bindingType, int index)
        {
            var fallback = new VisualElement { name = $"adapter-binding-fallback-{index}" };
            fallback.AddToClassList(FallbackRowClassName);
            fallback.Add(new Label(
                $"PropertyDrawer threw for '{bindingType?.FullName ?? "<unknown>"}'. See console for details."));
            return fallback;
        }

        private void ApplyDuplicateSlugMarkers()
        {
            var list = ResolveUnderlyingList();
            if (list == null)
            {
                _summaryBanner.Clear();
                _summaryBanner.style.display = DisplayStyle.None;
                return;
            }

            var slugCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null || string.IsNullOrEmpty(item.Slug)) continue;
                slugCounts.TryGetValue(item.Slug, out int count);
                slugCounts[item.Slug] = count + 1;
            }

            bool anyDuplicate = false;
            foreach (var c in slugCounts.Values)
            {
                if (c > 1) { anyDuplicate = true; break; }
            }

            if (anyDuplicate)
            {
                _summaryBanner.Clear();
                _summaryBanner.Add(new Label(
                    "[FacialControl] Duplicate adapter binding slugs detected. Save will be blocked until resolved ."));
                _summaryBanner.style.display = DisplayStyle.Flex;
            }
            else
            {
                _summaryBanner.Clear();
                _summaryBanner.style.display = DisplayStyle.None;
            }

            int rowCount = _rowsContainer.childCount;
            for (int i = 0; i < list.Count && i < rowCount; i++)
            {
                var item = list[i];
                bool isDup = item != null
                    && !string.IsNullOrEmpty(item.Slug)
                    && slugCounts.TryGetValue(item.Slug, out int c2)
                    && c2 > 1;

                var row = _rowsContainer[i];
                if (isDup)
                {
                    if (!row.ClassListContains(ErrorRowClassName))
                    {
                        row.AddToClassList(ErrorRowClassName);
                    }
                }
                else
                {
                    row.RemoveFromClassList(ErrorRowClassName);
                }
            }
        }

        // ------------------------------------------------------------------
        // SO 同期ヘルパー
        // ------------------------------------------------------------------

        private void CommitMutation()
        {
            var so = _listProperty.serializedObject;
            if (so.targetObject != null)
            {
                EditorUtility.SetDirty(so.targetObject);
            }
            so.Update();
            Rebuild();
        }

        private List<AdapterBindingBase> ResolveUnderlyingList()
        {
            var target = _listProperty.serializedObject.targetObject;
            if (target == null) return null;

            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                var field = t.GetField(
                    AdapterBindingsFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field.GetValue(target) as List<AdapterBindingBase>;
                }
                t = t.BaseType;
            }
            return null;
        }

        private void OpenAddDropdown()
        {
            if (_addDropdownState == null)
            {
                _addDropdownState = new AdvancedDropdownState();
            }
            var descriptors = AdapterBindingDiscovery.GetDescriptors();
            var dropdown = new AdapterBindingAddDropdown(_addDropdownState, descriptors, AddBindingFromDescriptor);
            dropdown.Show(worldBound);
        }

        // ------------------------------------------------------------------
        // PropertyDrawer 解決（reflection で CustomPropertyDrawer の m_Type を読む）
        // ------------------------------------------------------------------

        private static FieldInfo _customPropertyDrawerTargetTypeField;

        private static PropertyDrawer TryCreateCustomDrawer(Type targetType)
        {
            if (targetType == null) return null;

            var typeField = _customPropertyDrawerTargetTypeField ??=
                typeof(CustomPropertyDrawer).GetField(
                    "m_Type",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (typeField == null) return null;

            var drawerTypes = TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>();
            for (int i = 0; i < drawerTypes.Count; i++)
            {
                var drawerType = drawerTypes[i];
                if (drawerType == null) continue;
                if (drawerType.IsAbstract) continue;
                if (drawerType.IsGenericTypeDefinition) continue;
                if (!typeof(PropertyDrawer).IsAssignableFrom(drawerType)) continue;

                var attrs = drawerType.GetCustomAttributes(typeof(CustomPropertyDrawer), inherit: true);
                for (int j = 0; j < attrs.Length; j++)
                {
                    if (!(attrs[j] is CustomPropertyDrawer cpd)) continue;
                    var t = typeField.GetValue(cpd) as Type;
                    if (t == targetType)
                    {
                        try
                        {
                            return (PropertyDrawer)Activator.CreateInstance(drawerType);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                }
            }
            return null;
        }
    }
}
