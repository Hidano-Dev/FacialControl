using System.Text.RegularExpressions;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Edit プレビューの入口（<see cref="FacialTimelineEditorPreview.ApplyPreview"/>）が、Receiver の構成が欠けていても
    /// 例外にせず 1 回だけ警告してプレビューを継続することを固定する（Req 7.3）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class FacialTimelineEditorPreviewTests : SizedTestFixture
    {
        private GameObject _host;
        private TimelineAsset _timeline;
        private FacialTimelineBakeAsset _bake;

        [SetUp]
        public void SetUp()
        {
            TimelineEditorServices.EditWarningGate.ResetEpoch();
            FacialTimelineEditorPreview.ClearCache();
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            var track = _timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            track.Bake = _bake;
        }

        [TearDown]
        public void TearDown()
        {
            FacialTimelineEditorPreview.ClearCache();
            TimelineEditorServices.EditWarningGate.ResetEpoch();
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_timeline != null)
            {
                Object.DestroyImmediate(_timeline);
            }

            if (_bake != null)
            {
                Object.DestroyImmediate(_bake);
            }
        }

        [Test]
        public void ApplyPreview_ReceiverWithoutController_DoesNotThrowAndWarnsOnce()
        {
            _host = new GameObject("PreviewHostWithoutController");
            var receiver = _host.AddComponent<FacialTimelineReceiver>();
            receiver.BakeAsset = _bake;

            // 文言ではなく「Warning が 1 件だけ出る」ことを固定する（2 回目以降は警告ゲートで抑止）。
            LogAssert.Expect(LogType.Warning, new Regex(".*"));

            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.5d));
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.6d));
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.7d));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyPreview_NullReceiver_DoesNothing()
        {
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(null, _timeline, 0.5d));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
