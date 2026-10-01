using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class GazeAdvertisementResolverTests : SizedTestFixture
    {
        [Test]
        public void Parse_FlatPairs_ReturnsEntriesInPayloadOrder()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "eye_look", "ARKit_8BS", "gaze", "VRChat_XY" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("eye_look"));
            Assert.That(entries[0].Format, Is.EqualTo("ARKit_8BS"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("gaze"));
        }

        [Test]
        public void Parse_InvalidShapeAndEmptyIds_SkipsInvalidPairs()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "valid", "VRChat_XY", "orphan", "VRChat_XY", "", "ARKit_8BS" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("valid"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("orphan"));
            Assert.That(warned, Is.False);
        }

        [Test]
        public void Parse_UnknownFormat_SkipsPairAndWarnsOnlyOnce()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "bad", "Unknown", "also_bad", "Unknown", "good", "VRChat_XY" },
                entries,
                ref warned);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("good"));

            GazeAdvertisementResolver.Parse(
                new[] { "still_bad", "Unknown" },
                entries,
                ref warned);

            Assert.That(entries, Is.Empty);
            Assert.That(warned, Is.True);
        }

        [Test]
        public void Parse_DuplicateIds_FirstPairWinsOrdinal()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "gaze", "VRChat_XY", "gaze", "ARKit_8BS", "Gaze", "ARKit_8BS" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Format, Is.EqualTo("VRChat_XY"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("Gaze"));
        }

        [Test]
        public void ComputeNormalizedHash_OrderChangesDoNotChangeHash()
        {
            var scratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            var first = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("z", "VRChat_XY"),
                new GazeAdvertisementResolver.GazeAdvertisement("a", "ARKit_8BS")
            };
            var second = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("a", "ARKit_8BS"),
                new GazeAdvertisementResolver.GazeAdvertisement("z", "VRChat_XY")
            };

            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(first, scratch),
                Is.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(second, scratch)));
        }

        [Test]
        public void ComputeNormalizedHash_ContentOrFormatChangesChangeHash()
        {
            var baseline = new[] { new GazeAdvertisementResolver.GazeAdvertisement("gaze", "VRChat_XY") };
            var scratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();

            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(
                    new[] { new GazeAdvertisementResolver.GazeAdvertisement("other", "VRChat_XY") }, scratch),
                Is.Not.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(baseline, scratch)));
            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(
                    new[] { new GazeAdvertisementResolver.GazeAdvertisement("gaze", "ARKit_8BS") }, scratch),
                Is.Not.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(baseline, scratch)));
        }

        [Test]
        public void ComputeNormalizedHash_ReusedScratch_ProducesStableHash()
        {
            var entries = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("z", "VRChat_XY"),
                new GazeAdvertisementResolver.GazeAdvertisement("a", "ARKit_8BS")
            };
            var scratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            uint expected = GazeAdvertisementResolver.ComputeNormalizedHash(entries, scratch);

            for (int i = 0; i < 10; i++)
            {
                Assert.That(GazeAdvertisementResolver.ComputeNormalizedHash(entries, scratch), Is.EqualTo(expected));
            }
        }

        [Test]
        public void BuildPlan_ExcludesMatchingManualGazeEntriesUsingOrdinalComparison()
        {
            var advertised = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("eye_look", "ARKit_8BS"),
                new GazeAdvertisementResolver.GazeAdvertisement("gaze", "VRChat_XY"),
                new GazeAdvertisementResolver.GazeAdvertisement("Gaze", "VRChat_XY")
            };
            var manual = new[]
            {
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_ARKit_8BS,
                    expressionId = "eye_look"
                },
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "gaze",
                    addressPattern = "/avatar/parameters/gaze"
                },
                new OscMappingEntry
                {
                    mode = OscMappingMode.Normal_BlendShape,
                    expressionId = "Gaze"
                }
            };
            var plan = new List<GazeAdvertisementResolver.GazeAdvertisement>();

            GazeAdvertisementResolver.BuildPlan(advertised, manual, plan);

            Assert.That(plan, Has.Count.EqualTo(1));
            Assert.That(plan[0].ExpressionId, Is.EqualTo("Gaze"));
        }

        [Test]
        public void BuildPlan_WithNoManualGazeEntries_ReturnsEveryAdvertisedEntry()
        {
            var advertised = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("eye_look", "ARKit_8BS"),
                new GazeAdvertisementResolver.GazeAdvertisement("gaze", "VRChat_XY")
            };
            var plan = new List<GazeAdvertisementResolver.GazeAdvertisement>();

            GazeAdvertisementResolver.BuildPlan(advertised, null, plan);

            Assert.That(plan, Has.Count.EqualTo(2));
            Assert.That(plan[0].ExpressionId, Is.EqualTo("eye_look"));
            Assert.That(plan[1].ExpressionId, Is.EqualTo("gaze"));
        }

        [Test]
        public void Parse_ChannelAttributePairs_SkipsThemWithoutWarning()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[]
                {
                    "gaze", "VRChat_XY",
                    "gaze", "bone.left=Armature/Head/Eye_L",
                    "gaze", "bone.right=Armature/Head/Eye_R",
                    "gaze", "range=15,9,15,18",
                },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Format, Is.EqualTo("VRChat_XY"));
            Assert.That(warned, Is.False);
        }

        [Test]
        public void ComputeNormalizedHash_AttributePairsDiffer_ReturnsSameHash()
        {
            var withoutAttributes = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            var withAttributes = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;
            GazeAdvertisementResolver.Parse(new[] { "gaze", "VRChat_XY" }, withoutAttributes, ref warned);
            GazeAdvertisementResolver.Parse(
                new[] { "gaze", "VRChat_XY", "gaze", "range=30,30,30,30" },
                withAttributes,
                ref warned);
            var scratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();

            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(withAttributes, scratch),
                Is.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(withoutAttributes, scratch)));
        }

        [Test]
        public void ParseChannelOverrides_PathAndRangePairs_ReturnsOverridePerChannel()
        {
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[]
                {
                    "gaze", "VRChat_XY",
                    "gaze", "bone.left=Armature/Head/Eye_L",
                    "gaze", "range=20,10.5,25,30",
                    "camera", "range=1,2,3,4",
                },
                overrides,
                ref warned);

            Assert.That(overrides, Has.Count.EqualTo(2));
            GazeChannelOverride gaze = overrides["gaze"];
            Assert.That(gaze.LeftEyeBonePath, Is.EqualTo("Armature/Head/Eye_L"));
            Assert.That(gaze.HasRightEyeBonePath, Is.False);
            Assert.That(gaze.HasAngleLimits, Is.True);
            Assert.That(gaze.LookUpAngle, Is.EqualTo(20f));
            Assert.That(gaze.LookDownAngle, Is.EqualTo(10.5f));
            Assert.That(gaze.OuterYawAngle, Is.EqualTo(25f));
            Assert.That(gaze.InnerYawAngle, Is.EqualTo(30f));
            Assert.That(overrides["camera"].HasLeftEyeBonePath, Is.False);
            Assert.That(warned, Is.False);
        }

        [Test]
        public void ParseChannelOverrides_OldFormatWithoutAttributes_ReturnsEmpty()
        {
            var overrides = new Dictionary<string, GazeChannelOverride> { { "stale", default(GazeChannelOverride).WithLeftEyeBonePath("Eye") } };
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[] { "gaze", "VRChat_XY", "eye_look", "ARKit_8BS" },
                overrides,
                ref warned);

            Assert.That(overrides, Is.Empty);
            Assert.That(warned, Is.False);
        }

        [Test]
        public void ParseChannelOverrides_PathContainingEquals_KeepsTextAfterFirstEquals()
        {
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[] { "gaze", "bone.right=Root/目=右", "gaze", "range=15,9,15,18" },
                overrides,
                ref warned);

            Assert.That(overrides["gaze"].RightEyeBonePath, Is.EqualTo("Root/目=右"));
        }

        [Test]
        public void ParseChannelOverrides_InvalidRange_SkipsPairAndWarnsOnlyOnce()
        {
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[]
                {
                    "gaze", "bone.left=Eye_L",
                    "gaze", "range=1,2,3",
                    "camera", "range=1,NaN,3,4",
                    "valid", "range=1,2,3,4",
                },
                overrides,
                ref warned);

            Assert.That(overrides, Has.Count.EqualTo(1));
            Assert.That(overrides.ContainsKey("valid"), Is.True);
            Assert.That(warned, Is.True);
        }

        [Test]
        public void ParseChannelOverrides_PathWithoutRange_DoesNotCommitPartialGroup()
        {
            // MTU 分割された広告の途中では、path だけが届いて range= がまだ届いていないことがある。
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[] { "gaze", "VRChat_XY", "gaze", "bone.left=Eye_L" },
                overrides,
                ref warned);

            Assert.That(overrides, Is.Empty);
            Assert.That(warned, Is.False);
        }

        [Test]
        public void ParseChannelOverrides_EmptyPathValue_CommitsRangeWithoutPath()
        {
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(
                new[] { "gaze", "bone.left=", "", "range=1,2,3,4", "gaze", "range=1,2,3,4" },
                overrides,
                ref warned);

            Assert.That(overrides, Has.Count.EqualTo(1));
            Assert.That(overrides["gaze"].HasLeftEyeBonePath, Is.False);
            Assert.That(overrides["gaze"].HasAngleLimits, Is.True);
        }

        [Test]
        public void AppendChannelAttributes_PathsSpecified_AppendsPathsAndRange()
        {
            var pairs = new List<string>();
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

            GazeAdvertisementResolver.AppendChannelAttributes(pairs, "gaze", channel);

            CollectionAssert.AreEqual(
                new[]
                {
                    "gaze", "bone.left=Armature/Head/Eye_L",
                    "gaze", "bone.right=Armature/Head/Eye_R",
                    "gaze", "range=20,10.5,25,30",
                },
                pairs);
        }

        [Test]
        public void AppendChannelAttributes_PathsUnspecified_AppendsOnlyRange()
        {
            var pairs = new List<string>();

            GazeAdvertisementResolver.AppendChannelAttributes(pairs, "gaze", new GazeChannel { id = "gaze", rightEyeBonePath = "  " });

            CollectionAssert.AreEqual(new[] { "gaze", "range=15,9,15,18" }, pairs);
        }

        [Test]
        public void AppendChannelAttributes_ThenParseChannelOverrides_RoundTripsValues()
        {
            var pairs = new List<string>();
            var channel = new GazeChannel
            {
                id = "gaze",
                leftEyeBonePath = "Head/左目",
                lookUpAngle = 12.25f,
                lookDownAngle = 7f,
                outerYawAngle = 33.5f,
                innerYawAngle = 0.1f,
            };
            GazeAdvertisementResolver.AppendChannelAttributes(pairs, "gaze", channel);
            var overrides = new Dictionary<string, GazeChannelOverride>();
            bool warned = false;

            GazeAdvertisementResolver.ParseChannelOverrides(pairs, overrides, ref warned);

            GazeChannelOverride parsed = overrides["gaze"];
            Assert.That(parsed.LeftEyeBonePath, Is.EqualTo("Head/左目"));
            Assert.That(parsed.HasRightEyeBonePath, Is.False);
            Assert.That(parsed.LookUpAngle, Is.EqualTo(12.25f));
            Assert.That(parsed.LookDownAngle, Is.EqualTo(7f));
            Assert.That(parsed.OuterYawAngle, Is.EqualTo(33.5f));
            Assert.That(parsed.InnerYawAngle, Is.EqualTo(0.1f));
        }
    }
}
