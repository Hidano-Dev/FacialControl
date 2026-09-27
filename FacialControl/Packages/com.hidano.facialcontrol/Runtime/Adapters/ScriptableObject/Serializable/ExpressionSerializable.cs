using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// Expression 定義 (旧 JSON: expressions[])。
    /// <see cref="Hidano.FacialControl.Domain.Models.Expression"/> の Unity Serializable 投影。
    /// <para>
    /// レイヤー名配列で永続化される <see cref="layerOverrideMask"/>（Domain
    /// <see cref="Hidano.FacialControl.Domain.Models.LayerOverrideMask"/> の永続化形式）を持つ。
    /// <c>transitionDuration / transitionCurve / blendShapeValues</c> は snapshot 経路へ移行する bridge field である。
    /// </para>
    /// <para>
    /// <see cref="cachedSnapshot"/> は AnimationClip サンプリング結果を
    /// キャッシュするフィールド。Runtime fallback 経路から参照可能な永続化形式
    /// (<see cref="ExpressionSnapshotDto"/>) で保持する。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ExpressionSerializable
    {
        [Tooltip("Expression を識別する一意 ID。GUID 推奨。InputBinding の expressionId と一致させる。")]
        public string id;

        [Tooltip("Inspector / デバッグ表示用の表情名。")]
        public string name;

        [Tooltip("所属レイヤー名 (Layers セクションの name と一致させる)。")]
        public string layer;

        [Tooltip("表情の AnimationClip。時刻 0 の BlendShape / Bone 値から snapshot をベイクする。")]
        public AnimationClip animationClip;

        [Tooltip("[Bridge] 遷移時間 (秒)。0〜1 範囲外は自動クランプ。snapshot 経路へ移行予定。")]
        [Range(0f, 1f)]
        public float transitionDuration = Expression.DefaultTransitionDuration;

        [Tooltip("[Bridge] 遷移カーブ。snapshot 経路へ移行予定。")]
        public TransitionCurveSerializable transitionCurve = new TransitionCurveSerializable();

        [Tooltip("[Bridge] BlendShape 値の配列。snapshot 経路へ移行予定。")]
        public List<BlendShapeMappingSerializable> blendShapeValues = new List<BlendShapeMappingSerializable>();

        [Tooltip("他レイヤーへのオーバーライド対象を表すレイヤー名配列。Domain の LayerOverrideMask に対応する永続化形式。")]
        public List<string> layerOverrideMask = new List<string>();

        [Tooltip("AutoExporter がベイクした AnimationClip サンプリング結果のキャッシュ。Runtime fallback 経路で参照される。")]
        public ExpressionSnapshotDto cachedSnapshot;

        [Tooltip("Trigger / Analog 入力で重ね合わせる overlay を slot 単位で宣言する。Default / Suppress / 個別 snapshot override を保持する。")]
        public List<OverlaySlotBindingSerializable> overlays = new List<OverlaySlotBindingSerializable>();
    }
}
