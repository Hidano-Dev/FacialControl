using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Osc.Editor.AdapterBindings;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC 系 Adapter の Foldout ヘッダー要約（受信ポート・送信先）の文字列組み立てを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class OscAdapterBindingHeaderSummaryFormatterTests : SizedTestFixture
    {
        // ---------------------------------------------------------------
        // 受信
        // ---------------------------------------------------------------

        [Test]
        public void FormatReceiver_ValidPort_ReturnsColonPrefixedPort()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(9001);

            Assert.AreEqual(":9001", summary.Text);
            StringAssert.Contains("9001", summary.Tooltip);
        }

        [Test]
        public void FormatReceiver_OutOfRangePort_MarksInvalid()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(0);

            Assert.AreEqual(":0" + OscAdapterBindingHeaderSummaryFormatter.InvalidMark, summary.Text);
        }

        [Test]
        public void FormatReceiver_FromLegacySettings_TooltipMentionsLegacy()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(9001, fromLegacySettings: true);

            Assert.AreEqual(":9001", summary.Text);
            StringAssert.Contains(OscAdapterBindingHeaderSummaryFormatter.LegacySettingsNote, summary.Tooltip);
        }

        [Test]
        public void FormatReceiver_LegacyDisabled_MarksInvalid()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(
                9001, fromLegacySettings: true, legacyDisabled: true);

            Assert.AreEqual(":9001" + OscAdapterBindingHeaderSummaryFormatter.InvalidMark, summary.Text);
            StringAssert.Contains(OscAdapterBindingHeaderSummaryFormatter.LegacyDisabledNote, summary.Tooltip);
        }

        // ---------------------------------------------------------------
        // 送信
        // ---------------------------------------------------------------

        [Test]
        public void FormatSender_SingleEndpoint_ReturnsHostAndPort()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("127.0.0.1", 9000),
            });

            Assert.AreEqual("127.0.0.1:9000", summary.Text);
            Assert.IsFalse(summary.IsEmpty);
        }

        [Test]
        public void FormatSender_ThreeValidEndpoints_ReturnsFirstAndRemainingCount()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("127.0.0.1", 9000),
                new OscSenderEndpointConfig("192.168.0.10", 9000),
                new OscSenderEndpointConfig("renderer.local", 9012),
            });

            Assert.AreEqual("127.0.0.1:9000 他 2 件", summary.Text);
        }

        [Test]
        public void FormatSender_MultipleEndpoints_TooltipListsAllEndpoints()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("127.0.0.1", 9000),
                new OscSenderEndpointConfig("192.168.0.10", 9001),
                new OscSenderEndpointConfig("renderer.local", 9012, enabled: false),
            });

            StringAssert.Contains("127.0.0.1:9000", summary.Tooltip);
            StringAssert.Contains("192.168.0.10:9001", summary.Tooltip);
            StringAssert.Contains("renderer.local:9012" + OscAdapterBindingHeaderSummaryFormatter.InvalidMark, summary.Tooltip);
        }

        [Test]
        public void FormatSender_InvalidEndpointsFirst_SkipsThemInTextAndCount()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("10.0.0.1", 9000, enabled: false),
                new OscSenderEndpointConfig("10.0.0.2", 70000),
                new OscSenderEndpointConfig("10.0.0.3", 9000),
            });

            Assert.AreEqual("10.0.0.3:9000", summary.Text);
        }

        [Test]
        public void FormatSender_EmptyHost_TreatedAsDefaultEndpointLikeRuntime()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("  ", 9000),
            });

            Assert.AreEqual(OscSenderEndpointConfig.DefaultEndpoint + ":9000", summary.Text);
        }

        [Test]
        public void FormatSender_DuplicateHostPort_CountedOnceLikeRuntime()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("Renderer.local", 9000),
                new OscSenderEndpointConfig(" renderer.local ", 9000),
                new OscSenderEndpointConfig("10.0.0.1", 9000),
            });

            Assert.AreEqual("Renderer.local:9000 他 1 件", summary.Text);
            StringAssert.Contains("renderer.local:9000" + OscAdapterBindingHeaderSummaryFormatter.DuplicateMark, summary.Tooltip);
        }

        [Test]
        public void FormatSender_LegacyDisabled_ReturnsNoDestinationText()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(
                new List<OscSenderEndpointConfig> { new OscSenderEndpointConfig("127.0.0.1", 9000) },
                fromLegacySettings: true,
                legacyDisabled: true);

            Assert.AreEqual(OscAdapterBindingHeaderSummaryFormatter.NoValidDestinationText, summary.Text);
            StringAssert.Contains(OscAdapterBindingHeaderSummaryFormatter.LegacyDisabledNote, summary.Tooltip);
        }

        [Test]
        public void FormatSender_AllInvalid_ReturnsNoDestinationText()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>
            {
                new OscSenderEndpointConfig("127.0.0.1", 9000, enabled: false),
            });

            Assert.AreEqual(OscAdapterBindingHeaderSummaryFormatter.NoValidDestinationText, summary.Text);
        }

        [Test]
        public void FormatSender_EmptyOrNull_ReturnsNoDestinationText()
        {
            var empty = OscAdapterBindingHeaderSummaryFormatter.FormatSender(new List<OscSenderEndpointConfig>());
            var nullList = OscAdapterBindingHeaderSummaryFormatter.FormatSender(null);

            Assert.AreEqual(OscAdapterBindingHeaderSummaryFormatter.NoValidDestinationText, empty.Text);
            Assert.AreEqual(OscAdapterBindingHeaderSummaryFormatter.NoValidDestinationText, nullList.Text);
        }

        [Test]
        public void FormatSender_FromLegacySettings_TooltipMentionsLegacy()
        {
            var summary = OscAdapterBindingHeaderSummaryFormatter.FormatSender(
                new List<OscSenderEndpointConfig> { new OscSenderEndpointConfig("127.0.0.1", 9000) },
                fromLegacySettings: true);

            StringAssert.Contains(OscAdapterBindingHeaderSummaryFormatter.LegacySettingsNote, summary.Tooltip);
        }
    }
}
