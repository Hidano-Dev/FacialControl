using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// Builds safe StreamingAssets paths for REC sidecar files.
    /// </summary>
    public static class RecSidecarPath
    {
        public const string RecordingsFolderName = "recordings";
        public const string FileExtension = ".fcrec";

        public static bool TryBuildRecordingDirectoryPath(string assetName, out string directoryPath, out string error)
        {
            directoryPath = null;
            error = null;

            if (!TryNormalizeSegment(assetName, nameof(assetName), out string normalizedAssetName, out error))
            {
                return false;
            }

            directoryPath = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                normalizedAssetName,
                RecordingsFolderName);
            return true;
        }

        public static bool TryBuildRecordingFilePath(string assetName, string recordingName, out string filePath, out string error)
        {
            filePath = null;
            error = null;

            if (!TryBuildRecordingDirectoryPath(assetName, out string directoryPath, out error))
            {
                return false;
            }

            if (!TryNormalizeSegment(recordingName, nameof(recordingName), out string normalizedRecordingName, out error))
            {
                return false;
            }

            filePath = Path.Combine(directoryPath, normalizedRecordingName + FileExtension);
            return true;
        }

        /// <summary>
        /// キャラクター <paramref name="assetName"/> の保存済み録画を列挙する（並び順は <see cref="ListRecordings"/> と同じ）。
        /// ディレクトリの構築に失敗した場合のみ false を返す。録画フォルダがまだ無い場合は空の一覧で true を返す。
        /// </summary>
        public static bool TryListRecordings(
            string assetName,
            string excludedFilePath,
            out IReadOnlyList<RecRecordingEntry> recordings,
            out string error)
        {
            if (!TryBuildRecordingDirectoryPath(assetName, out string directoryPath, out error))
            {
                recordings = Array.Empty<RecRecordingEntry>();
                return false;
            }

            recordings = ListRecordings(directoryPath, excludedFilePath);
            return true;
        }

        /// <summary>
        /// <paramref name="directoryPath"/> 直下の <c>*.fcrec</c> を、更新日時の新しい順（同時刻ならテイク名の順）で列挙する。
        /// テイク名として読み込めない名前（前後の空白や <c>..</c> を含む等、<see cref="TryBuildRecordingFilePath"/> で
        /// 同じファイルに戻らないもの）のファイルは含めない。
        /// <paramref name="excludedFilePath"/> には録画中のファイルなど、一覧に出したくないファイルを指定する。
        /// ファイル I/O を伴うため、毎フレームではなく一覧の更新が必要なときだけ呼ぶこと。
        /// </summary>
        public static IReadOnlyList<RecRecordingEntry> ListRecordings(string directoryPath, string excludedFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return Array.Empty<RecRecordingEntry>();
            }

            string excludedFullPath = string.IsNullOrWhiteSpace(excludedFilePath) ? null : Path.GetFullPath(excludedFilePath);
            var recordings = new List<RecRecordingEntry>();
            string[] filePaths;
            try
            {
                filePaths = Directory.GetFiles(directoryPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"REC recordings could not be listed: {directoryPath} ({ex.Message})");
                return Array.Empty<RecRecordingEntry>();
            }

            for (int i = 0; i < filePaths.Length; i++)
            {
                string filePath = filePaths[i];

                // 検索パターンの拡張子一致は OS 依存の揺れがあるため、拡張子を明示的に比較する。
                // 読み込み側は常に小文字の FileExtension でパスを組み立てるので、大文字小文字も区別する。
                if (!string.Equals(Path.GetExtension(filePath), FileExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                if (excludedFullPath != null
                    && string.Equals(Path.GetFullPath(filePath), excludedFullPath, StringComparison.Ordinal))
                {
                    continue;
                }

                // 一覧の Name はそのまま LoadRecording に渡される前提なので、正規化で別名になるものは出さない。
                string name = Path.GetFileNameWithoutExtension(filePath);
                if (!TryNormalizeSegment(name, "recordingName", out string normalizedName, out _)
                    || !string.Equals(normalizedName, name, StringComparison.Ordinal))
                {
                    continue;
                }

                recordings.Add(new RecRecordingEntry(name, filePath, File.GetLastWriteTimeUtc(filePath)));
            }

            recordings.Sort(CompareNewestFirst);
            return recordings;
        }

        private static int CompareNewestFirst(RecRecordingEntry x, RecRecordingEntry y)
        {
            int byTime = y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(x.Name, y.Name);
        }

        /// <summary>
        /// 既存ファイルと衝突しないパスを返す。衝突時は <c>name-2</c>, <c>name-3</c>… と連番を付与し、
        /// 既存の録画を上書きしない（REC データは代替が効かないため、迷ったら捨てずに守る）。
        /// </summary>
        public static string ResolveUniqueFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A recording file path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                return filePath;
            }

            // ディレクトリ部分は入力の文字列をそのまま使う。Path.GetDirectoryName は Windows で区切りを '\' に
            // 正規化するため、Application.streamingAssetsPath 由来の '/' 混在パスから作った連番テイクのパスが
            // LoadRecording 側（TryBuildRecordingFilePath）の文字列と一致しなくなる。
            string fileName = Path.GetFileName(filePath);
            string directoryPrefix = filePath.Substring(0, filePath.Length - fileName.Length);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            for (int sequence = 2; ; sequence++)
            {
                string candidate = directoryPrefix
                    + baseName + "-" + sequence.ToString(CultureInfo.InvariantCulture) + extension;
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool TryNormalizeSegment(string value, string paramName, out string normalizedValue, out string error)
        {
            normalizedValue = null;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{paramName} was null or empty.";
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Contains("..", StringComparison.Ordinal) ||
                trimmed.IndexOf('/') >= 0 ||
                trimmed.IndexOf('\\') >= 0)
            {
                error = $"{paramName} contained a traversal segment.";
                return false;
            }

            char[] buffer = trimmed.ToCharArray();
            bool hasNonWhitespace = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (IsInvalidFileNameChar(buffer[i]))
                {
                    buffer[i] = '-';
                }

                if (!char.IsWhiteSpace(buffer[i]))
                {
                    hasNonWhitespace = true;
                }
            }

            if (!hasNonWhitespace)
            {
                error = $"{paramName} became empty after sanitization.";
                return false;
            }

            normalizedValue = new string(buffer);
            return true;
        }

        /// <summary>
        /// 録画ファイル名で '-' に置換する文字。<see cref="Path.GetInvalidFileNameChars"/> は OS ごとに違い
        /// （Linux / macOS では '/' と NUL だけ）、同じ名前が OS によって別のパスになるため、
        /// Windows の無効文字（<c>" &lt; &gt; | : * ? \ /</c>）と制御文字を固定集合として全 OS で揃える。
        /// StreamingAssets 配下の録画フォルダを Windows / Linux / macOS 間で持ち回っても同じ名前に解決される。
        /// </summary>
        private static bool IsInvalidFileNameChar(char c)
        {
            if (c < ' ')
            {
                return true;
            }

            switch (c)
            {
                case '"':
                case '<':
                case '>':
                case '|':
                case ':':
                case '*':
                case '?':
                case '\\':
                case '/':
                    return true;
                default:
                    return false;
            }
        }
    }
}
