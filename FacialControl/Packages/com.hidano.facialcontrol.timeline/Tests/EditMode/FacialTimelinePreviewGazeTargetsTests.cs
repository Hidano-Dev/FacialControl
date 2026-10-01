using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Timeline.Editor;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Timeline スクラブプレビューの目ボーン解決が、ランタイム (GazeBonePoseProvider) と同じく
    /// path 未指定の側で Humanoid の目ボーン fallback を使うことを検証する。
    /// </summary>
    [SmallTest]
    public sealed class FacialTimelinePreviewGazeTargetsTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        private GameObject _root;
        private Transform _head;
        private Transform _leftEye;
        private Transform _rightEye;
        private List<FacialTimelinePreviewEyeTarget> _results;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Character");
            _head = MakeChild("Head", _root.transform);
            _leftEye = MakeChild("LeftEye", _head);
            _rightEye = MakeChild("RightEye", _head);
            _results = new List<FacialTimelinePreviewEyeTarget>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
            _root = null;
        }

        [Test]
        public void Resolve_EmptyBonePathsWithFallback_UsesFallbackEyes()
        {
            var channel = new GazeChannel { id = "gaze" };

            Resolve(new[] { channel }, 1, CreateFallback());

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[0].IsLeftEye, Is.True);
            Assert.That(_results[0].IsFallback, Is.True);
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye));
            Assert.That(_results[1].IsLeftEye, Is.False);
        }

        [Test]
        public void Resolve_EmptyBonePathsWithoutFallback_ReturnsNoTargetAndNotRoot()
        {
            var channel = new GazeChannel { id = "gaze" };

            Resolve(new[] { channel }, 1, default);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void Resolve_WhitespaceBonePath_TreatedAsEmpty()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "  ", rightEyeBonePath = "\t" };

            Resolve(new[] { channel }, 1, default);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void Resolve_ExplicitBonePath_UsesPathAndChannelRestAndAxes()
        {
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/LeftEye",
                leftEyeInitialRotation = new Vector3(10f, 0f, 0f),
                leftEyeYawAxisLocal = Vector3.forward,
            };

            Resolve(new[] { channel }, 1, default);

            Assert.That(_results.Count, Is.EqualTo(1));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[0].IsFallback, Is.False);
            AssertRotation(Quaternion.Euler(10f, 0f, 0f), _results[0].RestRotation);
            Assert.That(_results[0].YawAxisLocal, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Resolve_ExplicitBonePathNotFound_SkipsThatEye()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "Missing/Eye" };

            Resolve(new[] { channel }, 1, CreateFallback());

            // 左目は path 指定なので fallback に落ちない。右目は path 未指定なので fallback を使う。
            Assert.That(_results.Count, Is.EqualTo(1));
            Assert.That(_results[0].Bone, Is.SameAs(_rightEye));
        }

        [Test]
        public void Resolve_FallbackRest_UsesRotationAtFallbackCreation()
        {
            var rest = Quaternion.Euler(10f, 20f, 5f);
            _leftEye.localRotation = rest;
            GazeEyeBoneFallback fallback = CreateFallback();
            // fallback 作成後の姿勢変化 (プレビューでの書き込み等) は rest に影響しない。
            _leftEye.localRotation = Quaternion.Euler(0f, 60f, 0f);
            var channel = new GazeChannel { id = "gaze" };

            Resolve(new[] { channel }, 1, fallback);

            AssertRotation(rest, _results[0].RestRotation);
        }

        [Test]
        public void Resolve_MultipleEmptyPathChannels_OnlyFirstUsesFallback()
        {
            var first = new GazeChannel { id = "gaze" };
            var second = new GazeChannel { id = "gaze-2" };

            Resolve(new[] { first, second }, 2, CreateFallback());

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].ChannelIndex, Is.EqualTo(0));
            Assert.That(_results[1].ChannelIndex, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_ExplicitPathPointsToFallbackBone_PathWins()
        {
            var fallbackChannel = new GazeChannel { id = "gaze" };
            var pathChannel = new GazeChannel { id = "gaze-2", leftEyeBonePath = "Head/LeftEye" };

            Resolve(new[] { fallbackChannel, pathChannel }, 2, CreateFallback());

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_rightEye));
            Assert.That(_results[0].IsFallback, Is.True);
            Assert.That(_results[1].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[1].IsFallback, Is.False);
            Assert.That(_results[1].ChannelIndex, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_ChannelCountLimit_IgnoresChannelsBeyondCount()
        {
            var first = new GazeChannel { id = "gaze", leftEyeBonePath = "Head/LeftEye", rightEyeBonePath = "Head/RightEye" };
            var second = new GazeChannel { id = "gaze-2" };

            Resolve(new[] { first, second }, 1, CreateFallback());

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].IsFallback, Is.False);
            Assert.That(_results[1].IsFallback, Is.False);
        }

        [Test]
        public void ComputeLocalRotation_FallbackTarget_ComposesOnTopOfFallbackRest()
        {
            var rest = Quaternion.Euler(10f, 20f, 5f);
            _leftEye.localRotation = rest;
            var channel = new GazeChannel { id = "gaze" };
            Resolve(new[] { channel }, 1, CreateFallback());

            Quaternion rotation = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[0], channel, 0f, 1f);

            AssertRotation(Quaternion.AngleAxis(-channel.lookUpAngle, Vector3.right) * rest, rotation);
        }

        [Test]
        public void ComputeLocalRotation_LeftEyePositiveX_UsesOuterYaw()
        {
            var channel = new GazeChannel { id = "gaze" };
            Resolve(new[] { channel }, 1, CreateFallback());

            Quaternion left = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[0], channel, 0.5f, 0f);
            Quaternion right = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[1], channel, 0.5f, 0f);

            AssertRotation(Quaternion.AngleAxis(-0.5f * channel.outerYawAngle, Vector3.up), left);
            AssertRotation(Quaternion.AngleAxis(-0.5f * channel.innerYawAngle, Vector3.up), right);
        }

        private void Resolve(IReadOnlyList<GazeChannel> channels, int channelCount, GazeEyeBoneFallback fallback)
        {
            FacialTimelinePreviewGazeTargets.Resolve(_root.transform, channels, channelCount, fallback, _results);
        }

        private GazeEyeBoneFallback CreateFallback()
        {
            return new GazeEyeBoneFallback(_leftEye, _rightEye, _root.transform);
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
                $"expected {expected.eulerAngles}, actual {actual.eulerAngles}");
        }
    }
}
