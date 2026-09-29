using System;
using System.Collections;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public class OverlayInputSourcePerformanceTests : SizedTestFixture
    {
        private const int WarmUpFrames = 10;
        private const int MeasuredFrames = 1000;
        private const string LayerName = "emotion";
        private const string BlinkSlot = "blink";
        private const string EyeBlinkLeft = "EyeBlinkLeft";
        private const string EyeBlinkRight = "EyeBlinkRight";
        private const string MouthSmile = "MouthSmile";

        private static readonly string[] BlendShapeNames =
        {
            EyeBlinkLeft,
            EyeBlinkRight,
            MouthSmile,
        };

        private sealed class StubActiveProvider : IActiveExpressionProvider
        {
            public Expression? Active;

            public Expression? TryGetTopActiveExpression(string layerName)
            {
                return Active;
            }
        }

        [UnityTest]
        public IEnumerator TryWriteValues_1000Frames_AllocatesZeroBytes()
        {
            var snapshot = CreateSnapshot(
                "anger-blink-inline",
                new BlendShapeSnapshot("Face", EyeBlinkLeft, 0.8f),
                new BlendShapeSnapshot("Face", EyeBlinkRight, 0.75f));
            var profile = BuildProfile(
                angerOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: snapshot),
                });
            var provider = new StubActiveProvider
            {
                Active = profile.FindExpressionById("anger"),
            };
            var source = CreateSource(profile, provider);
            var output = new float[BlendShapeNames.Length];

            for (int i = 0; i < WarmUpFrames; i++)
            {
                source.TryWriteValues(output);
                yield return null;
            }

            StabilizeManagedHeap();

            bool allFramesWrote = true;
            long before = ReadAllocatedBytes();
            for (int i = 0; i < MeasuredFrames; i++)
            {
                allFramesWrote &= source.TryWriteValues(output);
            }
            long after = ReadAllocatedBytes();
            yield return null;

            Assert.IsTrue(allFramesWrote);
            Assert.That(
                after - before,
                Is.EqualTo(0L),
                "OverlayInputSource.TryWriteValues per-frame allocation must be zero.");
        }

        private static FacialProfile BuildProfile(OverlaySlotBinding[] angerOverlays)
        {
            var anger = new Expression(
                id: "anger",
                name: "Anger",
                layer: LayerName,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: angerOverlays);
            var neutral = new Expression(
                id: "neutral",
                name: "Neutral",
                layer: LayerName,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: null);

            return new FacialProfile(
                schemaVersion: "1.0",
                layers: new[] { new LayerDefinition(LayerName, 0, ExclusionMode.LastWins) },
                expressions: new[] { anger, neutral },
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: null,
                slots: new[] { BlinkSlot });
        }

        private static OverlayInputSource CreateSource(
            FacialProfile profile,
            IActiveExpressionProvider provider)
        {
            return new OverlayInputSource(
                id: InputSourceId.Parse("overlay:blink"),
                slot: BlinkSlot,
                blendShapeCount: BlendShapeNames.Length,
                blendShapeNames: BlendShapeNames,
                profile: profile,
                activeProvider: provider,
                emotionLayerName: LayerName);
        }

        private static ExpressionSnapshot CreateSnapshot(
            string id,
            params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot(
                id: id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: blendShapes,
                bones: null,
                rendererPaths: new[] { "Face" });
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static long ReadAllocatedBytes()
        {
            return AllocatedBytesReader.Read();
        }

        private delegate long TotalAllocatedBytesReader(bool forceFullCollection);

        private static class AllocatedBytesReader
        {
            private static readonly Func<long> Reader = CreateReader();

            public static long Read()
            {
                return Reader();
            }

            private static Func<long> CreateReader()
            {
                var method = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
                if (method != null)
                {
                    var reader = (TotalAllocatedBytesReader)Delegate.CreateDelegate(
                        typeof(TotalAllocatedBytesReader),
                        method);
                    return () => reader(true);
                }

                return () => GC.GetTotalMemory(false);
            }
        }
    }
}
