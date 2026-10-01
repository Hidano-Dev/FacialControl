using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="ExpressionThumbnailFraming"/> の構図計算。
    /// 顔が画面に収まる・カメラが正面から顔を向く・顔ジョイントが無いときは全身を収める、を守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ExpressionThumbnailFramingTests : SizedTestFixture
    {
        private const float Tolerance = 1e-4f;

        // 身長 1.6m、足元が原点のモデル。Head ボーンは 1.45m。
        private static readonly Bounds HumanoidBounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 1.6f, 0.3f));
        private static readonly Vector3 HeadPosition = new Vector3(0f, 1.45f, 0f);

        [Test]
        public void Compute_WithFaceJoint_TargetsSlightlyAboveFaceJoint()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, Vector3.forward, Vector3.up);

            float radius = ExpressionThumbnailFraming.EstimateFaceRadius(HumanoidBounds);
            var expected = HeadPosition + Vector3.up * (radius * ExpressionThumbnailFraming.FaceCenterOffsetRatio);
            AssertVector(expected, pose.Target);
        }

        [Test]
        public void Compute_WithFaceJoint_CameraInFrontOfModelLooksAtTarget()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, Vector3.forward, Vector3.up);

            // モデルの正面（+Z）側に立ち、注視点を向く。
            Assert.That(pose.Position.z, Is.GreaterThan(pose.Target.z));
            var toTarget = (pose.Target - pose.Position).normalized;
            AssertVector(toTarget, pose.Rotation * Vector3.forward);
            // 画面の上がモデルの上。
            AssertVector(Vector3.up, pose.Rotation * Vector3.up);
        }

        [Test]
        public void Compute_WithFaceJoint_FaceSphereFitsInsideFieldOfView()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, Vector3.forward, Vector3.up);

            float radius = ExpressionThumbnailFraming.EstimateFaceRadius(HumanoidBounds);
            float distance = Vector3.Distance(pose.Position, pose.Target);
            float halfAngle = Mathf.Asin(radius / distance) * Mathf.Rad2Deg;

            Assert.That(halfAngle * 2f, Is.LessThanOrEqualTo(pose.FieldOfView + Tolerance));
            // 顔が小さく写りすぎない（余白が倍率の範囲に収まる）。
            Assert.That(halfAngle * 2f, Is.GreaterThan(pose.FieldOfView / (ExpressionThumbnailFraming.Margin * 1.5f)));
        }

        [Test]
        public void Compute_WithFaceJoint_ClipPlanesContainFace()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, Vector3.forward, Vector3.up);

            float radius = ExpressionThumbnailFraming.EstimateFaceRadius(HumanoidBounds);
            float distance = Vector3.Distance(pose.Position, pose.Target);

            Assert.That(pose.NearClip, Is.GreaterThan(0f));
            Assert.That(pose.NearClip, Is.LessThan(distance - radius));
            Assert.That(pose.FarClip, Is.GreaterThan(distance + radius));
        }

        [Test]
        public void Compute_WithoutFaceJoint_FramesWholeBounds()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, false, Vector3.zero, Vector3.forward, Vector3.up);

            AssertVector(HumanoidBounds.center, pose.Target);
            float distance = Vector3.Distance(pose.Position, pose.Target);
            float halfAngle = Mathf.Asin(HumanoidBounds.extents.magnitude / distance) * Mathf.Rad2Deg;
            Assert.That(halfAngle * 2f, Is.LessThanOrEqualTo(pose.FieldOfView + Tolerance));
        }

        [Test]
        public void Compute_RotatedModel_CameraFollowsModelForward()
        {
            var forward = Quaternion.Euler(0f, 90f, 0f) * Vector3.forward; // +X を向いたモデル
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, forward, Vector3.up);

            var fromTarget = (pose.Position - pose.Target).normalized;
            AssertVector(forward, fromTarget);
        }

        [Test]
        public void Compute_ForwardParallelToUp_ReturnsFinitePose()
        {
            var pose = ExpressionThumbnailFraming.Compute(HumanoidBounds, true, HeadPosition, Vector3.up, Vector3.up);

            Assert.That(float.IsNaN(pose.Position.x) || float.IsNaN(pose.Rotation.w), Is.False);
            AssertVector((pose.Target - pose.Position).normalized, pose.Rotation * Vector3.forward);
        }

        [Test]
        public void EstimateFaceRadius_TinyModel_ClampedToMinimum()
        {
            var tiny = new Bounds(Vector3.zero, new Vector3(0.01f, 0.01f, 0.01f));

            Assert.That(ExpressionThumbnailFraming.EstimateFaceRadius(tiny), Is.EqualTo(ExpressionThumbnailFraming.MinFaceRadius));
        }

        [Test]
        public void ComputeDistanceToFitSphere_KnownAngle_ReturnsRadiusOverSinHalfFov()
        {
            // 半角 30 度 → sin = 0.5 → 距離は半径の 2 倍。
            Assert.That(ExpressionThumbnailFraming.ComputeDistanceToFitSphere(0.5f, 60f), Is.EqualTo(1f).Within(Tolerance));
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance), $"x: expected {expected}, actual {actual}");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance), $"y: expected {expected}, actual {actual}");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance), $"z: expected {expected}, actual {actual}");
        }
    }
}
