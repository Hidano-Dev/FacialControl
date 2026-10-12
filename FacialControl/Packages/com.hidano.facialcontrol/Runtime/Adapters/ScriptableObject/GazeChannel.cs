using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>Gaze セクションで定義する 1 系統のチャネル。</summary>
    /// <remarks>
    /// 目線メニューで設定するのは可動範囲と、任意の左右ジョイント名だけ (HID-191)。
    /// <see cref="providerSlug"/> / <see cref="useDistinctLeftRight"/> / source id / 初期回転 / yaw・pitch 軸は
    /// 旧データの互換のために残しているだけで、実行時には読まない。初期回転と軸は実行時に元のアセットデータから求める
    /// (<see cref="Bone.GazeEyeBoneResolver"/>)。
    /// </remarks>
    [Serializable]
    public sealed class GazeChannel
    {
        public string id = string.Empty;
        /// <summary>旧データの互換用。実行時には読まない (どの binding の目線入力にもこのチャネルを使う)。</summary>
        public string providerSlug = string.Empty;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public bool useDistinctLeftRight;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public string sourceIdLeft = string.Empty;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public string sourceIdRight = string.Empty;

        /// <summary>
        /// 左目のジョイント名 (任意)。空の場合は実行時に Avatar の目線ボーン (Humanoid の <c>LeftEye</c>) を使う。
        /// 名前で GameObject を探し、完全一致を優先する (無ければ名前に含む最初のものを警告付きで使う)。
        /// 旧データの <c>Armature/.../LeftEye</c> 形式の path は末尾のセグメントを名前として扱う。
        /// </summary>
        public string leftEyeBonePath = string.Empty;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public Vector3 leftEyeInitialRotation;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public Vector3 leftEyeYawAxisLocal = Vector3.up;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public Vector3 leftEyePitchAxisLocal = Vector3.right;
        /// <summary>右目のジョイント名 (任意)。扱いは <see cref="leftEyeBonePath"/> と同じ (空なら Humanoid の <c>RightEye</c>)。</summary>
        public string rightEyeBonePath = string.Empty;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public Vector3 rightEyeInitialRotation;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
        public Vector3 rightEyeYawAxisLocal = Vector3.up;
        /// <summary>旧データの互換用。実行時には読まない。</summary>
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
