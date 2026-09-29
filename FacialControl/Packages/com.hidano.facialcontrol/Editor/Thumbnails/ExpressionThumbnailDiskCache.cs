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
                return pngBytes.Length > 0;
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを読み込めませんでした: {path} ({e.Message})");
                return false;
            }
            catch (UnauthorizedAccessException e)
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
            catch (IOException e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを保存できませんでした: {path} ({e.Message})");
            }
            catch (UnauthorizedAccessException e)
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
            catch (IOException e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを削除できませんでした: {path} ({e.Message})");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルキャッシュを削除できませんでした: {path} ({e.Message})");
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
