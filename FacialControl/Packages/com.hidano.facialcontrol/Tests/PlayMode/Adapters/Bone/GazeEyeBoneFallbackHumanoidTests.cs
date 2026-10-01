using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Bone
{
    /// <summary>
    /// 目ボーン path 未指定の GazeChannel が Humanoid Avatar の LeftEye / RightEye を駆動することを、
    /// 実 Humanoid Avatar (<see cref="AvatarBuilder.BuildHumanAvatar"/>) と <see cref="FacialController"/> で検証する。
    /// </summary>
    /// <remarks>
    /// PlayMode 配置の理由: 実 Humanoid Avatar と <see cref="FacialController"/> の LateUpdate が必要。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class GazeEyeBoneFallbackHumanoidTests : SizedTestFixture
    {
        private const string UnresolvedWarningFragment = "目線ボーン path が未指定";

        private GameObject _rootGo;
        private Avatar _avatar;
        private Mesh _mesh;
        private GazeProfileSO _profileAsset;
        private int _unresolvedWarningCount;

        [SetUp]
        public void SetUp()
        {
            _unresolvedWarningCount = 0;
            UnityEngine.Application.logMessageReceived += OnLogMessageReceived;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Application.logMessageReceived -= OnLogMessageReceived;
            if (_rootGo != null) UnityEngine.Object.DestroyImmediate(_rootGo);
            if (_profileAsset != null) UnityEngine.Object.DestroyImmediate(_profileAsset);
            if (_avatar != null) UnityEngine.Object.DestroyImmediate(_avatar);
            if (_mesh != null) UnityEngine.Object.DestroyImmediate(_mesh);
            _rootGo = null;
            _profileAsset = null;
            _avatar = null;
            _mesh = null;
        }

        [Test]
        public void FromAnimator_HumanoidWithEyes_ReturnsEyeTransformsWithCharacterRelativeAxes()
        {
            BuildHumanoid(includeEyes: true);
            _rootGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var animator = _rootGo.GetComponent<Animator>();

            var fallback = GazeEyeBoneFallback.FromAnimator(animator);

            Assert.That(fallback.LeftEye, Is.SameAs(animator.GetBoneTransform(HumanBodyBones.LeftEye)));
            Assert.That(fallback.RightEye, Is.SameAs(animator.GetBoneTransform(HumanBodyBones.RightEye)));
            // テスト用 Humanoid は全ボーン無回転なので、root を回しても軸はキャラクター基準の up / right。
            Assert.That((fallback.Left.YawAxisLocal - Vector3.up).magnitude, Is.LessThan(1e-4f));
            Assert.That((fallback.Left.PitchAxisLocal - Vector3.right).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void FromAnimator_HumanoidWithoutEyes_ReturnsNullEyes()
        {
            BuildHumanoid(includeEyes: false);

            var fallback = GazeEyeBoneFallback.FromAnimator(_rootGo.GetComponent<Animator>());

            Assert.That(fallback.LeftEye == null, Is.True);
            Assert.That(fallback.RightEye == null, Is.True);
        }

        [Test]
        public void FromAnimator_NonHumanoidOrNull_ReturnsNullEyesWithoutThrowing()
        {
            _rootGo = new GameObject("NonHumanoid");
            var animator = _rootGo.AddComponent<Animator>();

            var fallback = GazeEyeBoneFallback.FromAnimator(animator);
            var fromNull = GazeEyeBoneFallback.FromAnimator(null);

            Assert.That(fallback.LeftEye == null && fallback.RightEye == null, Is.True);
            Assert.That(fromNull.LeftEye == null && fromNull.RightEye == null, Is.True);
        }

        [Test]
        public void Apply_EmptyBonePathsOnHumanoid_DrivesHumanoidEyeBones()
        {
            BuildHumanoid(includeEyes: true);
            var animator = _rootGo.GetComponent<Animator>();
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            var channel = new GazeChannel { id = GazeSourceIdConvention.DefaultChannelId };
            var source = new FixedGazeSource(0.5f, 0.5f);

            using (var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_rootGo.transform),
                new[] { new GazeBoneBinding(channel, source, source) },
                GazeEyeBoneFallback.FromAnimator(animator)))
            {
                provider.Apply();

                Assert.That(Quaternion.Angle(Quaternion.identity, leftEye.localRotation), Is.GreaterThan(0.1f));
                Assert.That(Quaternion.Angle(Quaternion.identity, rightEye.localRotation), Is.GreaterThan(0.1f));
            }

            Assert.That(Quaternion.Angle(Quaternion.identity, leftEye.localRotation), Is.LessThan(0.01f),
                "Dispose で初期回転へ戻る");
        }

        [UnityTest]
        public IEnumerator FacialController_EmptyBonePathsOnHumanoid_DrivesHumanoidEyeBonesEveryFrame()
        {
            BuildHumanoid(includeEyes: true);
            var animator = _rootGo.GetComponent<Animator>();
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            var controller = AttachController(new FixedGazeSource(0.5f, 0.5f));

            controller.Initialize();
            yield return null;
            yield return null;

            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(Quaternion.Angle(Quaternion.identity, leftEye.localRotation), Is.GreaterThan(0.1f));
            Assert.That(Quaternion.Angle(Quaternion.identity, rightEye.localRotation), Is.GreaterThan(0.1f));
            Assert.That(_unresolvedWarningCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator FacialController_EmptyBonePathsOnNonHumanoid_WarnsOnceAndDoesNotThrow()
        {
            _rootGo = new GameObject("NonHumanoidCharacter");
            _rootGo.AddComponent<Animator>();
            var controller = AttachController(new FixedGazeSource(0.5f, 0.5f));

            Assert.DoesNotThrow(() => controller.Initialize());
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(_unresolvedWarningCount, Is.EqualTo(1), "警告は初期化 1 回につき 1 度だけ");
        }

        private FacialController AttachController(IAnalogInputSource gazeSource)
        {
            // Initialize は Animator と BlendShape 付き SkinnedMeshRenderer が無いと初期化をスキップする。
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_rootGo.transform, false);
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            meshObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
            var controller = _rootGo.AddComponent<FacialController>();

            _profileAsset = UnityEngine.ScriptableObject.CreateInstance<GazeProfileSO>();
            _profileAsset.ProfileToLoad = new FacialProfile(
                "1.0.0",
                new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                Array.Empty<Expression>());
            _profileAsset.WritableAdapterBindings.Add(new GazeSourceBinding { Slug = "fakegaze", Source = gazeSource });
            controller.CharacterSO = _profileAsset;
            return controller;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition != null && condition.Contains(UnresolvedWarningFragment))
            {
                _unresolvedWarningCount++;
            }
        }

        private void BuildHumanoid(bool includeEyes)
        {
            _rootGo = new GameObject("HumanoidRoot");

            var hips = MakeBone("Hips", _rootGo.transform, new Vector3(0f, 1.0f, 0f));
            var spine = MakeBone("Spine", hips, new Vector3(0f, 0.2f, 0f));
            var neck = MakeBone("Neck", spine, new Vector3(0f, 0.4f, 0f));
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

            var humanBones = new List<HumanBone>
            {
                MakeHumanBone(HumanBodyBones.Hips, "Hips"),
                MakeHumanBone(HumanBodyBones.Spine, "Spine"),
                MakeHumanBone(HumanBodyBones.Neck, "Neck"),
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
            if (includeEyes)
            {
                humanBones.Add(MakeHumanBone(HumanBodyBones.LeftEye, "LeftEye"));
                humanBones.Add(MakeHumanBone(HumanBodyBones.RightEye, "RightEye"));
            }

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
            for (int i = 0; i < t.childCount; i++)
            {
                CollectSkeletonBones(t.GetChild(i), output);
            }
        }

        /// <summary>
        /// テスト用 SO。protected な binding リストへ書き込めるようにし、
        /// <see cref="LoadProfile"/> は与えられた in-memory プロファイルを返す。
        /// 既定チャネル "gaze" (目ボーン path 空) は SO 側で補完される。
        /// </summary>
        public sealed class GazeProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public FacialProfile ProfileToLoad;

            public override FacialProfile LoadProfile()
            {
                return ProfileToLoad;
            }
        }

        /// <summary>既定チャネル "gaze" 向けの共有入力源 <c>{slug}:gaze</c> を registry へ登録する binding。</summary>
        [Serializable]
        private sealed class GazeSourceBinding : AdapterBindingBase
        {
            [NonSerialized] public IAnalogInputSource Source;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                ctx.InputSourceRegistry.Register(
                    AdapterSlug.Parse(Slug),
                    GazeSourceIdConvention.ComposeSub(GazeSourceIdConvention.DefaultChannelId, GazeSide.Shared),
                    Source);
            }
        }

        private sealed class FixedGazeSource : IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;
            public FixedGazeSource(float x, float y) { Id = "test:gaze"; _x = x; _y = y; }
            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }
            public bool TryReadScalar(out float value) { value = _x; return true; }
            public bool TryReadVector2(out float x, out float y) { x = _x; y = _y; return true; }
            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0) output[0] = _x;
                if (output.Length > 1) output[1] = _y;
                return output.Length >= 2;
            }
        }
    }
}
