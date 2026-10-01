using System;
using System.IO;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// Expression サムネイル PNG のディスクキャッシュ。
    /// <para>
    /// 既定の置き場所はプロジェクトの <c>Library/FacialControl/ExpressionThumbnails/</c>。
    /// Library はバージョン管理対象外なので、生成したバイナリがコミットされることはない。
    /// ファイル名は <see cref="ExpressionThumbnailCacheKey.Compute"/> のキー + <c>.png</c>。
    /// </para>
    /// </summary>
    public sealed class ExpressionThumbnailDiskCache
    {
        private const string FileExtension = ".png";

        private readonly string _directory;

        /// <param name="directory">キャッシュを置くディレクトリ。存在しなければ保存時に作成する</param>
        public ExpressionThumbnailDiskCache(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("directory must not be null or empty.", nameof(directory));

            _directory = directory;
        }

        public string Directory => _directory;

        /// <summary>
        /// プロジェクトの <c>Library/FacialControl/ExpressionThumbnails</c> を返す。
        /// </summary>
        public static string DefaultDirectory
        {
            get
            {
                var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                return Path.Combine(projectRoot ?? string.Empty, "Library", "FacialControl", "ExpressionThumbnails");
            }
        }

        /// <summary>
        /// キャッシュ済みの PNG を読む。無い・読めない場合は false。
        /// </summary>
        public bool TryLoad(string key, out byte[] pngBytes)
        {
            pngBytes = null;
            if (!IsValidKey(key)) return false;

            var path = GetPath(key);
            try
            {
                if (!File.Exists(path)) return false;
                pngBytes = File.ReadAllBytes(path);
                if (pngBytes.Length == 0) return false;

                // 使われたキャッシュを Prune で残すため、最終更新時刻を読み込み時刻へ進める。
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを読み込めませんでした: {path} ({e.Message})");
                return false;
            }
        }

        /// <summary>
        /// PNG を保存する。失敗しても例外は投げず警告だけ出す（キャッシュなので次回再生成すればよい）。
        /// </summary>
        public void Save(string key, byte[] pngBytes)
        {
            if (!IsValidKey(key) || pngBytes == null || pngBytes.Length == 0) return;

            var path = GetPath(key);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);

                // 書き込み途中のファイルを読まないよう、一時ファイルに書いてから置き換える。
                var tempPath = path + ".tmp";
                File.WriteAllBytes(tempPath, pngBytes);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを保存できませんでした: {path} ({e.Message})");
            }
        }

        /// <summary>
        /// キャッシュを削除する。存在しなければ何もしない。
        /// </summary>
        public void Delete(string key)
        {
            if (!IsValidKey(key)) return;

            var path = GetPath(key);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを削除できませんでした: {path} ({e.Message})");
            }
        }

        /// <summary>
        /// キャッシュのファイル数が <paramref name="maxFiles"/> を超えていれば、最終更新（最後に保存・読み込み
        /// された時刻）の古いものから削除する。Expression や参照モデルを編集するたびにキーが変わり、古い PNG が
        /// 残り続けるのを抑えるために、サービス生成時に呼ぶ。
        /// </summary>
        /// <returns>削除したファイル数</returns>
        public int Prune(int maxFiles)
        {
            if (maxFiles < 0) maxFiles = 0;

            try
            {
                if (!System.IO.Directory.Exists(_directory)) return 0;

                var files = new DirectoryInfo(_directory).GetFiles("*" + FileExtension);
                if (files.Length <= maxFiles) return 0;

                // 新しい順に並べ、上限より後ろを消す。
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                int deleted = 0;
                for (int i = maxFiles; i < files.Length; i++)
                {
                    try
                    {
                        files[i].Delete();
                        deleted++;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        // 使用中などで消せないファイルは次回に回す。
                    }
                }
                return deleted;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを整理できませんでした: {_directory} ({e.Message})");
                return 0;
            }
        }

        /// <summary>
        /// キーに対応するファイルパスを返す。
        /// </summary>
        public string GetPath(string key)
        {
            return Path.Combine(_directory, key + FileExtension);
        }

        /// <summary>
        /// キーがファイル名として安全か（16 進小文字のみ）を判定する。パス区切り等を含むキーは拒否する。
        /// </summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex) return false;
            }
            return true;
        }
    }
}
