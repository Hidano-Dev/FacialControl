using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class GazeChannelAttributesTests : SizedTestFixture
    {
        [Test]
        public void TryParseOverride_PathAndRange_ReturnsOverride()
        {
            bool warned = false;

            bool parsed = GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "bone.left=Armature/Head/Eye_L", "range=20,10.5,25,30" },
                out GazeChannelOverride value,
                ref warned);

            Assert.That(parsed, Is.True);
            Assert.That(value.LeftEyeBonePath, Is.EqualTo("Armature/Head/Eye_L"));
            Assert.That(value.HasRightEyeBonePath, Is.False);
            Assert.That(value.HasAngleLimits, Is.True);
            Assert.That(value.LookUpAngle, Is.EqualTo(20f));
            Assert.That(value.LookDownAngle, Is.EqualTo(10.5f));
            Assert.That(value.OuterYawAngle, Is.EqualTo(25f));
            Assert.That(value.InnerYawAngle, Is.EqualTo(30f));
            Assert.That(warned, Is.False);
        }

        [Test]
        public void TryParseOverride_UnknownAttribute_IgnoresItWithoutWarning()
        {
            bool warned = false;

            bool parsed = GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "VRChat_XY", "future=1", "range=15,9,15,18", "" },
                out GazeChannelOverride value,
                ref warned);

            Assert.That(parsed, Is.True);
            Assert.That(value.HasAngleLimits, Is.True);
            Assert.That(value.HasLeftEyeBonePath, Is.False);
            Assert.That(warned, Is.False);
        }

        [Test]
        public void TryParseOverride_PathWithoutRange_ReturnsFalse()
        {
            bool warned = false;

            bool parsed = GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "bone.left=Eye_L" },
                out GazeChannelOverride value,
                ref warned);

            Assert.That(parsed, Is.False);
            Assert.That(value.HasLeftEyeBonePath, Is.False);
            Assert.That(warned, Is.False);
        }

        [Test]
        public void TryParseOverride_NullOrEmptyAttributes_ReturnsFalse()
        {
            bool warned = false;

            Assert.That(GazeChannelAttributes.TryParseOverride("gaze", null, out _, ref warned), Is.False);
            Assert.That(GazeChannelAttributes.TryParseOverride("gaze", new string[0], out _, ref warned), Is.False);
        }

        [Test]
        public void TryParseOverride_PathContainingEquals_KeepsTextAfterFirstEquals()
        {
            bool warned = false;

            GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "bone.right=Root/目=右", "range=15,9,15,18" },
                out GazeChannelOverride value,
                ref warned);

            Assert.That(value.RightEyeBonePath, Is.EqualTo("Root/目=右"));
        }

        [Test]
        public void TryParseOverride_InvalidRange_ReturnsFalseAndWarnsOnlyOnce()
        {
            bool warned = false;

            bool first = GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "bone.left=Eye_L", "range=1,2,3" },
                out _,
                ref warned);
            bool second = GazeChannelAttributes.TryParseOverride(
                "camera",
                new[] { "range=1,NaN,3,4" },
                out _,
                ref warned);

            Assert.That(first, Is.False);
            Assert.That(second, Is.False);
            Assert.That(warned, Is.True);
        }

        [Test]
        public void TryParseOverride_EmptyPathValue_ReturnsRangeWithoutPath()
        {
            bool warned = false;

            bool parsed = GazeChannelAttributes.TryParseOverride(
                "gaze",
                new[] { "bone.left=", "range=1,2,3,4" },
                out GazeChannelOverride value,
                ref warned);

            Assert.That(parsed, Is.True);
            Assert.That(value.HasLeftEyeBonePath, Is.False);
            Assert.That(value.HasAngleLimits, Is.True);
        }

        [Test]
        public void AppendChannelAttributeValues_PathsSpecified_AppendsPathsAndRange()
        {
            var attributes = new List<string>();
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Armature/Head/Eye_L",
                rightEyeBonePath = "Armature/Head/Eye_R",
                lookUpAngle = 20f,
                lookDownAngle = 10.5f,
                outerYawAngle = 25f,
                innerYawAngle = 30f,
            };

            GazeChannelAttributes.AppendChannelAttributeValues(attributes, channel);

            CollectionAssert.AreEqual(
                new[]
                {
                    "bone.left=Armature/Head/Eye_L",
                    "bone.right=Armature/Head/Eye_R",
                    "range=20,10.5,25,30",
                },
                attributes);
        }

        [Test]
        public void AppendChannelAttributeValues_PathsUnspecified_AppendsOnlyRange()
        {
            var attributes = new List<string>();

            GazeChannelAttributes.AppendChannelAttributeValues(
                attributes,
                new GazeChannel { id = "gaze", rightEyeBonePath = "  " });

            CollectionAssert.AreEqual(new[] { "range=15,9,15,18" }, attributes);
        }

        [Test]
        public void AppendChannelAttributeValues_NullChannel_AppendsNothing()
        {
            var attributes = new List<string>();

            GazeChannelAttributes.AppendChannelAttributeValues(attributes, null);

            Assert.That(attributes, Is.Empty);
        }

        [Test]
        public void AppendChannelAttributeValues_ThenTryParseOverride_RoundTripsValues()
        {
            var attributes = new List<string>();
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/左目",
                lookUpAngle = 12.25f,
                lookDownAngle = 7f,
                outerYawAngle = 33.5f,
                innerYawAngle = 0.1f,
            };
            GazeChannelAttributes.AppendChannelAttributeValues(attributes, channel);
            bool warned = false;

            GazeChannelAttributes.TryParseOverride("gaze", attributes, out GazeChannelOverride parsed, ref warned);

            Assert.That(parsed.LeftEyeBonePath, Is.EqualTo("Head/左目"));
            Assert.That(parsed.HasRightEyeBonePath, Is.False);
            Assert.That(parsed.LookUpAngle, Is.EqualTo(12.25f));
            Assert.That(parsed.LookDownAngle, Is.EqualTo(7f));
            Assert.That(parsed.OuterYawAngle, Is.EqualTo(33.5f));
            Assert.That(parsed.InnerYawAngle, Is.EqualTo(0.1f));
        }
    }
}
