using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// AnimationClip 書き出しのオプション（<see cref="FacialAnimationClipExportOptions"/>。HID-422）の契約。
    /// 既定値は従来どおり（レイヤー単位・Timeline のフレームレート）で、指定したフレームレートは Timeline の値より優先する。
    /// </summary>
    [SmallTest]
    public sealed class FacialAnimationClipExportOptionsTests : SizedTestFixture
    {
        [Test]
        public void Default_SplitsPerLayerAndFollowsTimeline()
        {
            FacialAnimationClipExportOptions options = default;

            Assert.That(options.SplitMode, Is.EqualTo(FacialAnimationClipSplitMode.Layer));
            Assert.That(options.ResolveFrameRate(50d), Is.EqualTo(50d));
        }

        [Test]
        public void ResolveFrameRate_NoOverrideAndTimelineHasNone_UsesFallback()
        {
            var options = new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, 0d);

            Assert.That(options.ResolveFrameRate(0d), Is.EqualTo(FacialAnimationTrackConverter.FallbackFrameRate));
        }

        [Test]
        public void ResolveFrameRate_Override_UsesOverrideInsteadOfTimeline()
        {
            var options = new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, 30d);

            Assert.That(options.ResolveFrameRate(60d), Is.EqualTo(30d));
        }

        [TestCase(-30d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ResolveFrameRate_InvalidOverride_FollowsTimeline(double frameRate)
        {
            var options = new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, frameRate);

            Assert.That(options.ResolveFrameRate(60d), Is.EqualTo(60d));
        }
    }
}
