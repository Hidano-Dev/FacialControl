using System;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineBakeService"/> の SO overload が <see cref="TimelineProfileSource"/> 経由で
    /// StreamingAssets の profile.json を優先することを固定する。profile.json の読み書きを伴うため
    /// Small の <see cref="TimelineBakeServiceTests"/> とは分けて Medium に置く。
    /// </summary>
    [MediumTest]
    public sealed class TimelineBakeServiceProfileJsonTests : SizedTestFixture
    {
        private ProfileJsonTestFixture _fixture;
        private TimelineAsset _timeline;
        private FacialTimelineBakeAsset _bake;

        [SetUp]
        public void SetUp()
        {
            _fixture = ProfileJsonTestFixture.Create("TimelineBakeServiceProfileJsonTests", "smile");
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialExpressionTrack track = _timeline.CreateTrack<FacialExpressionTrack>(null, ProfileJsonTestFixture.LayerName);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 0.5d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "wink";
        }

        [TearDown]
        public void TearDown()
        {
            if (_bake != null)
            {
                UnityEngine.Object.DestroyImmediate(_bake);
            }

            UnityEngine.Object.DestroyImmediate(_timeline);
            _fixture.Dispose();
        }

        [Test]
        public void Bake_FromProfileAsset_WhenProfileJsonDiffersFromSo_UsesJsonProfile()
        {
            _fixture.WriteProfileJson(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), "smile", "wink");

            _bake = TimelineBakeService.Bake(_timeline, _fixture.ProfileAsset);

            Assert.That(_bake.ProfileContentHashHex, Is.EqualTo(_fixture.ContentHash(_fixture.ProfileAsset.LoadProfile())));
            Assert.That(
                _bake.ProfileContentHashHex,
                Is.Not.EqualTo(_fixture.ContentHash(_fixture.ProfileAsset.BuildFallbackProfile())));
        }

        [Test]
        public void IsStale_FromProfileAsset_ComparesAgainstProfileJson()
        {
            _fixture.WriteProfileJson(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), "smile", "wink");
            _bake = TimelineBakeService.Bake(_timeline, _fixture.ProfileAsset);

            Assert.That(
                TimelineBakeService.IsStale(_timeline, _fixture.ProfileAsset, _bake),
                Is.EqualTo(BakeStaleReason.None));

            _fixture.WriteProfileJson(new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc), "smile", "wink", "frown");

            Assert.That(
                TimelineBakeService.IsStale(_timeline, _fixture.ProfileAsset, _bake),
                Is.EqualTo(BakeStaleReason.ProfileChanged));
        }
    }
}
