using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 目線のジョイント名が未指定 (または見つからない) の <see cref="ScriptableObject.GazeChannel"/> に使う、
    /// Avatar に登録された左右目ボーン (Humanoid の LeftEye / RightEye) と、その rest 回転・yaw / pitch 軸。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FromAnimator"/> は rest 回転・軸を元のアセットデータ (Avatar の skeleton → バインドポーズ) から
    /// 求める (<see cref="GazeEyeBoneResolver"/>)。起動直後の頭部の向きや、既に掛かっている目線の入力に左右されない。
    /// </para>
    /// <para>
    /// 非 Humanoid・Avatar 未設定・Eye 未マップの側は <see cref="FallbackEye.Bone"/> が null になる。
    /// 警告は出さない (目線を実際に駆動しようとした側で呼出側が 1 回だけ警告する)。
    /// </para>
    /// </remarks>
    public readonly struct GazeEyeBoneFallback
    {
        /// <summary>1 つの目の fallback ボーンと、GazeChannel の InitialRotation / YawAxisLocal / PitchAxisLocal 相当の値。</summary>
        public readonly struct FallbackEye
        {
            public Transform Bone { get; }
            public Quaternion RestRotation { get; }
            public Vector3 YawAxisLocal { get; }
            public Vector3 PitchAxisLocal { get; }

            /// <summary>rest 回転・軸を指定して作る。軸は正規化する。</summary>
            public FallbackEye(Transform bone, Quaternion restRotation, Vector3 yawAxisLocal, Vector3 pitchAxisLocal)
            {
                Bone = bone;
                RestRotation = restRotation;
                YawAxisLocal = yawAxisLocal.sqrMagnitude < 1e-8f ? Vector3.up : yawAxisLocal.normalized;
                PitchAxisLocal = pitchAxisLocal.sqrMagnitude < 1e-8f ? Vector3.right : pitchAxisLocal.normalized;
            }

            /// <summary>rest 回転・軸をこの時点の姿勢から求めて作る (<see cref="DeriveRestAndAxes"/>)。</summary>
            public FallbackEye(Transform bone, Transform characterRoot)
            {
                Bone = bone;
                if (bone == null)
                {
                    RestRotation = Quaternion.identity;
                    YawAxisLocal = Vector3.up;
                    PitchAxisLocal = Vector3.right;
                    return;
                }

                DeriveRestAndAxes(bone, characterRoot, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);
                RestRotation = rest;
                YawAxisLocal = yaw;
                PitchAxisLocal = pitch;
            }
        }

        public FallbackEye Left { get; }
        public FallbackEye Right { get; }

        public Transform LeftEye => Left.Bone;
        public Transform RightEye => Right.Bone;

        /// <summary>左右の目から作る。</summary>
        public GazeEyeBoneFallback(FallbackEye left, FallbackEye right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>
        /// 左右目ボーンから fallback を作る。rest 回転と軸はこの時点の姿勢から導出する
        /// (<see cref="DeriveRestAndAxes"/>)。アセットデータから求める場合は <see cref="FromAnimator"/> を使う。
        /// </summary>
        public GazeEyeBoneFallback(Transform leftEye, Transform rightEye, Transform characterRoot)
        {
            Left = new FallbackEye(leftEye, characterRoot);
            Right = new FallbackEye(rightEye, characterRoot);
        }

        /// <summary>
        /// Humanoid Animator から <see cref="HumanBodyBones.LeftEye"/> / <see cref="HumanBodyBones.RightEye"/> を解決し、
        /// rest 回転・軸を元のアセットデータから求める。null / 非 Humanoid の場合は目ボーンが両方 null の値を返す (throw しない)。
        /// </summary>
        public static GazeEyeBoneFallback FromAnimator(Animator animator)
        {
            return GazeEyeBoneResolver.FromAnimator(animator).Fallback;
        }

        /// <summary>
        /// 目ボーンの呼出時点の姿勢から rest 回転と yaw / pitch 軸を導出する。元のアセットデータから求まらない場合の
        /// 最後の手段 (<see cref="GazeEyeBoneResolver.DeriveRestPose"/>)。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>rest 回転: 呼出時点の <see cref="Transform.localRotation"/>。</item>
        /// <item>yaw / pitch 軸: キャラクター root の上方向 / 右方向を目ボーンの親 local 空間へ変換したもの
        /// (root が回転していてもキャラクター基準の軸になる)。結果は正規化済み。</item>
        /// </list>
        /// </remarks>
        public static void DeriveRestAndAxes(
            Transform eye,
            Transform characterRoot,
            out Quaternion restRotation,
            out Vector3 yawAxisLocal,
            out Vector3 pitchAxisLocal)
        {
            restRotation = eye.localRotation;

            Vector3 up = characterRoot != null ? characterRoot.up : Vector3.up;
            Vector3 right = characterRoot != null ? characterRoot.right : Vector3.right;

            var parent = eye.parent;
            if (parent != null)
            {
                up = parent.InverseTransformDirection(up);
                right = parent.InverseTransformDirection(right);
            }

            yawAxisLocal = up.sqrMagnitude < 1e-8f ? Vector3.up : up.normalized;
            pitchAxisLocal = right.sqrMagnitude < 1e-8f ? Vector3.right : right.normalized;
        }
    }
}
