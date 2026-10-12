using System;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Bone
{
    /// <summary>
    /// <see cref="GazeBonePoseProvider"/> が <see cref="GazeEyeBoneResolver"/> で解決した目ボーン
    /// (ジョイント名 → Avatar の目線ボーン) を、目線メニューの可動範囲で駆動することを検証する。
    /// Humanoid Avatar からの解決自体は PlayMode の GazeEyeBoneFallbackHumanoidTests で検証する。
    /// </summary>
    [SmallTest]
    public sealed class GazeBonePoseProviderFallbackTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        private GameObject _root;
        private Transform _head;
        private Transform _leftEye;
        private Transform _rightEye;

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
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
        }

        [Test]
        public void Apply_EmptyJointNamesWithAvatarEyes_DrivesAvatarEyes()
        {
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0.5f, 0f);

            using (var provider = CreateProvider(channel, source, CreateResolver(_leftEye, _rightEye)))
            {
                provider.Apply();

                Assert.That(provider.HasUnresolvedFallbackEye, Is.False);
                // 左目: input.x > 0 は外側 (outerYawAngle) で、コード側の符号反転により -yaw。
                AssertRotation(Quaternion.AngleAxis(-0.5f * channel.outerYawAngle, Vector3.up), _leftEye.localRotation);
                // 右目: input.x > 0 は内側 (innerYawAngle)。
                AssertRotation(Quaternion.AngleAxis(-0.5f * channel.innerYawAngle, Vector3.up), _rightEye.localRotation);
            }
        }

        [Test]
        public void Apply_GazeMenuRange_UsesChannelAngles()
        {
            var channel = new GazeChannel { id = "gaze", lookUpAngle = 7f, lookDownAngle = 3f };
            var source = new FixedGazeSource(0f, 1f);

            using (var provider = CreateProvider(channel, source, CreateResolver(_leftEye, _rightEye)))
            {
                provider.Apply();

                AssertRotation(Quaternion.AngleAxis(-7f, Vector3.right), _leftEye.localRotation);
            }
        }

        [Test]
        public void Apply_JointName_DrivesNamedBoneInsteadOfAvatarEye()
        {
            var customEye = MakeChild("CustomEye", _head);
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "CustomEye",
            };
            var source = new FixedGazeSource(0.5f, 0f);

            using (var provider = CreateProvider(channel, source, CreateResolver(_leftEye, _rightEye)))
            {
                provider.Apply();

                Assert.That(Quaternion.Angle(Quaternion.identity, customEye.localRotation), Is.GreaterThan(0.1f),
                    "ジョイント名を指定した側は、その名前のボーンを駆動する");
                AssertRotation(Quaternion.identity, _leftEye.localRotation);
                Assert.That(Quaternion.Angle(Quaternion.identity, _rightEye.localRotation), Is.GreaterThan(0.1f),
                    "ジョイント名が空の側は Avatar の目線ボーンを駆動する");
            }
        }

        [Test]
        public void Apply_LegacyBonePath_UsesLastSegmentAsJointName()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "Armature/Head/LeftEye" };
            var source = new FixedGazeSource(0.5f, 0f);

            // Avatar の目線ボーンは無い。旧データの path は末尾の LeftEye で解決される。
            using (var provider = CreateProvider(channel, source, CreateResolver(null, null)))
            {
                provider.Apply();

                Assert.That(Quaternion.Angle(Quaternion.identity, _leftEye.localRotation), Is.GreaterThan(0.1f));
            }
        }

        [Test]
        public void Apply_JointName_IgnoresStoredInitialRotationAndUsesDerivedRest()
        {
            // GazeChannel に保存された初期回転は読まない。rest は resolver が求める (ここではアセットデータが
            // 無いので構築時点の姿勢)。
            var rest = Quaternion.Euler(0f, 45f, 0f);
            _leftEye.localRotation = rest;
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "LeftEye",
                leftEyeInitialRotation = new Vector3(0f, -80f, 0f),
            };
            var source = new FixedGazeSource(0f, 0f);

            using (var provider = CreateProvider(channel, source, CreateResolver(null, null)))
            {
                provider.Apply();

                AssertRotation(rest, _leftEye.localRotation);
            }
        }

        [Test]
        public void Apply_MultipleChannelsResolvingSameEye_OnlyFirstChannelDrivesIt()
        {
            var first = new GazeChannel { id = "gaze" };
            var second = new GazeChannel { id = "camera" };
            var firstSource = new FixedGazeSource(0.5f, 0f);
            var secondSource = new FixedGazeSource(-1f, -1f);

            using (var provider = new GazeBonePoseProvider(
                CreateResolver(_leftEye, _rightEye),
                new[]
                {
                    new GazeBoneBinding(first, firstSource, firstSource),
                    new GazeBoneBinding(second, secondSource, secondSource),
                }))
            {
                provider.Apply();

                Assert.That(provider.HasUnresolvedFallbackEye, Is.False);
                AssertRotation(Quaternion.AngleAxis(-0.5f * first.outerYawAngle, Vector3.up), _leftEye.localRotation);
            }
        }

        [Test]
        public void Constructor_NoJointNameAndNoAvatarEyes_ReportsUnresolvedWithoutThrowing()
        {
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0.5f, 0.5f);

            using (var provider = CreateProvider(channel, source, CreateResolver(null, null)))
            {
                Assert.DoesNotThrow(() => provider.Apply());

                Assert.That(provider.HasUnresolvedFallbackEye, Is.True);
                AssertRotation(Quaternion.identity, _leftEye.localRotation);
                AssertRotation(Quaternion.identity, _rightEye.localRotation);
            }

            // provider 自身は警告しない (警告の 1 回化は FacialController が担う)。
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Dispose_DrivenEye_RestoresOriginalRotation()
        {
            var original = Quaternion.Euler(3f, 4f, 5f);
            _leftEye.localRotation = original;
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(1f, 1f);

            var provider = CreateProvider(channel, source, CreateResolver(_leftEye, null));
            provider.Apply();
            Assert.That(Quaternion.Angle(original, _leftEye.localRotation), Is.GreaterThan(0.1f));

            provider.Dispose();

            AssertRotation(original, _leftEye.localRotation);
        }

        [Test]
        public void DeriveRestAndAxes_RotatedCharacterRoot_ReturnsCharacterRelativeAxes()
        {
            // root を Y 軸まわりに 90 度回しても、軸はキャラクター基準 (親 local の up / right) のまま。
            _root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            GazeEyeBoneFallback.DeriveRestAndAxes(
                _leftEye, _root.transform, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);

            AssertRotation(Quaternion.identity, rest);
            AssertVector(Vector3.up, yaw);
            AssertVector(Vector3.right, pitch);
        }

        [Test]
        public void DeriveRestAndAxes_RotatedParent_ReturnsRootAxesInParentLocalSpace()
        {
            _head.localRotation = Quaternion.Euler(0f, 0f, 90f);
            _leftEye.localRotation = Quaternion.Euler(0f, 30f, 0f);

            GazeEyeBoneFallback.DeriveRestAndAxes(
                _leftEye, _root.transform, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);

            AssertRotation(Quaternion.Euler(0f, 30f, 0f), rest);
            AssertVector(Quaternion.Inverse(_head.localRotation) * Vector3.up, yaw);
            AssertVector(Quaternion.Inverse(_head.localRotation) * Vector3.right, pitch);
        }

        private GazeEyeBoneResolver CreateResolver(Transform avatarLeftEye, Transform avatarRightEye)
        {
            return new GazeEyeBoneResolver(_root.transform, null, avatarLeftEye, avatarRightEye);
        }

        private static GazeBonePoseProvider CreateProvider(GazeChannel channel, IAnalogInputSource source, GazeEyeBoneResolver resolver)
        {
            return new GazeBonePoseProvider(
                resolver,
                new[] { new GazeBoneBinding(channel, source, source) });
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
