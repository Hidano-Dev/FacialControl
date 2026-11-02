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
        public void BuildSourceRemap_CustomSourceIdOutsideLayerDeclarations_IsKeptAndAnalogIsUnchecked()
        {
            // Analog / 目線は AdapterBinding が registry に直接登録するため（例: iFacialMocap の ifm:head）、
            // レイヤーの inputSources に無い id へも付け替えられる。
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            _window.SetRemapChoice(false, "ifm:head", "other:head");
            _window.RefreshDetections();

            Assert.That(_window.BuildSourceRemap().TryMapSource("ifm:head", out string target), Is.True);
            Assert.That(target, Is.EqualTo("other:head"), "読み込み直しても直接入力した対応先を残す");
            RecRemapSourceEntry entry = default;
            foreach (RecRemapSourceEntry candidate in _window.RemapReport.Sources)
            {
                if (candidate.RecordedId == "ifm:head")
                {
                    entry = candidate;
                }
            }

            Assert.That(entry.Status, Is.EqualTo(RecRemapStatus.Unchecked));
        }

        [Test]
        public void TryExportTimelineAsset_RemappedSourceWithKindOverride_DetectsKindByRecordedId()
        {
            // 種別の判定と上書きは REC 上の元の id（ifm:head）で行い、付け替えは出力の id（osc:gaze）にだけ効く。
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", ReceiverId), Pair("udp-lipsync", null), Pair("ifm:head", "osc:gaze") },
                new[] { Pair("lipsync", ReceiverId) });

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                _folderPath + "/Exported.playable",
                out RecToTimelineExporter.ExportResult result,
                sourceKindOverrides: new Dictionary<string, FacialValueChannelKind> { { "ifm:head", FacialValueChannelKind.Gaze } },
                sourceRemap: remap);

            Assert.That(success, Is.True);
            ChannelDetection gaze = default;
            foreach (ChannelDetection detection in result.ChannelDetections)
            {
                Assert.That(detection.SourceId, Is.Not.EqualTo("ifm:head"), "出力は付け替え後の id");
                if (detection.SourceId == "osc:gaze")
                {
                    gaze = detection;
                }
            }

            Assert.That(gaze.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(gaze.Reason, Is.EqualTo(ChannelDetectionReason.Overridden));
        }

        [Test]
        public void TryExportTimelineAsset_GazeHintMatchesOnlyRemappedId_ExportsAsGaze()
        {
            // 元の id（ifm:head）には Gaze の手がかりが無く、付け替え後の id（osc:gaze）が Profile の Gaze チャネル（既定 "gaze"）の
            // 規約 id に合う。どちらかが合えば Gaze とする。
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", ReceiverId), Pair("udp-lipsync", null), Pair("ifm:head", "osc:gaze") },
                new[] { Pair("lipsync", ReceiverId) });

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                _folderPath + "/Exported.playable",
                out RecToTimelineExporter.ExportResult result,
                sourceRemap: remap);

            Assert.That(success, Is.True);
            ChannelDetection gaze = FindDetection(result.ChannelDetections, "osc:gaze");
            Assert.That(gaze.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(gaze.Reason, Is.EqualTo(ChannelDetectionReason.ConventionGazeChannel));
        }

        [Test]
        public void DetectChannels_RemappedSequence_UsesRecordedIdFirstForOverridesThenRemappedId()
        {
            // DetectChannels / CreateTimelineAsset / PopulateTimeline のどの入口でも、付け替えた列は同じ判定になる。
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            RecTimeline recording = ReadSenderRecording();
            var remap = new RecSourceRemap(new[] { Pair("ifm:head", "osc:head") }, null);
            var sequence = new RecEventSequenceAdapter(recording, remap);

            ChannelDetection byRemappedId = FindDetection(
                RecToTimelineExporter.DetectChannels(
                    sequence, profile, new Dictionary<string, FacialValueChannelKind> { { "osc:head", FacialValueChannelKind.Gaze } }),
                "osc:head");
            ChannelDetection recordedIdWins = FindDetection(
                RecToTimelineExporter.DetectChannels(
                    sequence,
                    profile,
                    new Dictionary<string, FacialValueChannelKind>
                    {
                        { "ifm:head", FacialValueChannelKind.Analog },
                        { "osc:head", FacialValueChannelKind.Gaze },
                    }),
                "osc:head");

            Assert.That(byRemappedId.Kind, Is.EqualTo(FacialValueChannelKind.Gaze), "付け替え後の id でも上書きできる");
            Assert.That(recordedIdWins.Kind, Is.EqualTo(FacialValueChannelKind.Analog), "両方あれば元の id を優先する");
            Assert.That(recordedIdWins.Reason, Is.EqualTo(ChannelDetectionReason.Overridden));
        }

        [Test]
        public void TryExportTimelineAsset_RemapEntryForSourceNotInRec_DoesNotChangeKindOfExistingSource()
        {
            // REC に無い id（unused）から実在する ifm:head への対応があっても、ifm:head の種別判定は ifm:head のまま行う。
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            var remap = new RecSourceRemap(
                new[] { Pair("ifm", ReceiverId), Pair("udp-lipsync", null), Pair("unused", "ifm:head") },
                new[] { Pair("lipsync", ReceiverId) });

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                recordingPath,
                profile,
                _folderPath + "/Exported.playable",
                out RecToTimelineExporter.ExportResult result,
                sourceKindOverrides: new Dictionary<string, FacialValueChannelKind> { { "ifm:head", FacialValueChannelKind.Gaze } },
                sourceRemap: remap);

            Assert.That(success, Is.True);
            ChannelDetection head = default;
            foreach (ChannelDetection detection in result.ChannelDetections)
            {
                if (detection.SourceId == "ifm:head")
                {
                    head = detection;
                }
            }

            Assert.That(head.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
        }

        [Test]
        public void ExportTo_RecChangedAfterLoading_RefusesUntilReloadAndThenResetsChoices()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();
            _window.SetRemapChoice(false, "ifm", null);

            // 別の内容の REC を同じパスへ書き直す（長さを変えて識別子を確実に変える）。
            WriteSenderRecording(durationSeconds: 2.0d);
            File.SetLastWriteTimeUtc(recordingPath, DateTime.UtcNow.AddMinutes(1));

            Assert.That(_window.ExportTo(_folderPath + "/Window.playable"), Is.False, "読み込み時と違う REC には選択を当てない");
            Assert.That(_window.StatusText, Does.Contain("Reload Sources"));

            _window.RefreshDetections();

            Assert.That(_window.BuildSourceRemap().IsIdentity, Is.True, "REC が変わったら前の REC の選択（除外を含む）を引き継がない");
        }

        [Test]
        public void RefreshDetections_ProfileChanged_ResetsChoices()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();
            _window.SetRemapChoice(false, "ifm", null);

            _window.SetInputs(recordingPath, null);
            _window.RefreshDetections();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            Assert.That(_window.BuildSourceRemap().IsIdentity, Is.True, "Profile を外した間の見えない選択を持ち越さない");
        }

        [Test]
        public void ExportTo_InvalidCustomSourceId_ShowsProblemAndRefusesExport()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            _window.SetRemapChoice(false, "ifm", "osc receiver!");

            Assert.That(_window.RemapReport.HasErrors, Is.True);
            Assert.That(_window.RemapProblemText, Does.Contain("'osc receiver!'"));
            Assert.That(_window.ExportTo(_folderPath + "/Window.playable"), Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<TimelineAsset>(_folderPath + "/Window.playable"), Is.Null);
        }

        [Test]
        public void ExportTo_ExcludedTriggerSource_ListsDroppedTriggersAndExportsNoExpressionClip()
        {
            FacialCharacterProfileSO profile = CreateReceiverProfile();
            string recordingPath = WriteSenderRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            RecRemapSourceEntry keyboard = default;
            foreach (RecRemapSourceEntry entry in _window.RemapReport.Sources)
            {
                if (entry.RecordedId == "keyboard")
                {
                    keyboard = entry;
                }
            }

            Assert.That(keyboard.TriggerCount, Is.EqualTo(1), "表情トリガーだけの入力源も一覧に出す");

            _window.SetRemapChoice(false, "keyboard", null);
            Assert.That(_window.RemapProblemText, Does.Contain("表情トリガー 1 件"));

            LogAssert.Expect(LogType.Warning, new Regex(@"\[RecToTimelineExporter\].*does not play back(.|\n)*表情トリガー 1 件"));
            Assert.That(_window.ExportTo(_folderPath + "/Window.playable"), Is.True);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(_folderPath + "/Window.playable");
            Assert.That(ExpressionClipCount(timeline), Is.EqualTo(0), "除外した入力源の表情 Clip は書き出さない");
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
            profile.Expressions.Add(new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = ReceiverId,
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable { name = "jawOpen", value = 1f },
                },
            });
            AssetDatabase.CreateAsset(profile, _folderPath + "/ReceiverProfile.asset");
            return profile;
        }

        /// <summary>
        /// 送信側の REC: 値提供型 ifm / udp-lipsync、2 軸の Analog ifm:head、表情トリガー keyboard（smile）、レイヤー lipsync の weight。
        /// </summary>
        private string WriteSenderRecording(string fileName = "sender.fcrec", double durationSeconds = 1.0d)
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
                .WithBlendShapeNames(new[] { "eyeBlinkLeft", "jawOpen" })
                .WithGazeEyeBonePaths("Armature/Head/LeftEye", "Armature/Head/RightEye");
            var timeline = new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 3, 0),
                    RecEvent.CreateAnalogSample(0.20d, 1, 2),
                    RecEvent.CreateLayerWeightSample(0.30d, 0),
                    RecEvent.CreateValueProviderSample(0.50d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 2, 0),
                    RecEvent.CreateTriggerOff(0.60d, 3, 0),
                },
                new[] { "ifm", "ifm:head", "udp-lipsync", "keyboard" },
                new[] { "smile" },
                new[] { "lipsync" },
                durationSeconds,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new[] { 0.1f, 0.2f },
                    new[] { 0.5f },
                    new[] { 0.6f, 0.7f },
                    Array.Empty<float>(),
                },
                new IReadOnlyList<byte>[]
                {
                    Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(),
                });

            string path = _folderPath + "/" + fileName;
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

        private RecTimeline ReadSenderRecording()
        {
            Assert.That(RecBinaryFormat.TryRead(File.ReadAllBytes(WriteSenderRecording()), out RecBinaryFormat.ReadResult read, out string error), Is.True, error);
            return read.Timeline;
        }

        private static ChannelDetection FindDetection(IEnumerable<ChannelDetection> detections, string sourceId)
        {
            foreach (ChannelDetection detection in detections)
            {
                if (detection.SourceId == sourceId)
                {
                    return detection;
                }
            }

            Assert.Fail($"No detection for '{sourceId}'.");
            return default;
        }

        private static int ExpressionClipCount(TimelineAsset timeline)
        {
            int count = 0;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialExpressionTrack)
                {
                    foreach (TimelineClip unused in track.GetClips())
                    {
                        count++;
                    }
                }
            }

            return count;
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
