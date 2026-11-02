using System;
using System.IO;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.FileSystem
{
    /// <summary>
    /// Reads REC sidecar files from disk and reports recovery or failure through Unity logs.
    /// </summary>
    public static class RecFileReader
    {
        public static bool TryRead(string filePath, out RecBinaryFormat.ReadResult result)
        {
            return TryRead(filePath, out result, out _);
        }

        /// <summary>
        /// <see cref="TryRead(string, out RecBinaryFormat.ReadResult)"/> と同じく読み込み、読んだバイト列も返す
        /// （読み込んだ REC の同一性を内容から判定する用途。読めなかったときは null）。
        /// </summary>
        public static bool TryRead(string filePath, out RecBinaryFormat.ReadResult result, out byte[] bytes)
        {
            result = null;
            bytes = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Debug.LogError("REC load failed because filePath was null or empty.");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"REC load failed because file '{filePath}' did not exist.");
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Debug.LogError($"REC load failed while reading '{filePath}': {ex.Message}");
                bytes = null;
                return false;
            }

            if (!RecBinaryFormat.TryRead(bytes, out result, out string error))
            {
                Debug.LogError($"REC load failed for '{filePath}': {error}");
                bytes = null;
                return false;
            }

            if (result.RecoveredFromTruncatedTail)
            {
                Debug.LogWarning($"REC load recovered a truncated tail for '{filePath}'.");
            }

            return true;
        }
    }
}
