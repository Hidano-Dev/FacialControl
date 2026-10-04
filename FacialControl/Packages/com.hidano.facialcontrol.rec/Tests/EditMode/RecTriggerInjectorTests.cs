using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecTriggerInjectorTests : SizedTestFixture
    {
        [Test]
        public void TryBeginInjection_AllTriggerSources_AreSuspendedAndBaselineApplied()
        {
            var primary = CreateTriggerSource("input:primary");
            var secondary = CreateTriggerSource("input:secondary");
            primary.TriggerOn("smile");
            secondary.TriggerOn("smile");

            var injector = CreateInjector(
                id =>
                {
                    if (id == primary.Id)
                    {
                        return primary;
                    }

                    if (id == secondary.Id)
                    {
                        return secondary;
                    }

                    return null;
                },
                () => new[] { primary, secondary });

            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:primary", new[] { "angry" }),
                },
                null);

            Assert.That(injector.TryBeginInjection(baseline), Is.True);

            Assert.That(primary.IsTriggerInputSuspended, Is.True);
            Assert.That(secondary.IsTriggerInputSuspended, Is.True);
            Assert.That(primary.ActiveExpressionIds, Is.EqualTo(new[] { "angry" }));
            Assert.That(primary.ReadCurrentValues(), Is.EqualTo(new[] { 0.75f }));
            Assert.That(secondary.ActiveExpressionIds, Is.Empty);
            Assert.That(secondary.TryWriteValues(new float[1]), Is.False);
        }

        [Test]
        public void TryBeginInjection_WhenBaselineContainsMissingSource_LogsDistinctWarningAndSkips()
        {
            var source = CreateTriggerSource("input:primary");
            var injector = CreateInjector(
                id => id == source.Id ? source : null,
                () => new[] { source });

            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:primary", new[] { "smile" }),
                    new RecBaselineState.TriggerEntry("missing:trigger", new[] { "angry" }),
                },
                null);

            LogAssert.Expect(LogType.Warning, new Regex("trigger.*'missing:trigger'"));

            Assert.That(injector.TryBeginInjection(baseline), Is.True);

            Assert.That(source.IsTriggerInputSuspended, Is.True);
            Assert.That(source.ActiveExpressionIds, Is.EqualTo(new[] { "smile" }));
        }

        [Test]
        public void InjectTriggerOnAndOff_DuringInjection_DrivesOriginalTriggerSource()
        {
            var source = CreateTriggerSource("input:trigger");
            var injector = CreateInjector(
                id => id == source.Id ? source : null,
                () => new[] { source });

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);
            injector.InjectTriggerOn("input:trigger", "smile");
            injector.InjectTriggerOn("input:trigger", "angry");
            injector.InjectTriggerOff("input:trigger", "smile");

            Assert.That(source.ActiveExpressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void EndInjection_WhenSourceWasRemovedAfterBegin_ResumesCapturedInstance()
        {
            var source = CreateTriggerSource("input:trigger");
            bool isRegistered = true;
            var injector = CreateInjector(
                id => isRegistered && id == source.Id ? source : null,
                () => isRegistered ? new[] { source } : Array.Empty<TestTriggerSource>());

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);
            isRegistered = false;

            injector.EndInjection();

            Assert.That(source.IsTriggerInputSuspended, Is.False);
        }

        [Test]
        public void TryBeginInjection_WhenReentered_ReleasesPreviousSnapshotBeforeReacquiring()
        {
            var first = CreateTriggerSource("input:first");
            var second = CreateTriggerSource("input:second");
            var sources = new List<TestTriggerSource> { first };
            var injector = CreateInjector(
                id =>
                {
                    for (int i = 0; i < sources.Count; i++)
                    {
                        if (sources[i].Id == id)
                        {
                            return sources[i];
                        }
                    }

                    return null;
                },
                () => sources.ToArray());

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);
            sources.Clear();
            sources.Add(second);

            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:second", new[] { "angry" }),
                },
                null);

            Assert.That(injector.TryBeginInjection(baseline), Is.True);

            Assert.That(first.IsTriggerInputSuspended, Is.False);
            Assert.That(second.IsTriggerInputSuspended, Is.True);
            Assert.That(second.ActiveExpressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void TryBeginInjection_WhenReenteredWithSameSource_ReestablishesBaseline()
        {
            var source = CreateTriggerSource("input:trigger");
            var injector = CreateInjector(
                id => id == source.Id ? source : null,
                () => new[] { source });

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);
            injector.InjectTriggerOn("input:trigger", "smile");

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);

            Assert.That(source.IsTriggerInputSuspended, Is.True);
            Assert.That(source.ActiveExpressionIds, Is.Empty);

            injector.EndInjection();

            Assert.That(source.IsTriggerInputSuspended, Is.False);
        }

        private static RecTriggerInjector CreateInjector(
            Func<string, TestTriggerSource> resolveSource,
            Func<IReadOnlyList<TestTriggerSource>> getAllSources)
        {
            return new RecTriggerInjector(
                sourceId => resolveSource(sourceId),
                () => getAllSources());
        }

        private static TestTriggerSource CreateTriggerSource(string sourceId)
        {
            return new TestTriggerSource(
                sourceId,
                new FacialProfile(
                    "1.0.0",
                    new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                    new[]
                    {
                        new Expression("smile", "Smile", "emotion", blendShapeValues: new[] { new BlendShapeMapping("face", 0.25f) }),
                        new Expression("angry", "Angry", "emotion", blendShapeValues: new[] { new BlendShapeMapping("face", 0.75f) }),
                    }));
        }

        private sealed class TestTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestTriggerSource(string sourceId, FacialProfile profile)
                : base(
                    InputSourceId.Parse(sourceId),
                    blendShapeCount: 1,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: new[] { "face" },
                    profile: profile)
            {
            }

            public float[] ReadCurrentValues()
            {
                var values = new float[BlendShapeCount];
                TryWriteValues(values);
                return values;
            }
        }
    }
}
