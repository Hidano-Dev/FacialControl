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
                if (!string.Equals(Path.GetExtension(filePath), FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (excludedFullPath != null
                    && string.Equals(Path.GetFullPath(filePath), excludedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                recordings.Add(new RecRecordingEntry(
                    Path.GetFileNameWithoutExtension(filePath),
                    filePath,
                    File.GetLastWriteTimeUtc(filePath)));
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

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            for (int sequence = 2; ; sequence++)
            {
                string candidate = Path.Combine(
                    directory,
                    baseName + "-" + sequence.ToString(CultureInfo.InvariantCulture) + extension);
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
                trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                error = $"{paramName} contained a traversal segment.";
                return false;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] buffer = trimmed.ToCharArray();
            bool hasNonWhitespace = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalidChars, buffer[i]) >= 0)
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
    }
}
