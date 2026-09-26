using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.ExpressionCreator
{
    /// <summary>
    /// AnimationClip への BlendShape 値ベイク + メタデータ AnimationEvent 書き込みを担う
    /// Editor 専用 static helper。
    /// <para>
    /// ベイク経路:
    /// 1. 既存の EditorCurve を全削除（再ベイク時の整合性）
    /// 2. 各 BlendShape 値を <c>SkinnedMeshRenderer.blendShape.{name}</c> binding に
    ///    <see cref="AnimationCurve.Constant(float, float, float)"/> として書き込む
    /// 3. <see cref="AnimationClipExpressionSampler.MetaSetFunctionName"/> 予約 functionName で
    ///    <c>transitionDuration</c> / <c>transitionCurvePreset</c> を <see cref="AnimationEvent"/> として書き込む
    /// </para>
    /// <para>
    /// 逆ロード経路: <see cref="IExpressionAnimationClipSampler.SampleSnapshot"/> 経由で
    /// 時刻 0 の BlendShape 値を取得しスライダー初期値として返す。
    /// </para>
    /// </summary>
    public static class ExpressionClipBakery
    {
        /// <summary>
        /// Bakery の reserved meta keys。Sampler 側と同一仕様。
        /// </summary>
        public const string MetaKeyTransitionDuration = "transitionDuration";

        /// <summary>
        /// Bakery の reserved meta keys。Sampler 側と同一仕様。
        /// </summary>
        public const string MetaKeyTransitionCurvePreset = "transitionCurvePreset";

        private const string BlendShapePropertyPrefix = "blendShape.";

        /// <summary>
        /// Unity の <c>blendShape.*</c> アニメーションカーブ / <c>SkinnedMeshRenderer</c> weight のスケール (0..100)。
        /// <see cref="BlendShapeBakeEntry.Value"/> は正規化 0..1 のため、カーブ書き込み時に本値を乗算する。
        /// </summary>
        private const float BlendShapeWeightScale = 100f;

        /// <summary>
        /// 1 つの BlendShape ベイクエントリ。RendererPath は AnimationClip binding.path に渡される
        /// Transform 階層パス、Value は時刻 0 にベイクされる正規化値 (0..1)。
        /// AnimationClip カーブへは Unity 標準スケール (0..100) へ変換して書き込まれる。
        /// </summary>
        public readonly struct BlendShapeBakeEntry
        {
            public string RendererPath { get; }
            public string BlendShapeName { get; }
            public float Value { get; }

            public BlendShapeBakeEntry(string rendererPath, string blendShapeName, float value)
            {
                RendererPath = rendererPath ?? string.Empty;
                BlendShapeName = blendShapeName ?? string.Empty;
                Value = value;
            }
        }

        /// <summary>
        /// AnimationClip に BlendShape 値と TransitionDuration / TransitionCurvePreset メタデータを
        /// ベイクする。既存 EditorCurve は全削除される。
        /// </summary>
        /// <param name="clip">ベイク対象の AnimationClip（null 不可）</param>
        /// <param name="entries">BlendShape ベイクエントリ列（null 不可、空は許容）</param>
        /// <param name="transitionDuration">遷移時間（秒）。AnimationEvent.floatParameter として書き込む</param>
        /// <param name="transitionCurvePreset">遷移カーブプリセット。enum int 値を float として書き込む</param>
        public static void Bake(
            AnimationClip clip,
            IReadOnlyList<BlendShapeBakeEntry> entries,
            float transitionDuration,
            TransitionCurvePreset transitionCurvePreset)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            ClearExistingCurves(clip);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.BlendShapeName))
                    continue;

                var binding = new EditorCurveBinding
                {
                    path = entry.RendererPath ?? string.Empty,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = BlendShapePropertyPrefix + entry.BlendShapeName,
                };
                // 正規化値 (0..1) を Unity 標準の blendShape weight スケール (0..100) へ変換して書き込む。
                var curve = AnimationCurve.Constant(0f, 0f, entry.Value * BlendShapeWeightScale);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

        }

        /// <summary>
        /// AnimationClip 内の BlendShape 値を <see cref="IExpressionAnimationClipSampler.SampleSnapshot"/>
        /// 経由で取得し、(RendererPath, BlendShapeName) → Value のマップを返す。
        /// ExpressionCreatorWindow がスライダー初期値を復元するために使用する。
        /// </summary>
        public static Dictionary<(string rendererPath, string blendShapeName), float> LoadBlendShapeValues(
            AnimationClip clip,
            IExpressionAnimationClipSampler sampler)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (sampler == null)
                throw new ArgumentNullException(nameof(sampler));

            var snapshot = sampler.SampleSnapshot("__editor_load__", clip);
            var span = snapshot.BlendShapes.Span;
            var result = new Dictionary<(string, string), float>(span.Length);
            for (int i = 0; i < span.Length; i++)
            {
                var bs = span[i];
                result[(bs.RendererPath ?? string.Empty, bs.Name ?? string.Empty)] = bs.Value;
            }
            return result;
        }

        /// <summary>
        /// <see cref="LoadBlendShapeValues"/> の戻り値から、BlendShape 名のみをキーにした
        /// フォールバックマップを作る。
        /// <para>
        /// ランタイムの出力経路（<c>SkinnedMeshRendererBlendShapeWriter</c>）は RendererPath を
        /// 一切参照せず BlendShape 名だけで SkinnedMeshRenderer / BlendShapeIndex を解決する。
        /// そのため Editor 側も、Clip の binding.path がモデル階層と一致しない場合は
        /// BlendShape 名で解決する必要がある（そうしないと Editor 上だけ「存在しない」扱いになる）。
        /// </para>
        /// 同名 BlendShape が複数の RendererPath に存在する場合は、ランタイムが 1 つの出力 index に
        /// 集約するのと同様に 1 値へ畳み込む（絶対値が大きい方を採用）。
        /// </summary>
        public static Dictionary<string, float> BuildBlendShapeNameFallback(
            IReadOnlyDictionary<(string rendererPath, string blendShapeName), float> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var result = new Dictionary<string, float>(values.Count, StringComparer.Ordinal);
            foreach (var kv in values)
            {
                var name = kv.Key.blendShapeName;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!result.TryGetValue(name, out var existing)
                    || Mathf.Abs(kv.Value) > Mathf.Abs(existing))
                {
                    result[name] = kv.Value;
                }
            }

            return result;
        }

        private static void ClearExistingCurves(AnimationClip clip)
        {
            var existing = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < existing.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip, existing[i], null);
            }
        }
    }
}
