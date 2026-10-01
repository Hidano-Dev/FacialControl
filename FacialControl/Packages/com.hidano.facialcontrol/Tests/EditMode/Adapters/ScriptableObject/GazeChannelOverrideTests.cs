using System;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    /// <summary>
    /// 外部から受け取った目ボーン path・可動範囲を <see cref="GazeChannel"/> へ適用する規則を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class GazeChannelOverrideTests : SizedTestFixture
    {
        [Test]
        public void ApplyTo_LeftPathOverridden_UsesOverridePathAndKeepsLocalRight()
        {
            GazeChannel local = CreateLocalChannel();
            var channelOverride = new GazeChannelOverride(
                "Armature/Head/Eye_L", null, false, 0f, 0f, 0f, 0f);

            GazeChannel merged = channelOverride.ApplyTo(local);

            Assert.That(merged.leftEyeBonePath, Is.EqualTo("Armature/Head/Eye_L"));
            Assert.That(merged.rightEyeBonePath, Is.EqualTo(local.rightEyeBonePath));
        }

        [Test]
        public void ApplyTo_NoPathOverride_KeepsLocalPathsAndCalibration()
        {
            GazeChannel local = CreateLocalChannel();
            var channelOverride = new GazeChannelOverride(null, "  ", true, 20f, 10f, 25f, 30f);

            GazeChannel merged = channelOverride.ApplyTo(local);

            Assert.That(merged.leftEyeBonePath, Is.EqualTo(local.leftEyeBonePath));
            Assert.That(merged.rightEyeBonePath, Is.EqualTo(local.rightEyeBonePath));
            Assert.That(merged.leftEyeInitialRotation, Is.EqualTo(local.leftEyeInitialRotation));
            Assert.That(merged.rightEyeYawAxisLocal, Is.EqualTo(local.rightEyeYawAxisLocal));
        }

        [Test]
        public void ApplyTo_AngleLimitsOverridden_ReplacesAllFourAngles()
        {
            GazeChannel local = CreateLocalChannel();
            var channelOverride = new GazeChannelOverride(null, null, true, 20f, 10f, 25f, 30f);

            GazeChannel merged = channelOverride.ApplyTo(local);

            Assert.That(merged.lookUpAngle, Is.EqualTo(20f));
            Assert.That(merged.lookDownAngle, Is.EqualTo(10f));
            Assert.That(merged.outerYawAngle, Is.EqualTo(25f));
            Assert.That(merged.innerYawAngle, Is.EqualTo(30f));
        }

        [Test]
        public void ApplyTo_NoAngleLimits_KeepsLocalAngles()
        {
            GazeChannel local = CreateLocalChannel();
            var channelOverride = new GazeChannelOverride("Eye_L", null, false, 80f, 80f, 80f, 80f);

            GazeChannel merged = channelOverride.ApplyTo(local);

            Assert.That(merged.lookUpAngle, Is.EqualTo(local.lookUpAngle));
            Assert.That(merged.innerYawAngle, Is.EqualTo(local.innerYawAngle));
        }

        [Test]
        public void ApplyTo_AnyOverride_DoesNotMutateLocalChannel()
        {
            GazeChannel local = CreateLocalChannel();
            var channelOverride = new GazeChannelOverride("Eye_L", "Eye_R", true, 1f, 2f, 3f, 4f);

            GazeChannel merged = channelOverride.ApplyTo(local);

            Assert.That(merged, Is.Not.SameAs(local));
            Assert.That(local.leftEyeBonePath, Is.EqualTo("Head/LocalEye_L"));
            Assert.That(local.lookUpAngle, Is.EqualTo(15f));
            Assert.That(merged.id, Is.EqualTo(local.id));
            Assert.That(merged.providerSlug, Is.EqualTo(local.providerSlug));
        }

        [Test]
        public void Constructor_OutOfRangeAngles_ClampsToZeroToNinety()
        {
            var channelOverride = new GazeChannelOverride(null, null, true, -5f, 120f, float.NaN, 45f);

            Assert.That(channelOverride.LookUpAngle, Is.EqualTo(0f));
            Assert.That(channelOverride.LookDownAngle, Is.EqualTo(90f));
            Assert.That(channelOverride.OuterYawAngle, Is.EqualTo(0f));
            Assert.That(channelOverride.InnerYawAngle, Is.EqualTo(45f));
        }

        [Test]
        public void IsEmpty_BlankPathsWithoutAngleLimits_ReturnsTrue()
        {
            var channelOverride = new GazeChannelOverride("", " ", false, 10f, 10f, 10f, 10f);

            Assert.That(channelOverride.IsEmpty, Is.True);
            Assert.That(channelOverride.HasLeftEyeBonePath, Is.False);
        }

        [Test]
        public void Equals_SameValues_ReturnsTrue()
        {
            var left = default(GazeChannelOverride).WithLeftEyeBonePath("Eye_L").WithAngleLimits(1f, 2f, 3f, 4f);
            var right = new GazeChannelOverride("Eye_L", null, true, 1f, 2f, 3f, 4f);

            Assert.That(left.Equals(right), Is.True);
            Assert.That(left.WithRightEyeBonePath("Eye_R").Equals(right), Is.False);
        }

        [Test]
        public void ApplyTo_NullLocal_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => default(GazeChannelOverride).ApplyTo(null));
        }

        private static GazeChannel CreateLocalChannel()
        {
            return new GazeChannel
            {
                id = "gaze",
                providerSlug = "osc",
                leftEyeBonePath = "Head/LocalEye_L",
                leftEyeInitialRotation = new Vector3(1f, 2f, 3f),
                rightEyeBonePath = "Head/LocalEye_R",
                rightEyeYawAxisLocal = new Vector3(0f, 0f, 1f),
                lookUpAngle = 15f,
                lookDownAngle = 9f,
                outerYawAngle = 15f,
                innerYawAngle = 18f,
            };
        }
    }
}
