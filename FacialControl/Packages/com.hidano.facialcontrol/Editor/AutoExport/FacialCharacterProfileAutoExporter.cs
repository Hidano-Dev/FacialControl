using System;
using System.IO;
using System.Text;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Sampling;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hidano.FacialControl.Editor.AutoExport
{
    /// <summary>
    /// Play モード突入時 / ビルド開始時に、プロジェクト内の全 <see cref="FacialCharacterProfileSO"/> を
    /// 再サンプリング + <c>StreamingAssets/FacialControl/{SO 名}/profile.json</c> エクスポートして
    /// ランタイムが読む JSON を最新化する Editor 専用フック。
    /// <para>
    /// 既存の自動保存は Inspector 編集の <c>TrackSerializedObjectValue</c> 起点のみのため、
    /// 「クリップだけ差し替えてエクスポートを忘れる」「パッケージ更新で旧 profile.json が残る」といった
    /// ケースで profile.json が古いまま Play / ビルドに進み得る。本フックがその穴を塞ぐ。
    /// </para>
    /// <para>
    /// エクスポートは冪等（生成 JSON が既存 profile.json と同一なら書き込み自体を省き、最終更新時刻も変えない）。
    /// SO 単位の入口 <see cref="ExportIfEnabled"/> と完了イベント <see cref="Exported"/> を公開する。SO の <c>cachedSnapshot</c> はインメモリで再サンプリングするのみで
    /// アセットを dirty にしない（profile.json の最新化だけを目的とし、余計な保存・再インポートを避ける）。
    /// ただし既に dirty な（未保存編集を持つ）SO は、編集消失を防ぐためエクスポート前に .asset へ保存する。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class FacialCharacterProfileAutoExporter
    {
        static FacialCharacterProfileAutoExporter()
        {
            // ドメインリロードごとに静的コンストラクタが走るため、二重登録を避けてから登録する。
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Edit モード終了（= Play 突入直前）でのみ実行する。
            // EnteredPlayMode で行うと既にランタイムが JSON を読んだ後になり得るため ExitingEditMode を採用する。
            if (change != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            ExportAll("playmode");
        }

        /// <summary>
        /// プロジェクト内の全 <see cref="FacialCharacterProfileSO"/>（派生型含む）を再サンプリング +
        /// profile.json エクスポートする。個別 SO の失敗は警告ログ + skip で、全体は継続する。
        /// </summary>
        /// <param name="trigger">ログ用のトリガー識別子（<c>"playmode"</c> / <c>"build"</c> 等）。</param>
        /// <returns>profile.json を書き出せた SO 数。</returns>
        public static int ExportAll(string trigger)
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(FacialCharacterProfileSO));
            if (guids == null || guids.Length == 0)
            {
                return 0;
            }

            int exported = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var so = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(path);
                if (so == null)
                {
                    continue;
                }

                try
                {
                    if (ExportIfEnabled(so))
                    {
                        exported++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[FacialCharacterProfileAutoExporter] '{path}' の自動エクスポート ({trigger}) に失敗しました: {ex.Message}");
                }
            }

            return exported;
        }

        /// <summary>
        /// <see cref="ExportIfEnabled"/> が profile.json を実際に書き換えた直後に同期発火する（1 回）。
        /// 購読者の例外は <see cref="Debug.LogException(Exception)"/> に流し、書き出し処理は継続する。
        /// </summary>
        public static event Action<FacialCharacterProfileSO> Exported;

        /// <summary>
        /// 有効な SO（<see cref="FacialCharacterProfileSO.CharacterAssetName"/> 非空白）1 つについて、
        /// 未保存編集の保存 → AnimationClip の再サンプリング → JSON 生成 → 既存 profile.json との文字列比較を行い、
        /// 内容が異なる（ファイル無しを含む）ときだけ書き出す冪等入口。
        /// </summary>
        /// <param name="so">対象 SO。null / 名前空は何もせず false。</param>
        /// <returns>profile.json を書き換えたとき true（このとき <see cref="Exported"/> が 1 回発火する）。
        /// 同一内容で書き込みを省いた場合や無効な SO は false（ファイルの最終更新時刻は不変）。</returns>
        /// <remarks>
        /// 書き込み失敗などの例外は呼び出し側（<see cref="ExportAll"/> 等）へ伝播する。
        /// </remarks>
        public static bool ExportIfEnabled(FacialCharacterProfileSO so)
        {
            if (so == null)
            {
                return false;
            }

            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(so.CharacterAssetName);
            if (string.IsNullOrEmpty(profilePath))
            {
                return false;
            }

            // 未保存の編集（Inspector 破棄で自動保存 delayCall が失われた場合等）を
            // Play / ビルド前に .asset へ確定する。メモリ上の SO とディスクの .asset が
            // 不整合のままだと、以後ディスクへ書かれる契機がなく編集が失われ得る。
            // 再サンプリング（下記）より前に呼ぶことで、保存対象をユーザー編集分に限定する。
            AssetDatabase.SaveAssetIfDirty(so);

            // クリップを ÷100 正規化して cachedSnapshot に焼き直し（インメモリ）、その値で JSON を生成する。
            FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, new AnimationClipExpressionSampler());
            var dto = FacialCharacterProfileExporter.BuildProfileSnapshotDto(so);
            string json = new SystemTextJsonParser().SerializeProfileSnapshot(dto);

            // 内容が同一なら書き込みを省き、最終更新時刻を変えない。
            if (File.Exists(profilePath) && string.Equals(File.ReadAllText(profilePath, Encoding.UTF8), json, StringComparison.Ordinal))
            {
                return false;
            }

            FacialCharacterProfileExporter.EnsureParentDirectory(profilePath);
            File.WriteAllText(profilePath, json, Encoding.UTF8);

            RaiseExported(so);
            return true;
        }

        private static void RaiseExported(FacialCharacterProfileSO so)
        {
            var handlers = Exported;
            if (handlers == null)
            {
                return;
            }

            // 購読者 1 つの例外が他の購読者・後続 SO の書き出しを止めないよう個別に呼ぶ。
            var invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<FacialCharacterProfileSO>)invocationList[i])(so);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }

    /// <summary>
    /// ビルド開始時に全 <see cref="FacialCharacterProfileSO"/> を profile.json へ再エクスポートする
    /// <see cref="IPreprocessBuildWithReport"/> 実装。出荷ビルドに含まれる StreamingAssets を常に最新化する。
    /// </summary>
    public sealed class FacialCharacterProfileBuildExporter : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            FacialCharacterProfileAutoExporter.ExportAll("build");
        }
    }
}
