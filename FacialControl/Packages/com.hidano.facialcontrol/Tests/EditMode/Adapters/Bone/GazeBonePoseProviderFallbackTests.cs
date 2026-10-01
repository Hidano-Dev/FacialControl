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
    /// 目ボーン path 未指定の <see cref="GazeChannel"/> が <see cref="GazeEyeBoneFallback"/> の目ボーンを
    /// 駆動すること、path 指定時は従来どおり path を優先することを検証する。
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
        public void Apply_EmptyBonePathsWithFallbackEyes_DrivesFallbackEyes()
        {
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0.5f, 0f);

            using (var provider = CreateProvider(channel, source, new GazeEyeBoneFallback(_leftEye, _rightEye, _root.transform)))
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
        public void Apply_EmptyBonePathWithRestRotation_ComposesOnTopOfFallbackCreationTimeRest()
        {
            var rest = Quaternion.Euler(10f, 20f, 5f);
            _leftEye.localRotation = rest;
            var fallback = new GazeEyeBoneFallback(_leftEye, null, _root.transform);
            // fallback 作成後の姿勢変化 (アニメーション等) は rest に影響しない。
            _leftEye.localRotation = Quaternion.Euler(0f, 60f, 0f);
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0f, 1f);

            using (var provider = CreateProvider(channel, source, fallback))
            {
                provider.Apply();

                var expected = Quaternion.AngleAxis(-channel.lookUpAngle, Vector3.right) * rest;
                AssertRotation(expected, _leftEye.localRotation);
            }
        }

        [Test]
        public void Apply_MultipleChannelsWithEmptyPaths_OnlyFirstChannelDrivesFallbackEyes()
        {
            var first = new GazeChannel { id = "gaze" };
            var second = new GazeChannel { id = "camera" };
            var firstSource = new FixedGazeSource(0.5f, 0f);
            var secondSource = new FixedGazeSource(-1f, -1f);

            using (var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[]
                {
                    new GazeBoneBinding(first, firstSource, firstSource),
                    new GazeBoneBinding(second, secondSource, secondSource),
                },
                new GazeEyeBoneFallback(_leftEye, _rightEye, _root.transform)))
            {
                provider.Apply();

                Assert.That(provider.HasUnresolvedFallbackEye, Is.False);
                AssertRotation(Quaternion.AngleAxis(-0.5f * first.outerYawAngle, Vector3.up), _leftEye.localRotation);
            }
        }

        [Test]
        public void Dispose_TwoBindingsWritingSameBone_RestoresValueBeforeFirstWrite()
        {
            var original = Quaternion.Euler(0f, 10f, 0f);
            _leftEye.localRotation = original;
            var first = new GazeChannel { id = "gaze", leftEyeBonePath = "Head/LeftEye", leftEyeInitialRotation = new Vector3(0f, 10f, 0f) };
            var second = new GazeChannel { id = "camera", leftEyeBonePath = "Head/LeftEye", leftEyeInitialRotation = new Vector3(0f, 10f, 0f) };
            var source = new FixedGazeSource(1f, 1f);

            var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[] { new GazeBoneBinding(first, source, null), new GazeBoneBinding(second, source, null) });
            provider.Apply();
            provider.Dispose();

            AssertRotation(original, _leftEye.localRotation);
        }

        [Test]
        public void Apply_PathBindingTargetsSameBoneAsFallback_PathBindingWins()
        {
            // channel "gaze" は左目を path で、channel "camera" は path 未指定 (= 同じ Humanoid LeftEye)。
            var pathChannel = new GazeChannel { id = "gaze", leftEyeBonePath = "Head/LeftEye", rightEyeBonePath = "Head/RightEye" };
            var fallbackChannel = new GazeChannel { id = "camera" };
            var pathSource = new FixedGazeSource(0.5f, 0f);
            var fallbackSource = new FixedGazeSource(-1f, -1f);

            using (var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[]
                {
                    new GazeBoneBinding(pathChannel, pathSource, pathSource),
                    new GazeBoneBinding(fallbackChannel, fallbackSource, fallbackSource),
                },
                new GazeEyeBoneFallback(_leftEye, _rightEye, _root.transform)))
            {
                provider.Apply();

                AssertRotation(Quaternion.AngleAxis(-0.5f * pathChannel.outerYawAngle, Vector3.up), _leftEye.localRotation);
                AssertRotation(Quaternion.AngleAxis(-0.5f * pathChannel.innerYawAngle, Vector3.up), _rightEye.localRotation);
            }
        }

        [Test]
        public void Apply_SpecifiedBonePath_UsesPathAndIgnoresFallback()
        {
            var customEye = MakeChild("CustomEye", _head);
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/CustomEye",
                rightEyeBonePath = "Head/RightEye",
            };
            var source = new FixedGazeSource(0.5f, 0f);

            using (var provider = CreateProvider(channel, source, new GazeEyeBoneFallback(_leftEye, _rightEye, _root.transform)))
            {
                provider.Apply();

                Assert.That(Quaternion.Angle(Quaternion.identity, customEye.localRotation), Is.GreaterThan(0.1f),
                    "path 指定側は path のボーンを駆動する");
                AssertRotation(Quaternion.identity, _leftEye.localRotation);
                Assert.That(Quaternion.Angle(Quaternion.identity, _rightEye.localRotation), Is.GreaterThan(0.1f));
            }
        }

        [Test]
        public void Apply_SpecifiedBonePath_UsesStoredInitialRotationNotCurrentPose()
        {
            // path 指定時の既存挙動: rest は GazeChannel に保存された InitialRotation を使う。
            _leftEye.localRotation = Quaternion.Euler(0f, 45f, 0f);
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/LeftEye",
                leftEyeInitialRotation = Vector3.zero,
            };
            var source = new FixedGazeSource(0f, 0f);

            using (var provider = CreateProvider(channel, source, default))
            {
                provider.Apply();

                AssertRotation(Quaternion.identity, _leftEye.localRotation);
            }
        }

        [Test]
        public void Constructor_EmptyBonePathsWithoutFallback_ReportsUnresolvedWithoutThrowing()
        {
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0.5f, 0.5f);

            using (var provider = CreateProvider(channel, source, new GazeEyeBoneFallback(null, null, _root.transform)))
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
        public void Constructor_LegacyOverloadWithEmptyPaths_DrivesNothing()
        {
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(0.5f, 0.5f);

            using (var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[] { new GazeBoneBinding(channel, source, source) }))
            {
                provider.Apply();

                Assert.That(provider.HasUnresolvedFallbackEye, Is.False);
                AssertRotation(Quaternion.identity, _leftEye.localRotation);
                AssertRotation(Quaternion.identity, _rightEye.localRotation);
            }
        }

        [Test]
        public void Dispose_FallbackEye_RestoresOriginalRotation()
        {
            var original = Quaternion.Euler(3f, 4f, 5f);
            _leftEye.localRotation = original;
            var channel = new GazeChannel { id = "gaze" };
            var source = new FixedGazeSource(1f, 1f);

            var provider = CreateProvider(channel, source, new GazeEyeBoneFallback(_leftEye, null, _root.transform));
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
            // エディタ自動割当 (参照モデル root が無回転) と同じく、root の up / right を親 local 空間で表す。
            _head.localRotation = Quaternion.Euler(0f, 0f, 90f);
            _leftEye.localRotation = Quaternion.Euler(0f, 30f, 0f);

            GazeEyeBoneFallback.DeriveRestAndAxes(
                _leftEye, _root.transform, out Quaternion rest, out Vector3 yaw, out Vector3 pitch);

            AssertRotation(Quaternion.Euler(0f, 30f, 0f), rest);
            AssertVector(Quaternion.Inverse(_head.localRotation) * Vector3.up, yaw);
            AssertVector(Quaternion.Inverse(_head.localRotation) * Vector3.right, pitch);
        }

        private GazeBonePoseProvider CreateProvider(GazeChannel channel, IAnalogInputSource source, GazeEyeBoneFallback fallback)
        {
            return new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[] { new GazeBoneBinding(channel, source, source) },
                fallback);
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
