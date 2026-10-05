using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Timeline のスクラブプレビューで目線を書き込む目ボーン 1 本分。
    /// rest 回転と yaw / pitch 軸は、path 指定なら <see cref="GazeChannel"/> の保存値、
    /// path 未指定なら <see cref="GazeEyeBoneFallback"/> の導出値。
    /// </summary>
    internal readonly struct FacialTimelinePreviewEyeTarget
    {
        public FacialTimelinePreviewEyeTarget(
            int channelIndex,
            int sourceIndex,
            bool isLeftEye,
            Transform bone,
            Quaternion restRotation,
            Vector3 yawAxisLocal,
            Vector3 pitchAxisLocal,
            bool isFallback)
        {
            ChannelIndex = channelIndex;
            SourceIndex = sourceIndex;
            IsLeftEye = isLeftEye;
            Bone = bone;
            RestRotation = restRotation;
            YawAxisLocal = yawAxisLocal;
            PitchAxisLocal = pitchAxisLocal;
            IsFallback = isFallback;
        }

        /// <summary>対応する <see cref="GazeChannel"/> の index（角度制限・rest の取得元）。</summary>
        public int ChannelIndex { get; }

        /// <summary>
        /// この目を駆動する Gaze 値チャネルの index（<see cref="FacialTimelinePreviewGazeTargets.Resolve"/> に渡した source id 列の index）。
        /// source id 列を渡さず全 channel を対象にした場合は -1。
        /// </summary>
        public int SourceIndex { get; }

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
    /// <item>Gaze 値チャネル（Bake / Value トラックの ChannelSubId = REC の source id）と <see cref="GazeChannel"/> の対応は
    /// トラックの index ではなく id で決める（Req 7.4）: GazeChannel の明示 source id（<see cref="GazeChannel.sourceIdLeft"/> /
    /// <see cref="GazeChannel.sourceIdRight"/>）との完全一致を優先し、無ければ <see cref="GazeSourceIdConvention"/> で分解した
    /// チャネル id が <see cref="GazeChannel.id"/> と一致するもの（<c>.left</c> / <c>.right</c> はその目だけ、suffix 無しは両目）。
    /// 一致する値チャネルが無い目は駆動しない（ランタイムは入力源の無い channel を駆動しない）。</item>
    /// <item>path を指定した側はランタイムと同じ <see cref="BoneTransformResolver"/> で解決する
    /// (root からの相対 path / ボーン名 / 末尾一致。見つからなければ駆動しない)。</item>
    /// <item>path が空・空白の側は <see cref="GazeEyeBoneFallback"/> の目ボーンを使う。目ごとに、path 未指定の
    /// 最初の駆動 channel だけが fallback を使う。fallback の目ボーンが無い側は駆動しない。</item>
    /// <item>path 指定の target が fallback と同じボーンを指す場合は path 指定側を優先し、fallback 側を外す。</item>
    /// </list>
    /// </remarks>
    internal static class FacialTimelinePreviewGazeTargets
    {
        private const int AllChannelsSource = -1;
        private const int NotDriven = -2;

        /// <summary>
        /// <paramref name="configs"/> の目ボーンを解決し、<paramref name="results"/> に channel 順 (左目 → 右目) で追加する。
        /// </summary>
        /// <param name="sourceIds">
        /// 駆動に使える Gaze 値チャネルの source id 列（Value トラックの ChannelSubId）。null / 空要素は一致しない。
        /// 列そのものが null なら全 channel の全目を対象にする（Timeline のプロパティ登録用）。
        /// </param>
        /// <param name="resolver">
        /// path 指定の目ボーンを解決する resolver。解決失敗の警告は resolver ごとに 1 回なので、
        /// 呼出側はスクラブ中に同じ resolver を使い回す。
        /// </param>
        public static void Resolve(
            BoneTransformResolver resolver,
            IReadOnlyList<GazeChannel> configs,
            IReadOnlyList<string> sourceIds,
            GazeEyeBoneFallback fallback,
            List<FacialTimelinePreviewEyeTarget> results)
        {
            if (resolver == null || configs == null || results == null)
            {
                return;
            }

            int start = results.Count;
            bool leftFallbackClaimed = false;
            bool rightFallbackClaimed = false;
            for (int i = 0; i < configs.Count; i++)
            {
                GazeChannel config = configs[i];
                if (config == null)
                {
                    continue;
                }

                int leftSource = AllChannelsSource;
                int rightSource = AllChannelsSource;
                if (sourceIds != null)
                {
                    ResolveEyeSources(config, sourceIds, out leftSource, out rightSource);
                }

                if (leftSource != NotDriven)
                {
                    AddEye(resolver, config, i, leftSource, true, fallback.Left, ref leftFallbackClaimed, results);
                }

                if (rightSource != NotDriven)
                {
                    AddEye(resolver, config, i, rightSource, false, fallback.Right, ref rightFallbackClaimed, results);
                }
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

        /// <summary>
        /// <paramref name="config"/> の左右の目を駆動する値チャネルの index を id で求める（見つからなければ <see cref="NotDriven"/>）。
        /// 明示 source id の完全一致 → 規約 id の目指定（.left / .right）→ 規約 id の両目（suffix 無し）の順に優先する。
        /// 同じ優先度で複数一致した場合は列の先頭を使う。
        /// </summary>
        private static void ResolveEyeSources(
            GazeChannel config,
            IReadOnlyList<string> sourceIds,
            out int leftSource,
            out int rightSource)
        {
            int explicitLeft = NotDriven;
            int explicitRight = NotDriven;
            int conventionLeft = NotDriven;
            int conventionRight = NotDriven;
            int conventionShared = NotDriven;
            for (int s = 0; s < sourceIds.Count; s++)
            {
                string sourceId = sourceIds[s];
                if (string.IsNullOrEmpty(sourceId))
                {
                    continue;
                }

                if (explicitLeft == NotDriven && Matches(config.sourceIdLeft, sourceId))
                {
                    explicitLeft = s;
                }

                if (explicitRight == NotDriven && Matches(config.sourceIdRight, sourceId))
                {
                    explicitRight = s;
                }

                if (string.IsNullOrEmpty(config.id)
                    || !GazeSourceIdConvention.TryParse(sourceId, out _, out string channelId, out GazeSide side)
                    || !string.Equals(channelId, config.id, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (side)
                {
                    case GazeSide.Left:
                        if (conventionLeft == NotDriven)
                        {
                            conventionLeft = s;
                        }

                        break;
                    case GazeSide.Right:
                        if (conventionRight == NotDriven)
                        {
                            conventionRight = s;
                        }

                        break;
                    default:
                        if (conventionShared == NotDriven)
                        {
                            conventionShared = s;
                        }

                        break;
                }
            }

            leftSource = Pick(explicitLeft, conventionLeft, conventionShared);
            rightSource = Pick(explicitRight, conventionRight, conventionShared);
        }

        private static int Pick(int explicitSource, int sideSource, int sharedSource)
        {
            if (explicitSource != NotDriven)
            {
                return explicitSource;
            }

            return sideSource != NotDriven ? sideSource : sharedSource;
        }

        private static bool Matches(string declaredSourceId, string sourceId)
        {
            return !string.IsNullOrEmpty(declaredSourceId) && string.Equals(declaredSourceId, sourceId, StringComparison.Ordinal);
        }

        private static void AddEye(
            BoneTransformResolver resolver,
            GazeChannel config,
            int channelIndex,
            int sourceIndex,
            bool isLeftEye,
            GazeEyeBoneFallback.FallbackEye fallbackEye,
            ref bool fallbackClaimed,
            List<FacialTimelinePreviewEyeTarget> results)
        {
            string bonePath = isLeftEye ? config.leftEyeBonePath : config.rightEyeBonePath;
            if (!string.IsNullOrWhiteSpace(bonePath))
            {
                Transform bone = resolver.Resolve(bonePath);
                if (bone == null)
                {
                    return;
                }

                results.Add(new FacialTimelinePreviewEyeTarget(
                    channelIndex,
                    sourceIndex,
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
                sourceIndex,
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
