using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Bone;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Bone
{
    /// <summary>
    /// <see cref="GazeEyeBoneResolver"/> の目線ボーンの解決規則 (ジョイント名 → Avatar の目線ボーン) と、
    /// rest 回転・軸を実行時の姿勢ではなくアセットデータ (バインドポーズ) から求めることを検証する。
    /// Avatar の skeleton から求める経路は PlayMode の GazeEyeBoneFallbackHumanoidTests で検証する。
    /// </summary>
    [SmallTest]
    public sealed class GazeEyeBoneResolverTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        private GameObject _root;
        private Transform _head;
        private Transform _leftEye;
        private Transform _rightEye;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Character");
            _head = MakeChild("Head", _root.transform);
            _leftEye = MakeChild("LeftEye", _head);
            _rightEye = MakeChild("RightEye", _head);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            _root = null;
            _mesh = null;
        }

        [TestCase(null, "")]
        [TestCase("   ", "")]
        [TestCase("LeftEye", "LeftEye")]
        [TestCase("  左目  ", "左目")]
        [TestCase("Armature/Hips/Head/LeftEye", "LeftEye")]
        [TestCase("Armature/頭/ 左目 ", "左目")]
        public void ToJointName_StoredValue_ReturnsLastSegmentName(string stored, string expected)
        {
            Assert.That(GazeEyeBoneResolver.ToJointName(stored), Is.EqualTo(expected));
        }

        [Test]
        public void FindJoint_ExactAndPartialMatches_PrefersExactMatchEvenIfPartialComesFirst()
        {
            // 前順で先に現れる "LeftEyeHighlight" は部分一致。後ろの完全一致を優先する。
            var highlight = MakeChild("LeftEyeHighlight", _root.transform);
            highlight.SetAsFirstSibling();

            Transform found = GazeEyeBoneResolver.FindJoint(_root.transform, "LeftEye", out bool isExact);

            Assert.That(found, Is.SameAs(_leftEye));
            Assert.That(isExact, Is.True);
        }

        [Test]
        public void FindJoint_MultipleExactMatches_ReturnsFirstInDepthFirstOrder()
        {
            var other = MakeChild("Accessory", _root.transform);
            MakeChild("LeftEye", other);

            Transform found = GazeEyeBoneResolver.FindJoint(_root.transform, "LeftEye", out bool isExact);

            Assert.That(found, Is.SameAs(_leftEye));
            Assert.That(isExact, Is.True);
        }

        [Test]
        public void FindJoint_OnlyPartialMatch_ReturnsFirstPartialMatch()
        {
            Transform found = GazeEyeBoneResolver.FindJoint(_root.transform, "Eye", out bool isExact);

            Assert.That(found, Is.SameAs(_leftEye));
            Assert.That(isExact, Is.False);
        }

        [Test]
        public void Resolve_PartialMatch_UsesItAndWarnsOnce()
        {
            var resolver = new GazeEyeBoneResolver(_root.transform, null, null, null);
            LogAssert.Expect(LogType.Warning, new Regex("完全一致するボーンが無いため.*'LeftEye'"));

            var first = resolver.Resolve("Left", isLeftEye: true);
            var second = resolver.Resolve("Left", isLeftEye: true);

            Assert.That(first.Bone, Is.SameAs(_leftEye));
            Assert.That(second.Bone, Is.SameAs(_leftEye));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Resolve_NoMatch_WarnsAndFallsBackToAvatarEye()
        {
            var resolver = new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye);
            LogAssert.Expect(LogType.Warning, new Regex("'Missing' に一致するボーンが見つからない"));

            var eye = resolver.Resolve("Armature/Missing", isLeftEye: false);

            Assert.That(eye.Bone, Is.SameAs(_rightEye));
        }

        [Test]
        public void Resolve_EmptyJointName_UsesAvatarEyeWithoutWarning()
        {
            var resolver = new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye);

            Assert.That(resolver.Resolve(string.Empty, isLeftEye: true).Bone, Is.SameAs(_leftEye));
            Assert.That(resolver.Resolve(null, isLeftEye: false).Bone, Is.SameAs(_rightEye));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Resolve_NoAvatarEyes_ReturnsNullBone()
        {
            var resolver = new GazeEyeBoneResolver(_root.transform, null, null, null);

            Assert.That(resolver.Resolve(string.Empty, isLeftEye: true).Bone == null, Is.True);
        }

        [Test]
        public void DeriveRestPose_HeadNotFacingForwardAndEyeAlreadyRotated_UsesBindPose()
        {
            // バインド時: 頭は FBX でよくある -90 度の X 回転、左目は少しだけ回った rest。
            Quaternion headBind = Quaternion.Euler(-90f, 0f, 0f);
            Quaternion eyeBind = Quaternion.Euler(5f, 0f, 0f);
            _head.localRotation = headBind;
            _leftEye.localRotation = eyeBind;
            BindSkinnedMesh(_head, _leftEye, _rightEye);

            // 起動直後: 頭が正面を向いておらず (首を傾げている)、目線の入力も既に掛かっている。
            _head.localRotation = headBind * Quaternion.Euler(0f, 0f, 35f);
            _leftEye.localRotation = Quaternion.Euler(0f, 25f, 0f);
            _root.transform.rotation = Quaternion.Euler(0f, 120f, 0f);

            var resolver = new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye);
            GazeEyeRestPoseSource source = resolver.DeriveRestPose(
                _leftEye, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);

            Assert.That(source, Is.EqualTo(GazeEyeRestPoseSource.BindPose));
            AssertRotation(eyeBind, rest);
            AssertVector(Quaternion.Inverse(headBind) * Vector3.up, yaw);
            AssertVector(Quaternion.Inverse(headBind) * Vector3.right, pitch);
        }

        [Test]
        public void Fallback_AvatarEyesWithBindPose_UsesBindPoseRest()
        {
            Quaternion eyeBind = Quaternion.Euler(0f, 0f, 10f);
            _leftEye.localRotation = eyeBind;
            BindSkinnedMesh(_head, _leftEye, _rightEye);
            _leftEye.localRotation = Quaternion.Euler(0f, 40f, 0f);

            var resolver = new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye);

            AssertRotation(eyeBind, resolver.Fallback.Left.RestRotation);
        }

        [Test]
        public void DeriveRestPose_NoAssetData_UsesCurrentPose()
        {
            _leftEye.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var resolver = new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye);

            GazeEyeRestPoseSource source = resolver.DeriveRestPose(
                _leftEye, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);

            Assert.That(source, Is.EqualTo(GazeEyeRestPoseSource.CurrentPose));
            AssertRotation(Quaternion.Euler(0f, 30f, 0f), rest);
            AssertVector(Vector3.up, yaw);
            AssertVector(Vector3.right, pitch);
        }

        /// <summary>現在の姿勢をバインドポーズとして持つ SkinnedMeshRenderer を root 配下に作る。</summary>
        private void BindSkinnedMesh(params Transform[] bones)
        {
            var body = MakeChild("Body", _root.transform);
            var renderer = body.gameObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { name = "BodyMesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            var bindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bindPoses[i] = bones[i].worldToLocalMatrix * body.localToWorldMatrix;
            }

            _mesh.bindposes = bindPoses;
            renderer.sharedMesh = _mesh;
            renderer.bones = bones;
        }

        private static Transform MakeChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance),
                $"expected {expected.eulerAngles} but was {actual.eulerAngles}");
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(1e-4f),
                $"expected {expected} but was {actual}");
        }
    }
}
