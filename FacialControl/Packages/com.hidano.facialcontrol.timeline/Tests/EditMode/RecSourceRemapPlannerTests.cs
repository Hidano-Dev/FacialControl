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
            new RecRecordedSource("keyboard", RecRecordedSourceUsage.Trigger, 2, new[] { "smile", "angry" }),
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

            Assert.That(Ids(report), Is.EqualTo(new[] { "ifm", "ifm:head", "keyboard", "udp-lipsync" }), "表情トリガーだけの入力源も除外できるよう含める");
            Assert.That(StatusOf(report, "keyboard"), Is.EqualTo(RecRemapStatus.Mapped), "表情トリガーだけの入力源は入力源 id を再生に使わない");
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
            RecRemapReport report = Analyze(RecSourceRemap.Identity, new HashSet<string> { "smile" });

            Assert.That(report.MissingExpressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void Analyze_ExcludedTriggerSource_DoesNotReportItsExpressionsAndKeepsTriggerCount()
        {
            var remap = new RecSourceRemap(new[] { Pair("keyboard", null) }, null);

            RecRemapReport report = Analyze(remap, new HashSet<string>());

            Assert.That(report.MissingExpressionIds, Is.Empty, "書き出さないトリガーの表情は「無い」と報告しない");
            Assert.That(StatusOf(report, "keyboard"), Is.EqualTo(RecRemapStatus.Excluded));
            Assert.That(Find(report, "keyboard").TriggerCount, Is.EqualTo(2), "除外で消える表情 Clip の件数を示す");
        }

        [Test]
        public void Analyze_TwoTriggerSourcesToSameTarget_ReportsCollision()
        {
            var sources = new[]
            {
                new RecRecordedSource("keyboard", RecRecordedSourceUsage.Trigger, 1, new[] { "smile" }),
                new RecRecordedSource("pad", RecRecordedSourceUsage.Trigger, 1, new[] { "smile" }),
            };
            var remap = new RecSourceRemap(new[] { Pair("pad", "keyboard") }, null);

            RecRemapReport report = RecSourceRemapPlanner.Analyze(sources, null, ReceiverSources, ReceiverLayers, null, remap);

            Assert.That(StatusOf(report, "keyboard"), Is.EqualTo(RecRemapStatus.Collision));
            Assert.That(StatusOf(report, "pad"), Is.EqualTo(RecRemapStatus.Collision));
            Assert.That(report.HasErrors, Is.True);
        }

        [Test]
        public void Analyze_TargetIdWithInvalidCharacters_ReportsInvalidIdAsError()
        {
            var remap = new RecSourceRemap(new[] { Pair("ifm", "osc receiver!") }, null);

            RecRemapReport report = Analyze(remap);

            Assert.That(StatusOf(report, "ifm"), Is.EqualTo(RecRemapStatus.InvalidId));
            Assert.That(report.HasErrors, Is.True);
            Assert.That(report.HasCollisions, Is.False);
        }

        private static RecRemapReport Analyze(RecSourceRemap remap, ICollection<string> targetExpressionIds = null)
        {
            return RecSourceRemapPlanner.Analyze(
                SenderSources,
                SenderLayers,
                ReceiverSources,
                ReceiverLayers,
                targetExpressionIds ?? new HashSet<string> { "smile", "angry" },
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
