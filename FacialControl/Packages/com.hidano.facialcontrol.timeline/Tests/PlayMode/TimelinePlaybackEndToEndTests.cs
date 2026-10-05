#if UNITY_EDITOR
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// REC Export の出力 TimelineAsset をそのまま使い、4 手順（Export した Timeline を Director にセット / Receiver を
    /// FacialController と同じ GameObject に置く / 録画時と同じ Profile SO / Play）だけで Expression・Analog・Gaze が
    /// SkinnedMeshRenderer と目ボーンに再現されることを固定する end-to-end テスト（受け入れ条件 (1)(3)(4)）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class TimelinePlaybackEndToEndTests : SizedTestFixture
    {
        private TimelineE2EFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void Export_FixtureRecording_ProducesTracksAndLocatorFindsBake()
        {
            _fixture = TimelineE2EFixture.Create();

            Assert.That(_fixture.ExportResult.Success, Is.True);
            BakeLocateResult located = FacialTimelineBakeLocator.Locate(_fixture.Timeline, null);
            Assert.That(located.Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(located.Bake, Is.SameAs(_fixture.ExportResult.BakeAsset));

            FacialValueTrack analog = FindValueTrack(_fixture.Timeline, RecFixtureWriter.AnalogSourceId);
            FacialValueTrack gaze = FindValueTrack(_fixture.Timeline, RecFixtureWriter.GazeSourceId);
            Assert.That(analog, Is.Not.Null, "前提: osc:lt の Value トラックが Export される");
            Assert.That(analog.ChannelKind, Is.EqualTo(FacialValueChannelKind.Analog));
            Assert.That(gaze, Is.Not.Null, "前提: osc:gaze の Value トラックが Export される");
            Assert.That(gaze.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
        }

        private static FacialValueTrack FindValueTrack(TimelineAsset timeline, string channelSubId)
        {
            var tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] is FacialValueTrack valueTrack
                    && string.Equals(valueTrack.ChannelSubId, channelSubId, System.StringComparison.Ordinal))
                {
                    return valueTrack;
                }
            }

            return null;
        }
    }
}
#endif
