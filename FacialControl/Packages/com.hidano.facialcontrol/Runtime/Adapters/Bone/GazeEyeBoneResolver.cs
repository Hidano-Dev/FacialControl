using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 目線メニューのジョイント名 (任意) から目線ボーンを解決し、その rest 回転・yaw / pitch 軸を
    /// 元のアセットデータから求める。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 目線ボーンの解決規則 (HID-191):
    /// <list type="bullet">
    /// <item>ジョイント名が空なら、Avatar に登録された目線ボーン (Humanoid の LeftEye / RightEye) を使う。</item>
    /// <item>ジョイント名が <c>/</c> を含むパスなら、末尾のセグメントを名前として扱う (旧データの path の移行)。</item>
    /// <item>キャラクター root 配下 (root 自身を含む、非アクティブを含む、深さ優先の前順) から、名前が完全一致する
    /// 最初の Transform を使う。</item>
    /// <item>完全一致が無ければ、名前に入力を含む最初の Transform を使い、警告を 1 回出す。</item>
    /// <item>部分一致も無ければ警告を 1 回出し、Avatar の目線ボーンに戻す。</item>
    /// </list>
    /// </para>
    /// <para>
    /// rest 回転と軸は実行時の姿勢に依存させない。起動直後に頭部が正面を向いているとは限らず、
    /// 目線の入力が既に掛かっていることもあるため、次の順に元のアセットデータから求める
    /// (<see cref="DeriveRestPose"/>)。
    /// <list type="number">
    /// <item>Avatar の <see cref="HumanDescription.skeleton"/> (Avatar 作成時の姿勢)。</item>
    /// <item>root 配下の <see cref="SkinnedMeshRenderer"/> のバインドポーズ。</item>
    /// <item>どちらからも求まらない場合に限り、構築時点の姿勢 (<see cref="GazeEyeBoneFallback.DeriveRestAndAxes"/>)。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 解決結果と rest 回転・軸はキャッシュする。構築と解決は初期化時 (目線 provider の構築時) に行い、
    /// 毎フレームは呼ばない。
    /// </para>
    /// </remarks>
    public sealed class GazeEyeBoneResolver
    {
        private const string LogPrefix = "[FacialControl] ";

        private readonly Transform _characterRoot;
        private readonly Avatar _avatar;
        private readonly Transform _avatarLeftEye;
        private readonly Transform _avatarRightEye;
        private readonly Dictionary<string, Transform> _jointCache = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<Transform, GazeEyeBoneFallback.FallbackEye> _poseCache =
            new Dictionary<Transform, GazeEyeBoneFallback.FallbackEye>();

        private Dictionary<string, Quaternion> _skeletonRotations;
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;
        private bool _hasFallback;
        private GazeEyeBoneFallback _fallback;

        /// <param name="characterRoot">ジョイント名を探す範囲の root で、軸の基準 (上方向 / 右方向) になる Transform (通常は Animator)。</param>
        /// <param name="avatar">rest 回転・軸を求める Avatar。null 可。</param>
        /// <param name="avatarLeftEye">ジョイント名が空の左目に使う、Avatar の目線ボーン。null 可。</param>
        /// <param name="avatarRightEye">ジョイント名が空の右目に使う、Avatar の目線ボーン。null 可。</param>
        public GazeEyeBoneResolver(Transform characterRoot, Avatar avatar, Transform avatarLeftEye, Transform avatarRightEye)
        {
            _characterRoot = characterRoot;
            _avatar = avatar;
            _avatarLeftEye = avatarLeftEye;
            _avatarRightEye = avatarRightEye;
        }

        /// <summary>
        /// Animator から作る。Humanoid の Avatar なら LeftEye / RightEye を Avatar の目線ボーンにする。
        /// null の場合は目線ボーンを 1 つも解決できない resolver を返す (throw しない)。
        /// </summary>
        public static GazeEyeBoneResolver FromAnimator(Animator animator)
        {
            if (animator == null)
            {
                return new GazeEyeBoneResolver(null, null, null, null);
            }

            Avatar avatar = animator.avatar;
            bool isHuman = avatar != null && avatar.isHuman;
            return new GazeEyeBoneResolver(
                animator.transform,
                avatar,
                isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null,
                isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null);
        }

        /// <summary>Avatar の目線ボーンと、その rest 回転・軸。</summary>
        public GazeEyeBoneFallback Fallback
        {
            get
            {
                if (!_hasFallback)
                {
                    _fallback = new GazeEyeBoneFallback(GetPose(_avatarLeftEye), GetPose(_avatarRightEye));
                    _hasFallback = true;
                }

                return _fallback;
            }
        }

        /// <summary>
        /// 1 つの目の目線ボーンを解決する。ジョイント名で見つからなければ Avatar の目線ボーンを返す。
        /// それも無ければ <see cref="GazeEyeBoneFallback.FallbackEye.Bone"/> が null の値を返す。
        /// </summary>
        public GazeEyeBoneFallback.FallbackEye Resolve(string jointName, bool isLeftEye)
        {
            string name = ToJointName(jointName);
            if (name.Length > 0)
            {
                Transform joint = FindJointCached(name);
                if (joint != null)
                {
                    return GetPose(joint);
                }
            }

            return isLeftEye ? Fallback.Left : Fallback.Right;
        }

        /// <summary>
        /// 目線メニューに保存された値をジョイント名にする。前後の空白を除き、<c>/</c> を含むパスなら末尾のセグメントを返す。
        /// null・空白は空文字列。
        /// </summary>
        public static string ToJointName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            int separator = trimmed.LastIndexOf('/');
            return separator >= 0 ? trimmed.Substring(separator + 1).Trim() : trimmed;
        }

        /// <summary>
        /// <paramref name="root"/> 配下 (root 自身を含む、深さ優先の前順) から <paramref name="jointName"/> の Transform を探す。
        /// 完全一致を優先し、無ければ名前に <paramref name="jointName"/> を含む最初の Transform を返す
        /// (<paramref name="isExactMatch"/> が false)。どちらも無ければ null。警告は出さない。
        /// </summary>
        public static Transform FindJoint(Transform root, string jointName, out bool isExactMatch)
        {
            isExactMatch = false;
            if (root == null || string.IsNullOrEmpty(jointName))
            {
                return null;
            }

            Transform partial = null;
            Transform exact = FindJointRecursive(root, jointName, ref partial);
            if (exact != null)
            {
                isExactMatch = true;
                return exact;
            }

            return partial;
        }

        private static Transform FindJointRecursive(Transform current, string jointName, ref Transform partial)
        {
            string currentName = current.name;
            if (string.Equals(currentName, jointName, StringComparison.Ordinal))
            {
                return current;
            }

            if (partial == null && currentName != null && currentName.IndexOf(jointName, StringComparison.Ordinal) >= 0)
            {
                partial = current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindJointRecursive(current.GetChild(i), jointName, ref partial);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private Transform FindJointCached(string name)
        {
            if (_jointCache.TryGetValue(name, out Transform cached))
            {
                return cached;
            }

            Transform joint = FindJoint(_characterRoot, name, out bool isExactMatch);
            if (joint == null)
            {
                Debug.LogWarning(
                    $"{LogPrefix}目線のジョイント名 '{name}' に一致するボーンが見つかりません。Avatar の目線ボーン (Humanoid の LeftEye / RightEye) があればそれを使います。");
            }
            else if (!isExactMatch)
            {
                Debug.LogWarning(
                    $"{LogPrefix}目線のジョイント名 '{name}' に完全一致するボーンが無いため、名前に含む '{joint.name}' を使います。");
            }

            _jointCache[name] = joint;
            return joint;
        }

        private GazeEyeBoneFallback.FallbackEye GetPose(Transform eye)
        {
            if (eye == null)
            {
                return default;
            }

            if (_poseCache.TryGetValue(eye, out GazeEyeBoneFallback.FallbackEye pose))
            {
                return pose;
            }

            DeriveRestPose(eye, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);
            pose = new GazeEyeBoneFallback.FallbackEye(eye, rest, yaw, pitch);
            _poseCache[eye] = pose;
            return pose;
        }

        /// <summary>
        /// <paramref name="eye"/> の rest 回転と yaw / pitch 軸 (親 local 空間、キャラクター root の上方向 / 右方向) を
        /// 元のアセットデータから求める。求めた元を返す。
        /// </summary>
        public GazeEyeRestPoseSource DeriveRestPose(Transform eye, out Quaternion restRotation, out Vector3 yawAxisLocal, out Vector3 pitchAxisLocal)
        {
            if (eye == null)
            {
                throw new ArgumentNullException(nameof(eye));
            }

            if (TryDeriveFromSkeleton(eye, out restRotation, out Quaternion parentInRoot))
            {
                ToAxes(parentInRoot, out yawAxisLocal, out pitchAxisLocal);
                return GazeEyeRestPoseSource.AvatarSkeleton;
            }

            if (TryDeriveFromBindPoses(eye, out restRotation, out parentInRoot))
            {
                ToAxes(parentInRoot, out yawAxisLocal, out pitchAxisLocal);
                return GazeEyeRestPoseSource.BindPose;
            }

            GazeEyeBoneFallback.DeriveRestAndAxes(eye, _characterRoot, out restRotation, out yawAxisLocal, out pitchAxisLocal);
            return GazeEyeRestPoseSource.CurrentPose;
        }

        /// <summary>
        /// Avatar の skeleton (Transform 名 → Avatar 作成時の localRotation) から、目の rest 回転と、
        /// 親のキャラクター root に対する回転を求める。目から root までのいずれかが skeleton に無ければ false。
        /// </summary>
        private bool TryDeriveFromSkeleton(Transform eye, out Quaternion restRotation, out Quaternion parentInRoot)
        {
            restRotation = Quaternion.identity;
            parentInRoot = Quaternion.identity;
            Dictionary<string, Quaternion> rotations = GetSkeletonRotations();
            if (rotations == null || !rotations.TryGetValue(eye.name, out restRotation))
            {
                restRotation = Quaternion.identity;
                return false;
            }

            Transform current = eye.parent;
            while (current != null && current != _characterRoot)
            {
                if (!rotations.TryGetValue(current.name, out Quaternion local))
                {
                    return false;
                }

                parentInRoot = local * parentInRoot;
                current = current.parent;
            }

            // root 配下に無い目は root を基準にできない。
            return _characterRoot == null || current == _characterRoot;
        }

        private Dictionary<string, Quaternion> GetSkeletonRotations()
        {
            if (_skeletonRotations != null)
            {
                return _skeletonRotations.Count > 0 ? _skeletonRotations : null;
            }

            _skeletonRotations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            if (_avatar == null || !_avatar.isValid)
            {
                return null;
            }

            SkeletonBone[] skeleton = _avatar.humanDescription.skeleton;
            for (int i = 0; skeleton != null && i < skeleton.Length; i++)
            {
                string boneName = skeleton[i].name;
                if (!string.IsNullOrEmpty(boneName) && !_skeletonRotations.ContainsKey(boneName))
                {
                    _skeletonRotations.Add(boneName, skeleton[i].rotation);
                }
            }

            return _skeletonRotations.Count > 0 ? _skeletonRotations : null;
        }

        /// <summary>
        /// 目と親の両方をボーンに持つ <see cref="SkinnedMeshRenderer"/> のバインドポーズから、目の rest 回転と、
        /// 親のキャラクター root に対する回転を求める。該当するメッシュが無ければ false。
        /// </summary>
        /// <remarks>
        /// バインドポーズはメッシュ空間からボーン空間への変換なので、その逆行列の回転がバインド時のメッシュ空間での
        /// ボーンの回転になる。メッシュ (SkinnedMeshRenderer の Transform) の root に対する回転は、通常アニメーション
        /// しないため構築時点の値を使う。
        /// </remarks>
        private bool TryDeriveFromBindPoses(Transform eye, out Quaternion restRotation, out Quaternion parentInRoot)
        {
            restRotation = Quaternion.identity;
            parentInRoot = Quaternion.identity;
            Transform parent = eye.parent;
            if (parent == null || _characterRoot == null)
            {
                return false;
            }

            if (_skinnedMeshRenderers == null)
            {
                _skinnedMeshRenderers = _characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            for (int i = 0; i < _skinnedMeshRenderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = _skinnedMeshRenderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                Transform[] bones = renderer.bones;
                int eyeIndex = Array.IndexOf(bones, eye);
                int parentIndex = Array.IndexOf(bones, parent);
                if (eyeIndex < 0 || parentIndex < 0)
                {
                    continue;
                }

                Matrix4x4[] bindPoses = renderer.sharedMesh.bindposes;
                if (eyeIndex >= bindPoses.Length || parentIndex >= bindPoses.Length)
                {
                    continue;
                }

                Quaternion eyeInMesh = bindPoses[eyeIndex].inverse.rotation;
                Quaternion parentInMesh = bindPoses[parentIndex].inverse.rotation;
                Quaternion meshInRoot = Quaternion.Inverse(_characterRoot.rotation) * renderer.transform.rotation;
                restRotation = Quaternion.Inverse(parentInMesh) * eyeInMesh;
                parentInRoot = meshInRoot * parentInMesh;
                return true;
            }

            return false;
        }

        private static void ToAxes(Quaternion parentInRoot, out Vector3 yawAxisLocal, out Vector3 pitchAxisLocal)
        {
            Quaternion inverse = Quaternion.Inverse(parentInRoot);
            Vector3 up = inverse * Vector3.up;
            Vector3 right = inverse * Vector3.right;
            yawAxisLocal = up.sqrMagnitude < 1e-8f ? Vector3.up : up.normalized;
            pitchAxisLocal = right.sqrMagnitude < 1e-8f ? Vector3.right : right.normalized;
        }
    }

    /// <summary><see cref="GazeEyeBoneResolver.DeriveRestPose"/> が rest 回転・軸を求めた元。</summary>
    public enum GazeEyeRestPoseSource
    {
        /// <summary>Avatar の <see cref="HumanDescription.skeleton"/>。</summary>
        AvatarSkeleton,

        /// <summary><see cref="SkinnedMeshRenderer"/> のバインドポーズ。</summary>
        BindPose,

        /// <summary>アセットデータから求まらず、構築時点の姿勢を使った。</summary>
        CurrentPose,
    }
}
