#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// 値提供型（REC の kind 7 / 8）を含む REC を Export し、Receiver で Play したときに、各フレームの BlendShape 値が
    /// REC 再生の注入ソース（<see cref="RecPlaybackValueProviderSource"/>）に同じレコードを適用した結果と一致することを固定する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class TimelineValueProviderEndToEndTests : SizedTestFixture
    {
        /// <summary>renderer の BlendShape weight（0..100 スケール）の許容誤差。</summary>
        private const float WeightTolerance = 0.01f;

        private const double FrameSeconds = 1d / 60d;

        private TimelineE2EFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void Export_ValueProviderRecording_ProducesNamedValueProviderTrack()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeValueProvider = true });

            FacialValueTrack track = FindValueTrack(_fixture.Timeline, RecFixtureWriter.ValueProviderSourceId);
            Assert.That(track, Is.Not.Null);
            Assert.That(track.ChannelKind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
            var clip = (FacialValueClip)FirstClip(track).asset;
            Assert.That(clip.BlendShapeIndices, Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(
                clip.BlendShapeNames,
                Is.EqualTo(new[] { TimelineE2EFixture.BlinkBlendShape, TimelineE2EFixture.JawOpenBlendShape, TimelineE2EFixture.EyeWideBlendShape }),
                "REC に記録された録画時の BlendShape 名で保存される");
        }

        [UnityTest]
        public IEnumerator Play_ValueProviderRecording_EveryFrameMatchesRecPlaybackSource()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            string[] blendShapes = TimelineE2EFixture.MeshBlendShapes;
            var expected = new float[blendShapes.Length];
            var mismatches = new StringBuilder();

            int frameCount = (int)Math.Floor(_fixture.Recording.DurationSeconds / FrameSeconds);
            for (int frame = 0; frame < frameCount; frame++)
            {
                double time = frame * FrameSeconds;
                yield return character.EvaluateAt(time);
                if (frame == 0)
                {
                    Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), Describe(character));
                }

                ComputeRecPlaybackWeights(time, blendShapes.Length, expected);
                for (int i = 0; i < blendShapes.Length; i++)
                {
                    if (i == 0 || i == 1)
                    {
                        // Smile / Squint は trigger / analog のチャネル（既存の e2e が固定）。値提供型の比較対象外。
                        continue;
                    }

                    float actual = character.GetBlendShapeWeight(blendShapes[i]);
                    if (Math.Abs(actual - expected[i]) > WeightTolerance)
                    {
                        mismatches.Append($"t={time:0.000} {blendShapes[i]}: timeline={actual:0.###} rec={expected[i]:0.###}\n");
                    }
                }
            }

            Assert.That(mismatches.Length, Is.EqualTo(0), "値提供型の BlendShape が REC 再生と食い違うフレーム:\n" + mismatches);
            Assert.That(
                character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ValueProviderTakeoverAttached, RecFixtureWriter.ValueProviderSourceId),
                Is.True,
                Describe(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ValueProviderBlendShapeMismatch), Is.False);
        }

        [UnityTest]
        public IEnumerator DirectorStop_AfterValueProviderPlayback_RestoresLiveSource()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            FakeValueProviderAdapterBinding live = FindLiveBinding(_fixture);

            yield return character.EvaluateAt(0.1d);
            Assert.That(ResolveRegistry(character, RecFixtureWriter.ValueProviderSourceId), Is.Not.SameAs(live.Source), "前提: 再生中は乗っ取られる");
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.BlinkBlendShape), Is.EqualTo(20f).Within(WeightTolerance), "前提: 基準値 0.2");

            yield return character.StopDirector();

            Assert.That(ResolveRegistry(character, RecFixtureWriter.ValueProviderSourceId), Is.SameAs(live.Source), "停止で live の入力源に戻る");
            live.Source.Set(2, 0.35f);
            yield return null;
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.BlinkBlendShape), Is.EqualTo(35f).Within(WeightTolerance), "live の値に追従する");
        }

        /// <summary>
        /// REC 再生の注入ソースに、基準と時刻 <paramref name="timeSeconds"/> までのレコードを記録順に適用し、
        /// レイヤー weight 1 で renderer に出る weight（0..100）を求める（無効 / 非寄与は 0）。
        /// </summary>
        private static void ComputeRecPlaybackWeights(double timeSeconds, int blendShapeCount, float[] weights)
        {
            var source = new RecPlaybackValueProviderSource(RecFixtureWriter.ValueProviderSourceId, blendShapeCount, null);
            RecFixtureWriter.ValueProviderRecord baseline = RecFixtureWriter.ValueProviderBaseline;
            source.ApplyState(baseline.IsValid, baseline.MaskBytes, baseline.Values);
            IReadOnlyList<RecFixtureWriter.ValueProviderRecord> records = RecFixtureWriter.ValueProviderSamples;
            for (int i = 0; i < records.Count && records[i].TimeSeconds <= timeSeconds; i++)
            {
                source.ApplyState(records[i].IsValid, records[i].MaskBytes, records[i].Values);
            }

            Array.Clear(weights, 0, weights.Length);
            if (!source.TryWriteValues(weights))
            {
                Array.Clear(weights, 0, weights.Length);
                return;
            }

            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = source.ContributeMask[i] ? Mathf.Clamp01(weights[i]) * 100f : 0f;
            }
        }

        private static FakeValueProviderAdapterBinding FindLiveBinding(TimelineE2EFixture fixture)
        {
            foreach (var binding in TimelineE2EFixture.GetWritableAdapterBindings(fixture.ProfileAsset))
            {
                if (binding is FakeValueProviderAdapterBinding valueProvider)
                {
                    return valueProvider;
                }
            }

            Assert.Fail("前提: Profile に値提供型の Fake binding がある");
            return null;
        }

        private static IInputSource ResolveRegistry(TimelineE2ECharacter character, string id)
        {
            return character.Controller.InputSourceRegistry.TryResolve(id, out IInputSource source) ? source : null;
        }

        private static FacialValueTrack FindValueTrack(TimelineAsset timeline, string channelSubId)
        {
            var tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] is FacialValueTrack valueTrack
                    && string.Equals(valueTrack.ChannelSubId, channelSubId, StringComparison.Ordinal))
                {
                    return valueTrack;
                }
            }

            return null;
        }

        private static TimelineClip FirstClip(TrackAsset track)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                return clip;
            }

            Assert.Fail("前提: Clip がある");
            return null;
        }

        private static string Describe(TimelineE2ECharacter character)
        {
            var builder = new StringBuilder();
            builder.Append("state=").Append(character.Receiver.SessionState).Append(" items=[");
            var items = character.Receiver.Diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                builder.Append(items[i].Severity).Append(':').Append(items[i].Code).Append('(').Append(items[i].Subject).Append(") ");
            }

            return builder.Append(']').ToString();
        }
    }
}
#endif
