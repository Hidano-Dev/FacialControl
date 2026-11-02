using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 送信側（iFacialMocap + UDP リップシンク）で録った REC を、OSC 受信だけのキャラの Profile へ対応表で付け替えて Export する（HID-412）。
    /// </summary>
    [MediumTest]
    public sealed class RecSourceRemapExportTests : SizedTestFixture
    {
        private const string ReceiverId = "osc-receiver";

        private string _folderPath;
        private RecTimelineExportWindow _window;

        [SetUp]
        public void SetUp()
        {
            string folderName = "RecSourceRemapExportTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            _folderPath = "Assets/" + folderName;
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (!string.IsNullOrEmpty(_folderPath) && AssetDatabase.IsValidFolder(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
            }

            _folderPath = null;
        }

        [Test]
        public void TryExportTimelineAsset_SenderRecWithRemapToReceiver_TracksTargetReceiverSourceAndLayer()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                _folderPath + "/Exported.playable",
                out RecToTimelineExporter.ExportResult result,
                sourceRemap: CreateReceiverRemap());

            Assert.That(success, Is.True);
            Assert.That(ValueTrackIds(result.Timeline), Is.EqualTo(new[] { ReceiverId }), "ifm だけを osc-receiver へ付け替え、ほかは除外");
            Assert.That(LayerWeightTrackNames(result.Timeline), Is.EqualTo(new[] { ReceiverId }));

            FacialValueTrack valueTrack = FindValueTrack(result.Timeline, ReceiverId);
            Assert.That(valueTrack.ChannelKind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
            var clip = (FacialValueClip)First(valueTrack.GetClips()).asset;
            Assert.That(clip.BlendShapeNames, Is.EqualTo(new[] { "eyeBlinkLeft", "jawOpen" }), "BlendShape は名前で対応付く");

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(
                TimelineAssetScanner.Scan(result.Timeline).Tracks, TimelineProfileSource.Resolve(profile));
            Assert.That(derivation.InvalidChannelSubIds, Is.Empty);
        }

        [Test]
        public void TryExportTimelineAsset_IdentityRemapOnReceiver_WarnsUnresolvedSourcesAndLayers()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RecToTimelineExporter\].*does not play back(.|\n)*'ifm'(.|\n)*'lipsync'"));

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                _folderPath + "/Exported.playable",
                out RecToTimelineExporter.ExportResult result,
                sourceRemap: RecSourceRemap.Identity);

            Assert.That(success, Is.True, "未対応があっても書き出しは続ける（Console と Export ウィンドウで示す）");
            Assert.That(ValueTrackIds(result.Timeline), Does.Contain("ifm"));
        }

        [Test]
        public void TryExportTimelineAsset_RemapWithCollision_FailsWithoutCreatingAsset()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            string outputPath = _folderPath + "/Exported.playable";
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", ReceiverId), Pair("udp-lipsync", ReceiverId) },
                null);
            LogAssert.Expect(LogType.Error, new Regex(@"\[RecToTimelineExporter\].*collisions"));

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                outputPath,
                out RecToTimelineExporter.ExportResult result,
                sourceRemap: remap);

            Assert.That(success, Is.False);
            Assert.That(result.Success, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<TimelineAsset>(outputPath), Is.Null);
        }

        [Test]
        public void ExportTo_WindowRemapChoices_ListsUnresolvedThenExportsToReceiver()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);

            _window.RefreshDetections();

            Assert.That(_window.RemapReport, Is.Not.Null);
            Assert.That(_window.RemapProblemText, Does.Contain("'ifm'").And.Contain("'udp-lipsync'").And.Contain("'lipsync'"),
                "既定（そのまま）では対象 Profile に無い入力源・レイヤーを Export 前に示す");

            _window.SetRemapChoice(false, "ifm", ReceiverId);
            _window.SetRemapChoice(false, "udp-lipsync", null);
            _window.SetRemapChoice(false, "ifm:head", null);
            _window.SetRemapChoice(true, "lipsync", ReceiverId);

            Assert.That(_window.RemapProblemText, Is.Empty);
            Assert.That(_window.ExportTo(_folderPath + "/Window.playable"), Is.True);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(_folderPath + "/Window.playable");
            Assert.That(ValueTrackIds(timeline), Is.EqualTo(new[] { ReceiverId }));
            Assert.That(LayerWeightTrackNames(timeline), Is.EqualTo(new[] { ReceiverId }));
        }

        [Test]
        public void ExportTo_WindowRemapCollision_RefusesExport()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            _window.SetRemapChoice(false, "ifm", ReceiverId);
            _window.SetRemapChoice(false, "udp-lipsync", ReceiverId);

            Assert.That(_window.RemapReport.HasCollisions, Is.True);
            Assert.That(_window.ExportTo(_folderPath + "/Window.playable"), Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<TimelineAsset>(_folderPath + "/Window.playable"), Is.Null);
        }

        private static RecSourceRemap CreateReceiverRemap()
        {
            return new RecSourceRemap(
                new[] { Pair("ifm", ReceiverId), Pair("udp-lipsync", null), Pair("ifm:head", null) },
                new[] { Pair("lipsync", ReceiverId) });
        }

        /// <summary>OSC 受信だけのキャラ: レイヤーも入力源も <c>osc-receiver</c> だけ。</summary>
        private FacialCharacterProfileSO CreateReceiverProfile()
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profile.SchemaVersion = "1.0.0";
            var layer = new LayerDefinitionSerializable
            {
                name = ReceiverId,
                priority = 0,
                exclusionMode = ExclusionMode.Blend,
            };
            layer.inputSources.Add(new InputSourceDeclarationSerializable { id = ReceiverId, weight = 1f });
            profile.Layers.Add(layer);
            AssetDatabase.CreateAsset(profile, _folderPath + "/ReceiverProfile.asset");
            return profile;
        }

        /// <summary>送信側の REC: 値提供型 ifm / udp-lipsync、Analog ifm:head、レイヤー lipsync の weight。</summary>
        private string WriteSenderRecording()
        {
            var baseline = new RecBaselineState(
                    null,
                    null,
                    new[]
                    {
                        new RecBaselineState.ValueProviderEntry("ifm", true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f }),
                        new RecBaselineState.ValueProviderEntry("udp-lipsync", true, new byte[] { 0b0000_0010 }, new[] { 0.3f }),
                    },
                    null,
                    new[] { new LayerWeightEntry("lipsync", 1f) },
                    null)
                .WithBlendShapeNames(new[] { "eyeBlinkLeft", "jawOpen" });
            var timeline = new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateAnalogSample(0.20d, 1, 3),
                    RecEvent.CreateLayerWeightSample(0.30d, 0),
                    RecEvent.CreateValueProviderSample(0.50d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 2, 0),
                },
                new[] { "ifm", "ifm:head", "udp-lipsync" },
                Array.Empty<string>(),
                new[] { "lipsync" },
                1.0d,
                new IReadOnlyList<float>[]
                {
                    new[] { 0.1f, 0.2f, 0.3f },
                    new[] { 0.5f },
                    new[] { 0.6f, 0.7f },
                },
                new IReadOnlyList<byte>[] { Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>() });

            string path = _folderPath + "/sender.fcrec";
            File.WriteAllBytes(path, RecBinaryFormat.Serialize(timeline, 123L));
            return Path.GetFullPath(path);
        }

        private static string[] ValueTrackIds(TimelineAsset timeline)
        {
            var ids = new List<string>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialValueTrack valueTrack)
                {
                    ids.Add(valueTrack.ChannelSubId);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        private static string[] LayerWeightTrackNames(TimelineAsset timeline)
        {
            var names = new List<string>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialLayerWeightTrack weightTrack)
                {
                    names.Add(weightTrack.LayerName);
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        private static FacialValueTrack FindValueTrack(TimelineAsset timeline, string channelSubId)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialValueTrack valueTrack && valueTrack.ChannelSubId == channelSubId)
                {
                    return valueTrack;
                }
            }

            Assert.Fail($"No value track '{channelSubId}'.");
            return null;
        }

        private static TimelineClip First(IEnumerable<TimelineClip> clips)
        {
            foreach (TimelineClip clip in clips)
            {
                return clip;
            }

            Assert.Fail("Track has no clip.");
            return null;
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
