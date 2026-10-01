using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>Gaze セクションで定義する 1 系統のチャネル。</summary>
    [Serializable]
    public sealed class GazeChannel
    {
        public string id = string.Empty;
        public string providerSlug = string.Empty;
        public bool useDistinctLeftRight;
        public string sourceIdLeft = string.Empty;
        public string sourceIdRight = string.Empty;

        /// <summary>
        /// 左目ボーンの相対 path (任意)。空の場合は実行時に Humanoid Avatar の <c>LeftEye</c> を使い、
        /// <see cref="leftEyeInitialRotation"/> / yaw・pitch 軸は使わずにその場で導出する。
        /// </summary>
        public string leftEyeBonePath = string.Empty;
        public Vector3 leftEyeInitialRotation;
        public Vector3 leftEyeYawAxisLocal = Vector3.up;
        public Vector3 leftEyePitchAxisLocal = Vector3.right;
        /// <summary>右目ボーンの相対 path (任意)。空の場合の扱いは <see cref="leftEyeBonePath"/> と同じ (Humanoid の <c>RightEye</c>)。</summary>
        public string rightEyeBonePath = string.Empty;
        public Vector3 rightEyeInitialRotation;
        public Vector3 rightEyeYawAxisLocal = Vector3.up;
        public Vector3 rightEyePitchAxisLocal = Vector3.right;

        [Range(0f, 90f)] public float lookUpAngle = 15f;
        [Range(0f, 90f)] public float lookDownAngle = 9f;
        [Range(0f, 90f)] public float outerYawAngle = 15f;
        [Range(0f, 90f)] public float innerYawAngle = 18f;

        /// <summary>全フィールドを複製した新しいインスタンスを返す (浅いコピー。フィールドはすべて値型か不変の string)。</summary>
        public GazeChannel Clone()
        {
            return (GazeChannel)MemberwiseClone();
        }
    }
}
