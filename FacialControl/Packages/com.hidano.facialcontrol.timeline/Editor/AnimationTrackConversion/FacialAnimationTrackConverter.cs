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

            var resolver = new ReceiverResolver(director);
            List<FacialTimelineReceiver> receivers = CollectSelectedReceivers(resolver, selection);
            if (receivers.Count == 0)
            {
                return results;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();
            for (int i = 0; i < receivers.Count; i++)
            {
                if (TryConvertReceiver(director, timeline, resolver, receivers[i], out AnimationTrackConversionResult result))
                {
                    results.Add(result);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            return results;
        }

        private static List<FacialTimelineReceiver> CollectSelectedReceivers(ReceiverResolver resolver, IEnumerable<TrackAsset> selection)
        {
            var receivers = new List<FacialTimelineReceiver>();
            foreach (TrackAsset track in selection)
            {
                if (!IsFacialTrack(track))
                {
                    continue;
                }

                FacialTimelineReceiver receiver = resolver.Resolve(track);
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
            ReceiverResolver resolver,
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

            // 合成（Bake・導出）はトラックのミュートを見ないため、ミュート中の独自 Track があると再生されない内容まで焼き込んでしまう。
            if (TryFindMutedSourceTrack(timeline, resolver, receiver, out TrackAsset mutedTrack))
            {
                Debug.LogError(LogPrefix + $"ミュート中の独自 Track '{mutedTrack.name}' があるため変換しません（変換済みの Timeline か、再生しないトラックが残っています）。ミュートを解除するか削除してから変換してください。", receiver);
                return false;
            }

            FacialProfile profile = TimelineProfileSource.Resolve(profileAsset);
            using var compositor = new TimelinePreviewCompositor(controller, profileAsset, profile, receiver.BakeAsset, timeline);
            if (!compositor.CanRender)
            {
                Debug.LogError(LogPrefix + $"'{timeline.name}' の Bake を解決できないため変換しません（{compositor.BakeLocate.Status}）。Timeline を保存して自動ベイクを待ってから変換してください。", receiver);
                return false;
            }

            if (!IsBakeUpToDate(timeline, profileAsset, compositor.BakeLocate.Bake))
            {
                return false;
            }

            // Animator は FacialController の親階層（自身を含む）→ 子階層の順に探す。無ければ変換が成功したときだけ追加する。
            Animator animator = controller.GetComponentInParent<Animator>(true);
            if (animator == null)
            {
                animator = controller.GetComponentInChildren<Animator>(true);
            }

            Transform root = animator != null ? animator.transform : controller.transform;
            AnimationClip clip = BuildClip(timeline, controller, profileAsset, compositor, root, animator != null && animator.isHuman ? animator : null);
            if (AnimationUtility.GetCurveBindings(clip).Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(clip);
                Debug.LogError(LogPrefix + $"'{controller.name}' の BlendShape・目ボーンが Animator の階層に無いため、変換するカーブがありません。変換しません。", receiver);
                return false;
            }

            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(controller.gameObject);
            }

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

            List<TrackAsset> muted = MuteSourceTracks(timeline, resolver, receiver);
            EditorUtility.SetDirty(timeline);
            result = new AnimationTrackConversionResult(receiver, track, clip, clipPath, muted);
            return true;
        }

        /// <summary>
        /// Bake が Timeline と Profile の現在の内容から作られているかを返す。古ければ再ベイクを予約して false（変換しない）。
        /// Edit プレビューは古い Bake でも描きつつ再ベイクを待つが、変換は値を .anim に固定するので古い Bake では行わない。
        /// </summary>
        private static bool IsBakeUpToDate(TimelineAsset timeline, FacialCharacterProfileSO profileAsset, FacialTimelineBakeAsset bake)
        {
            BakeStaleReason stale = TimelineBakeService.IsStale(timeline, profileAsset, bake);
            if (stale == BakeStaleReason.None)
            {
                return true;
            }

            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            watcher?.MarkDirty(timeline, stale == BakeStaleReason.ProfileChanged ? TimelineDirtyReason.ProfileMismatch : TimelineDirtyReason.ClipEdit);
            Debug.LogError(LogPrefix + $"'{timeline.name}' の Bake が現在の {(stale == BakeStaleReason.ProfileChanged ? "Profile" : "Timeline")} と一致しないため変換しません。自動再ベイクの完了後にもう一度変換してください。", timeline);
            return false;
        }

        private static bool TryFindMutedSourceTrack(
            TimelineAsset timeline,
            ReceiverResolver resolver,
            FacialTimelineReceiver receiver,
            out TrackAsset mutedTrack)
        {
            // Lane の子トラックは出力トラックに含まれないため、Group・子トラックを含む全階層を親のミュートごと調べる。
            var tracks = new List<(TrackAsset track, bool mutedInHierarchy)>();
            CollectAllTracks(timeline, tracks);
            for (int i = 0; i < tracks.Count; i++)
            {
                (TrackAsset track, bool muted) = tracks[i];
                if (muted && IsFacialTrack(track) && resolver.Resolve(track) == receiver)
                {
                    mutedTrack = track;
                    return true;
                }
            }

            mutedTrack = null;
            return false;
        }

        /// <summary>Group・子トラックを含む全トラックを、親階層のミュートを畳み込んだ状態とともに深さ優先で集める。</summary>
        internal static void CollectAllTracks(TimelineAsset timeline, List<(TrackAsset track, bool mutedInHierarchy)> results)
        {
            foreach (TrackAsset root in timeline.GetRootTracks())
            {
                CollectTrack(root, false, results);
            }
        }

        private static void CollectTrack(TrackAsset track, bool parentMuted, List<(TrackAsset track, bool mutedInHierarchy)> results)
        {
            bool muted = parentMuted || track.muted;
            results.Add((track, muted));
            foreach (TrackAsset child in track.GetChildTracks())
            {
                CollectTrack(child, muted, results);
            }
        }

        private static List<TrackAsset> MuteSourceTracks(TimelineAsset timeline, ReceiverResolver resolver, FacialTimelineReceiver receiver)
        {
            var muted = new List<TrackAsset>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!IsFacialTrack(track) || track.muted || resolver.Resolve(track) != receiver)
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
            Transform root,
            Animator humanoid)
        {
            double frameRate = timeline.editorSettings.frameRate > 0d ? timeline.editorSettings.frameRate : FallbackFrameRate;
            SampleTimeline samples = SampleTimeline.Create(timeline.duration, frameRate, CollectStepCandidateTimes(timeline, compositor.BakeLocate.Bake));
            var clip = new AnimationClip { frameRate = (float)frameRate };

            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            CollectBlendShapeCurves(controller, compositor, root, samples, bindings, curves);
            CollectGazeCurves(controller, profileAsset, compositor, root, humanoid, samples, bindings, curves);
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            return clip;
        }

        private static void CollectBlendShapeCurves(
            FacialController controller,
            TimelinePreviewCompositor compositor,
            Transform root,
            SampleTimeline samples,
            List<EditorCurveBinding> bindings,
            List<AnimationCurve> curves)
        {
            IReadOnlyList<string> names = compositor.BlendShapeNames;
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

                    built[outputIndex] ??= BuildScalarCurve(samples, values[outputIndex], BlendShapeTolerance);
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), BlendShapePropertyPrefix + shapeName));
                    curves.Add(built[outputIndex]);
                }
            }
        }

        private static void CollectGazeCurves(
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            TimelinePreviewCompositor compositor,
            Transform root,
            Animator humanoid,
            SampleTimeline samples,
            List<EditorCurveBinding> bindings,
            List<AnimationCurve> curves)
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
                    Quaternion rotation = compositor.ComputeGazeRotation(samples.Times[i], target, gazeChannels);
                    components[(i * 4) + 0] = rotation.x;
                    components[(i * 4) + 1] = rotation.y;
                    components[(i * 4) + 2] = rotation.z;
                    components[(i * 4) + 3] = rotation.w;
                }

                QuaternionChannelMetric.AlignHemispheres(components);
                AnimationCurve[] rotationCurves = BuildQuaternionCurves(samples, components, GazeRotationToleranceDegrees);
                for (int c = 0; c < 4; c++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), RotationProperties[c]));
                    curves.Add(rotationCurves[c]);
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
        /// 残すキーから AnimationCurve を作る。接線は Linear、<see cref="ReducedKey.HoldUntilNext"/> のキーと次のキーの間は Constant
        /// （接線の値は tangent mode から Unity が計算する）。
        /// </summary>
        internal static AnimationCurve CreateCurve(IReadOnlyList<double> times, List<ReducedKey> keys, Func<int, float> valueAt)
        {
            var frames = new Keyframe[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                frames[k] = new Keyframe((float)times[keys[k].SampleIndex], valueAt(keys[k].SampleIndex));
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
            var tracks = new List<(TrackAsset track, bool mutedInHierarchy)>();
            CollectAllTracks(timeline, tracks);
            foreach ((TrackAsset track, bool _) in tracks)
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
    }

    /// <summary>
    /// トラックが紐づく Receiver を解決する（binding。未設定なら、この Director を使う Receiver がシーンに 1 つだけのときその Receiver）。
    /// 未設定トラック用のシーン走査は 1 回の変換につき 1 回だけ行う。
    /// </summary>
    internal sealed class ReceiverResolver
    {
        private readonly PlayableDirector _director;
        private bool _fallbackResolved;
        private FacialTimelineReceiver _fallback;

        public ReceiverResolver(PlayableDirector director)
        {
            _director = director;
        }

        public FacialTimelineReceiver Resolve(TrackAsset track)
        {
            FacialTimelineReceiver bound = FacialTimelineEditorPreview.ResolveBoundReceiver(_director, track);
            if (bound != null)
            {
                return bound;
            }

            if (!_fallbackResolved)
            {
                _fallbackResolved = true;
                _fallback = FindSingleReceiverOfDirector();
            }

            return _fallback;
        }

        private FacialTimelineReceiver FindSingleReceiverOfDirector()
        {
            FacialTimelineReceiver found = null;
            FacialTimelineReceiver[] all = UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                FacialTimelineReceiver candidate = all[i];
                if (TimelineTrackBindingResolver.ResolveDirector(candidate, candidate.DirectorOverride, out _) != _director)
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

            var candidates = new List<double>();
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
                    candidates.Add(t);
                    probes.Add(t - FacialAnimationTrackConverter.StepProbeSeconds);
                }
            }

            // 「直前の値」のすぐ次のサンプルが段差候補の時刻になるよう、(t - probe, t) に入るフレームは評価しない。
            candidates.Sort();
            all.RemoveAll(frame => IsInsideProbeWindow(candidates, frame));
            all.AddRange(candidates);
            all.AddRange(probes);
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

        private static bool IsInsideProbeWindow(List<double> sortedCandidates, double frame)
        {
            int index = sortedCandidates.BinarySearch(frame);
            if (index < 0)
            {
                index = ~index;
            }

            // frame 以上で最小の候補 t について、t - probe < frame < t なら窓の中。
            while (index < sortedCandidates.Count && sortedCandidates[index] - frame <= MergeTolerance)
            {
                index++;
            }

            return index < sortedCandidates.Count
                && sortedCandidates[index] - FacialAnimationTrackConverter.StepProbeSeconds < frame - MergeTolerance;
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
