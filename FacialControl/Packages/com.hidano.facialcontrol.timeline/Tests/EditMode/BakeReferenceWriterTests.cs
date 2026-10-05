using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class BakeReferenceWriterTests : SizedTestFixture
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        [Test]
        public void Apply_RootAndChildFacialTracks_WritesSameBakeToAllHolders()
        {
            TimelineAsset timeline = CreateTimeline(out List<IFacialTimelineBakeHolder> holders);
            FacialTimelineBakeAsset bake = CreateBake();

            bool changed = BakeReferenceWriter.Apply(timeline, bake);

            Assert.That(changed, Is.True);
            Assert.That(holders, Has.Count.EqualTo(4));
            for (int i = 0; i < holders.Count; i++)
            {
                Assert.That(holders[i].Bake, Is.SameAs(bake), $"holder {i}");
            }

            BakeLocateResult located = FacialTimelineBakeLocator.Locate(timeline, null);
            Assert.That(located.Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(located.Bake, Is.SameAs(bake));
        }

        [Test]
        public void Apply_ReplacesMixedReferences_ResolvesConflict()
        {
            TimelineAsset timeline = CreateTimeline(out List<IFacialTimelineBakeHolder> holders);
            FacialTimelineBakeAsset stale = CreateBake();
            FacialTimelineBakeAsset bake = CreateBake();
            holders[0].Bake = stale;
            holders[2].Bake = bake;
            Assert.That(FacialTimelineBakeLocator.Locate(timeline, null).Status, Is.EqualTo(BakeLocateStatus.Conflict));

            BakeReferenceWriter.Apply(timeline, bake);

            Assert.That(FacialTimelineBakeLocator.Locate(timeline, null).Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(holders[0].Bake, Is.SameAs(bake));
        }

        [Test]
        public void Apply_AllHoldersAlreadyReferenceBake_ReturnsFalse()
        {
            TimelineAsset timeline = CreateTimeline(out _);
            FacialTimelineBakeAsset bake = CreateBake();
            BakeReferenceWriter.Apply(timeline, bake);

            bool changed = BakeReferenceWriter.Apply(timeline, bake);

            Assert.That(changed, Is.False);
        }

        [Test]
        public void Apply_SetsHideInHierarchyOnBake()
        {
            TimelineAsset timeline = CreateTimeline(out _);
            FacialTimelineBakeAsset bake = CreateBake();

            BakeReferenceWriter.Apply(timeline, bake);

            Assert.That(bake.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.HideInHierarchy));
        }

        private TimelineAsset CreateTimeline(out List<IFacialTimelineBakeHolder> holders)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            holders = new List<IFacialTimelineBakeHolder>();

            FacialExpressionTrack root = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            holders.Add(root);
            holders.Add(timeline.CreateTrack<FacialExpressionTrack>(root, "emotion Lane 1"));
            FacialValueTrack value = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            holders.Add(value);
            holders.Add(timeline.CreateTrack<FacialExpressionTrack>(null, "overlay"));
            timeline.CreateTrack<GroupTrack>(null, "NotFacial");
            return timeline;
        }

        private FacialTimelineBakeAsset CreateBake()
        {
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            _created.Add(bake);
            return bake;
        }
    }
}
