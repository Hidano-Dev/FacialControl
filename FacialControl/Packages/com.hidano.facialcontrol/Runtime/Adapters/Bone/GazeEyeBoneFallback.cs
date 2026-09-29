using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 目線ボーン path が未指定の <see cref="ScriptableObject.GazeChannel"/> に使う、
    /// Humanoid Avatar から解決した左右目ボーンと、その rest 回転・yaw / pitch 軸。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transform の解決と rest 回転・軸の導出は構築時に 1 回だけ行う。
    /// <see cref="GazeBonePoseProvider"/> は gaze 入力源の登録変化のたびに再構築されるため、
    /// 呼出側 (FacialController) は初期化時に作った値を使い回し、頭部が動いている最中の姿勢から
    /// 軸を取り直さないようにする。
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

        /// <summary>
        /// 左右目ボーンから fallback を作る。rest 回転と軸はこの時点の姿勢から導出する
        /// (<see cref="DeriveRestAndAxes"/>)。
        /// </summary>
        public GazeEyeBoneFallback(Transform leftEye, Transform rightEye, Transform characterRoot)
        {
            Left = new FallbackEye(leftEye, characterRoot);
            Right = new FallbackEye(rightEye, characterRoot);
        }

        /// <summary>
        /// Humanoid Animator から <see cref="HumanBodyBones.LeftEye"/> / <see cref="HumanBodyBones.RightEye"/> を解決する。
        /// null / 非 Humanoid の場合は目ボーンが両方 null の値を返す (throw しない)。
        /// </summary>
        public static GazeEyeBoneFallback FromAnimator(Animator animator)
        {
            if (animator == null)
            {
                return default;
            }

            var avatar = animator.avatar;
            if (avatar == null || !avatar.isHuman)
            {
                return default;
            }

            return new GazeEyeBoneFallback(
                animator.GetBoneTransform(HumanBodyBones.LeftEye),
                animator.GetBoneTransform(HumanBodyBones.RightEye),
                animator.transform);
        }

        /// <summary>
        /// 実行時に解決した目ボーンから、<see cref="ScriptableObject.GazeChannel"/> がエディタで保存する
        /// InitialRotation / YawAxisLocal / PitchAxisLocal 相当の値を導出する。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>rest 回転: 呼出時点の <see cref="Transform.localRotation"/>
        /// (エディタは参照モデルの <c>localEulerAngles</c> を保存している)。</item>
        /// <item>yaw / pitch 軸: キャラクター root の上方向 / 右方向を目ボーンの親 local 空間へ変換したもの
        /// (エディタは参照モデル配置時の world 上方向 / 右方向を親 local 空間へ変換している。
        /// root が無回転ならエディタの値と一致し、root が回転していてもキャラクター基準の軸になる)。
        /// 結果は正規化済み。</item>
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
