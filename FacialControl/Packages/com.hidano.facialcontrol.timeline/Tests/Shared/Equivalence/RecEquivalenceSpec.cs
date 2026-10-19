using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// REC → Timeline 往復検証で使うキャラクター構成（BlendShape・Expression・レイヤー・入力源 id）。
    /// シナリオ生成・Small 層の評価・PlayMode のキャラクター生成が同じ定義を参照する。
    /// </summary>
    public static class RecEquivalenceSpec
    {
        public const string EmotionLayer = "emotion";
        public const string OverlayLayer = "overlay";
        public const string FaceLayer = "face";

        /// <summary>trigger 入力源（<see cref="FakeTriggerAdapterBinding"/> が登録する。InputSystem の trigger sink の代役）。</summary>
        public const string TriggerSourceId = FakeTriggerAdapterBinding.DefaultSlug + ":" + FakeTriggerAdapterBinding.TriggerSub;

        /// <summary>1 軸の Analog 入力源（<see cref="FakeAnalogAdapterBinding"/> の <c>osc:lt</c>）。</summary>
        public const string AnalogSourceId = FakeAnalogAdapterBinding.DefaultSlug + ":" + FakeAnalogAdapterBinding.AnalogSub;

        /// <summary>2 軸の目線入力源（<see cref="FakeAnalogAdapterBinding"/> の <c>osc:gaze</c>）。</summary>
        public const string GazeSourceId = FakeAnalogAdapterBinding.DefaultSlug + ":" + FakeAnalogAdapterBinding.GazeSub;

        /// <summary>Analog 値を掛ける Expression（overlay レイヤー）の消費者の id。</summary>
        public const string AnalogExpressionSourceId = FakeAnalogAdapterBinding.DefaultSlug + ":analog-expression";

        /// <summary>値提供型の入力源（<see cref="FakeValueProviderAdapterBinding"/>）。</summary>
        public const string ValueProviderSourceId = FakeValueProviderAdapterBinding.DefaultSlug;

        public const string SmileExpressionId = "smile";
        public const string AngryExpressionId = "angry";
        public const string SurpriseExpressionId = "surprise";

        /// <summary>遷移時間 0 の Expression。</summary>
        public const string CalmExpressionId = "calm";

        /// <summary>Analog で駆動する Expression（overlay レイヤー）。</summary>
        public const string SquintExpressionId = "squint";

        public const string LeftEyeName = "LeftEye";
        public const string RightEyeName = "RightEye";

        /// <summary>メッシュの BlendShape（FacialController の index 順）。値提供型の index もこの並び。</summary>
        public static readonly string[] MeshBlendShapes =
        {
            "Smile",
            "Angry",
            "Surprise",
            "Calm",
            "Squint",
            "Blink",
            "JawOpen",
            "EyeWide",
        };

        /// <summary>Analog で駆動する Expression（<see cref="SquintExpressionId"/>）の BlendShape 値（lt = 1 のときの消費者の出力）。</summary>
        public const float AnalogExpressionValue = 0.8f;

        /// <summary>Analog の消費者（<see cref="AnalogExpressionSourceId"/>）の ContributeMask（Squint のビットだけ）。</summary>
        public static byte[] AnalogExpressionMaskBytes()
        {
            int index = Array.IndexOf(MeshBlendShapes, "Squint");
            var mask = new byte[(MeshBlendShapes.Length + 7) / 8];
            mask[index >> 3] |= (byte)(1 << (index & 7));
            return mask;
        }

        /// <summary>値提供型が書く BlendShape の index（Blink / JawOpen / EyeWide）。</summary>
        public static readonly int[] ValueProviderBlendShapeIndices = { 5, 6, 7 };

        /// <summary>emotion レイヤーで trigger から使う Expression。</summary>
        public static readonly string[] TriggerExpressionIds =
        {
            SmileExpressionId,
            AngryExpressionId,
            SurpriseExpressionId,
            CalmExpressionId,
        };

        /// <summary>Expression の定義（id / レイヤー / BlendShape / 値 / 遷移時間 / カーブ）。</summary>
        public static IReadOnlyList<ExpressionSpec> Expressions { get; } = new[]
        {
            new ExpressionSpec(SmileExpressionId, EmotionLayer, "Smile", 1f, 0.3f, TransitionCurveType.Linear),
            new ExpressionSpec(AngryExpressionId, EmotionLayer, "Angry", 1f, 0.2f, TransitionCurveType.EaseOut),
            new ExpressionSpec(SurpriseExpressionId, EmotionLayer, "Surprise", 1f, 0.12f, TransitionCurveType.EaseIn),
            new ExpressionSpec(CalmExpressionId, EmotionLayer, "Calm", 0.7f, 0f, TransitionCurveType.Linear),
            new ExpressionSpec(SquintExpressionId, OverlayLayer, "Squint", AnalogExpressionValue, 0.1f, TransitionCurveType.Linear),
        };

        /// <summary>Small 層（シーンを使わない遷移計算）で使う Domain の Profile。PlayMode の Profile SO と同じ内容。</summary>
        public static FacialProfile CreateDomainProfile()
        {
            var expressions = new Expression[Expressions.Count];
            for (int i = 0; i < expressions.Length; i++)
            {
                ExpressionSpec spec = Expressions[i];
                expressions[i] = new Expression(
                    id: spec.Id,
                    name: spec.Id,
                    layer: spec.Layer,
                    transitionDuration: spec.TransitionDuration,
                    transitionCurve: new TransitionCurve(spec.Curve),
                    blendShapeValues: new[] { new BlendShapeMapping(spec.BlendShape, spec.Value) });
            }

            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins),
                    new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
                    new LayerDefinition(FaceLayer, 2, ExclusionMode.LastWins),
                },
                expressions: expressions);
        }

        public static string CurvePresetName(TransitionCurveType curve)
        {
            switch (curve)
            {
                case TransitionCurveType.EaseIn:
                    return "EaseIn";
                case TransitionCurveType.EaseOut:
                    return "EaseOut";
                case TransitionCurveType.EaseInOut:
                    return "EaseInOut";
                default:
                    return "Linear";
            }
        }

        public readonly struct ExpressionSpec
        {
            public ExpressionSpec(string id, string layer, string blendShape, float value, float transitionDuration, TransitionCurveType curve)
            {
                Id = id;
                Layer = layer;
                BlendShape = blendShape;
                Value = value;
                TransitionDuration = transitionDuration;
                Curve = curve;
            }

            public string Id { get; }

            public string Layer { get; }

            public string BlendShape { get; }

            /// <summary>BlendShape の値（0..1）。</summary>
            public float Value { get; }

            public float TransitionDuration { get; }

            public TransitionCurveType Curve { get; }
        }
    }
}
