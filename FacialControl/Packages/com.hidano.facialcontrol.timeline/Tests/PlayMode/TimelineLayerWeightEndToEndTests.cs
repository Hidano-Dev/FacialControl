#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// レイヤー weight（発話ゲート等が <c>FacialController.SetLayerWeight</c> で書く inter-layer weight）を含む REC を Export し、
    /// Receiver で Play したときに REC 再生と同じタイミングでレイヤー weight が変わることを固定する（HID-182）。
    /// </summary>
    /// <remarks>
    /// 記録: trigger（smile, emotion レイヤー）が 0.2〜0.8 秒 on、emotion のレイヤー weight が 0.41 秒で 0、0.61 秒で 1。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class TimelineLayerWeightEndToEndTests : SizedTestFixture
    {
        /// <summary>renderer の BlendShape weight（0..100 スケール）の許容誤差。</summary>
        private const float WeightTolerance = 0.01f;

        private TimelineE2EFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void Export_LayerWeightRecording_ProducesLayerWeightTrack()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeLayerWeights = true });

            var tracks = TimelineAssetScanner.CollectLayerWeightTracks(_fixture.Timeline);
            Assert.That(tracks.Count, Is.EqualTo(1));
            Assert.That(tracks[0].LayerName, Is.EqualTo(TimelineE2EFixture.EmotionLayer));
            Assert.That(_fixture.Bake, Is.Not.Null, "レイヤー weight トラックがあっても Bake は通常どおり作られる");
        }

        [UnityTest]
        public IEnumerator Play_LayerWeightRecording_FollowsRecordedLayerWeightEvenWhenLiveWrites()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeLayerWeights = true });
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return character.EvaluateAt(0.35d);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(character.Receiver.IsLayerWeightOverrideActive, Is.True);
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape),
                Is.EqualTo(TimelineE2EFixture.SmileExpressionValue * 100f).Within(WeightTolerance),
                "記録の weight 1 区間");

            yield return character.EvaluateAt(0.5d);
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape),
                Is.EqualTo(0f).Within(WeightTolerance),
                "記録の weight 0 区間はレイヤーが消える（発話ゲートが閉じた区間と同じ）");

            // live の書き込み（無音中の発話ゲートが毎フレーム 0 を書くのと同じ）は再生中は効かない。
            character.Controller.SetLayerWeight(TimelineE2EFixture.EmotionLayer, 0f);
            yield return character.EvaluateAt(0.7d);
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape),
                Is.EqualTo(TimelineE2EFixture.SmileExpressionValue * 100f).Within(WeightTolerance),
                "記録の weight 1 区間は live の 0 に上書きされない");
        }

        [UnityTest]
        public IEnumerator DirectorStop_AfterLayerWeightPlayback_RestoresWeightAndResumesLiveWrites()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { IncludeLayerWeights = true });
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            IWeightInjectionGate gate = character.Controller.WeightInjectionGate;
            character.Controller.SetLayerWeight(TimelineE2EFixture.EmotionLayer, 0.6f);

            yield return character.EvaluateAt(0.5d);
            Assert.That(gate.IsLiveWeightSuspended, Is.True, "前提: 再生中は live の weight を止める");
            Assert.That(GetLayerWeight(gate, TimelineE2EFixture.EmotionLayer), Is.EqualTo(0f), "前提: 記録の weight");

            yield return character.StopDirector();

            Assert.That(character.Receiver.IsLayerWeightOverrideActive, Is.False);
            Assert.That(gate.IsLiveWeightSuspended, Is.False, "停止で live の書き込みを再開する");
            Assert.That(GetLayerWeight(gate, TimelineE2EFixture.EmotionLayer), Is.EqualTo(0.6f), "再生前の weight に戻す");
            character.Controller.SetLayerWeight(TimelineE2EFixture.EmotionLayer, 0.3f);
            Assert.That(GetLayerWeight(gate, TimelineE2EFixture.EmotionLayer), Is.EqualTo(0.3f), "live の書き込みが効く");
        }

        [UnityTest]
        public IEnumerator Play_RecordingWithoutLayerWeights_LeavesLiveWeightsUntouched()
        {
            _fixture = TimelineE2EFixture.Create();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return character.EvaluateAt(0.5d);

            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(character.Receiver.IsLayerWeightOverrideActive, Is.False);
            Assert.That(character.Controller.WeightInjectionGate.IsLiveWeightSuspended, Is.False, "レイヤー weight トラックが無い Timeline は live を止めない");
        }

        private static float GetLayerWeight(IWeightInjectionGate gate, string layerName)
        {
            var buffer = new List<LayerWeightEntry>();
            gate.CollectLayerWeights(buffer);
            foreach (LayerWeightEntry entry in buffer)
            {
                if (entry.LayerName == layerName)
                {
                    return entry.Weight;
                }
            }

            Assert.Fail($"前提: レイヤー '{layerName}' がある");
            return float.NaN;
        }
    }
}
#endif
