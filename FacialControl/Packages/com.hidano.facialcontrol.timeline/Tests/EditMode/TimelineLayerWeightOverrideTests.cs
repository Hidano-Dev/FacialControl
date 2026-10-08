using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineLayerWeightOverrideTests : SizedTestFixture
    {
        [Test]
        public void Begin_GateAvailable_SuspendsLiveWeightsAndResetsLayersToDeclared()
        {
            var gate = new FakeWeightGate(("emotion", 0.4f), ("lipsync", 0f));
            var weightOverride = new TimelineLayerWeightOverride();

            bool begun = weightOverride.Begin(gate);

            Assert.That(begun, Is.True);
            Assert.That(weightOverride.IsActive, Is.True);
            Assert.That(gate.IsLiveWeightSuspended, Is.True, "live の weight 書き込み（発話ゲート等）を止める");
            Assert.That(gate.LayerWeights["emotion"], Is.EqualTo(1f), "トラックの無いレイヤーは宣言値 1 で再生する（REC 再生と同じ）");
            Assert.That(gate.LayerWeights["lipsync"], Is.EqualTo(1f));
        }

        [Test]
        public void Apply_Active_InjectsLayerWeight()
        {
            var gate = new FakeWeightGate(("lipsync", 0f));
            var weightOverride = new TimelineLayerWeightOverride();
            weightOverride.Begin(gate);

            weightOverride.Apply("lipsync", 0.75f);

            Assert.That(gate.LayerWeights["lipsync"], Is.EqualTo(0.75f));
        }

        [Test]
        public void Apply_NotBegun_DoesNothing()
        {
            var gate = new FakeWeightGate(("lipsync", 0.3f));
            var weightOverride = new TimelineLayerWeightOverride();

            weightOverride.Apply("lipsync", 0.75f);

            Assert.That(gate.LayerWeights["lipsync"], Is.EqualTo(0.3f));
        }

        [Test]
        public void Apply_UnknownLayer_WarnsOncePerLayer()
        {
            var gate = new FakeWeightGate(("lipsync", 0f));
            var weightOverride = new TimelineLayerWeightOverride();
            weightOverride.Begin(gate);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"'missing'"));
            weightOverride.Apply("missing", 0.5f);
            weightOverride.Apply("missing", 0.6f);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void End_AfterBegin_RestoresWeightsCapturedAtBeginAndResumesLiveWeights()
        {
            var gate = new FakeWeightGate(("emotion", 0.4f), ("lipsync", 0f));
            var weightOverride = new TimelineLayerWeightOverride();
            weightOverride.Begin(gate);
            weightOverride.Apply("lipsync", 1f);

            weightOverride.End();

            Assert.That(weightOverride.IsActive, Is.False);
            Assert.That(gate.IsLiveWeightSuspended, Is.False);
            Assert.That(gate.LayerWeights["emotion"], Is.EqualTo(0.4f), "再生前の weight に戻す");
            Assert.That(gate.LayerWeights["lipsync"], Is.EqualTo(0f));
        }

        [Test]
        public void End_Twice_ResumesOnlyOnce()
        {
            var gate = new FakeWeightGate(("lipsync", 0f));
            var weightOverride = new TimelineLayerWeightOverride();
            weightOverride.Begin(gate);

            weightOverride.End();
            weightOverride.End();

            Assert.That(gate.ResumeCount, Is.EqualTo(1));
        }

        [Test]
        public void Begin_LiveWeightsAlreadySuspended_DoesNotTakeOver()
        {
            // REC 再生など別の注入者が既に止めているなら奪わない（止めた側の注入を壊さない）。
            var gate = new FakeWeightGate(("lipsync", 0.2f));
            gate.SuspendLiveWeights();
            var weightOverride = new TimelineLayerWeightOverride();

            bool begun = weightOverride.Begin(gate);
            weightOverride.Apply("lipsync", 1f);
            weightOverride.End();

            Assert.That(begun, Is.False);
            Assert.That(gate.LayerWeights["lipsync"], Is.EqualTo(0.2f));
            Assert.That(gate.IsLiveWeightSuspended, Is.True, "他者の停止を解除しない");
        }

        [Test]
        public void Begin_DuplicateLayerNames_DoesNotTakeOver()
        {
            var gate = new FakeWeightGate(("lipsync", 0.2f)) { LayerNamesAreUnique = false };
            var weightOverride = new TimelineLayerWeightOverride();

            Assert.That(weightOverride.Begin(gate), Is.False);
            Assert.That(gate.IsLiveWeightSuspended, Is.False);
        }

        [Test]
        public void Begin_NullGate_ReturnsFalse()
        {
            var weightOverride = new TimelineLayerWeightOverride();

            Assert.That(weightOverride.Begin(null), Is.False);
            Assert.That(weightOverride.IsActive, Is.False);
        }

        private sealed class FakeWeightGate : IWeightInjectionGate
        {
            private readonly List<string> _order = new List<string>();

            public FakeWeightGate(params (string Name, float Weight)[] layers)
            {
                foreach ((string name, float weight) in layers)
                {
                    _order.Add(name);
                    LayerWeights[name] = weight;
                }
            }

            public Dictionary<string, float> LayerWeights { get; } = new Dictionary<string, float>(StringComparer.Ordinal);

            public int ResumeCount { get; private set; }

            public bool IsLiveWeightSuspended { get; private set; }

            public bool LayerNamesAreUnique { get; set; } = true;

            public bool SuspendLiveWeights()
            {
                if (IsLiveWeightSuspended)
                {
                    return false;
                }

                IsLiveWeightSuspended = true;
                return true;
            }

            public bool ResumeLiveWeights()
            {
                if (!IsLiveWeightSuspended)
                {
                    return false;
                }

                IsLiveWeightSuspended = false;
                ResumeCount++;
                return true;
            }

            public void ResetWeightsToDeclared()
            {
                throw new NotSupportedException("入力源 weight まで宣言値に戻すため、Timeline からは呼ばない");
            }

            public bool TrySetBaselineLayerWeight(string layerName, float weight) => TryInjectLayerWeight(layerName, weight);

            public bool TrySetBaselineInputSourceWeight(string layerName, string slotId, float weight) => false;

            public bool TryInjectLayerWeight(string layerName, float weight)
            {
                if (!LayerWeights.ContainsKey(layerName))
                {
                    return false;
                }

                LayerWeights[layerName] = Mathf.Clamp01(weight);
                return true;
            }

            public bool TryInjectInputSourceWeight(string layerName, string slotId, float weight) => false;

            public void CollectLayerWeights(List<LayerWeightEntry> buffer)
            {
                buffer.Clear();
                foreach (string name in _order)
                {
                    buffer.Add(new LayerWeightEntry(name, LayerWeights[name]));
                }
            }

            public void CollectInputSourceWeights(List<InputSourceWeightEntry> buffer)
            {
                buffer.Clear();
            }
        }
    }
}
