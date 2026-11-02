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
    /// <summary>変換で作った AnimationClip 1 本と、それを置いた AnimationTrack（HID-410）。</summary>
    internal readonly struct AnimationTrackConversionClip
    {
        public AnimationTrackConversionClip(FacialAnimationClipPart part, AnimationTrack track, string clipPath)
        {
            Part = part;
            Track = track;
            ClipPath = clipPath;
        }

        /// <summary>ビルダーが作った Clip（保存済み）と、その種類・名前・カーブ数・キー数。</summary>
        public FacialAnimationClipPart Part { get; }

        /// <summary>作った AnimationTrack（Animator にバインド済み）。</summary>
        public AnimationTrack Track { get; }

        /// <summary>AnimationClip の保存先（<c>&lt;Timeline 名&gt;_&lt;対象名&gt;_&lt;ClipName&gt;.anim</c>。同名があれば番号付き）。</summary>
        public string ClipPath { get; }
    }

    /// <summary>1 対象（Receiver）分の変換結果。</summary>
    internal readonly struct AnimationTrackConversionResult
    {
        public AnimationTrackConversionResult(
            FacialTimelineReceiver receiver,
            IReadOnlyList<AnimationTrackConversionClip> clips,
            IReadOnlyList<TrackAsset> mutedTracks)
        {
            Receiver = receiver;
            Clips = clips;
            MutedTracks = mutedTracks;
        }

        public FacialTimelineReceiver Receiver { get; }

        /// <summary>
        /// 作った AnimationClip と AnimationTrack（レイヤー別 → Base → Gaze の順。カーブの無いものは作らない）。
        /// 各 Clip は互いに別のプロパティだけを持つ。
        /// </summary>
        public IReadOnlyList<AnimationTrackConversionClip> Clips { get; }

        /// <summary>ミュートした FacialControl の独自 Track。</summary>
        public IReadOnlyList<TrackAsset> MutedTracks { get; }
    }

    /// <summary>
    /// FacialControl の独自 Track を、同じ対象（Receiver）ごとに合成した最終出力の AnimationTrack へ変換する（HID-190）。
    /// </summary>
    /// <remarks>
    /// <para>独自 Track はレイヤーの priority・weight・overlay の suppress 等で合成されて最終値になるため、トラックごとではなく
    /// 対象ごとに Edit プレビューと同じ合成（<see cref="TimelinePreviewCompositor"/>）でまとめて評価する。合成後の最終値を
    /// BlendShape ごとの担当レイヤーで振り分け、レイヤー別の AnimationClip と目ボーン回転の AnimationClip を作り、
    /// 1 本ずつ AnimationTrack に置く（HID-410。<see cref="FacialAnimationClipBuilder"/>）。Clip 同士は同じプロパティを触らないので、
    /// 全トラックを再生した結果は合成結果と一致する。Clip の値は合成後の数値なので、編集してもレイヤー合成は再計算されない。
    /// シーンの renderer / ボーンは書き換えず、合成出力を直接読む。</para>
    /// <para>キーは Timeline のフレームごとに評価した値から、直線で結んだときのずれが閾値以下の中間キーを消して作る
    /// （<see cref="KeyframeReducer"/>）。Clip 境界・状態イベント・階段カーブのキーの時刻では直前の値も評価し、値が跳ぶ箇所は
    /// 段差（右接線 Constant）にする。</para>
    /// <para>変換元の独自 Track はミュートして残す。Track の作成・ミュート・Director の binding は 1 つの Undo にまとめる
    /// （保存した .anim ファイルは Undo では消えない）。</para>
    /// </remarks>
    internal static class FacialAnimationTrackConverter
    {

        /// <summary>Timeline に有効なフレームレートが無いときに使う値。</summary>
        public const double FallbackFrameRate = 60d;

        public const string UndoName = "Convert FacialControl Tracks To AnimationTrack";

        private const string LogPrefix = "[FacialAnimationTrackConverter] ";

        public static bool IsFacialTrack(TrackAsset track)
        {
            return track is FacialExpressionTrack || track is FacialValueTrack || track is FacialLayerWeightTrack;
        }

        /// <summary>
        /// <paramref name="selection"/> のうち独自 Track が紐づく対象ごとに、レイヤー別・Base・Gaze の AnimationClip を 1 本ずつ置いた
        /// AnimationTrack 群を作る（対象 1 つにつき結果 1 つ。<see cref="AnimationTrackConversionResult.Clips"/>）。
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

            // 合成（Bake・導出・Edit プレビュー）は TimelineAsset 全体を 1 つの対象として評価するため、複数の対象の独自 Track が
            // 同じ Timeline にあると、各対象の AnimationClip に他の対象のトラックが混ざる。分けられないので変換しない。
            // 対象を解決できない独自 Track も、Play では適用されないのに合成には入るため同じく変換しない。
            if (!TryFindSingleTarget(timeline, resolver, out TrackAsset unresolved))
            {
                Debug.LogError(
                    LogPrefix + (unresolved != null
                        ? $"'{timeline.name}' の独自 Track '{unresolved.name}' の対象（FacialTimelineReceiver）を解決できないため変換しません。トラックの binding を設定してください。"
                        : $"'{timeline.name}' には複数の対象（FacialTimelineReceiver）の独自 Track があるため変換しません。合成は Timeline 全体を 1 つの対象として評価するので、対象ごとに Timeline を分けてから変換してください。"),
                    timeline);
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

        /// <summary>
        /// Timeline の独自 Track がすべて同じ 1 つの対象に解決できれば true。解決できないトラックがあれば
        /// <paramref name="unresolved"/> にそれを入れて false、複数の対象に分かれていれば null のまま false。
        /// </summary>
        private static bool TryFindSingleTarget(TimelineAsset timeline, ReceiverResolver resolver, out TrackAsset unresolved)
        {
            unresolved = null;
            var tracks = new List<(TrackAsset track, bool mutedInHierarchy)>();
            CollectAllTracks(timeline, tracks);
            FacialTimelineReceiver first = null;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!IsFacialTrack(tracks[i].track))
                {
                    continue;
                }

                FacialTimelineReceiver receiver = resolver.Resolve(tracks[i].track);
                if (receiver == null)
                {
                    unresolved = tracks[i].track;
                    return false;
                }

                if (first == null)
                {
                    first = receiver;
                }
                else if (first != receiver)
                {
                    return false;
                }
            }

            return true;
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
            List<FacialAnimationClipPart> parts = BuildClips(
                timeline, controller, profileAsset, compositor, root, animator != null && animator.isHuman ? animator : null);
            if (parts.Count == 0)
            {
                Debug.LogError(LogPrefix + $"'{controller.name}' の BlendShape・目ボーンが Animator の階層に無いため、変換するカーブがありません。変換しません。", receiver);
                return false;
            }

            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(controller.gameObject);
            }

            Undo.RegisterCompleteObjectUndo(timeline, UndoName);
            Undo.RecordObject(director, UndoName);
            var clips = new List<AnimationTrackConversionClip>(parts.Count);
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                FacialAnimationClipPart part = parts[i];
                AnimationClip clip = part.Clip;
                clip.name = timeline.name + "_" + controller.name + "_" + part.ClipName;
                string clipPath = ResolveClipPath(timeline, controller.name + "_" + part.ClipName);
                AssetDatabase.CreateAsset(clip, clipPath);

                var track = timeline.CreateTrack<AnimationTrack>(null, controller.name + " (" + part.ClipName + ")");
                Undo.RegisterCreatedObjectUndo(track, UndoName);
                TimelineClip timelineClip = track.CreateClip(clip);
                timelineClip.start = 0d;
                timelineClip.displayName = clip.name;
                if (timelineClip.asset != null)
                {
                    Undo.RegisterCreatedObjectUndo(timelineClip.asset, UndoName);
                }

                // Clip 同士は別のプロパティだけを持つので、すべて同じ Animator にバインドしても値は混ざらない。
                director.SetGenericBinding(track, animator);
                clips.Add(new AnimationTrackConversionClip(part, track, clipPath));
                summary.Append(i == 0 ? string.Empty : ", ").Append($"{part.ClipName}: カーブ {part.CurveCount} / キー {part.KeyCount}");
            }

            List<TrackAsset> muted = MuteSourceTracks(timeline, resolver, receiver);
            EditorUtility.SetDirty(timeline);
            Debug.Log(LogPrefix + $"'{controller.name}' を {clips.Count} 本の AnimationClip に変換しました（{summary}）。", receiver);
            result = new AnimationTrackConversionResult(receiver, clips, muted);
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

        private static List<FacialAnimationClipPart> BuildClips(
            TimelineAsset timeline,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            TimelinePreviewCompositor compositor,
            Transform root,
            Animator humanoid)
        {
            double frameRate = timeline.editorSettings.frameRate > 0d ? timeline.editorSettings.frameRate : FallbackFrameRate;
            SampleTimeline samples = SampleTimeline.Create(timeline.duration, frameRate, CollectStepCandidateTimes(timeline, compositor.BakeLocate.Bake));
            return FacialAnimationClipBuilder.Build(compositor, controller, profileAsset, root, humanoid, samples, frameRate);
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
}
