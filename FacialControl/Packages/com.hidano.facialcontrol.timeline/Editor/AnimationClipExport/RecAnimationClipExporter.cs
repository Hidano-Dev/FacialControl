using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.AnimationClipExport
{
    /// <summary>REC から直接書き出した AnimationClip 1 本。</summary>
    internal readonly struct RecAnimationClipExportClip
    {
        public RecAnimationClipExportClip(FacialAnimationClipPart part, string assetPath)
        {
            Part = part;
            AssetPath = assetPath;
        }

        /// <summary>ビルダーが作った Clip（保存済み）と、その種類・名前・カーブ数・キー数。</summary>
        public FacialAnimationClipPart Part { get; }

        /// <summary>保存先（<c>&lt;REC 名&gt;_&lt;対象名&gt;_&lt;ClipName&gt;.anim</c>。同名があれば番号付き）。</summary>
        public string AssetPath { get; }
    }

    /// <summary><see cref="RecAnimationClipExporter.TryExport"/> の結果。</summary>
    internal sealed class RecAnimationClipExportResult
    {
        public RecAnimationClipExportResult(
            IReadOnlyList<RecAnimationClipExportClip> clips,
            string timelineAssetPath,
            IReadOnlyList<string> remapProblems,
            string error)
        {
            Clips = clips ?? Array.Empty<RecAnimationClipExportClip>();
            TimelineAssetPath = timelineAssetPath;
            RemapProblems = remapProblems ?? Array.Empty<string>();
            Error = error;
        }

        public bool Succeeded => Error == null;

        /// <summary>保存した AnimationClip（レイヤー別 → Base → Gaze の順）。</summary>
        public IReadOnlyList<RecAnimationClipExportClip> Clips { get; }

        /// <summary>任意で作った TimelineAsset の保存先（作らなければ null）。</summary>
        public string TimelineAssetPath { get; }

        /// <summary>対象 Profile で再生されない入力源・レイヤー・表情（書き出しは続けた。<see cref="RecSourceRemapResolver.DescribeProblems"/>）。</summary>
        public IReadOnlyList<string> RemapProblems { get; }

        /// <summary>書き出さなかった理由（成功なら null）。</summary>
        public string Error { get; }

        internal static RecAnimationClipExportResult Failed(string error, IReadOnlyList<string> remapProblems = null)
        {
            return new RecAnimationClipExportResult(null, null, remapProblems, error);
        }
    }

    /// <summary>
    /// REC（.fcrec）から、TimelineAsset・Bake・PlayableDirector をユーザーに作らせずに AnimationClip（.anim）を直接書き出す（HID-409）。
    /// </summary>
    /// <remarks>
    /// <para>評価は、REC Export（<see cref="RecToTimelineExporter"/>）と同じ規則で組んだ TimelineAsset と Bake をメモリ上にだけ作り、
    /// AnimationTrack 変換と同じ合成（<see cref="TimelinePreviewCompositor"/>）とビルダー（<see cref="FacialAnimationClipBuilder"/>）で行う。
    /// REC 再生と同じ遷移計算（HID-318）を Timeline 経路と共有し、従来の「Export → Timeline → AnimationTrack 変換」と同じ値になる。
    /// 一時オブジェクトはアセットとして保存せず、書き出し後に必ず破棄する。</para>
    /// <para>シーンの FacialController・renderer・ボーンは書き換えない（Animator も追加しない）。カーブの path は、FacialController の
    /// 親階層（自身を含む）→ 子階層の順に見つかった Animator の Transform、無ければ FacialController の Transform を基準にする。</para>
    /// </remarks>
    internal static class RecAnimationClipExporter
    {
        private const string LogPrefix = "[RecAnimationClipExporter] ";

        /// <param name="recordingPath">REC ファイルのパス。</param>
        /// <param name="controller">書き出し対象（シーン上の FacialController。BlendShape 名・renderer・目ボーンの取得元）。</param>
        /// <param name="profileAsset">合成に使う Profile（null なら <paramref name="controller"/> の Character SO）。</param>
        /// <param name="sourceRemap">REC の入力源・レイヤーの対応表（HID-412。null なら付け替えない）。</param>
        /// <param name="outputFolder">保存先フォルダ（プロジェクト相対。例 <c>Assets/Faces</c>）。</param>
        /// <param name="createTimeline">Clip を並べた AnimationTrack 入りの TimelineAsset も作るか。</param>
        public static bool TryExport(
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            RecSourceRemap sourceRemap,
            string outputFolder,
            bool createTimeline,
            out RecAnimationClipExportResult result)
        {
            result = TryExportCore(recordingPath, controller, profileAsset, sourceRemap, outputFolder, createTimeline);
            if (!result.Succeeded)
            {
                Debug.LogError(LogPrefix + result.Error);
                return false;
            }

            return true;
        }

        private static RecAnimationClipExportResult TryExportCore(
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            RecSourceRemap sourceRemap,
            string outputFolder,
            bool createTimeline)
        {
            if (controller == null)
            {
                return RecAnimationClipExportResult.Failed("FacialController を指定してください。");
            }

            if (EditorUtility.IsPersistent(controller))
            {
                return RecAnimationClipExportResult.Failed("prefab アセットではなく、シーン上の FacialController を指定してください。");
            }

            profileAsset = profileAsset != null ? profileAsset : controller.CharacterSO;
            if (profileAsset == null)
            {
                return RecAnimationClipExportResult.Failed($"'{controller.name}' に Character SO（Profile SO）が無いため、Profile を指定してください。");
            }

            string folder = NormalizeFolder(outputFolder);
            if (folder == null || !AssetDatabase.IsValidFolder(folder))
            {
                return RecAnimationClipExportResult.Failed($"出力フォルダ '{outputFolder}' がプロジェクト内（Assets 配下）に見つかりません。");
            }

            if (!RecFileReader.TryRead(recordingPath, out RecBinaryFormat.ReadResult readResult))
            {
                return RecAnimationClipExportResult.Failed($"REC '{recordingPath}' を読み込めませんでした（詳細は Console）。");
            }

            FacialProfile profile = TimelineProfileSource.Resolve(profileAsset);
            RecSourceRemap remap = sourceRemap != null && !sourceRemap.IsIdentity ? sourceRemap : null;
            IReadOnlyList<string> problems = Array.Empty<string>();
            if (remap != null)
            {
                RecRemapReport report = RecSourceRemapResolver.Analyze(readResult.Timeline, profile, remap);
                problems = RecSourceRemapResolver.DescribeProblems(report);
                if (report.HasCollisions)
                {
                    return RecAnimationClipExportResult.Failed(
                        "入力源・レイヤーの対応表で対応先が重複しているため書き出しません:\n" + string.Join("\n", problems), problems);
                }

                if (problems.Count > 0)
                {
                    Debug.LogWarning(LogPrefix + $"'{recordingPath}' には Profile '{profileAsset.name}' で再生されない入力源・レイヤー・表情があります:\n" + string.Join("\n", problems));
                }
            }

            var sequence = new RecEventSequenceAdapter(readResult.Timeline, remap);
            WarnSkippedRecords(sequence, recordingPath);

            TimelineAsset scratchTimeline = null;
            FacialTimelineBakeAsset scratchBake = null;
            List<FacialAnimationClipPart> parts = null;
            try
            {
                // REC Export と同じ規則でメモリ上にだけ組む（保存しない）。
                scratchTimeline = RecToTimelineExporter.CreateTimelineAsset(sequence, profileAsset);
                scratchTimeline.hideFlags = HideFlags.HideAndDontSave;
                scratchBake = TimelineBakeService.Bake(scratchTimeline, profileAsset);
                scratchBake.hideFlags = HideFlags.HideAndDontSave;
                BakeReferenceWriter.Apply(scratchTimeline, scratchBake);

                using (var compositor = new TimelinePreviewCompositor(controller, profileAsset, profile, null, scratchTimeline))
                {
                    if (!compositor.CanRender)
                    {
                        return RecAnimationClipExportResult.Failed($"REC から組んだ合成を評価できません（{compositor.BakeLocate.Status}）。", problems);
                    }

                    Animator animator = ResolveAnimator(controller);
                    Transform root = animator != null ? animator.transform : controller.transform;
                    double frameRate = scratchTimeline.editorSettings.frameRate > 0d
                        ? scratchTimeline.editorSettings.frameRate
                        : FacialAnimationTrackConverter.FallbackFrameRate;
                    SampleTimeline samples = SampleTimeline.Create(
                        scratchTimeline.duration,
                        frameRate,
                        FacialAnimationTrackConverter.CollectStepCandidateTimes(scratchTimeline, scratchBake));
                    parts = FacialAnimationClipBuilder.Build(
                        compositor, controller, profileAsset, root, animator != null && animator.isHuman ? animator : null, samples, frameRate);
                }
            }
            finally
            {
                DestroyScratch(scratchTimeline, scratchBake);
            }

            if (parts.Count == 0)
            {
                return RecAnimationClipExportResult.Failed($"'{controller.name}' の BlendShape・目ボーンが見つからないため、書き出すカーブがありません。", problems);
            }

            string baseName = FacialAnimationTrackConverter.SanitizeFileName(
                Path.GetFileNameWithoutExtension(recordingPath) + "_" + controller.name);
            var clips = new List<RecAnimationClipExportClip>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                FacialAnimationClipPart part = parts[i];
                string clipName = baseName + "_" + FacialAnimationTrackConverter.SanitizeFileName(part.ClipName);
                part.Clip.name = clipName;
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + clipName + ".anim");
                AssetDatabase.CreateAsset(part.Clip, assetPath);
                clips.Add(new RecAnimationClipExportClip(part, assetPath));
            }

            string timelinePath = createTimeline ? CreateTimelineWithClips(folder, baseName, controller.name, clips) : null;
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + $"'{recordingPath}' を {clips.Count} 本の AnimationClip に書き出しました（{Describe(clips)}）。");
            return new RecAnimationClipExportResult(clips, timelinePath, problems, null);
        }

        /// <summary>FacialController の親階層（自身を含む）→ 子階層の順に Animator を探す（AnimationTrack 変換と同じ）。</summary>
        internal static Animator ResolveAnimator(FacialController controller)
        {
            Animator animator = controller.GetComponentInParent<Animator>(true);
            return animator != null ? animator : controller.GetComponentInChildren<Animator>(true);
        }

        /// <summary>プロジェクト相対のフォルダパスへ正規化する（末尾の区切り・区切り文字の違いを吸収。Assets 外は null）。</summary>
        internal static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            string normalized = folder.Trim().Replace('\\', '/').TrimEnd('/');
            string dataPath = UnityEngine.Application.dataPath.Replace('\\', '/');
            if (normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Assets" + normalized.Substring(dataPath.Length);
            }

            return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal) ? normalized : null;
        }

        private static string CreateTimelineWithClips(
            string folder,
            string baseName,
            string targetName,
            IReadOnlyList<RecAnimationClipExportClip> clips)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + ".playable");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            for (int i = 0; i < clips.Count; i++)
            {
                FacialAnimationClipPart part = clips[i].Part;
                var track = timeline.CreateTrack<AnimationTrack>(null, targetName + " (" + part.ClipName + ")");
                TimelineClip timelineClip = track.CreateClip(part.Clip);
                timelineClip.start = 0d;
                timelineClip.displayName = part.Clip.name;
            }

            EditorUtility.SetDirty(timeline);
            return path;
        }

        /// <summary>メモリ上に組んだ TimelineAsset（トラック・Clip を含む）と Bake を破棄する。</summary>
        private static void DestroyScratch(TimelineAsset timeline, FacialTimelineBakeAsset bake)
        {
            if (timeline != null)
            {
                var tracks = new List<(TrackAsset track, bool mutedInHierarchy)>();
                FacialAnimationTrackConverter.CollectAllTracks(timeline, tracks);
                for (int i = tracks.Count - 1; i >= 0; i--)
                {
                    TrackAsset track = tracks[i].track;
                    if (track == null)
                    {
                        continue;
                    }

                    foreach (TimelineClip clip in track.GetClips())
                    {
                        if (clip.asset != null)
                        {
                            UnityEngine.Object.DestroyImmediate(clip.asset);
                        }
                    }

                    UnityEngine.Object.DestroyImmediate(track);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }

            if (bake != null)
            {
                UnityEngine.Object.DestroyImmediate(bake);
            }
        }

        private static void WarnSkippedRecords(RecEventSequenceAdapter sequence, string recordingPath)
        {
            if (sequence.SkippedWeightEventCount > 0)
            {
                Debug.LogWarning(LogPrefix + $"'{recordingPath}' の入力源 weight のレコード {sequence.SkippedWeightEventCount} 件は Timeline 経路と同じく書き出しに反映されません。");
            }

            if (sequence.SkippedExpressionEventCount > 0)
            {
                Debug.LogWarning(LogPrefix + $"'{recordingPath}' の Expression の Activate / Deactivate レコード（kind 9 / 10）{sequence.SkippedExpressionEventCount} 件は Timeline 経路と同じく書き出しに反映されません。");
            }
        }

        private static string Describe(IReadOnlyList<RecAnimationClipExportClip> clips)
        {
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < clips.Count; i++)
            {
                FacialAnimationClipPart part = clips[i].Part;
                summary.Append(i == 0 ? string.Empty : ", ").Append($"{part.ClipName}: カーブ {part.CurveCount} / キー {part.KeyCount}");
            }

            return summary.ToString();
        }
    }
}
