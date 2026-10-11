using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>1 対象（Receiver）分の変換結果。</summary>
    internal readonly struct AnimationTrackConversionResult
    {
        public AnimationTrackConversionResult(
            FacialTimelineReceiver receiver,
            AnimationTrack track,
            AnimationClip clip,
            string clipPath,
            IReadOnlyList<TrackAsset> mutedTracks)
        {
            Receiver = receiver;
            Track = track;
            Clip = clip;
            ClipPath = clipPath;
            MutedTracks = mutedTracks;
        }

        public FacialTimelineReceiver Receiver { get; }

        /// <summary>作った AnimationTrack（Animator にバインド済み）。</summary>
        public AnimationTrack Track { get; }

        /// <summary>保存した AnimationClip。</summary>
        public AnimationClip Clip { get; }

        /// <summary>AnimationClip の保存先（<c>&lt;Timeline 名&gt;_&lt;対象名&gt;.anim</c>。同名があれば番号付き）。</summary>
        public string ClipPath { get; }

        /// <summary>ミュートした FacialControl の独自 Track。</summary>
        public IReadOnlyList<TrackAsset> MutedTracks { get; }
    }

    /// <summary>
    /// FacialControl の独自 Track を、同じ対象（Receiver）ごとに合成した最終出力の AnimationTrack へ変換する（HID-190）。
    /// </summary>
    /// <remarks>
    /// <para>独自 Track はレイヤーの priority・weight・overlay の suppress 等で合成されて最終値になるため、トラックごとではなく
    /// 対象ごとに Edit プレビューと同じ合成（<see cref="TimelinePreviewCompositor"/>）でまとめて評価し、BlendShape と目ボーンの
    /// カーブを持つ AnimationClip を 1 つ作る。シーンの renderer / ボーンは書き換えず、合成出力を直接読む。</para>
    /// <para>キーは Timeline のフレームごとに評価した値から、直線で結んだときのずれが閾値以下の中間キーを消して作る
    /// （<see cref="KeyframeReducer"/>）。Clip 境界・状態イベント・階段カーブのキーの時刻では直前の値も評価し、値が跳ぶ箇所は
    /// 段差（右接線 Constant）にする。</para>
    /// <para>変換元の独自 Track はミュートして残す。Track の作成・ミュート・Director の binding は 1 つの Undo にまとめる
    /// （保存した .anim ファイルは Undo では消えない）。</para>
    /// </remarks>
    internal static class FacialAnimationTrackConverter
    {
        /// <summary>BlendShape の間引き閾値（AnimationClip 上の 0..100 スケール。ドメイン値で 0.001）。</summary>
        public const float BlendShapeTolerance = 0.1f;

        /// <summary>目ボーン回転の間引き閾値（度）。</summary>
        public const float GazeRotationToleranceDegrees = 0.1f;

        /// <summary>段差を調べる時刻で、直前の値を評価する時間幅（秒）。</summary>
        public const double StepProbeSeconds = 5e-4d;

        /// <summary>Timeline に有効なフレームレートが無いときに使う値。</summary>
        public const double FallbackFrameRate = 60d;

        public const string UndoName = "Convert FacialControl Tracks To AnimationTrack";

        private const string LogPrefix = "[FacialAnimationTrackConverter] ";
        private const string BlendShapePropertyPrefix = "blendShape.";
        private static readonly string[] RotationProperties =
        {
            "m_LocalRotation.x",
            "m_LocalRotation.y",
            "m_LocalRotation.z",
            "m_LocalRotation.w",
        };

        public static bool IsFacialTrack(TrackAsset track)
        {
            return track is FacialExpressionTrack || track is FacialValueTrack || track is FacialLayerWeightTrack;
        }

        /// <summary>
        /// <paramref name="selection"/> のうち独自 Track が紐づく対象ごとに 1 本の AnimationTrack を作る。
        /// 独自 Track が無い・対象を解決できない選択は空の結果を返す（理由はログに出す）。
        /// </summary>
        public static List<AnimationTrackConversionResult> Convert(PlayableDirector director, IEnumerable<TrackAsset> selection)
        {
            var results = new List<AnimationTrackConversionResult>();
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null || selection == null)
            {
                Debug.LogWarning(LogPrefix + "PlayableDirector に TimelineAsset がセットされていないため変換できません。");
                return results;
            }

            List<FacialTimelineReceiver> receivers = CollectSelectedReceivers(director, selection);
            if (receivers.Count == 0)
            {
                return results;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();
            for (int i = 0; i < receivers.Count; i++)
            {
                if (TryConvertReceiver(director, timeline, receivers[i], out AnimationTrackConversionResult result))
                {
                    results.Add(result);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            return results;
        }

        /// <summary>トラックが紐づく Receiver（binding。未設定なら、この Director を使う Receiver が 1 つだけのときその Receiver）。</summary>
        internal static FacialTimelineReceiver ResolveReceiver(PlayableDirector director, TrackAsset track)
        {
            FacialTimelineReceiver bound = FacialTimelineEditorPreview.ResolveBoundReceiver(director, track);
            if (bound != null)
            {
                return bound;
            }

            FacialTimelineReceiver found = null;
            FacialTimelineReceiver[] all = UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                FacialTimelineReceiver candidate = all[i];
                if (TimelineTrackBindingResolver.ResolveDirector(candidate, candidate.DirectorOverride, out _) != director)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = candidate;
            }

            return found;
        }

        private static List<FacialTimelineReceiver> CollectSelectedReceivers(PlayableDirector director, IEnumerable<TrackAsset> selection)
        {
            var receivers = new List<FacialTimelineReceiver>();
            foreach (TrackAsset track in selection)
            {
                if (!IsFacialTrack(track))
                {
                    continue;
                }

                FacialTimelineReceiver receiver = ResolveReceiver(director, track);
                if (receiver == null)
                {
                    Debug.LogWarning(LogPrefix + $"トラック '{track.name}' の対象（FacialTimelineReceiver）を解決できないため変換しません。トラックの binding を設定してください。");
                    continue;
                }

                if (!receivers.Contains(receiver))
                {
                    receivers.Add(receiver);
                }
            }

            return receivers;
        }

        private static bool TryConvertReceiver(
            PlayableDirector director,
            TimelineAsset timeline,
            FacialTimelineReceiver receiver,
            out AnimationTrackConversionResult result)
        {
            result = default;
            FacialController controller = FacialTimelineEditorPreview.ResolveController(receiver);
            FacialCharacterProfileSO profileAsset = controller != null ? controller.CharacterSO : null;
            if (profileAsset == null)
            {
                Debug.LogError(LogPrefix + $"'{receiver.name}' の FacialController または Character SO（Profile SO）が見つからないため変換しません。", receiver);
                return false;
            }

            FacialProfile profile = TimelineProfileSource.Resolve(profileAsset);
            using var compositor = new TimelinePreviewCompositor(controller, profileAsset, profile, receiver.BakeAsset, timeline);
            if (!compositor.CanRender)
            {
                Debug.LogError(LogPrefix + $"'{timeline.name}' の Bake を解決できないため変換しません（{compositor.BakeLocate.Status}）。Timeline を保存して自動ベイクを待ってから変換してください。", receiver);
                return false;
            }

            if (compositor.ProfileCheck == TimelineDiagnosticCode.ProfileMismatch)
            {
                Debug.LogWarning(LogPrefix + "Bake が現在の Profile と異なるスナップショットから作られています。Bake の値（Edit プレビューと同じ値）で変換します。", receiver);
            }

            Animator animator = controller.GetComponentInParent<Animator>(true);
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(controller.gameObject);
            }

            AnimationClip clip = BuildClip(timeline, controller, profileAsset, compositor, animator);
            clip.name = timeline.name + "_" + controller.name;
            string clipPath = ResolveClipPath(timeline, controller.name);
            AssetDatabase.CreateAsset(clip, clipPath);

            Undo.RegisterCompleteObjectUndo(timeline, UndoName);
            var track = timeline.CreateTrack<AnimationTrack>(null, controller.name + " (Animation)");
            Undo.RegisterCreatedObjectUndo(track, UndoName);
            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = 0d;
            timelineClip.displayName = clip.name;
            if (timelineClip.asset != null)
            {
                Undo.RegisterCreatedObjectUndo(timelineClip.asset, UndoName);
            }

            Undo.RecordObject(director, UndoName);
            director.SetGenericBinding(track, animator);

            List<TrackAsset> muted = MuteSourceTracks(director, timeline, receiver);
            EditorUtility.SetDirty(timeline);
            result = new AnimationTrackConversionResult(receiver, track, clip, clipPath, muted);
            return true;
        }

        private static List<TrackAsset> MuteSourceTracks(PlayableDirector director, TimelineAsset timeline, FacialTimelineReceiver receiver)
        {
            var muted = new List<TrackAsset>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!IsFacialTrack(track) || track.muted || ResolveReceiver(director, track) != receiver)
                {
                    continue;
                }

                Undo.RecordObject(track, UndoName);
                track.muted = true;
                EditorUtility.SetDirty(track);
                muted.Add(track);
            }

            return muted;
        }

        private static string ResolveClipPath(TimelineAsset timeline, string targetName)
        {
            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            string folder = string.IsNullOrEmpty(timelinePath) ? "Assets" : Path.GetDirectoryName(timelinePath).Replace('\\', '/');
            string fileName = SanitizeFileName(timeline.name + "_" + targetName) + ".anim";
            return AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
        }

        internal static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                // Path.GetInvalidFileNameChars は OS で異なる（Linux は '/' と NUL だけ）ので、Windows で使えない文字も置き換える。
                if (Array.IndexOf(invalid, chars[i]) >= 0 || "<>:\"/\\|?*".IndexOf(chars[i]) >= 0 || chars[i] < 32)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static AnimationClip BuildClip(
            TimelineAsset timeline,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            TimelinePreviewCompositor compositor,
            Animator animator)
        {
            double frameRate = timeline.editorSettings.frameRate > 0d ? timeline.editorSettings.frameRate : FallbackFrameRate;
            SampleTimeline samples = SampleTimeline.Create(timeline.duration, frameRate, CollectStepCandidateTimes(timeline, compositor.BakeLocate.Bake));
            var clip = new AnimationClip { frameRate = (float)frameRate };

            WriteBlendShapeCurves(clip, controller, compositor, animator, samples);
            WriteGazeCurves(clip, controller, profileAsset, compositor, animator, samples);
            return clip;
        }

        private static void WriteBlendShapeCurves(
            AnimationClip clip,
            FacialController controller,
            TimelinePreviewCompositor compositor,
            Animator animator,
            SampleTimeline samples)
        {
            IReadOnlyList<string> names = compositor.BlendShapeNames;
            int shapeCount = names.Count;
            int sampleCount = samples.Times.Count;
            var values = new float[shapeCount][];
            for (int s = 0; s < shapeCount; s++)
            {
                values[s] = new float[sampleCount];
            }

            for (int i = 0; i < sampleCount; i++)
            {
                if (!compositor.TryEvaluateOutput(samples.Times[i], out ReadOnlySpan<float> output))
                {
                    continue;
                }

                int count = Math.Min(shapeCount, output.Length);
                for (int s = 0; s < count; s++)
                {
                    // renderer へ書く値（SkinnedMeshRendererBlendShapeWriter と同じ 0..100 スケール）。
                    values[s][i] = output[s] * 100f;
                }
            }

            var curves = new AnimationCurve[shapeCount];
            SkinnedMeshRenderer[] renderers = TimelinePreviewCompositor.ResolveRenderers(controller);
            for (int r = 0; r < renderers.Length; r++)
            {
                SkinnedMeshRenderer renderer = renderers[r];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                if (!TryGetPath(renderer.transform, animator, out string path))
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    string shapeName = mesh.GetBlendShapeName(shapeIndex);
                    int outputIndex = IndexOf(names, shapeName);
                    if (outputIndex < 0)
                    {
                        continue;
                    }

                    curves[outputIndex] ??= BuildScalarCurve(samples, values[outputIndex], BlendShapeTolerance);
                    AnimationUtility.SetEditorCurve(
                        clip,
                        EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), BlendShapePropertyPrefix + shapeName),
                        curves[outputIndex]);
                }
            }
        }

        private static void WriteGazeCurves(
            AnimationClip clip,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            TimelinePreviewCompositor compositor,
            Animator animator,
            SampleTimeline samples)
        {
            IReadOnlyList<GazeChannel> gazeChannels = profileAsset.GazeChannels;
            if (gazeChannels == null || gazeChannels.Count == 0)
            {
                return;
            }

            FacialTimelineEditorPreview.GetGazeEyeResolver(controller, out BoneTransformResolver resolver, out GazeEyeBoneFallback fallback);
            var targets = new List<FacialTimelinePreviewEyeTarget>();
            compositor.ResolveGazeTargets(gazeChannels, resolver, fallback, targets);
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

            Transform leftEye = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform rightEye = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
            int sampleCount = samples.Times.Count;
            for (int b = 0; b < bones.Count; b++)
            {
                Transform bone = bones[b];
                if (bone == null || !TryGetPath(bone, animator, out string path))
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
                    Quaternion rotation = compositor.ComputeGazeRotation(samples.Times[i], target, gazeChannels);
                    components[(i * 4) + 0] = rotation.x;
                    components[(i * 4) + 1] = rotation.y;
                    components[(i * 4) + 2] = rotation.z;
                    components[(i * 4) + 3] = rotation.w;
                }

                QuaternionChannelMetric.AlignHemispheres(components);
                AnimationCurve[] curves = BuildQuaternionCurves(samples, components, GazeRotationToleranceDegrees);
                for (int c = 0; c < 4; c++)
                {
                    AnimationUtility.SetEditorCurve(
                        clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform), RotationProperties[c]),
                        curves[c]);
                }
            }
        }

        internal static AnimationCurve BuildScalarCurve(SampleTimeline samples, float[] values, float tolerance)
        {
            var metric = new ScalarChannelMetric(samples.Times, values);
            bool[] steps = samples.DetectSteps(metric, tolerance);
            List<ReducedKey> keys = KeyframeReducer.Reduce(steps, values.Length, metric, tolerance);
            return CreateCurve(samples.Times, keys, i => values[i]);
        }

        internal static AnimationCurve[] BuildQuaternionCurves(SampleTimeline samples, float[] components, float toleranceDegrees)
        {
            var metric = new QuaternionChannelMetric(samples.Times, components);
            bool[] steps = samples.DetectSteps(metric, toleranceDegrees);
            List<ReducedKey> keys = KeyframeReducer.Reduce(steps, components.Length / 4, metric, toleranceDegrees);
            var curves = new AnimationCurve[4];
            for (int c = 0; c < 4; c++)
            {
                int component = c;
                curves[c] = CreateCurve(samples.Times, keys, i => components[(i * 4) + component]);
            }

            return curves;
        }

        /// <summary>
        /// 残すキーから AnimationCurve を作る。接線は Linear、<see cref="ReducedKey.HoldUntilNext"/> のキーと次のキーの間は Constant。
        /// </summary>
        internal static AnimationCurve CreateCurve(IReadOnlyList<double> times, List<ReducedKey> keys, Func<int, float> valueAt)
        {
            var frames = new Keyframe[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                frames[k] = new Keyframe((float)times[keys[k].SampleIndex], valueAt(keys[k].SampleIndex));
            }

            for (int k = 0; k < frames.Length - 1; k++)
            {
                float slope = keys[k].HoldUntilNext
                    ? float.PositiveInfinity
                    : (frames[k + 1].value - frames[k].value) / Math.Max(frames[k + 1].time - frames[k].time, float.Epsilon);
                frames[k].outTangent = slope;
                frames[k + 1].inTangent = slope;
            }

            if (frames.Length > 0)
            {
                frames[0].inTangent = frames.Length > 1 && !keys[0].HoldUntilNext ? frames[0].outTangent : 0f;
                Keyframe last = frames[frames.Length - 1];
                last.outTangent = frames.Length > 1 && !keys[keys.Count - 2].HoldUntilNext ? last.inTangent : 0f;
                frames[frames.Length - 1] = last;
            }

            var curve = new AnimationCurve(frames);
            for (int k = 0; k < frames.Length; k++)
            {
                bool holdBefore = k > 0 && keys[k - 1].HoldUntilNext;
                AnimationUtility.SetKeyBroken(curve, k, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, k, holdBefore ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, k, keys[k].HoldUntilNext ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear);
            }

            return curve;
        }

        /// <summary>
        /// 値が跳び得る時刻（独自 Track の Clip 境界・Bake の状態イベント・階段カーブのキー）を集める。
        /// </summary>
        private static List<double> CollectStepCandidateTimes(TimelineAsset timeline, FacialTimelineBakeAsset bake)
        {
            var times = new List<double>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!IsFacialTrack(track))
                {
                    continue;
                }

                foreach (TimelineClip clip in track.GetClips())
                {
                    times.Add(clip.start);
                    times.Add(clip.end);
                    switch (clip.asset)
                    {
                        case FacialValueClip valueClip:
                            AddStairKeyTimes(times, clip.start, valueClip.Axes);
                            AddStairKeyTimes(times, clip.start, valueClip.Contributes);
                            AddStairKeyTimes(times, clip.start, valueClip.Validity);
                            break;
                        case FacialLayerWeightClip weightClip:
                            AddStairKeyTimes(times, clip.start, weightClip.Weight);
                            break;
                    }
                }
            }

            TimelineStateEvent[] events = bake != null ? bake.StateEvents : null;
            if (events != null)
            {
                for (int i = 0; i < events.Length; i++)
                {
                    times.Add(events[i].TimeSeconds);
                }
            }

            return times;
        }

        private static void AddStairKeyTimes(List<double> times, double clipStart, AnimationCurve[] curves)
        {
            if (curves == null)
            {
                return;
            }

            for (int i = 0; i < curves.Length; i++)
            {
                AddStairKeyTimes(times, clipStart, curves[i]);
            }
        }

        private static void AddStairKeyTimes(List<double> times, double clipStart, AnimationCurve curve)
        {
            if (curve == null)
            {
                return;
            }

            Keyframe[] keys = curve.keys;
            for (int k = 0; k < keys.Length; k++)
            {
                if (float.IsInfinity(keys[k].inTangent) || (k > 0 && float.IsInfinity(keys[k - 1].outTangent)))
                {
                    times.Add(clipStart + keys[k].time);
                }
            }
        }

        private static bool TryGetPath(Transform target, Animator animator, out string path)
        {
            if (target != animator.transform && !target.IsChildOf(animator.transform))
            {
                Debug.LogWarning(LogPrefix + $"'{target.name}' は Animator（{animator.name}）の子ではないため AnimationClip に含めません。", target);
                path = null;
                return false;
            }

            path = AnimationUtility.CalculateTransformPath(target, animator.transform);
            return true;
        }

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 変換で評価する時刻列。Timeline のフレーム（0 〜 duration）に、値が跳び得る時刻 t ごとの (t - <see cref="FacialAnimationTrackConverter.StepProbeSeconds"/>, t) を加える。
    /// </summary>
    internal sealed class SampleTimeline
    {
        private const double MergeTolerance = 1e-6d;

        private readonly List<double> _times;

        // _probeIndices[k] は「直前の値」を評価したサンプル（値が跳び得る時刻の直前）。跳ぶかどうかはチャネルごとに判定する。
        private readonly List<int> _probeIndices;

        private SampleTimeline(List<double> times, List<int> probeIndices)
        {
            _times = times;
            _probeIndices = probeIndices;
        }

        public IReadOnlyList<double> Times => _times;

        public static SampleTimeline Create(double duration, double frameRate, IReadOnlyList<double> stepCandidates)
        {
            duration = Math.Max(0d, duration);
            var all = new List<double>();
            int frameCount = (int)Math.Ceiling((duration * frameRate) - MergeTolerance);
            for (int f = 0; f <= frameCount; f++)
            {
                all.Add(Math.Min(f / frameRate, duration));
            }

            var probes = new List<double>();
            if (stepCandidates != null)
            {
                for (int i = 0; i < stepCandidates.Count; i++)
                {
                    double t = stepCandidates[i];
                    if (t - FacialAnimationTrackConverter.StepProbeSeconds <= 0d || t > duration + MergeTolerance)
                    {
                        continue;
                    }

                    t = Math.Min(t, duration);
                    all.Add(t);
                    all.Add(t - FacialAnimationTrackConverter.StepProbeSeconds);
                    probes.Add(t - FacialAnimationTrackConverter.StepProbeSeconds);
                }
            }

            all.Sort();
            var times = new List<double>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (times.Count == 0 || all[i] - times[times.Count - 1] > MergeTolerance)
                {
                    times.Add(all[i]);
                }
            }

            probes.Sort();
            var probeIndices = new List<int>(probes.Count);
            int cursor = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                while (cursor < times.Count && times[cursor] < probes[i] - MergeTolerance)
                {
                    cursor++;
                }

                if (cursor + 1 < times.Count && (probeIndices.Count == 0 || probeIndices[probeIndices.Count - 1] != cursor))
                {
                    probeIndices.Add(cursor);
                }
            }

            return new SampleTimeline(times, probeIndices);
        }

        /// <summary>
        /// 「直前の値」のサンプル p と次のサンプル p + 1 の間で、保持のずれが <paramref name="tolerance"/> を超えるものを段差とする
        /// （戻り値の [p + 1] が true）。
        /// </summary>
        public bool[] DetectSteps(IKeyframeChannelMetric metric, float tolerance)
        {
            var steps = new bool[_times.Count];
            for (int i = 0; i < _probeIndices.Count; i++)
            {
                int p = _probeIndices[i];
                if (metric.DeviationFromHold(p, p + 1) > tolerance)
                {
                    steps[p + 1] = true;
                }
            }

            return steps;
        }
    }
}
