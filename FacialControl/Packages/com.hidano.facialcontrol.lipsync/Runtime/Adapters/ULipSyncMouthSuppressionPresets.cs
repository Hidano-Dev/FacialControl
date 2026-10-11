using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// 発話中に 0 で押さえ込む「フェイシャルキャプチャの口まわり」BlendShape 名の既定集合と、
    /// Suppress Blend Shape Names への追加規則。
    /// </summary>
    /// <remarks>
    /// ARKit 52 のうち口の開閉・形状系 21 個。<c>mouthSmile*</c> / <c>mouthFrown*</c> / <c>mouthDimple*</c> は
    /// 感情表現としてキャプチャに残すため含めない。
    /// iFacialMocap の命名（<c>_L</c> / <c>_R</c> 接尾辞）の読み替え規則は ifacialmocap パッケージの
    /// <c>IFacialMocapBlendShapeCatalog</c> と同じものを複製している（lipsync から ifacialmocap へ依存しないため）。
    /// </remarks>
    public static class ULipSyncMouthSuppressionPresets
    {
        private const string CaptureLeftSuffix = "_L";
        private const string CaptureRightSuffix = "_R";
        private const string ArKitLeftSuffix = "Left";
        private const string ArKitRightSuffix = "Right";

        private static readonly string[] FacialCaptureMouthNames =
        {
            "jawOpen", "jawForward", "jawLeft", "jawRight",
            "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight",
            "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight",
            "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthUpperUpLeft", "mouthUpperUpRight",
            "mouthStretchLeft", "mouthStretchRight",
        };

        /// <summary>口の開閉・形状系 21 個の ARKit 名（定義順）。</summary>
        public static IReadOnlyList<string> FacialCaptureMouthBlendShapeNames => FacialCaptureMouthNames;

        /// <summary>
        /// iFacialMocap のキャプチャ名を ARKit 正準名へ読み替える（<c>_L</c> → <c>Left</c>、<c>_R</c> → <c>Right</c>）。
        /// 接尾辞が無い名前はそのまま返す。
        /// </summary>
        public static string ToArKitName(string captureName)
        {
            if (string.IsNullOrEmpty(captureName))
            {
                return captureName;
            }

            if (captureName.EndsWith(CaptureLeftSuffix, StringComparison.Ordinal))
            {
                return captureName.Substring(0, captureName.Length - CaptureLeftSuffix.Length) + ArKitLeftSuffix;
            }

            if (captureName.EndsWith(CaptureRightSuffix, StringComparison.Ordinal))
            {
                return captureName.Substring(0, captureName.Length - CaptureRightSuffix.Length) + ArKitRightSuffix;
            }

            return captureName;
        }

        /// <summary>キャプチャ名（iFacialMocap 命名または ARKit 名）が口の開閉・形状系 21 個のどれかか。</summary>
        public static bool IsFacialCaptureMouthName(string captureName)
        {
            string arKitName = ToArKitName(captureName);
            if (string.IsNullOrEmpty(arKitName))
            {
                return false;
            }

            for (int i = 0; i < FacialCaptureMouthNames.Length; i++)
            {
                if (string.Equals(FacialCaptureMouthNames[i], arKitName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 「フェイシャルキャプチャの口まわりを追加」の結果を返す。既存の項目は順序ごと残し、
        /// 21 個の ARKit 名 → マッピング上書きの反映先 BlendShape 名の順に、重複しないものを末尾へ追加する。
        /// </summary>
        /// <param name="existing">現在の Suppress Blend Shape Names（null 可）。</param>
        /// <param name="captureMappings">
        /// iFacialMocap Receiver の Mappings 上書き（キャプチャ名, 反映先 BlendShape 名）。null / 空なら上書きなし。
        /// 口まわりのキャプチャ名に割り当てた反映先だけを追加する。
        /// </param>
        /// <param name="meshBlendShapeNames">
        /// 参照モデルのメッシュにある BlendShape 名。null ならメッシュで絞り込まない。
        /// </param>
        public static List<string> AppendFacialCaptureMouthNames(
            IReadOnlyList<string> existing,
            IReadOnlyList<KeyValuePair<string, string>> captureMappings,
            ICollection<string> meshBlendShapeNames)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    result.Add(existing[i]);
                    if (existing[i] != null)
                    {
                        seen.Add(existing[i]);
                    }
                }
            }

            for (int i = 0; i < FacialCaptureMouthNames.Length; i++)
            {
                TryAppend(result, seen, FacialCaptureMouthNames[i], meshBlendShapeNames);
            }

            if (captureMappings != null)
            {
                for (int i = 0; i < captureMappings.Count; i++)
                {
                    KeyValuePair<string, string> mapping = captureMappings[i];
                    if (IsFacialCaptureMouthName(mapping.Key))
                    {
                        TryAppend(result, seen, mapping.Value, meshBlendShapeNames);
                    }
                }
            }

            return result;
        }

        private static void TryAppend(
            List<string> result,
            HashSet<string> seen,
            string name,
            ICollection<string> meshBlendShapeNames)
        {
            if (string.IsNullOrEmpty(name) || seen.Contains(name))
            {
                return;
            }

            if (meshBlendShapeNames != null && !meshBlendShapeNames.Contains(name))
            {
                return;
            }

            result.Add(name);
            seen.Add(name);
        }
    }
}
