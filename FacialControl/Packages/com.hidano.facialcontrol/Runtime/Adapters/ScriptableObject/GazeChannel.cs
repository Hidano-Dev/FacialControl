using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>Gaze セクションで定義する 1 系統のチャネル。</summary>
    /// <remarks>
    /// 目線メニューで設定するのは可動範囲と、任意の左右ジョイント名だけ (HID-191)。
    /// 初期回転と yaw・pitch 軸は実行時に元のアセットデータから求める (<see cref="Bone.GazeEyeBoneResolver"/>)。
    /// 旧データの <c>providerSlug</c> / <c>useDistinctLeftRight</c> / <c>sourceIdLeft</c> / <c>sourceIdRight</c> /
    /// 左右の初期回転 / yaw・pitch 軸は削除した (HID-196)。旧アセット / JSON に残っていても読み飛ばす。
    /// </remarks>
    [Serializable]
    public sealed class GazeChannel
    {
        public string id = string.Empty;

        /// <summary>
        /// 左目のジョイント名 (任意)。空の場合は実行時に Avatar の目線ボーン (Humanoid の <c>LeftEye</c>) を使う。
        /// 名前で GameObject を探し、完全一致を優先する (無ければ名前に含む最初のものを警告付きで使う)。
        /// 旧データの <c>Armature/.../LeftEye</c> 形式の path は末尾のセグメントを名前として扱う。
        /// </summary>
        public string leftEyeBonePath = string.Empty;
        /// <summary>右目のジョイント名 (任意)。扱いは <see cref="leftEyeBonePath"/> と同じ (空なら Humanoid の <c>RightEye</c>)。</summary>
        public string rightEyeBonePath = string.Empty;

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
