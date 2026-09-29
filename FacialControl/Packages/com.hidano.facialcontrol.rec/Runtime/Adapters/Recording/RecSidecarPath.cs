using System;
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
