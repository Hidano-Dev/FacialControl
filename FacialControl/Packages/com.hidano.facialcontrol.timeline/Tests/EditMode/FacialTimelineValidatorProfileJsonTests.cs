using System;
using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor.Validation;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="FacialTimelineValidator.Validate(TimelineAsset)"/> が Profile を
    /// <c>TimelineProfileSource</c> 経由（Bake の Profile GUID → profile.json 優先）で解決することを固定する。
    /// AssetDatabase と profile.json を使うため Small の <see cref="FacialTimelineValidatorTests"/> とは分けて Medium に置く。
    /// </summary>
    [MediumTest]
    public sealed class FacialTimelineValidatorProfileJsonTests : SizedTestFixture
    {
        private ProfileJsonTestFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = ProfileJsonTestFixture.Create("FacialTimelineValidatorProfileJsonTests", "smile");
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
        }

        [Test]
        public void Validate_TimelineWithBakeProfileGuid_UsesProfileJsonExpressions()
        {
            _fixture.WriteProfileJson(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), "smile", "wink");
            TimelineAsset timeline = CreateTimelineWithBake("wink", "frown");

            FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline);

            var missingIds = new List<string>();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Kind == FacialTimelineIssueKind.MissingExpressionId)
                {
                    missingIds.Add(report.Issues[i].ClipName);
                }
            }

            Assert.That(missingIds, Is.EquivalentTo(new[] { "frown" }), "JSON にだけある wink は有効、どちらにも無い frown は不足");
        }

        private TimelineAsset CreateTimelineWithBake(params string[] expressionIds)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, _fixture.FolderPath + "/Timeline.playable");
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, ProfileJsonTestFixture.LayerName);
            for (int i = 0; i < expressionIds.Length; i++)
            {
                TimelineClip clip = track.CreateClip<FacialExpressionClip>();
                clip.start = i;
                clip.duration = 0.5d;
                clip.displayName = expressionIds[i];
                ((FacialExpressionClip)clip.asset).ExpressionId = expressionIds[i];
            }

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            bake.ProfileAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_fixture.ProfileAsset));
            AssetDatabase.AddObjectToAsset(bake, timeline);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }
    }
}
