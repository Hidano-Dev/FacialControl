using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class RecSourceRemapPlannerTests : SizedTestFixture
    {
        // 送信側（iFacialMocap + UDP リップシンク）で録った REC と、OSC 受信だけのキャラの Profile（HID-412 の事例）。
        private static readonly RecRecordedSource[] SenderSources =
        {
            new RecRecordedSource("ifm", RecRecordedSourceUsage.ValueProvider),
            new RecRecordedSource("ifm:head", RecRecordedSourceUsage.Analog),
            new RecRecordedSource("keyboard", RecRecordedSourceUsage.Trigger),
            new RecRecordedSource("udp-lipsync", RecRecordedSourceUsage.ValueProvider),
        };

        private static readonly string[] SenderLayers = { "lipsync" };
        private static readonly HashSet<string> ReceiverSources = new HashSet<string> { "osc-receiver" };
        private static readonly HashSet<string> ReceiverLayers = new HashSet<string> { "osc-receiver" };

        [Test]
        public void TryMapSource_NoEntry_ReturnsSameId()
        {
            var remap = new RecSourceRemap(new[] { Pair("ifm", "osc-receiver") }, null);

            Assert.That(remap.TryMapSource("udp-lipsync", out string target), Is.True);
            Assert.That(target, Is.EqualTo("udp-lipsync"));
        }

        [Test]
        public void TryMapSource_MappedEntry_ReturnsTargetId()
        {
            var remap = new RecSourceRemap(new[] { Pair("ifm", "osc-receiver") }, null);

            Assert.That(remap.TryMapSource("ifm", out string target), Is.True);
            Assert.That(target, Is.EqualTo("osc-receiver"));
        }

        [Test]
        public void TryMapSource_NullTarget_ReturnsFalseAsExcluded()
        {
            var remap = new RecSourceRemap(new[] { Pair("udp-lipsync", null) }, null);

            Assert.That(remap.TryMapSource("udp-lipsync", out _), Is.False);
        }

        [Test]
        public void TryMapLayer_MappedEntry_ReturnsTargetLayer()
        {
            var remap = new RecSourceRemap(null, new[] { Pair("lipsync", "osc-receiver") });

            Assert.That(remap.TryMapLayer("lipsync", out string target), Is.True);
            Assert.That(target, Is.EqualTo("osc-receiver"));
        }

        [Test]
        public void IsIdentity_OnlySameIdEntries_ReturnsTrue()
        {
            var remap = new RecSourceRemap(new[] { Pair("ifm", "ifm") }, null);

            Assert.That(remap.IsIdentity, Is.True);
            Assert.That(RecSourceRemap.Identity.IsIdentity, Is.True);
        }

        [Test]
        public void IsIdentity_ExcludedEntry_ReturnsFalse()
        {
            var remap = new RecSourceRemap(new[] { Pair("ifm", null) }, null);

            Assert.That(remap.IsIdentity, Is.False);
        }

        [Test]
        public void Analyze_IdentityOnDifferentCharacter_ReportsValueSourcesAndLayersAsUnresolved()
        {
            RecRemapReport report = Analyze(RecSourceRemap.Identity);

            Assert.That(Ids(report), Is.EqualTo(new[] { "ifm", "ifm:head", "udp-lipsync" }), "トリガー専用の入力源は含めない");
            Assert.That(StatusOf(report, "ifm"), Is.EqualTo(RecRemapStatus.NotInTarget));
            Assert.That(StatusOf(report, "udp-lipsync"), Is.EqualTo(RecRemapStatus.NotInTarget));
            Assert.That(StatusOf(report, "ifm:head"), Is.EqualTo(RecRemapStatus.Unchecked), "Analog だけの入力源はレイヤーの宣言では確かめられない");
            Assert.That(report.Layers[0].Status, Is.EqualTo(RecRemapStatus.NotInTarget));
            Assert.That(report.HasUnresolved, Is.True);
            Assert.That(report.HasCollisions, Is.False);
        }

        [Test]
        public void Analyze_MappedToReceiverAndRestExcluded_HasNoProblems()
        {
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", "osc-receiver"), Pair("udp-lipsync", null), Pair("ifm:head", null) },
                new[] { Pair("lipsync", "osc-receiver") });

            RecRemapReport report = Analyze(remap);

            Assert.That(StatusOf(report, "ifm"), Is.EqualTo(RecRemapStatus.Mapped));
            Assert.That(TargetOf(report, "ifm"), Is.EqualTo("osc-receiver"));
            Assert.That(StatusOf(report, "udp-lipsync"), Is.EqualTo(RecRemapStatus.Excluded));
            Assert.That(TargetOf(report, "udp-lipsync"), Is.Null);
            Assert.That(report.Layers[0].Status, Is.EqualTo(RecRemapStatus.Mapped));
            Assert.That(report.HasUnresolved, Is.False);
            Assert.That(report.HasCollisions, Is.False);
        }

        [Test]
        public void Analyze_TwoValueSourcesToSameTarget_ReportsCollisionOnBoth()
        {
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", "osc-receiver"), Pair("udp-lipsync", "osc-receiver") },
                null);

            RecRemapReport report = Analyze(remap);

            Assert.That(StatusOf(report, "ifm"), Is.EqualTo(RecRemapStatus.Collision));
            Assert.That(StatusOf(report, "udp-lipsync"), Is.EqualTo(RecRemapStatus.Collision));
            Assert.That(report.HasCollisions, Is.True);
        }

        [Test]
        public void Analyze_SourceMappedOntoAnotherRecordedId_ReportsCollision()
        {
            // ifm を udp-lipsync へ付け替えると、そのままの udp-lipsync と同じ id になる。
            var remap = new RecSourceRemap(new[] { Pair("ifm", "udp-lipsync") }, null);

            RecRemapReport report = Analyze(remap);

            Assert.That(StatusOf(report, "ifm"), Is.EqualTo(RecRemapStatus.Collision));
            Assert.That(StatusOf(report, "udp-lipsync"), Is.EqualTo(RecRemapStatus.Collision));
        }

        [Test]
        public void Analyze_TriggeredExpressionMissingInTarget_ListsExpressionId()
        {
            RecRemapReport report = RecSourceRemapPlanner.Analyze(
                SenderSources,
                SenderLayers,
                new[] { "smile", "angry", "smile" },
                ReceiverSources,
                ReceiverLayers,
                new HashSet<string> { "smile" },
                RecSourceRemap.Identity);

            Assert.That(report.MissingExpressionIds, Is.EqualTo(new[] { "angry" }));
        }

        private static RecRemapReport Analyze(RecSourceRemap remap)
        {
            return RecSourceRemapPlanner.Analyze(
                SenderSources,
                SenderLayers,
                null,
                ReceiverSources,
                ReceiverLayers,
                null,
                remap);
        }

        private static string[] Ids(RecRemapReport report)
        {
            var ids = new string[report.Sources.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = report.Sources[i].RecordedId;
            }

            return ids;
        }

        private static RecRemapStatus StatusOf(RecRemapReport report, string recordedId)
        {
            return Find(report, recordedId).Status;
        }

        private static string TargetOf(RecRemapReport report, string recordedId)
        {
            return Find(report, recordedId).TargetId;
        }

        private static RecRemapSourceEntry Find(RecRemapReport report, string recordedId)
        {
            for (int i = 0; i < report.Sources.Count; i++)
            {
                if (report.Sources[i].RecordedId == recordedId)
                {
                    return report.Sources[i];
                }
            }

            Assert.Fail($"'{recordedId}' is not in the report.");
            return default;
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
