using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// Expression サムネイル用カメラの構図計算。Unity のシーン状態に依存しない純粋な計算のみを持つ。
    /// <para>
    /// 顔ジョイント（Humanoid の Head ボーン等）があればその少し上を注視点にして顔へ寄せる。
    /// 見つからなければモデル全体の bounds を収める。
    /// </para>
    /// </summary>
    public static class ExpressionThumbnailFraming
    {
        /// <summary>サムネイルカメラの FoV（度）。</summary>
        public const float FieldOfView = 20f;

        /// <summary>
        /// 顔の半径をモデルの高さから推定する比率。身長 1.6m で半径 約 0.14m（頭部がほぼ収まる大きさ）。
        /// </summary>
        public const float FaceRadiusPerModelHeight = 0.09f;

        /// <summary>推定した顔の半径の下限（m）。極端に小さいモデルでもニアクリップに潰されないようにする。</summary>
        public const float MinFaceRadius = 0.02f;

        /// <summary>
        /// 注視点を顔ジョイントから上へずらす量（顔の半径に対する比率）。
        /// Head ボーンは首の付け根寄りにあるため、顔の中心へ寄せる。
        /// </summary>
        public const float FaceCenterOffsetRatio = 0.35f;

        /// <summary>被写体の外接球に対する余白の倍率。</summary>
        public const float Margin = 1.1f;

        /// <summary>
        /// 構図を計算する。
        /// </summary>
        /// <param name="modelBounds">モデル全体のワールド bounds</param>
        /// <param name="hasFaceJoint">顔ジョイントが見つかったか</param>
        /// <param name="faceJointPosition">顔ジョイントのワールド位置（<paramref name="hasFaceJoint"/> が false なら無視）</param>
        /// <param name="modelForward">モデルの正面方向（ワールド）。カメラはこの方向からモデルを向く</param>
        /// <param name="modelUp">モデルの上方向（ワールド）</param>
        public static ExpressionThumbnailCameraPose Compute(
            Bounds modelBounds,
            bool hasFaceJoint,
            Vector3 faceJointPosition,
            Vector3 modelForward,
            Vector3 modelUp)
        {
            var up = modelUp.sqrMagnitude > 1e-8f ? modelUp.normalized : Vector3.up;
            var forward = modelForward.sqrMagnitude > 1e-8f ? modelForward.normalized : Vector3.forward;

            // forward が up と平行な不正入力でも LookRotation が破綻しないよう、up を直交化する。
            var orthoUp = Vector3.ProjectOnPlane(up, forward);
            if (orthoUp.sqrMagnitude < 1e-8f)
            {
                orthoUp = Vector3.ProjectOnPlane(Vector3.up, forward);
                if (orthoUp.sqrMagnitude < 1e-8f)
                    orthoUp = Vector3.ProjectOnPlane(Vector3.forward, forward);
            }
            orthoUp.Normalize();

            Vector3 target;
            float radius;
            if (hasFaceJoint)
            {
                radius = EstimateFaceRadius(modelBounds);
                target = faceJointPosition + orthoUp * (radius * FaceCenterOffsetRatio);
            }
            else
            {
                radius = Mathf.Max(modelBounds.extents.magnitude, MinFaceRadius);
                target = modelBounds.center;
            }

            float distance = ComputeDistanceToFitSphere(radius * Margin, FieldOfView);
            var position = target + forward * distance;
            var rotation = Quaternion.LookRotation(-forward, orthoUp);

            // 被写体の手前側を削らない範囲でニアクリップを大きめに取り、深度精度を確保する。
            float nearClip = Mathf.Max(distance - radius * Margin * 2f, distance * 0.05f);
            float farClip = distance + Mathf.Max(modelBounds.extents.magnitude, radius) * 2f + 1f;

            return new ExpressionThumbnailCameraPose(position, rotation, target, FieldOfView, nearClip, farClip);
        }

        /// <summary>
        /// モデルの高さから顔の半径を推定する。
        /// </summary>
        public static float EstimateFaceRadius(Bounds modelBounds)
        {
            return Mathf.Max(modelBounds.size.y * FaceRadiusPerModelHeight, MinFaceRadius);
        }

        /// <summary>
        /// 半径 <paramref name="radius"/> の球が、縦 FoV <paramref name="fieldOfViewDegrees"/> の
        /// 正方形ビューにちょうど収まるカメラ距離を返す。
        /// </summary>
        public static float ComputeDistanceToFitSphere(float radius, float fieldOfViewDegrees)
        {
            float halfFovRad = Mathf.Clamp(fieldOfViewDegrees, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
            return radius / Mathf.Sin(halfFovRad);
        }
    }

    /// <summary>
    /// <see cref="ExpressionThumbnailFraming.Compute"/> の結果。
    /// </summary>
    public readonly struct ExpressionThumbnailCameraPose
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Target { get; }
        public float FieldOfView { get; }
        public float NearClip { get; }
        public float FarClip { get; }

        public ExpressionThumbnailCameraPose(
            Vector3 position, Quaternion rotation, Vector3 target,
            float fieldOfView, float nearClip, float farClip)
        {
            Position = position;
            Rotation = rotation;
            Target = target;
            FieldOfView = fieldOfView;
            NearClip = nearClip;
            FarClip = farClip;
        }
    }
}
