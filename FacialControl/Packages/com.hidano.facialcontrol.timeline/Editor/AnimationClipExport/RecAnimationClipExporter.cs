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

    /// <summary><see cref="RecAnimationClipExporter.TryExport(string, FacialController, FacialCharacterProfileSO, RecSourceRemap, string, bool, out RecAnimationClipExportResult)"/> の結果。</summary>
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
    /// AnimationTrack 変換と同じ合成（<see cref="TimelinePreviewCompositor"/>）とサンプリング（<see cref="FacialAnimationTrackConverter.BuildClips"/>）で行う。
    /// REC 再生と同じ遷移計算（HID-318）を Timeline 経路と共有し、従来の「Export → Timeline → AnimationTrack 変換」と同じ値になる。
    /// 一時オブジェクト（TimelineAsset・トラック・Clip アセット・Bake）はアセットとして保存せず、例外時も含めて必ず破棄する。</para>
    /// <para>シーンの FacialController・renderer・ボーンは書き換えない（Animator も追加しない）。カーブの path は、FacialController の
    /// 親階層（自身を含む）→ 子階層の順に見つかった Animator の Transform、無ければ FacialController の Transform を基準にする。</para>
    /// <para>prefab アセット上の FacialController を渡した場合は、prefab を隔離シーンへ一時的に読み込んで評価し、終われば破棄する
    /// （開いているシーンにも prefab アセットにも書き込まない。HID-417）。</para>
    /// <para>保存は 1 回の AssetDatabase 編集にまとめ、途中で失敗したらそれまでに保存したアセットを削除する（中途半端な出力を残さない）。</para>
    /// </remarks>
    internal static class RecAnimationClipExporter
    {
        private const string LogPrefix = "[RecAnimationClipExporter] ";

        /// <param name="recordingPath">REC ファイルのパス。</param>
        /// <param name="controller">書き出し対象（シーン上の FacialController か prefab アセット上のもの。BlendShape 名・renderer・目ボーンの取得元）。</param>
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
            if (!RecFileReader.TryRead(recordingPath, out RecBinaryFormat.ReadResult readResult))
            {
                return Fail($"REC '{recordingPath}' を読み込めませんでした（詳細は Console）。", out result);
            }

            return TryExport(readResult, recordingPath, controller, profileAsset, sourceRemap, outputFolder, createTimeline, out result);
        }

        /// <summary>読み込み済みの REC から書き出す（ウィンドウが読み込んだ REC を読み直さないため）。</summary>
        /// <param name="recordingPath">出力名とログに使う REC のパス（読み直さない）。</param>
        internal static bool TryExport(
            RecBinaryFormat.ReadResult readResult,
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            RecSourceRemap sourceRemap,
            string outputFolder,
            bool createTimeline,
            out RecAnimationClipExportResult result)
        {
            result = TryExportCore(readResult, recordingPath, controller, profileAsset, sourceRemap, outputFolder, createTimeline);
            if (!result.Succeeded)
            {
                Debug.LogError(LogPrefix + result.Error);
                return false;
            }

            return true;
        }

        private static bool Fail(string error, out RecAnimationClipExportResult result)
        {
            result = RecAnimationClipExportResult.Failed(error);
            Debug.LogError(LogPrefix + error);
            return false;
        }

        private static RecAnimationClipExportResult TryExportCore(
            RecBinaryFormat.ReadResult readResult,
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            RecSourceRemap sourceRemap,
            string outputFolder,
            bool createTimeline)
        {
            if (readResult == null || readResult.Timeline == null)
            {
                return RecAnimationClipExportResult.Failed("REC が読み込まれていません。");
            }

            if (controller == null)
            {
                return RecAnimationClipExportResult.Failed("FacialController を指定してください。");
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

            // 対応表の判定とログは REC Export と同じ処理を使う（衝突・不正な id は Error、未対応・除外は Warning）。
            FacialProfile profile = TimelineProfileSource.Resolve(profileAsset);
            RecSourceRemap remap = sourceRemap != null && !sourceRemap.IsIdentity ? sourceRemap : null;
            if (!RecToTimelineExporter.TryCheckSourceRemap(
                    readResult.Timeline, profile, profileAsset.name, remap, recordingPath, out IReadOnlyList<string> problems))
            {
                return RecAnimationClipExportResult.Failed(
                    "入力源・レイヤーの対応表で対応先が重複している、または入力源 id の形式が不正なため書き出しません:\n" + string.Join("\n", problems), problems);
            }

            var sequence = new RecEventSequenceAdapter(readResult.Timeline, remap);
            RecToTimelineExporter.WarnSkippedWeightRecords(sequence, recordingPath);
            RecToTimelineExporter.WarnSkippedExpressionRecords(sequence, recordingPath);

            List<FacialAnimationClipPart> parts = BuildPartsForTarget(sequence, controller, profileAsset, profile, out string buildError);
            if (parts == null)
            {
                return RecAnimationClipExportResult.Failed(buildError, problems);
            }

            if (parts.Count == 0)
            {
                return RecAnimationClipExportResult.Failed($"'{controller.name}' の BlendShape・目ボーンが見つからないため、書き出すカーブがありません。", problems);
            }

            string baseName = FacialAnimationTrackConverter.SanitizeFileName(
                Path.GetFileNameWithoutExtension(recordingPath) + "_" + controller.name);
            List<RecAnimationClipExportClip> clips = SaveAll(parts, folder, baseName, controller.name, createTimeline, out string timelinePath);
            Debug.Log(LogPrefix + $"'{recordingPath}' を {clips.Count} 本の AnimationClip に書き出しました（{Describe(clips)}）。");
            return new RecAnimationClipExportResult(clips, timelinePath, problems, null);
        }

        /// <summary>テスト用: prefab アセットを評価するために読み込んだ一時インスタンスの FacialController を受け取る。null なら何もしない。</summary>
        internal static Action<FacialController> PrefabInstanceLoadedForTests;

        /// <summary>
        /// 対象がシーン上の FacialController ならそのまま、prefab アセット上のものなら prefab を隔離シーンへ一時的に読み込んで評価する（HID-417）。
        /// </summary>
        /// <remarks>
        /// 読み込みは <see cref="PrefabUtility.LoadPrefabContents"/>（開いているシーンに置かず、prefab アセットにも保存しない）。
        /// 評価が終われば例外時も含めて <see cref="PrefabUtility.UnloadPrefabContents"/> で隔離シーンごと破棄する。
        /// </remarks>
        private static List<FacialAnimationClipPart> BuildPartsForTarget(
            RecEventSequenceAdapter sequence,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            out string error)
        {
            if (!EditorUtility.IsPersistent(controller))
            {
                return BuildParts(sequence, controller, profileAsset, profile, out error);
            }

            if (!TryLoadPrefabInstance(controller, out GameObject contentsRoot, out FacialController instance, out error))
            {
                return null;
            }

            try
            {
                PrefabInstanceLoadedForTests?.Invoke(instance);
                return BuildParts(sequence, instance, profileAsset, profile, out error);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }
        }

        /// <summary>
        /// prefab アセット上の FacialController を含む prefab を隔離シーンに読み込み、読み込んだ側の対応する FacialController を返す。
        /// 対応は、prefab のルートからの子の並び（sibling index）と、同じ GameObject 上の FacialController の並びで取る。
        /// </summary>
        private static bool TryLoadPrefabInstance(
            FacialController assetController,
            out GameObject contentsRoot,
            out FacialController instance,
            out string error)
        {
            contentsRoot = null;
            instance = null;
            error = null;
            string assetPath = AssetDatabase.GetAssetPath(assetController);
            if (string.IsNullOrEmpty(assetPath) || PrefabUtility.GetPrefabAssetType(assetController.gameObject) == PrefabAssetType.Model
                || PrefabUtility.GetPrefabAssetType(assetController.gameObject) == PrefabAssetType.NotAPrefab)
            {
                error = $"'{assetController.name}' は prefab アセット上の FacialController ではないため評価できません。シーン上の FacialController か prefab を指定してください。";
                return false;
            }

            Transform assetRoot = assetController.transform.root;
            var siblingPath = new List<int>();
            for (Transform t = assetController.transform; t != assetRoot; t = t.parent)
            {
                siblingPath.Add(t.GetSiblingIndex());
            }

            int componentIndex = Array.IndexOf(assetController.GetComponents<FacialController>(), assetController);

            contentsRoot = PrefabUtility.LoadPrefabContents(assetPath);
            Transform target = contentsRoot.transform;
            for (int i = siblingPath.Count - 1; i >= 0 && target != null; i--)
            {
                target = siblingPath[i] < target.childCount ? target.GetChild(siblingPath[i]) : null;
            }

            FacialController[] candidates = target != null ? target.GetComponents<FacialController>() : Array.Empty<FacialController>();
            if (componentIndex < 0 || componentIndex >= candidates.Length)
            {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
                contentsRoot = null;
                error = $"prefab '{assetPath}' を読み込みましたが、'{assetController.name}' の FacialController が見つかりませんでした。";
                return false;
            }

            instance = candidates[componentIndex];
            return true;
        }

        /// <summary>
        /// REC Export と同じ規則で TimelineAsset と Bake をメモリ上にだけ組み、変換と同じサンプリングで Clip（未保存）を作る。
        /// 評価できなければ null と理由を返す。一時オブジェクトは例外時も含めて必ず破棄する。
        /// </summary>
        private static List<FacialAnimationClipPart> BuildParts(
            RecEventSequenceAdapter sequence,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            out string error)
        {
            error = null;
            TimelineAsset scratchTimeline = null;
            FacialTimelineBakeAsset scratchBake = null;
            try
            {
                // 生成した TimelineAsset をこちらが持ってからトラックを作る（作りかけで例外が出ても破棄できる）。
                scratchTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
                scratchTimeline.hideFlags = HideFlags.HideAndDontSave;
                RecToTimelineExporter.PopulateTimelineForProfileAsset(scratchTimeline, sequence, profileAsset, profile);
                scratchBake = TimelineBakeService.Bake(scratchTimeline, profileAsset);
                scratchBake.hideFlags = HideFlags.HideAndDontSave;
                BakeReferenceWriter.Apply(scratchTimeline, scratchBake);

                using (var compositor = new TimelinePreviewCompositor(controller, profileAsset, profile, null, scratchTimeline))
                {
                    if (!compositor.CanRender)
                    {
                        error = $"REC から組んだ合成を評価できません（{compositor.BakeLocate.Status}）。";
                        return null;
                    }

                    return FacialAnimationTrackConverter.BuildClips(
                        scratchTimeline,
                        scratchBake,
                        compositor,
                        controller,
                        profileAsset,
                        FacialAnimationTrackConverter.ResolveAnimator(controller));
                }
            }
            finally
            {
                DestroyScratch(scratchTimeline, scratchBake);
            }
        }

        /// <summary>
        /// Clip（と任意で TimelineAsset）を 1 回の AssetDatabase 編集で保存する。途中で例外が出たら、保存したアセットを削除し、
        /// 未保存の Clip を破棄してから例外を投げ直す。
        /// </summary>
        private static List<RecAnimationClipExportClip> SaveAll(
            List<FacialAnimationClipPart> parts,
            string folder,
            string baseName,
            string targetName,
            bool createTimeline,
            out string timelinePath)
        {
            timelinePath = null;
            var clips = new List<RecAnimationClipExportClip>(parts.Count);
            var createdPaths = new List<string>(parts.Count + 1);
            bool completed = false;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    FacialAnimationClipPart part = parts[i];
                    string clipName = baseName + "_" + FacialAnimationTrackConverter.SanitizeFileName(part.ClipName);
                    part.Clip.name = clipName;
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + clipName + ".anim");
                    AssetDatabase.CreateAsset(part.Clip, assetPath);
                    createdPaths.Add(assetPath);
                    clips.Add(new RecAnimationClipExportClip(part, assetPath));
                }

                if (createTimeline)
                {
                    timelinePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + ".playable");
                    CreateTimelineWithClips(timelinePath, targetName, clips);
                    createdPaths.Add(timelinePath);
                }

                completed = true;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (!completed)
                {
                    Rollback(parts, createdPaths);
                }
            }

            AssetDatabase.SaveAssets();
            return clips;
        }

        private static void Rollback(List<FacialAnimationClipPart> parts, List<string> createdPaths)
        {
            for (int i = 0; i < createdPaths.Count; i++)
            {
                AssetDatabase.DeleteAsset(createdPaths[i]);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                AnimationClip clip = parts[i].Clip;
                if (clip != null && !EditorUtility.IsPersistent(clip))
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }
            }
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

        /// <summary>テスト用: 保存の途中で例外を起こす（ロールバックの確認）。null なら何もしない。</summary>
        internal static Action<string> BeforeCreateTimelineForTests;

        private static void CreateTimelineWithClips(string path, string targetName, IReadOnlyList<RecAnimationClipExportClip> clips)
        {
            BeforeCreateTimelineForTests?.Invoke(path);
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
        }

        /// <summary>メモリ上に組んだ TimelineAsset（子のトラック・Clip アセットを含む）と Bake を破棄する。</summary>
        internal static void DestroyScratch(TimelineAsset timeline, FacialTimelineBakeAsset bake)
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
