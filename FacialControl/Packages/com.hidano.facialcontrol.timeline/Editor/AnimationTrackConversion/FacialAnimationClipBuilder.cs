using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;
using UnityEngine;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// AnimationClip へ書き出す合成結果の評価器。時刻ごとの最終 BlendShape 値・レイヤーごとの寄与・目ボーン回転を返す。
    /// <see cref="TimelinePreviewCompositor"/> が実装する（Timeline の Bake から評価する）。
    /// </summary>
    internal interface IFacialOutputEvaluator
    {
        /// <summary><see cref="TryEvaluateOutput"/> の出力と同じ並びの BlendShape 名。</summary>
        IReadOnlyList<string> BlendShapeNames { get; }

        /// <summary>指定時刻を合成し、最終出力（正規化 weight。<see cref="BlendShapeNames"/> の並び）を返す。</summary>
        bool TryEvaluateOutput(double timeSeconds, out ReadOnlySpan<float> output);

        /// <summary>
        /// 直前の <see cref="TryEvaluateOutput"/> で最終ブレンドに入ったレイヤーが書いた BlendShape を
        /// <paramref name="contributes"/>[layerIndex * BlendShape 数 + shapeIndex] へ true として積む（Profile のレイヤー宣言順）。
        /// </summary>
        void AccumulateLayerContributions(Span<bool> contributes);

        /// <summary>目ボーン回転を書く対象を解決する（ボーンは書き換えない）。</summary>
        void ResolveGazeTargets(
            IReadOnlyList<GazeChannel> gazeChannels,
            GazeEyeBoneResolver resolver,
            List<FacialTimelinePreviewEyeTarget> targets);

        /// <summary><see cref="ResolveGazeTargets"/> で解決した目ボーン 1 本の、指定時刻の localRotation。</summary>
        Quaternion ComputeGazeRotation(double timeSeconds, FacialTimelinePreviewEyeTarget target, IReadOnlyList<GazeChannel> gazeChannels);
    }

    /// <summary><see cref="FacialAnimationClipPart"/> の中身の種類。</summary>
    internal enum FacialAnimationClipKind
    {
        /// <summary>担当レイヤー 1 つ分の BlendShape。</summary>
        Layer,

        /// <summary>どのレイヤーも値を書かなかった BlendShape（ベース表情の値か 0 のまま）。</summary>
        Base,

        /// <summary>目ボーン回転。</summary>
        Gaze,
    }

    /// <summary>
    /// <see cref="FacialAnimationClipBuilder.Build"/> が作った AnimationClip 1 本（未保存）。
    /// </summary>
    internal readonly struct FacialAnimationClipPart
    {
        public FacialAnimationClipPart(FacialAnimationClipKind kind, string name, int layerIndex, AnimationClip clip, int curveCount, int keyCount)
        {
            Kind = kind;
            Name = name;
            LayerIndex = layerIndex;
            Clip = clip;
            CurveCount = curveCount;
            KeyCount = keyCount;
        }

        public FacialAnimationClipKind Kind { get; }

        /// <summary>レイヤー名（<see cref="FacialAnimationClipKind.Layer"/>）、<see cref="FacialAnimationClipBuilder.BaseClipName"/>、<see cref="FacialAnimationClipBuilder.GazeClipName"/>。</summary>
        public string Name { get; }

        /// <summary>Profile のレイヤー index（<see cref="FacialAnimationClipKind.Layer"/> 以外は -1）。</summary>
        public int LayerIndex { get; }

        public AnimationClip Clip { get; }

        /// <summary>Clip のカーブ（binding）数。</summary>
        public int CurveCount { get; }

        /// <summary>Clip の全カーブのキー数の合計。</summary>
        public int KeyCount { get; }
    }

    /// <summary>
    /// 合成結果を、BlendShape ごとの担当レイヤーで振り分けたレイヤー別の AnimationClip と目ボーン回転の AnimationClip にする（HID-410）。
    /// </summary>
    /// <remarks>
    /// <para>各 BlendShape は担当レイヤー 1 つの Clip にだけ入れる。担当レイヤーは、書き出す区間のどこかで最終ブレンドに入って
    /// その BlendShape を書いたレイヤーのうち、Profile 上で優先度が最も高いレイヤー（同じ優先度なら後に宣言したレイヤー。
    /// LayerBlender が後に適用する方）。どのレイヤーも書かなかった BlendShape は <see cref="FacialAnimationClipKind.Base"/> の Clip に入れる。
    /// Clip 同士で同じプロパティを触らないので、AnimationTrack を何本並べても Animator 側で値が混ざらず、最終値は FacialControl の合成結果と一致する。</para>
    /// <para>Clip に入る値は全レイヤーを合成した後の最終値で、担当レイヤー以外の影響も含む。Clip を編集しても FacialControl の
    /// レイヤー合成は再計算されない。</para>
    /// <para>PlayableDirector / TimelineAsset には依存しない。評価する時刻とフレームレートは呼び出し側が渡す。Editor 専用。</para>
    /// </remarks>
    internal static class FacialAnimationClipBuilder
    {
        /// <summary>どのレイヤーも値を書かなかった BlendShape の担当（<see cref="AssignOwnerLayers"/>）。</summary>
        public const int NoOwner = -1;

        public const string BaseClipName = "Base";
        public const string GazeClipName = "Gaze";

        private const string LogPrefix = "[FacialAnimationClipBuilder] ";
        private const string BlendShapePropertyPrefix = "blendShape.";
        private static readonly string[] RotationProperties =
        {
            "m_LocalRotation.x",
            "m_LocalRotation.y",
            "m_LocalRotation.z",
            "m_LocalRotation.w",
        };

        /// <summary>
        /// <paramref name="samples"/> の時刻ごとに評価し、レイヤー別（Profile の宣言順）→ Base → Gaze の順に、カーブを持つ Clip だけを返す。
        /// </summary>
        /// <param name="evaluator">合成結果の評価器。</param>
        /// <param name="controller">書き出し対象（renderer の取得元）。</param>
        /// <param name="profileAsset">GazeChannels の取得元。</param>
        /// <param name="profile">レイヤー名と優先度の取得元（<paramref name="evaluator"/> を組んだ Profile と同じもの）。</param>
        /// <param name="root">カーブの path の基準（Animator の Transform）。</param>
        /// <param name="humanoid">Humanoid の Animator（目ボーンが Avatar にマップされているかの警告用。無ければ null）。</param>
        /// <param name="samples">評価する時刻列。</param>
        /// <param name="frameRate">Clip のフレームレート。</param>
        public static List<FacialAnimationClipPart> Build(
            IFacialOutputEvaluator evaluator,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            Transform root,
            Animator humanoid,
            SampleTimeline samples,
            double frameRate)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (profileAsset == null) throw new ArgumentNullException(nameof(profileAsset));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (samples == null) throw new ArgumentNullException(nameof(samples));

            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            int layerCount = layers.Length;
            var groups = new CurveGroup[layerCount + 2];
            int baseGroup = layerCount;
            int gazeGroup = layerCount + 1;
            for (int g = 0; g < groups.Length; g++)
            {
                groups[g] = new CurveGroup();
            }

            CollectBlendShapeCurves(evaluator, controller, layers, root, samples, groups, baseGroup);
            CollectGazeCurves(evaluator, controller, profileAsset, root, humanoid, samples, groups[gazeGroup]);

            var parts = new List<FacialAnimationClipPart>();
            for (int g = 0; g < groups.Length; g++)
            {
                CurveGroup group = groups[g];
                if (group.Bindings.Count == 0)
                {
                    continue;
                }

                var clip = new AnimationClip { frameRate = (float)frameRate };
                AnimationUtility.SetEditorCurves(clip, group.Bindings.ToArray(), group.Curves.ToArray());
                int keyCount = 0;
                for (int c = 0; c < group.Curves.Count; c++)
                {
                    keyCount += group.Curves[c].length;
                }

                if (g < layerCount)
                {
                    parts.Add(new FacialAnimationClipPart(FacialAnimationClipKind.Layer, layers[g].Name, g, clip, group.Bindings.Count, keyCount));
                }
                else
                {
                    bool isBase = g == baseGroup;
                    parts.Add(new FacialAnimationClipPart(
                        isBase ? FacialAnimationClipKind.Base : FacialAnimationClipKind.Gaze,
                        isBase ? BaseClipName : GazeClipName,
                        -1,
                        clip,
                        group.Bindings.Count,
                        keyCount));
                }
            }

            return parts;
        }

        /// <summary>
        /// BlendShape ごとの担当レイヤー（Profile のレイヤー index。どのレイヤーも書かなければ <see cref="NoOwner"/>）を決める。
        /// </summary>
        /// <param name="contributes">[layerIndex * <paramref name="shapeCount"/> + shapeIndex] が「そのレイヤーが書いた」を表す寄与。</param>
        /// <param name="layers">Profile のレイヤー（宣言順）。</param>
        /// <param name="shapeCount">BlendShape 数。</param>
        public static int[] AssignOwnerLayers(bool[] contributes, ReadOnlySpan<LayerDefinition> layers, int shapeCount)
        {
            var owners = new int[shapeCount];
            for (int s = 0; s < shapeCount; s++)
            {
                int owner = NoOwner;
                for (int l = 0; l < layers.Length; l++)
                {
                    int index = (l * shapeCount) + s;
                    if (index >= contributes.Length || !contributes[index])
                    {
                        continue;
                    }

                    // 同じ優先度は後に宣言したレイヤー（LayerBlender の安定ソートで後に適用され、最終値を決める方）を選ぶ。
                    if (owner == NoOwner || layers[l].Priority >= layers[owner].Priority)
                    {
                        owner = l;
                    }
                }

                owners[s] = owner;
            }

            return owners;
        }

        private static void CollectBlendShapeCurves(
            IFacialOutputEvaluator evaluator,
            FacialController controller,
            ReadOnlySpan<LayerDefinition> layers,
            Transform root,
            SampleTimeline samples,
            CurveGroup[] groups,
            int baseGroup)
        {
            IReadOnlyList<string> names = evaluator.BlendShapeNames;
            int shapeCount = names.Count;
            var outputIndexByName = new Dictionary<string, int>(shapeCount, StringComparer.Ordinal);
            for (int s = 0; s < shapeCount; s++)
            {
                outputIndexByName[names[s]] = s;
            }

            int sampleCount = samples.Times.Count;
            var values = new float[shapeCount][];
            for (int s = 0; s < shapeCount; s++)
            {
                values[s] = new float[sampleCount];
            }

            var contributes = new bool[layers.Length * shapeCount];
            for (int i = 0; i < sampleCount; i++)
            {
                if (!evaluator.TryEvaluateOutput(samples.Times[i], out ReadOnlySpan<float> output))
                {
                    continue;
                }

                int count = Math.Min(shapeCount, output.Length);
                for (int s = 0; s < count; s++)
                {
                    // renderer へ書く値（SkinnedMeshRendererBlendShapeWriter と同じ 0..100 スケール）。
                    values[s][i] = output[s] * 100f;
                }

                evaluator.AccumulateLayerContributions(contributes);
            }

            int[] owners = AssignOwnerLayers(contributes, layers, shapeCount);
            var built = new AnimationCurve[shapeCount];
            SkinnedMeshRenderer[] renderers = TimelinePreviewCompositor.ResolveRenderers(controller);
            for (int r = 0; r < renderers.Length; r++)
            {
                SkinnedMeshRenderer renderer = renderers[r];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || !TryGetPath(renderer.transform, root, out string path))
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    string shapeName = mesh.GetBlendShapeName(shapeIndex);
                    if (!outputIndexByName.TryGetValue(shapeName, out int outputIndex))
                    {
                        continue;
                    }

                    built[outputIndex] ??= FacialAnimationTrackConverter.BuildScalarCurve(
                        samples, values[outputIndex], FacialAnimationTrackConverter.BlendShapeTolerance);
                    int owner = owners[outputIndex];
                    CurveGroup group = groups[owner == NoOwner ? baseGroup : owner];
                    group.Bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), BlendShapePropertyPrefix + shapeName));
                    group.Curves.Add(built[outputIndex]);
                }
            }
        }

        private static void CollectGazeCurves(
            IFacialOutputEvaluator evaluator,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            Transform root,
            Animator humanoid,
            SampleTimeline samples,
            CurveGroup group)
        {
            IReadOnlyList<GazeChannel> gazeChannels = profileAsset.GazeChannels;
            if (gazeChannels == null || gazeChannels.Count == 0)
            {
                return;
            }

            GazeEyeBoneResolver resolver = FacialTimelineEditorPreview.GetGazeEyeResolver(controller);
            var targets = new List<FacialTimelinePreviewEyeTarget>();
            evaluator.ResolveGazeTargets(gazeChannels, resolver, targets);
            if (targets.Count == 0)
            {
                return;
            }

            // 同じボーンを複数の target が指す場合は、プレビュー（後から書いた方が残る）と同じく最後の target を使う。
            var bones = new List<Transform>();
            var lastTargetOfBone = new List<int>();
            for (int t = 0; t < targets.Count; t++)
            {
                int index = bones.IndexOf(targets[t].Bone);
                if (index < 0)
                {
                    bones.Add(targets[t].Bone);
                    lastTargetOfBone.Add(t);
                }
                else
                {
                    lastTargetOfBone[index] = t;
                }
            }

            Transform leftEye = humanoid != null ? humanoid.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform rightEye = humanoid != null ? humanoid.GetBoneTransform(HumanBodyBones.RightEye) : null;
            int sampleCount = samples.Times.Count;
            for (int b = 0; b < bones.Count; b++)
            {
                Transform bone = bones[b];
                if (bone == null || !TryGetPath(bone, root, out string path))
                {
                    continue;
                }

                if (bone == leftEye || bone == rightEye)
                {
                    Debug.LogWarning(LogPrefix + $"目ボーン '{bone.name}' は Humanoid の Avatar にマップされています。Humanoid の Animator ではこのボーンの回転カーブが適用されないことがあります。", bone);
                }

                FacialTimelinePreviewEyeTarget target = targets[lastTargetOfBone[b]];
                var components = new float[sampleCount * 4];
                for (int i = 0; i < sampleCount; i++)
                {
                    Quaternion rotation = evaluator.ComputeGazeRotation(samples.Times[i], target, gazeChannels);
                    components[(i * 4) + 0] = rotation.x;
                    components[(i * 4) + 1] = rotation.y;
                    components[(i * 4) + 2] = rotation.z;
                    components[(i * 4) + 3] = rotation.w;
                }

                QuaternionChannelMetric.AlignHemispheres(components);
                AnimationCurve[] rotationCurves = FacialAnimationTrackConverter.BuildQuaternionCurves(
                    samples, components, FacialAnimationTrackConverter.GazeRotationToleranceDegrees);
                for (int c = 0; c < 4; c++)
                {
                    group.Bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), RotationProperties[c]));
                    group.Curves.Add(rotationCurves[c]);
                }
            }
        }

        private static bool TryGetPath(Transform target, Transform root, out string path)
        {
            if (!target.IsChildOf(root))
            {
                Debug.LogWarning(LogPrefix + $"'{target.name}' は Animator（{root.name}）の階層に無いため AnimationClip に含めません。", target);
                path = null;
                return false;
            }

            path = AnimationUtility.CalculateTransformPath(target, root);
            return true;
        }

        private sealed class CurveGroup
        {
            public readonly List<EditorCurveBinding> Bindings = new List<EditorCurveBinding>();
            public readonly List<AnimationCurve> Curves = new List<AnimationCurve>();
        }
    }
}
