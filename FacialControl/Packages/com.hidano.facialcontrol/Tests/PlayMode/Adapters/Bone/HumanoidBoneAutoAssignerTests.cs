using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Bone;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Bone
{
    /// <summary>
    /// HumanoidBoneAutoAssigner のテスト（Red、PlayMode）。
    ///
    /// PlayMode 配置の理由: 実 <see cref="UnityEngine.Animator"/> +
    /// Humanoid <see cref="UnityEngine.Avatar"/> が必要。Avatar は
    /// <see cref="UnityEngine.AvatarBuilder.BuildHumanAvatar"/> でランタイム構築する。
    ///
    /// 検証項目:
    ///   - Humanoid Animator から <see cref="HumanBodyBones.LeftEye"/> /
    ///     <see cref="HumanBodyBones.RightEye"/> の bone 名を取得できる。
    ///   - <see cref="HumanBodyBones.Head"/> を優先し、Head 不在時に
    ///     <c>useNeckFallback=true</c> で <see cref="HumanBodyBones.Neck"/> を返す。
    ///   - 非 Humanoid Animator または該当スロット未マップの場合に empty 返却 +
    ///     <see cref="Debug.LogWarning"/> であり throw しない。
    ///   - オプトイン（明示呼出のみ、自動実行されない）であること。
    ///   - Adapters/Bone 配下に配置されていること。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class HumanoidBoneAutoAssignerTests : SizedTestFixture
    {
        private GameObject _rootGo;
        private Avatar _avatar;

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_rootGo);
                _rootGo = null;
            }
            if (_avatar != null)
            {
                UnityEngine.Object.DestroyImmediate(_avatar);
                _avatar = null;
            }
        }

        // ================================================================
        // ResolveEyeBoneNames: Humanoid + Eyes
        // ================================================================

        [Test]
        public void ResolveEyeBoneNames_HumanoidWithEyes_ReturnsLeftRightBoneNames()
        {
            // Eye マップ済 Humanoid から LeftEye / RightEye の bone 名が返ること。
            BuildHumanoid(includeEyes: true, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            var result = HumanoidBoneAutoAssigner.ResolveEyeBoneNames(animator);

            Assert.AreEqual("LeftEye", result.LeftEye, "LeftEye の bone 名が解決できること");
            Assert.AreEqual("RightEye", result.RightEye, "RightEye の bone 名が解決できること");
        }

        // ================================================================
        // ResolveEyeBoneNames: Humanoid だが Eye 未マップ
        // ================================================================

        [Test]
        public void ResolveEyeBoneNames_HumanoidWithoutEyes_ReturnsEmptyAndWarns()
        {
            // 該当スロット未マップは empty + Warning。
            BuildHumanoid(includeEyes: false, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            // LeftEye / RightEye いずれも未マップなので少なくとも 1 件 Warning。
            // dedupe を強制しない（実装側の選択）が、最低限の警告を期待する。
            LogAssert.Expect(LogType.Warning, new Regex("Eye"));

            HumanoidBoneAutoAssigner.EyeBoneNames result = default;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveEyeBoneNames(animator),
                "Eye 未マップでも throw しないこと");

            Assert.That(string.IsNullOrEmpty(result.LeftEye), Is.True,
                "Eye 未マップの LeftEye は empty を返すこと");
            Assert.That(string.IsNullOrEmpty(result.RightEye), Is.True,
                "Eye 未マップの RightEye は empty を返すこと");
        }

        // ================================================================
        // ResolveEyeBoneNames: 非 Humanoid
        // ================================================================

        [Test]
        public void ResolveEyeBoneNames_NonHumanoidAnimator_ReturnsEmptyAndWarns()
        {
            // 非 Humanoid Animator は empty + Warning。
            _rootGo = new GameObject("NonHumanoid");
            _rootGo.AddComponent<Animator>(); // avatar 未設定 = 非 Humanoid 扱い
            var animator = _rootGo.GetComponent<Animator>();

            LogAssert.Expect(LogType.Warning, new Regex("[Hh]umanoid|[Aa]vatar"));

            HumanoidBoneAutoAssigner.EyeBoneNames result = default;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveEyeBoneNames(animator),
                "非 Humanoid でも throw しないこと");

            Assert.That(string.IsNullOrEmpty(result.LeftEye), Is.True,
                "非 Humanoid の LeftEye は empty");
            Assert.That(string.IsNullOrEmpty(result.RightEye), Is.True,
                "非 Humanoid の RightEye は empty");
        }

        [Test]
        public void ResolveEyeBoneNames_NullAnimator_ReturnsEmptyAndWarnsWithoutThrow()
        {
            // null Animator でも throw しない（防御的契約）。
            LogAssert.ignoreFailingMessages = true;

            HumanoidBoneAutoAssigner.EyeBoneNames result = default;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveEyeBoneNames(null));

            Assert.That(string.IsNullOrEmpty(result.LeftEye), Is.True);
            Assert.That(string.IsNullOrEmpty(result.RightEye), Is.True);

            LogAssert.ignoreFailingMessages = false;
        }

        // ================================================================
        // ResolveBasisBoneName: Humanoid Head
        // ================================================================

        [Test]
        public void ResolveBasisBoneName_HumanoidWithHead_ReturnsHeadBoneName()
        {
            // Head 優先で bone 名を返す。
            BuildHumanoid(includeEyes: true, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            string result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(animator, useNeckFallback: true);

            Assert.AreEqual("Head", result, "Head が存在する場合は Head の bone 名を返すこと");
        }

        [Test]
        public void ResolveBasisBoneName_HumanoidWithHead_UseNeckFallbackFalse_ReturnsHeadBoneName()
        {
            // useNeckFallback=false でも Head が存在すれば Head を返す。
            BuildHumanoid(includeEyes: true, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            string result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(animator, useNeckFallback: false);

            Assert.AreEqual("Head", result, "Head 存在時は useNeckFallback の値に依らず Head を返すこと");
        }

        // ================================================================
        // ResolveBasisBoneName: Head 不在 → Neck フォールバック
        // ================================================================

        [Test]
        public void ResolveBasisBoneName_HeadMissing_UseNeckFallbackTrue_ReturnsNeckBoneName()
        {
            // Head Transform を破棄して GetBoneTransform(Head) が null になる状況を作る。
            // useNeckFallback=true の場合は Neck の bone 名を返す。
            BuildHumanoid(includeEyes: false, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            // Head Transform を破棄（Animator が Head を返せなくなる）。
            var headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            Assert.IsNotNull(headTransform, "セットアップ前提: Head Transform が存在すること");
            UnityEngine.Object.DestroyImmediate(headTransform.gameObject);

            string result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(animator, useNeckFallback: true);

            Assert.AreEqual("Neck", result,
                "Head 不在 + useNeckFallback=true は Neck の bone 名を返すこと");
        }

        [Test]
        public void ResolveBasisBoneName_HeadMissing_UseNeckFallbackFalse_ReturnsEmptyAndWarns()
        {
            // useNeckFallback=false の場合、Head 不在は empty + Warning。
            BuildHumanoid(includeEyes: false, includeNeck: true);
            var animator = _rootGo.GetComponent<Animator>();

            var headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            Assert.IsNotNull(headTransform);
            UnityEngine.Object.DestroyImmediate(headTransform.gameObject);

            LogAssert.Expect(LogType.Warning, new Regex("[Hh]ead"));

            string result = null;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(animator, useNeckFallback: false));

            Assert.That(string.IsNullOrEmpty(result), Is.True,
                "useNeckFallback=false で Head 不在のときは empty を返すこと");
        }

        // ================================================================
        // ResolveBasisBoneName: 非 Humanoid
        // ================================================================

        [Test]
        public void ResolveBasisBoneName_NonHumanoidAnimator_ReturnsEmptyAndWarns()
        {
            _rootGo = new GameObject("NonHumanoid");
            _rootGo.AddComponent<Animator>(); // avatar 未設定 = 非 Humanoid 扱い
            var animator = _rootGo.GetComponent<Animator>();

            LogAssert.Expect(LogType.Warning, new Regex("[Hh]umanoid|[Aa]vatar"));

            string result = null;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(animator),
                "非 Humanoid でも throw しないこと");

            Assert.That(string.IsNullOrEmpty(result), Is.True,
                "非 Humanoid は empty を返すこと");
        }

        [Test]
        public void ResolveBasisBoneName_NullAnimator_ReturnsEmptyWithoutThrow()
        {
            LogAssert.ignoreFailingMessages = true;

            string result = null;
            Assert.DoesNotThrow(() => result = HumanoidBoneAutoAssigner.ResolveBasisBoneName(null));

            Assert.That(string.IsNullOrEmpty(result), Is.True);

            LogAssert.ignoreFailingMessages = false;
        }

        // ================================================================
        // オプトイン契約: 静的クラス + 明示呼出のみ
        // ================================================================

        [Test]
        public void HumanoidBoneAutoAssigner_IsStaticClassUnderAdaptersBoneNamespace()
        {
            // オプトイン契約と Adapters/Bone 配下配置の構造的保証。
            // 静的クラスとして定義されており、明示呼出のみで動作することを型レベルで担保する。
            var type = typeof(HumanoidBoneAutoAssigner);

            Assert.IsTrue(type.IsAbstract && type.IsSealed,
                "HumanoidBoneAutoAssigner は static class（abstract+sealed）であること（オプトイン）");
            Assert.AreEqual("Hidano.FacialControl.Adapters.Bone", type.Namespace,
                "Adapters.Bone 名前空間に配置されていること");
        }

        // ================================================================
        // ヘルパー: ランタイムで最小 Humanoid Avatar を構築
        // ================================================================

        /// <summary>
        /// AvatarBuilder.BuildHumanAvatar で最小構成 Humanoid を生成し、Animator を付与する。
        /// 必須ボーン（Hips, Spine, 四肢, Head）に加え、オプションで Neck と Eyes を含める。
        /// </summary>
        private void BuildHumanoid(bool includeEyes, bool includeNeck)
        {
            _rootGo = new GameObject("HumanoidRoot");
            _rootGo.transform.position = Vector3.zero;

            // 階層構築（rest pose は単純な T-pose）
            var hips = MakeBone("Hips", _rootGo.transform, new Vector3(0f, 1.0f, 0f));
            var spine = MakeBone("Spine", hips, new Vector3(0f, 0.2f, 0f));
            var neck = includeNeck ? MakeBone("Neck", spine, new Vector3(0f, 0.4f, 0f)) : spine;
            var head = MakeBone("Head", neck, new Vector3(0f, 0.2f, 0f));
            if (includeEyes)
            {
                MakeBone("LeftEye", head, new Vector3(-0.04f, 0.1f, 0.08f));
                MakeBone("RightEye", head, new Vector3(0.04f, 0.1f, 0.08f));
            }

            var lUpperArm = MakeBone("LeftUpperArm", spine, new Vector3(-0.2f, 0.4f, 0f));
            var lLowerArm = MakeBone("LeftLowerArm", lUpperArm, new Vector3(-0.25f, 0f, 0f));
            MakeBone("LeftHand", lLowerArm, new Vector3(-0.25f, 0f, 0f));

            var rUpperArm = MakeBone("RightUpperArm", spine, new Vector3(0.2f, 0.4f, 0f));
            var rLowerArm = MakeBone("RightLowerArm", rUpperArm, new Vector3(0.25f, 0f, 0f));
            MakeBone("RightHand", rLowerArm, new Vector3(0.25f, 0f, 0f));

            var lUpperLeg = MakeBone("LeftUpperLeg", hips, new Vector3(-0.1f, -0.05f, 0f));
            var lLowerLeg = MakeBone("LeftLowerLeg", lUpperLeg, new Vector3(0f, -0.4f, 0f));
            MakeBone("LeftFoot", lLowerLeg, new Vector3(0f, -0.4f, 0f));

            var rUpperLeg = MakeBone("RightUpperLeg", hips, new Vector3(0.1f, -0.05f, 0f));
            var rLowerLeg = MakeBone("RightLowerLeg", rUpperLeg, new Vector3(0f, -0.4f, 0f));
            MakeBone("RightFoot", rLowerLeg, new Vector3(0f, -0.4f, 0f));

            // HumanBone[] の構築
            var humanBones = new List<HumanBone>
            {
                MakeHumanBone(HumanBodyBones.Hips, "Hips"),
                MakeHumanBone(HumanBodyBones.Spine, "Spine"),
                MakeHumanBone(HumanBodyBones.Head, "Head"),
                MakeHumanBone(HumanBodyBones.LeftUpperArm, "LeftUpperArm"),
                MakeHumanBone(HumanBodyBones.LeftLowerArm, "LeftLowerArm"),
                MakeHumanBone(HumanBodyBones.LeftHand, "LeftHand"),
                MakeHumanBone(HumanBodyBones.RightUpperArm, "RightUpperArm"),
                MakeHumanBone(HumanBodyBones.RightLowerArm, "RightLowerArm"),
                MakeHumanBone(HumanBodyBones.RightHand, "RightHand"),
                MakeHumanBone(HumanBodyBones.LeftUpperLeg, "LeftUpperLeg"),
                MakeHumanBone(HumanBodyBones.LeftLowerLeg, "LeftLowerLeg"),
                MakeHumanBone(HumanBodyBones.LeftFoot, "LeftFoot"),
                MakeHumanBone(HumanBodyBones.RightUpperLeg, "RightUpperLeg"),
                MakeHumanBone(HumanBodyBones.RightLowerLeg, "RightLowerLeg"),
                MakeHumanBone(HumanBodyBones.RightFoot, "RightFoot"),
            };
            if (includeNeck)
            {
                humanBones.Add(MakeHumanBone(HumanBodyBones.Neck, "Neck"));
            }
            if (includeEyes)
            {
                humanBones.Add(MakeHumanBone(HumanBodyBones.LeftEye, "LeftEye"));
                humanBones.Add(MakeHumanBone(HumanBodyBones.RightEye, "RightEye"));
            }

            // SkeletonBone[] は配下全 Transform から構築
            var skeletonBones = new List<SkeletonBone>();
            CollectSkeletonBones(_rootGo.transform, skeletonBones);

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeletonBones.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0.0f,
                hasTranslationDoF = false,
            };

            _avatar = AvatarBuilder.BuildHumanAvatar(_rootGo, description);
            Assert.IsTrue(_avatar.isValid,
                "セットアップ前提: AvatarBuilder.BuildHumanAvatar が valid な Avatar を返すこと");
            _avatar.name = "TestHumanoidAvatar";

            var animator = _rootGo.AddComponent<Animator>();
            animator.avatar = _avatar;
        }

        private static Transform MakeBone(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static HumanBone MakeHumanBone(HumanBodyBones bodyBone, string transformName)
        {
            return new HumanBone
            {
                humanName = HumanTrait.BoneName[(int)bodyBone],
                boneName = transformName,
                limit = new HumanLimit { useDefaultValues = true },
            };
        }

        private static void CollectSkeletonBones(Transform t, List<SkeletonBone> output)
        {
            output.Add(new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            });
            int count = t.childCount;
            for (int i = 0; i < count; i++)
            {
                CollectSkeletonBones(t.GetChild(i), output);
            }
        }
    }
}
