using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    /// <summary>
    /// <see cref="ULipSyncVoiceGate"/> の契約（ヒステリシス・Hold・Attack / Release・途絶・しきい値 0）を固定する。
    /// UDP LipSync の <c>UdpLipSyncVoiceGate</c> と同じ挙動であることを守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class ULipSyncVoiceGateTests : SizedTestFixture
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void Defaults_NewInstance_MatchSpecifiedValues()
        {
            var gate = new ULipSyncVoiceGate();

            Assert.That(gate.OnThreshold, Is.EqualTo(0.05f));
            Assert.That(gate.OffThreshold, Is.EqualTo(0.02f));
            Assert.That(gate.HoldTime, Is.EqualTo(0.2f));
            Assert.That(gate.AttackTime, Is.EqualTo(0.05f));
            Assert.That(gate.ReleaseTime, Is.EqualTo(0.2f));
            Assert.That(gate.StaleTimeout, Is.EqualTo(0.5f));
            Assert.That(gate.IsSpeaking, Is.False);
            Assert.That(gate.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void Tick_ActivityBelowOn_StaysSilent()
        {
            var gate = CreateInstantGate();

            gate.Tick(0.1f, 0.04f, true);

            Assert.That(gate.IsSpeaking, Is.False);
            Assert.That(gate.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void Tick_ActivityAtOn_StartsSpeaking()
        {
            var gate = CreateInstantGate();

            gate.Tick(0.1f, 0.05f, true);

            Assert.That(gate.IsSpeaking, Is.True);
            Assert.That(gate.Weight, Is.EqualTo(1f));
        }

        [Test]
        public void Tick_SpeakingAndActivityBetweenOffAndOn_KeepsSpeaking()
        {
            var gate = CreateInstantGate();
            gate.Tick(0.1f, 0.5f, true);

            gate.Tick(1f, 0.03f, true);

            Assert.That(gate.IsSpeaking, Is.True, "発話中は Off 以上なら継続する（ヒステリシス）。");
        }

        [Test]
        public void Tick_BelowOffShorterThanHold_KeepsSpeaking()
        {
            var gate = CreateInstantGate();
            gate.HoldTime = 0.2f;
            gate.Tick(0.1f, 0.5f, true);

            gate.Tick(0.1f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.True);
        }

        [Test]
        public void Tick_BelowOffForHoldTime_StopsSpeaking()
        {
            var gate = CreateInstantGate();
            gate.HoldTime = 0.2f;
            gate.Tick(0.1f, 0.5f, true);

            gate.Tick(0.1f, 0f, true);
            gate.Tick(0.1f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.False);
            Assert.That(gate.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void Tick_ActivityRecoversDuringHold_ResetsHoldTimer()
        {
            var gate = CreateInstantGate();
            gate.HoldTime = 0.2f;
            gate.Tick(0.1f, 0.5f, true);

            gate.Tick(0.15f, 0f, true);
            gate.Tick(0.01f, 0.5f, true);
            gate.Tick(0.15f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.True, "Off 以上に戻ったら Hold の経過を数え直す。");
        }

        [Test]
        public void Tick_OffGreaterThanOn_TreatsOffAsOn()
        {
            var gate = CreateInstantGate();
            gate.OnThreshold = 0.1f;
            gate.OffThreshold = 0.5f;
            gate.Tick(0.1f, 0.2f, true);

            gate.Tick(1f, 0.15f, true);

            Assert.That(gate.IsSpeaking, Is.True, "Off が On より大きい設定は On と同じ扱い。");
        }

        [Test]
        public void Tick_ZeroThresholdAndZeroActivity_StaysSilent()
        {
            var gate = CreateInstantGate();
            gate.OnThreshold = 0f;
            gate.OffThreshold = 0f;

            gate.Tick(0.1f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.False, "しきい値 0 は activity > 0 で判定する。");
        }

        [Test]
        public void Tick_ZeroThresholdAndPositiveActivity_StartsSpeaking()
        {
            var gate = CreateInstantGate();
            gate.OnThreshold = 0f;
            gate.OffThreshold = 0f;

            gate.Tick(0.1f, 0.001f, true);

            Assert.That(gate.IsSpeaking, Is.True);
        }

        [Test]
        public void Tick_ZeroOffThresholdAndZeroActivity_StopsAfterHold()
        {
            var gate = CreateInstantGate();
            gate.OffThreshold = 0f;
            gate.HoldTime = 0f;
            gate.Tick(0.1f, 0.5f, true);

            gate.Tick(0.1f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.False, "Off が 0 でもゲートが閉じなくならない。");
        }

        [Test]
        public void Tick_Attack_RaisesWeightByDeltaOverAttackTime()
        {
            var gate = new ULipSyncVoiceGate { AttackTime = 0.1f, ReleaseTime = 0.2f };

            gate.Tick(0.05f, 1f, true);

            Assert.That(gate.Weight, Is.EqualTo(0.5f).Within(Tolerance));

            gate.Tick(0.1f, 1f, true);

            Assert.That(gate.Weight, Is.EqualTo(1f), "目標を超えない。");
        }

        [Test]
        public void Tick_Release_LowersWeightByDeltaOverReleaseTime()
        {
            var gate = new ULipSyncVoiceGate { AttackTime = 0f, ReleaseTime = 0.2f, HoldTime = 0f };
            gate.Tick(0.1f, 1f, true);

            gate.Tick(0.05f, 0f, true);

            Assert.That(gate.IsSpeaking, Is.False);
            Assert.That(gate.Weight, Is.EqualTo(0.75f).Within(Tolerance));
        }

        [Test]
        public void Tick_NoInputForStaleTimeout_TreatsActivityAsZeroAndCloses()
        {
            var gate = CreateInstantGate();
            gate.StaleTimeout = 0.5f;
            gate.HoldTime = 0f;
            gate.Tick(0.1f, 0.8f, true);

            gate.Tick(0.3f, 0.8f, false);
            Assert.That(gate.IsSpeaking, Is.True, "途絶が Stale Timeout 未満なら最後の値で判定する。");

            gate.Tick(0.3f, 0.8f, false);

            Assert.That(gate.IsStale, Is.True);
            Assert.That(gate.Activity, Is.EqualTo(0f));
            Assert.That(gate.IsSpeaking, Is.False, "マイク停止で口が開いたまま残らない。");
        }

        [Test]
        public void Tick_StaleTimeoutZero_NeverStale()
        {
            var gate = CreateInstantGate();
            gate.StaleTimeout = 0f;
            gate.Tick(0.1f, 0.8f, true);

            gate.Tick(10f, 0.8f, false);

            Assert.That(gate.IsStale, Is.False);
            Assert.That(gate.IsSpeaking, Is.True);
        }

        [Test]
        public void Tick_ActivityOutOfRange_IsClamped()
        {
            var gate = CreateInstantGate();

            gate.Tick(0.1f, 3f, true);
            Assert.That(gate.Activity, Is.EqualTo(1f));

            gate.Tick(0.1f, -1f, true);
            Assert.That(gate.Activity, Is.EqualTo(0f));
        }

        [Test]
        public void Reset_AfterSpeaking_ReturnsToSilentWithZeroWeight()
        {
            var gate = CreateInstantGate();
            gate.Tick(0.1f, 1f, true);

            gate.Reset();

            Assert.That(gate.IsSpeaking, Is.False);
            Assert.That(gate.Weight, Is.EqualTo(0f));
            Assert.That(gate.Activity, Is.EqualTo(0f));
        }

        private static ULipSyncVoiceGate CreateInstantGate()
        {
            return new ULipSyncVoiceGate { AttackTime = 0f, ReleaseTime = 0f };
        }
    }
}
