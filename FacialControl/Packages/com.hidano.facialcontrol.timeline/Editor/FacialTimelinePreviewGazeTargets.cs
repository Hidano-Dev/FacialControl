using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using UnityEngine;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Timeline のスクラブプレビューで目線を書き込む目ボーン 1 本分。
    /// rest 回転と yaw / pitch 軸は、path 指定なら <see cref="GazeChannel"/> の保存値、
    /// path 未指定なら <see cref="GazeEyeBoneFallback"/> の導出値。
    /// </summary>
    public readonly struct FacialTimelinePreviewEyeTarget
    {
        public FacialTimelinePreviewEyeTarget(
            int channelIndex,
            bool isLeftEye,
            Transform bone,
            Quaternion restRotation,
            Vector3 yawAxisLocal,
            Vector3 pitchAxisLocal,
            bool isFallback)
        {
            ChannelIndex = channelIndex;
            IsLeftEye = isLeftEye;
            Bone = bone;
            RestRotation = restRotation;
            YawAxisLocal = yawAxisLocal;
            PitchAxisLocal = pitchAxisLocal;
            IsFallback = isFallback;
        }

        /// <summary>対応する <see cref="GazeChannel"/> の index。</summary>
        public int ChannelIndex { get; }
        public bool IsLeftEye { get; }
        public Transform Bone { get; }
        public Quaternion RestRotation { get; }
        public Vector3 YawAxisLocal { get; }
        public Vector3 PitchAxisLocal { get; }

        /// <summary>path 未指定で Humanoid の目ボーンへ fallback した target か。</summary>
        public bool IsFallback { get; }
    }

    /// <summary>
    /// Timeline のスクラブプレビュー用に、<see cref="GazeChannel"/> 群から書き込み先の目ボーンを解決する。
    /// ランタイムの <see cref="GazeBonePoseProvider"/> と同じ規則に揃える。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>path を指定した側は character root からの相対 path で解決する (見つからなければ駆動しない)。</item>
    /// <item>path が空・空白の側は <see cref="GazeEyeBoneFallback"/> の目ボーンを使う。目ごとに、path 未指定の
    /// 最初の channel だけが fallback を使う。fallback の目ボーンが無い側は駆動しない。</item>
    /// <item>path 指定の target が fallback と同じボーンを指す場合は path 指定側を優先し、fallback 側を外す。</item>
    /// </list>
    /// </remarks>
    public static class FacialTimelinePreviewGazeTargets
    {
        /// <summary>
        /// <paramref name="configs"/> の先頭 <paramref name="channelCount"/> 件について目ボーンを解決し、
        /// <paramref name="results"/> に channel 順 (左目 → 右目) で追加する。
        /// </summary>
        public static void Resolve(
            Transform root,
            IReadOnlyList<GazeChannel> configs,
            int channelCount,
            GazeEyeBoneFallback fallback,
            List<FacialTimelinePreviewEyeTarget> results)
        {
            if (root == null || configs == null || results == null)
            {
                return;
            }

            int start = results.Count;
            bool leftFallbackClaimed = false;
            bool rightFallbackClaimed = false;
            int count = Mathf.Min(channelCount, configs.Count);
            for (int i = 0; i < count; i++)
            {
                GazeChannel config = configs[i];
                if (config == null)
                {
                    continue;
                }

                AddEye(root, config, i, true, fallback.Left, ref leftFallbackClaimed, results);
                AddEye(root, config, i, false, fallback.Right, ref rightFallbackClaimed, results);
            }

            RemoveFallbackOwnedByPath(results, start);
        }

        /// <summary>
        /// 正規化済みの目線入力 (x: 右が正、y: 上が正、各 -1〜1) から、<paramref name="target"/> の localRotation を求める。
        /// </summary>
        public static Quaternion ComputeLocalRotation(
            FacialTimelinePreviewEyeTarget target,
            GazeChannel config,
            float x,
            float y)
        {
            Vector3 yawAxis = SafeNormalize(target.YawAxisLocal, Vector3.up);
            Vector3 pitchAxis = SafeNormalize(target.PitchAxisLocal, Vector3.right);

            float yaw = ComputeYawDegrees(target.IsLeftEye, config, x);
            float pitch = y >= 0f ? y * Mathf.Max(0f, config.lookUpAngle) : y * Mathf.Max(0f, config.lookDownAngle);

            return Quaternion.AngleAxis(-yaw, yawAxis) *
                   Quaternion.AngleAxis(-pitch, pitchAxis) *
                   target.RestRotation;
        }

        private static void AddEye(
            Transform root,
            GazeChannel config,
            int channelIndex,
            bool isLeftEye,
            GazeEyeBoneFallback.FallbackEye fallbackEye,
            ref bool fallbackClaimed,
            List<FacialTimelinePreviewEyeTarget> results)
        {
            string bonePath = isLeftEye ? config.leftEyeBonePath : config.rightEyeBonePath;
            if (!string.IsNullOrWhiteSpace(bonePath))
            {
                Transform bone = root.Find(bonePath);
                if (bone == null)
                {
                    return;
                }

                results.Add(new FacialTimelinePreviewEyeTarget(
                    channelIndex,
                    isLeftEye,
                    bone,
                    Quaternion.Euler(isLeftEye ? config.leftEyeInitialRotation : config.rightEyeInitialRotation),
                    isLeftEye ? config.leftEyeYawAxisLocal : config.rightEyeYawAxisLocal,
                    isLeftEye ? config.leftEyePitchAxisLocal : config.rightEyePitchAxisLocal,
                    false));
                return;
            }

            if (fallbackClaimed || fallbackEye.Bone == null)
            {
                return;
            }

            fallbackClaimed = true;
            results.Add(new FacialTimelinePreviewEyeTarget(
                channelIndex,
                isLeftEye,
                fallbackEye.Bone,
                fallbackEye.RestRotation,
                fallbackEye.YawAxisLocal,
                fallbackEye.PitchAxisLocal,
                true));
        }

        private static void RemoveFallbackOwnedByPath(List<FacialTimelinePreviewEyeTarget> results, int start)
        {
            for (int i = results.Count - 1; i >= start; i--)
            {
                if (!results[i].IsFallback)
                {
                    continue;
                }

                for (int j = start; j < results.Count; j++)
                {
                    if (!results[j].IsFallback && results[j].Bone == results[i].Bone)
                    {
                        results.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static float ComputeYawDegrees(bool isLeftEye, GazeChannel config, float x)
        {
            float outerYaw = Mathf.Max(0f, config.outerYawAngle);
            float innerYaw = Mathf.Max(0f, config.innerYawAngle);
            if (isLeftEye)
            {
                return x >= 0f ? x * outerYaw : x * innerYaw;
            }

            return x >= 0f ? x * innerYaw : x * outerYaw;
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude < 1e-8f ? fallback : value.normalized;
        }
    }
}
