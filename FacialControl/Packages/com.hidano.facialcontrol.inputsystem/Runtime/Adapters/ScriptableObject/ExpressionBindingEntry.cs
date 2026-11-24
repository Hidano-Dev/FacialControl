using System;
using UnityEngine;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.InputSystem.Adapters.ScriptableObject
{
    /// <summary>
    /// InputAction 名と Expression ID を 1 対 1 に対応させる Inspector 編集用エントリ。
    /// <see cref="Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding"/> の
    /// <c>_expressionBindings</c> 内要素として使用。
    /// </summary>
    /// <remarks>
    /// device 種別 (Keyboard / Controller) は <c>ExpressionInputSourceAdapter</c> が
    /// <see cref="UnityEngine.InputSystem.InputAction.bindings"/> から自動推定するため、
    /// 旧 <c>category</c> field は撤去された。
    /// 旧 <c>InputSystemGazeBinding</c> と
    /// <c>AnalogExpressionBindingEntry</c> 由来の機能を <see cref="bindingMode"/> による
    /// 1 リスト内分別で統合する。
    /// </remarks>
    [Serializable]
    public sealed class ExpressionBindingEntry
    {
        [Tooltip("動作モード。Normal=通常キー、Gaze=目線、Analog=連続値で expression を駆動。")]
        public BindingMode bindingMode = BindingMode.Normal;

        [Tooltip("対象 InputAction 名 (InputActionAsset の対象 ActionMap 配下に存在するもの)。")]
        public string actionName;

        [Tooltip("Gaze モードのみ有効。false の場合は actionName を左右共通で使い、true の場合は actionNameLeft/actionNameRight を左右別に使う。")]
        public bool useDistinctLeftRight;

        [Tooltip("Gaze モードかつ useDistinctLeftRight=true の時に使う左目用 InputAction 名。")]
        public string actionNameLeft = string.Empty;

        [Tooltip("Gaze モードかつ useDistinctLeftRight=true の時に使う右目用 InputAction 名。")]
        public string actionNameRight = string.Empty;

        [Tooltip("通常は発火対象の Expression ID (FacialCharacterProfileSO.Expressions[].id)。"
            + " Gaze モードでは Gaze チャネル ID (FacialCharacterProfileSO.GazeChannels[].id) を指定する。")]
        public string expressionId;

        [Tooltip(
            "押下時の動作モード (Normal モード時のみ有効)。"
            + " Hold: 押している間だけ ON、ボタンを離すと OFF。"
            + " Toggle: 押すたびに ON/OFF が切替わる。")]
        public TriggerMode triggerMode = TriggerMode.Hold;

        [Tooltip(
            "Overlay モード時のみ有効: 駆動する overlay slot 識別子（例: blink）。"
            + " 同じ slot を複数の Action で駆動した場合、最後に書き込まれた weight が反映される。")]
        public string overlaySlot;

        [Tooltip(
            "Overlay モード時のみ有効: 重ね合わせを行う対象レイヤー名。"
            + " このレイヤーの weight を Action 値で毎フレーム更新する。"
            + " 通常は emotion レイヤーより priority が大きいレイヤーを指定する（typically \"overlay\"）。")]
        public string overlayTargetLayer = "overlay";

        [Tooltip(
            "Analog / Overlay モード時のみ有効: Action の値 (0..1, 横軸) を weight (縦軸) に変換するカーブ。"
            + " 既定は直線 (値をそのまま使う)。下に膨らませると半押しでは弱く、奥まで押すと一気に強くなる。")]
        public AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
}
