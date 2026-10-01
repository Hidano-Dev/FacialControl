using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// ARKit 52 / PerfectSync パラメータの名前表・レイヤーグループ分類・検出を提供する静的サービス。
    /// 完全一致マッチングのみ対応。未対応パラメータは警告なしでスキップする。
    /// </summary>
    public static class ARKitDetector
    {
        // --- ARKit 52 標準パラメータ ---

        /// <summary>
        /// ARKit 52 標準 BlendShape パラメータ名の配列。
        /// Apple ARKit Face Tracking の全 52 パラメータに対応。
        /// </summary>
        public static readonly string[] ARKit52Names = new[]
        {
            // Eyes (14)
            "eyeBlinkLeft",
            "eyeLookDownLeft",
            "eyeLookInLeft",
            "eyeLookOutLeft",
            "eyeLookUpLeft",
            "eyeSquintLeft",
            "eyeWideLeft",
            "eyeBlinkRight",
            "eyeLookDownRight",
            "eyeLookInRight",
            "eyeLookOutRight",
            "eyeLookUpRight",
            "eyeSquintRight",
            "eyeWideRight",

            // Jaw (4)
            "jawForward",
            "jawLeft",
            "jawRight",
            "jawOpen",

            // Mouth (23)
            "mouthClose",
            "mouthFunnel",
            "mouthPucker",
            "mouthLeft",
            "mouthRight",
            "mouthSmileLeft",
            "mouthSmileRight",
            "mouthFrownLeft",
            "mouthFrownRight",
            "mouthDimpleLeft",
            "mouthDimpleRight",
            "mouthStretchLeft",
            "mouthStretchRight",
            "mouthRollLower",
            "mouthRollUpper",
            "mouthShrugLower",
            "mouthShrugUpper",
            "mouthPressLeft",
            "mouthPressRight",
            "mouthLowerDownLeft",
            "mouthLowerDownRight",
            "mouthUpperUpLeft",
            "mouthUpperUpRight",

            // Brow (5)
            "browDownLeft",
            "browDownRight",
            "browInnerUp",
            "browOuterUpLeft",
            "browOuterUpRight",

            // Cheek (3)
            "cheekPuff",
            "cheekSquintLeft",
            "cheekSquintRight",

            // Nose (2)
            "noseSneerLeft",
            "noseSneerRight",

            // Tongue (1)
            "tongueOut",
        };

        // --- PerfectSync 拡張パラメータ ---

        /// <summary>
        /// PerfectSync 拡張パラメータ名の配列。
        /// ARKit 52 に含まれない追加パラメータ。
        /// </summary>
        public static readonly string[] PerfectSyncNames = new[]
        {
            // Tongue 拡張
            "tongueUp",
            "tongueDown",
            "tongueLeft",
            "tongueRight",
            "tongueFlat",
            "tongueLongStep1",
            "tongueLongStep2",
            "tongueTwistLeft",
            "tongueTwistRight",

            // Cheek 拡張
            "cheekSuckLeft",
            "cheekSuckRight",

            // Mouth 拡張
            "mouthTightenerLeft",
            "mouthTightenerRight",
        };

        // --- レイヤーグルーピング定義 ---

        private static readonly Dictionary<string, string> _layerGroupMap;

        static ARKitDetector()
        {
            _layerGroupMap = new Dictionary<string, string>();

            // ARKit 52 + PerfectSync 全パラメータのグルーピングを構築
            foreach (var name in ARKit52Names)
            {
                _layerGroupMap[name] = ClassifyLayerGroup(name);
            }
            foreach (var name in PerfectSyncNames)
            {
                _layerGroupMap[name] = ClassifyLayerGroup(name);
            }
        }

        /// <summary>
        /// パラメータ名からレイヤーグループを分類する内部メソッド。
        /// プレフィックスに基づいてグループを判定する。
        /// </summary>
        private static string ClassifyLayerGroup(string name)
        {
            if (name.StartsWith("eye", StringComparison.Ordinal))
                return "eye";
            if (name.StartsWith("jaw", StringComparison.Ordinal))
                return "mouth";
            if (name.StartsWith("mouth", StringComparison.Ordinal))
                return "mouth";
            if (name.StartsWith("tongue", StringComparison.Ordinal))
                return "mouth";
            if (name.StartsWith("brow", StringComparison.Ordinal))
                return "brow";
            if (name.StartsWith("cheek", StringComparison.Ordinal))
                return "cheek";
            if (name.StartsWith("nose", StringComparison.Ordinal))
                return "nose";

            return null;
        }

        // --- 検出メソッド ---

        /// <summary>
        /// BlendShape 名配列から ARKit 52 パラメータを完全一致で検出する。
        /// 未対応パラメータは警告なしでスキップする。
        /// </summary>
        /// <param name="blendShapeNames">モデルの BlendShape 名配列</param>
        /// <returns>検出された ARKit パラメータ名の配列</returns>
        public static string[] DetectARKit(string[] blendShapeNames)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            return MatchNames(blendShapeNames, ARKit52Names);
        }

        /// <summary>
        /// BlendShape 名配列から PerfectSync 拡張パラメータを完全一致で検出する。
        /// 未対応パラメータは警告なしでスキップする。
        /// </summary>
        /// <param name="blendShapeNames">モデルの BlendShape 名配列</param>
        /// <returns>検出された PerfectSync パラメータ名の配列</returns>
        public static string[] DetectPerfectSync(string[] blendShapeNames)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            return MatchNames(blendShapeNames, PerfectSyncNames);
        }

        /// <summary>
        /// BlendShape 名配列から ARKit 52 + PerfectSync 全パラメータを完全一致で検出する。
        /// 未対応パラメータは警告なしでスキップする。
        /// </summary>
        /// <param name="blendShapeNames">モデルの BlendShape 名配列</param>
        /// <returns>検出されたパラメータ名の配列（ARKit + PerfectSync）</returns>
        public static string[] DetectAll(string[] blendShapeNames)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            var arkit = DetectARKit(blendShapeNames);
            var ps = DetectPerfectSync(blendShapeNames);

            var result = new string[arkit.Length + ps.Length];
            Array.Copy(arkit, 0, result, 0, arkit.Length);
            Array.Copy(ps, 0, result, arkit.Length, ps.Length);
            return result;
        }

        /// <summary>
        /// パラメータ名のレイヤーグループを取得する。
        /// ARKit 52 / PerfectSync に含まれないパラメータは null を返す。
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>レイヤーグループ名（eye / mouth / brow / cheek / nose）、未知の場合は null</returns>
        public static string GetLayerGroup(string parameterName)
        {
            if (parameterName == null)
                throw new ArgumentNullException(nameof(parameterName));

            if (_layerGroupMap.TryGetValue(parameterName, out var group))
                return group;

            return null;
        }

        // --- 内部ヘルパー ---

        /// <summary>
        /// BlendShape 名配列からターゲットリストに完全一致する名前を抽出する。
        /// </summary>
        private static string[] MatchNames(string[] blendShapeNames, string[] targetNames)
        {
            var matched = new List<string>();

            // ターゲットを HashSet で高速検索
            var targetSet = new HashSet<string>(targetNames);

            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                if (targetSet.Contains(blendShapeNames[i]))
                {
                    matched.Add(blendShapeNames[i]);
                }
            }

            return matched.ToArray();
        }
    }
}
