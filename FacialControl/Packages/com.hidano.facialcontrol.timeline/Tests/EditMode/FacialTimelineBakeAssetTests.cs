using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineBakeAssetTests : SizedTestFixture
    {
        [Test]
        public void Properties_WhenAssignedNull_NormalizeToEmptyCollectionsAndStrings()
        {
            var asset = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            try
            {
                asset.SourceHashHex = null;
                asset.ProfileAssetGuid = null;
                asset.ExpressionBakes = null;
                asset.ValueBakes = null;
                asset.StateEvents = null;

                Assert.That(asset.SourceHashHex, Is.EqualTo(string.Empty));
                Assert.That(asset.ProfileAssetGuid, Is.EqualTo(string.Empty));
                Assert.That(asset.ExpressionBakes, Is.Empty);
                Assert.That(asset.ValueBakes, Is.Empty);
                Assert.That(asset.StateEvents, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void NestedSchema_RetainsAssignedBakeData()
        {
            var asset = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            try
            {
                var smileCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                var gazeX = AnimationCurve.Linear(0f, -1f, 1f, 1f);
                var gazeY = AnimationCurve.Linear(0f, 1f, 1f, -1f);

                asset.SourceHashHex = "0123abcd";
                asset.ProfileAssetGuid = "profile-guid";
                asset.SampleRate = 120f;
                asset.ExpressionBakes = new[]
                {
                    new ExpressionSourceBake
                    {
                        LayerName = "Expressions",
                        Curves = new[]
                        {
                            new BlendShapeCurve
                            {
                                BlendShapeName = "Smile",
                                Curve = smileCurve,
                            },
                        },
                    },
                };
                asset.ValueBakes = new[]
                {
                    new ValueChannelBake
                    {
                        Sub = "gaze-main",
                        IsGaze = true,
                        Axes = new[] { gazeX, gazeY },
                    },
                };
                asset.StateEvents = new[]
                {
                    new TimelineStateEvent(0.25d, TimelineStateEvent.KindOn, "smile", "Expressions"),
                };

                Assert.That(asset.SourceHashHex, Is.EqualTo("0123abcd"));
                Assert.That(asset.ProfileAssetGuid, Is.EqualTo("profile-guid"));
                Assert.That(asset.SampleRate, Is.EqualTo(120f));
                Assert.That(asset.ExpressionBakes, Has.Length.EqualTo(1));
                Assert.That(asset.ExpressionBakes[0].LayerName, Is.EqualTo("Expressions"));
                Assert.That(asset.ExpressionBakes[0].Curves, Has.Length.EqualTo(1));
                Assert.That(asset.ExpressionBakes[0].Curves[0].BlendShapeName, Is.EqualTo("Smile"));
                Assert.That(asset.ExpressionBakes[0].Curves[0].Curve, Is.SameAs(smileCurve));
                Assert.That(asset.ValueBakes, Has.Length.EqualTo(1));
                Assert.That(asset.ValueBakes[0].Sub, Is.EqualTo("gaze-main"));
                Assert.That(asset.ValueBakes[0].IsGaze, Is.True);
                Assert.That(asset.ValueBakes[0].Axes, Is.EqualTo(new[] { gazeX, gazeY }));
                Assert.That(asset.StateEvents, Has.Length.EqualTo(1));
                Assert.That(asset.StateEvents[0].ExpressionId, Is.EqualTo("smile"));
                Assert.That(asset.StateEvents[0].LayerName, Is.EqualTo("Expressions"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void UnitySerialization_PersistsSchemaFields()
        {
            var asset = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            try
            {
                asset.SourceHashHex = "feedbeef";
                asset.ProfileAssetGuid = "profile-guid";
                asset.SampleRate = 60f;
                asset.ExpressionBakes = new[]
                {
                    new ExpressionSourceBake
                    {
                        LayerName = "Emotion",
                        Curves = new[]
                        {
                            new BlendShapeCurve
                            {
                                BlendShapeName = "Blink",
                                Curve = AnimationCurve.Constant(0f, 1f, 0.5f),
                            },
                        },
                    },
                };
                asset.ValueBakes = new[]
                {
                    new ValueChannelBake
                    {
                        Sub = "analog-1",
                        IsGaze = false,
                        Axes = new[] { AnimationCurve.Linear(0f, 0f, 1f, 1f) },
                    },
                };
                asset.StateEvents = new[]
                {
                    new TimelineStateEvent(1.5d, TimelineStateEvent.KindOff, "blink", "Emotion"),
                };

                string json = EditorJsonUtility.ToJson(asset);

                StringAssert.Contains("\"sourceHashHex\":\"feedbeef\"", json);
                StringAssert.Contains("\"profileAssetGuid\":\"profile-guid\"", json);
                StringAssert.Contains("\"sampleRate\":60.0", json);
                StringAssert.Contains("\"layerName\":\"Emotion\"", json);
                StringAssert.Contains("\"blendShapeName\":\"Blink\"", json);
                StringAssert.Contains("\"sub\":\"analog-1\"", json);
                StringAssert.Contains("\"expressionId\":\"blink\"", json);
                StringAssert.Contains("\"layerName\":\"Emotion\"", json);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
