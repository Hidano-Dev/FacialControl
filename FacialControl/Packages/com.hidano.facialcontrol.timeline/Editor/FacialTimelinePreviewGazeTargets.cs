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
    /// rest 回転と yaw / pitch 軸は <see cref="GazeEyeBoneResolver"/> がアセットデータから求めた値 (ランタイムと同じ)。
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

        /// <summary>
        /// ジョイント名が未指定で、Avatar の目線ボーン (Humanoid の目ボーン) を使う target か。
        /// REC に記録されたパスで解決した target は false。
        /// </summary>
        public bool IsFallback { get; }
    }

    /// <summary>
    /// Timeline のスクラブプレビュー用に、<see cref="GazeChannel"/> 群から書き込み先の目ボーンを解決する。
    /// ランタイムの <see cref="GazeBonePoseProvider"/> と同じ規則に揃える。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Gaze 値チャネル（Bake / Value トラックの ChannelSubId = REC の source id）と <see cref="GazeChannel"/> の対応は
    /// トラックの index ではなく id で決める（Req 7.4）: <see cref="GazeSourceIdConvention"/> で分解した
    /// チャネル id が <see cref="GazeChannel.id"/> と一致するもの（<c>.left</c> / <c>.right</c> はその目だけ、suffix 無しは両目）。
    /// ランタイムの GazeChannelResolver と同じく、旧データの明示 source id（<see cref="GazeChannel.sourceIdLeft"/> /
    /// <see cref="GazeChannel.sourceIdRight"/>）は読まない (HID-191)。
    /// 一致する値チャネルが無い目は駆動しない（ランタイムは入力源の無い channel を駆動しない）。</item>
    /// <item>目線ボーンはランタイムと同じ <see cref="GazeEyeBoneResolver"/> で解決する (ジョイント名 → 見つからなければ
    /// Avatar の目線ボーン)。どちらも無い側は駆動しない。</item>
    /// <item>駆動する Gaze 値チャネル (トラック) に REC で記録した目線ボーンのパスがあれば、その目はパス (Animator のルート相対) で
    /// 解決する (HID-192。Generic のモデルでも Avatar に頼らずに引ける)。パスが見つからなければ上の規則に戻す。</item>
    /// <item>同じボーンに解決された目は、先の channel (channel 順、左目 → 右目) だけが駆動する。</item>
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
        /// 目線ボーンの resolver。解決結果・警告は resolver ごとにキャッシュされるので、
        /// 呼出側はスクラブ中に同じ resolver を使い回す。
        /// </param>
        /// <param name="leftEyeBonePaths">
        /// <paramref name="sourceIds"/> と同じ並びの、REC に記録された左目の目線ボーンのパス。null / 空要素は記録なし。
        /// </param>
        /// <param name="rightEyeBonePaths">同じく右目のパス。</param>
        public static void Resolve(
            GazeEyeBoneResolver resolver,
            IReadOnlyList<GazeChannel> configs,
            IReadOnlyList<string> sourceIds,
            List<FacialTimelinePreviewEyeTarget> results,
            IReadOnlyList<string> leftEyeBonePaths = null,
            IReadOnlyList<string> rightEyeBonePaths = null)
        {
            if (resolver == null || configs == null || results == null)
            {
                return;
            }

            int start = results.Count;
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
                    AddEye(resolver, config, i, leftSource, true, GetPath(leftEyeBonePaths, leftSource), start, results);
                }

                if (rightSource != NotDriven)
                {
                    AddEye(resolver, config, i, rightSource, false, GetPath(rightEyeBonePaths, rightSource), start, results);
                }
            }
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
        /// 規約 id の目指定（.left / .right）→ 規約 id の両目（suffix 無し）の順に優先する。
        /// 同じ優先度で複数一致した場合は列の先頭を使う。
        /// </summary>
        private static void ResolveEyeSources(
            GazeChannel config,
            IReadOnlyList<string> sourceIds,
            out int leftSource,
            out int rightSource)
        {
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

            leftSource = conventionLeft != NotDriven ? conventionLeft : conventionShared;
            rightSource = conventionRight != NotDriven ? conventionRight : conventionShared;
        }

        private static void AddEye(
            GazeEyeBoneResolver resolver,
            GazeChannel config,
            int channelIndex,
            int sourceIndex,
            bool isLeftEye,
            string recordedPath,
            int start,
            List<FacialTimelinePreviewEyeTarget> results)
        {
            string jointName = GazeEyeBoneResolver.ToJointName(isLeftEye ? config.leftEyeBonePath : config.rightEyeBonePath);
            GazeEyeBoneFallback.FallbackEye eye = string.IsNullOrEmpty(recordedPath)
                ? default
                : resolver.ResolveRelativePath(recordedPath);
            bool usesRecordedPath = eye.Bone != null;
            if (!usesRecordedPath)
            {
                eye = resolver.Resolve(jointName, isLeftEye);
            }

            if (eye.Bone == null)
            {
                return;
            }

            for (int i = start; i < results.Count; i++)
            {
                if (results[i].Bone == eye.Bone)
                {
                    return;
                }
            }

            results.Add(new FacialTimelinePreviewEyeTarget(
                channelIndex,
                sourceIndex,
                isLeftEye,
                eye.Bone,
                eye.RestRotation,
                eye.YawAxisLocal,
                eye.PitchAxisLocal,
                !usesRecordedPath && jointName.Length == 0));
        }

        private static string GetPath(IReadOnlyList<string> paths, int sourceIndex)
        {
            return paths != null && sourceIndex >= 0 && sourceIndex < paths.Count ? paths[sourceIndex] : null;
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
