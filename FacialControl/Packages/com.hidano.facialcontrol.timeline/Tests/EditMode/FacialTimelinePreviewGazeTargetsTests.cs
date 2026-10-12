using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Timeline.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Timeline スクラブプレビューの目ボーン解決を検証する。
    /// Gaze チャネルはトラックの index ではなくチャネル id（REC source id → GazeSourceIdConvention のチャネル id）で
    /// 解決し（Req 7.4）、目ボーンはランタイム (GazeBonePoseProvider) と同じく
    /// <see cref="GazeEyeBoneResolver"/> で解決する (ジョイント名 → 見つからなければ Avatar の目線ボーン)。
    /// </summary>
    [SmallTest]
    public sealed class FacialTimelinePreviewGazeTargetsTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        private GameObject _root;
        private Transform _head;
        private Transform _leftEye;
        private Transform _rightEye;
        private Transform _leftEye2;
        private Transform _rightEye2;
        private List<FacialTimelinePreviewEyeTarget> _results;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Character");
            _head = MakeChild("Head", _root.transform);
            _leftEye = MakeChild("LeftEye", _head);
            _rightEye = MakeChild("RightEye", _head);
            _leftEye2 = MakeChild("LeftEye2", _head);
            _rightEye2 = MakeChild("RightEye2", _head);
            _results = new List<FacialTimelinePreviewEyeTarget>();
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

        // ================================================================
        // チャネル id 解決（index 結合の廃止）
        // ================================================================

        [Test]
        public void Resolve_ConventionSourceId_DrivesChannelWhoseIdMatches()
        {
            GazeChannel first = PathChannel("gaze-a", "Head/LeftEye", "Head/RightEye");
            GazeChannel second = PathChannel("gaze-b", "Head/LeftEye2", "Head/RightEye2");

            Resolve(new[] { first, second }, new[] { "osc:gaze-b" }, withAvatarEyes: false);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye2));
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye2));
            Assert.That(_results[0].ChannelIndex, Is.EqualTo(1));
            Assert.That(_results[0].SourceIndex, Is.EqualTo(0));
            Assert.That(_results[1].SourceIndex, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_ConventionSideSuffix_DrivesOnlyThatEye()
        {
            GazeChannel channel = PathChannel("gaze", "Head/LeftEye", "Head/RightEye");

            Resolve(new[] { channel }, new[] { "osc:gaze.right", "osc:gaze.left" }, withAvatarEyes: false);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(FindSource(_leftEye), Is.EqualTo(1), "左目は osc:gaze.left");
            Assert.That(FindSource(_rightEye), Is.EqualTo(0), "右目は osc:gaze.right");
        }

        [Test]
        public void Resolve_NoMatchingSourceId_DrivesNothing()
        {
            GazeChannel channel = PathChannel("gaze", "Head/LeftEye", "Head/RightEye");

            Resolve(new[] { channel }, new[] { "osc:other", "osc:lt" }, withAvatarEyes: true);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void Resolve_SourceOrderSwapped_KeepsSameBoneToSourceMapping()
        {
            GazeChannel first = PathChannel("gaze-a", "Head/LeftEye", "Head/RightEye");
            GazeChannel second = PathChannel("gaze-b", "Head/LeftEye2", "Head/RightEye2");
            string[] ordered = { "osc:gaze-a", "osc:gaze-b" };
            string[] swapped = { "osc:gaze-b", "osc:gaze-a" };

            Resolve(new[] { first, second }, ordered, withAvatarEyes: false);
            Dictionary<Transform, string> orderedMap = MapBoneToSource(ordered);
            _results.Clear();
            Resolve(new[] { first, second }, swapped, withAvatarEyes: false);
            Dictionary<Transform, string> swappedMap = MapBoneToSource(swapped);

            Assert.That(orderedMap.Count, Is.EqualTo(4));
            Assert.That(swappedMap, Is.EquivalentTo(orderedMap));
            Assert.That(orderedMap[_leftEye2], Is.EqualTo("osc:gaze-b"));
        }

        [Test]
        public void Resolve_NullSourceIds_TargetsAllChannelsForPropertyRegistration()
        {
            GazeChannel first = PathChannel("gaze-a", "Head/LeftEye", "Head/RightEye");
            GazeChannel second = PathChannel("gaze-b", "Head/LeftEye2", "Head/RightEye2");

            Resolve(new[] { first, second }, null, withAvatarEyes: false);

            Assert.That(_results.Count, Is.EqualTo(4));
        }

        // ================================================================
        // 目ボーンの解決規則（ランタイムと同じ）
        // ================================================================

        [Test]
        public void Resolve_EmptyBonePathsWithFallback_UsesFallbackEyes()
        {
            var channel = new GazeChannel { id = "gaze" };

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: true);

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

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: false);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void Resolve_WhitespaceBonePath_TreatedAsEmpty()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "  ", rightEyeBonePath = "\t" };

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: false);

            Assert.That(_results, Is.Empty);
        }

        [Test]
        public void Resolve_JointName_UsesDerivedRestAndAxes()
        {
            // 初期回転・軸は resolver が求めた値を使う (ここではアセットデータが無いので現在の姿勢)。
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/LeftEye",
            };

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: false);

            Assert.That(_results.Count, Is.EqualTo(1));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[0].IsFallback, Is.False);
            AssertRotation(Quaternion.identity, _results[0].RestRotation);
            Assert.That(_results[0].YawAxisLocal, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Resolve_JointNameNotFound_WarnsAndFallsBackToAvatarEye()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "Missing/Nope" };
            LogAssert.Expect(LogType.Warning, new Regex("'Nope'"));

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: true);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye));
        }

        [Test]
        public void Resolve_SameResolver_KeepsRestFromFirstResolution()
        {
            var rest = Quaternion.Euler(10f, 20f, 5f);
            _leftEye.localRotation = rest;
            var resolver = CreateResolver(withAvatarEyes: true);
            var channel = new GazeChannel { id = "gaze" };
            FacialTimelinePreviewGazeTargets.Resolve(resolver, new[] { channel }, new[] { "osc:gaze" }, _results);
            // 解決後の姿勢変化 (プレビューでの書き込み等) は rest に影響しない。
            _leftEye.localRotation = Quaternion.Euler(0f, 60f, 0f);
            _results.Clear();

            FacialTimelinePreviewGazeTargets.Resolve(resolver, new[] { channel }, new[] { "osc:gaze" }, _results);

            AssertRotation(rest, _results[0].RestRotation);
        }

        [Test]
        public void Resolve_MultipleEmptyPathChannels_OnlyFirstUsesFallback()
        {
            var first = new GazeChannel { id = "gaze" };
            var second = new GazeChannel { id = "gaze-2" };

            Resolve(new[] { first, second }, new[] { "osc:gaze", "osc:gaze-2" }, withAvatarEyes: true);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].ChannelIndex, Is.EqualTo(0));
            Assert.That(_results[1].ChannelIndex, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_TwoChannelsResolvingSameBone_FirstChannelWins()
        {
            var fallbackChannel = new GazeChannel { id = "gaze" };
            var nameChannel = new GazeChannel { id = "gaze-2", leftEyeBonePath = "Head/LeftEye" };

            Resolve(new[] { fallbackChannel, nameChannel }, new[] { "osc:gaze", "osc:gaze-2" }, withAvatarEyes: true);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[0].ChannelIndex, Is.EqualTo(0));
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye));
            Assert.That(_results[1].ChannelIndex, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_FirstEmptyPathChannelNotDriven_PassesFallbackToNextChannel()
        {
            var undriven = new GazeChannel { id = "gaze" };
            var driven = new GazeChannel { id = "gaze-2" };

            Resolve(new[] { undriven, driven }, new[] { "osc:gaze-2" }, withAvatarEyes: true);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].ChannelIndex, Is.EqualTo(1));
            Assert.That(_results[1].ChannelIndex, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_JointName_ResolvesByNameInsteadOfAvatarEye()
        {
            var channel = new GazeChannel { id = "gaze", leftEyeBonePath = "LeftEye2" };

            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: true);

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye2));
            Assert.That(_results[0].IsFallback, Is.False);
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye));
            Assert.That(_results[1].IsFallback, Is.True);
        }

        // ================================================================
        // REC に記録された目線ボーンのパス (HID-192)
        // ================================================================

        [Test]
        public void Resolve_RecordedPathsOnGenericRig_ResolvesEyesByPathWithoutAvatar()
        {
            // 書き出し側は Generic (Avatar の目線ボーンなし) で、目線メニューのジョイント名も未指定。
            var channel = new GazeChannel { id = "gaze" };

            FacialTimelinePreviewGazeTargets.Resolve(
                CreateResolver(withAvatarEyes: false),
                new[] { channel },
                new[] { "osc:gaze" },
                _results,
                new[] { "Head/LeftEye2" },
                new[] { "Head/RightEye2" });

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye2));
            Assert.That(_results[0].IsLeftEye, Is.True);
            Assert.That(_results[0].IsFallback, Is.False);
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye2));
            Assert.That(_results[1].IsLeftEye, Is.False);
            Assert.That(_results[1].IsFallback, Is.False);
        }

        [Test]
        public void Resolve_RecordedPathTakesPrecedenceOverJointName()
        {
            var channel = PathChannel("gaze", "LeftEye", "RightEye");

            FacialTimelinePreviewGazeTargets.Resolve(
                CreateResolver(withAvatarEyes: true),
                new[] { channel },
                new[] { "osc:gaze" },
                _results,
                new[] { "Head/LeftEye2" },
                new[] { "Head/RightEye2" });

            Assert.That(_results[0].Bone, Is.SameAs(_leftEye2));
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye2));
        }

        [Test]
        public void Resolve_RecordedPathNotFound_WarnsAndFallsBackToJointRules()
        {
            var channel = new GazeChannel { id = "gaze" };
            LogAssert.Expect(LogType.Warning, new Regex("'Armature/Missing/LeftEye'"));

            FacialTimelinePreviewGazeTargets.Resolve(
                CreateResolver(withAvatarEyes: true),
                new[] { channel },
                new[] { "osc:gaze" },
                _results,
                new[] { "Armature/Missing/LeftEye" },
                new string[] { null });

            Assert.That(_results.Count, Is.EqualTo(2));
            Assert.That(_results[0].Bone, Is.SameAs(_leftEye));
            Assert.That(_results[0].IsFallback, Is.True);
            Assert.That(_results[1].Bone, Is.SameAs(_rightEye));
        }

        [Test]
        public void ComputeLocalRotation_FallbackTarget_ComposesOnTopOfFallbackRest()
        {
            var rest = Quaternion.Euler(10f, 20f, 5f);
            _leftEye.localRotation = rest;
            var channel = new GazeChannel { id = "gaze" };
            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: true);

            Quaternion rotation = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[0], channel, 0f, 1f);

            AssertRotation(Quaternion.AngleAxis(-channel.lookUpAngle, Vector3.right) * rest, rotation);
        }

        [Test]
        public void ComputeLocalRotation_LeftEyePositiveX_UsesOuterYaw()
        {
            var channel = new GazeChannel { id = "gaze" };
            Resolve(new[] { channel }, new[] { "osc:gaze" }, withAvatarEyes: true);

            Quaternion left = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[0], channel, 0.5f, 0f);
            Quaternion right = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(_results[1], channel, 0.5f, 0f);

            AssertRotation(Quaternion.AngleAxis(-0.5f * channel.outerYawAngle, Vector3.up), left);
            AssertRotation(Quaternion.AngleAxis(-0.5f * channel.innerYawAngle, Vector3.up), right);
        }

        private void Resolve(IReadOnlyList<GazeChannel> channels, IReadOnlyList<string> sourceIds, bool withAvatarEyes)
        {
            FacialTimelinePreviewGazeTargets.Resolve(CreateResolver(withAvatarEyes), channels, sourceIds, _results);
        }

        private int FindSource(Transform bone)
        {
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Bone == bone)
                {
                    return _results[i].SourceIndex;
                }
            }

            Assert.Fail($"{bone.name} が解決結果に無い");
            return -1;
        }

        private Dictionary<Transform, string> MapBoneToSource(IReadOnlyList<string> sourceIds)
        {
            var map = new Dictionary<Transform, string>();
            for (int i = 0; i < _results.Count; i++)
            {
                map[_results[i].Bone] = sourceIds[_results[i].SourceIndex];
            }

            return map;
        }

        private GazeEyeBoneResolver CreateResolver(bool withAvatarEyes)
        {
            return withAvatarEyes
                ? new GazeEyeBoneResolver(_root.transform, null, _leftEye, _rightEye)
                : new GazeEyeBoneResolver(_root.transform, null, null, null);
        }

        private static GazeChannel PathChannel(string id, string leftPath, string rightPath)
        {
            return new GazeChannel { id = id, leftEyeBonePath = leftPath, rightEyeBonePath = rightPath };
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
