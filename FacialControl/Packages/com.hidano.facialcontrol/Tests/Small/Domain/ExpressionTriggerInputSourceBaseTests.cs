using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// ExpressionTriggerInputSourceBase のテスト 。
    /// </summary>
    /// <remarks>
    /// 観測完了条件:
    /// <list type="bullet">
    ///   <item>独立スタックと独立 TransitionCalculator 状態を保持する。</item>
    ///   <item><c>TriggerOn</c> / <c>TriggerOff</c> でスタックが push/remove される。</item>
    ///   <item><c>Tick</c> で遷移が進行する。</item>
    ///   <item><c>TryWriteValues</c> で補間結果が書込まれる。</item>
    ///   <item>2 インスタンスを同レイヤーで走らせ smile / angry を同時 ON しても、
    ///     各インスタンスの CurrentValues が独立に遷移し互いに干渉しない。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class ExpressionTriggerInputSourceBaseTests : SizedTestFixture
    {
        private sealed class TestTriggerEventObserver : ITriggerEventObserver
        {
            public readonly List<(string sourceId, string expressionId)> OnEvents = new();
            public readonly List<(string sourceId, string expressionId)> OffEvents = new();

            public void OnTriggerOn(string sourceId, string expressionId)
            {
                OnEvents.Add((sourceId, expressionId));
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
                OffEvents.Add((sourceId, expressionId));
            }
        }

        private sealed class TestExpressionTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestExpressionTriggerSource(
                InputSourceId id,
                int blendShapeCount,
                int maxStackDepth,
                ExclusionMode exclusionMode,
                IReadOnlyList<string> blendShapeNames,
                FacialProfile profile)
                : base(id, blendShapeCount, maxStackDepth, exclusionMode, blendShapeNames, profile)
            {
            }

            public int StackDepthExceededCount { get; private set; }

            public IReadOnlyList<string> ActiveIdsForTest => ActiveExpressionIds;

            protected override void OnStackDepthExceeded()
            {
                StackDepthExceededCount++;
            }
        }

        private static readonly string[] BlendShapeNames = { "smile", "angry", "sad", "surprised" };

        private static FacialProfile BuildProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", priority: 0, ExclusionMode.LastWins)
            };

            var expressions = new[]
            {
                new Expression(
                    id: "smile",
                    name: "smile",
                    layer: "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression(
                    id: "angry",
                    name: "angry",
                    layer: "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[]
                    {
                        new BlendShapeMapping("angry", 1.0f)
                    }),
                new Expression(
                    id: "sad",
                    name: "sad",
                    layer: "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[]
                    {
                        new BlendShapeMapping("sad", 1.0f)
                    })
            };

            return new FacialProfile("1.0", layers: layers, expressions: expressions);
        }

        private static TestExpressionTriggerSource CreateSource(
            string id = "input",
            int maxStackDepth = 8,
            ExclusionMode exclusionMode = ExclusionMode.LastWins)
        {
            return new TestExpressionTriggerSource(
                InputSourceId.Parse(id),
                blendShapeCount: BlendShapeNames.Length,
                maxStackDepth: maxStackDepth,
                exclusionMode: exclusionMode,
                blendShapeNames: BlendShapeNames,
                profile: BuildProfile());
        }

        [Test]
        public void Ctor_StoresIdTypeAndBlendShapeCount()
        {
            var source = CreateSource("input");

            Assert.AreEqual("input", source.Id);
            Assert.AreEqual(InputSourceType.ExpressionTrigger, source.Type);
            Assert.AreEqual(BlendShapeNames.Length, source.BlendShapeCount);
        }

        [Test]
        public void Type_IsAlwaysExpressionTrigger_ViaIInputSource()
        {
            IInputSource source = CreateSource("analog-blendshape");

            Assert.AreEqual(InputSourceType.ExpressionTrigger, source.Type);
        }

        [Test]
        public void Ctor_RejectsNegativeBlendShapeCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TestExpressionTriggerSource(
                    InputSourceId.Parse("input"),
                    blendShapeCount: -1,
                    maxStackDepth: 1,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: BlendShapeNames,
                    profile: BuildProfile()));
        }

        [Test]
        public void Ctor_RejectsNonPositiveMaxStackDepth()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TestExpressionTriggerSource(
                    InputSourceId.Parse("input"),
                    blendShapeCount: 4,
                    maxStackDepth: 0,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: BlendShapeNames,
                    profile: BuildProfile()));
        }

        [Test]
        public void Ctor_RejectsNullBlendShapeNames()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TestExpressionTriggerSource(
                    InputSourceId.Parse("input"),
                    blendShapeCount: 4,
                    maxStackDepth: 1,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: null,
                    profile: BuildProfile()));
        }

        [Test]
        public void TriggerOn_PushesExpressionToStack()
        {
            var source = CreateSource();

            source.TriggerOn("smile");

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
        }

        [Test]
        public void TriggerOn_SameExpressionTwice_MovesToTopOfStack()
        {
            var source = CreateSource();

            source.TriggerOn("smile");
            source.TriggerOn("angry");
            source.TriggerOn("smile");

            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
            Assert.AreEqual("smile", source.ActiveIdsForTest[1]);
        }

        [Test]
        public void TriggerOff_RemovesExpressionFromStack()
        {
            var source = CreateSource();
            source.TriggerOn("smile");
            source.TriggerOn("angry");

            source.TriggerOff("smile");

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
        }

        [Test]
        public void TriggerOff_UnknownId_IsSilentlyIgnored()
        {
            var source = CreateSource();
            source.TriggerOn("smile");

            Assert.DoesNotThrow(() => source.TriggerOff("unknown-id"));
            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
        }

        [Test]
        public void TriggerOn_WithObserver_NotifiesObserverAfterStackUpdate()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);

            source.TriggerOn("smile");

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual(1, observer.OnEvents.Count);
            Assert.AreEqual(("input", "smile"), observer.OnEvents[0]);
            Assert.AreEqual(0, observer.OffEvents.Count);
        }

        [Test]
        public void TriggerOff_WithObserver_NotifiesObserverOnlyWhenRemovalSucceeds()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.TriggerOn("smile");

            observer.OnEvents.Clear();

            source.TriggerOff("smile");
            source.TriggerOff("unknown-id");

            Assert.AreEqual(0, source.ActiveIdsForTest.Count);
            Assert.AreEqual(0, observer.OnEvents.Count);
            Assert.AreEqual(1, observer.OffEvents.Count);
            Assert.AreEqual(("input", "smile"), observer.OffEvents[0]);
        }

        [Test]
        public void TriggerOn_WithoutObserver_KeepsExistingBehavior()
        {
            var source = CreateSource("input");

            Assert.DoesNotThrow(() => source.TriggerOn("smile"));

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
        }

        [Test]
        public void ResetToExpressionStack_AppliesFinalStateWithoutObserverNotification()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);

            source.ResetToExpressionStack(new[] { "smile", "angry" });

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual("angry", source.ActiveIdsForTest[1]);
            Assert.AreEqual(0f, buffer[0], 1e-5f);
            Assert.AreEqual(1f, buffer[1], 1e-5f);
            Assert.AreEqual(0, observer.OnEvents.Count);
            Assert.AreEqual(0, observer.OffEvents.Count);
        }

        [Test]
        public void ResetToExpressionStack_EmptyStackClearsImmediately()
        {
            var source = CreateSource();
            source.TriggerOn("smile");
            source.Tick(1.0f);

            source.ResetToExpressionStack(Array.Empty<string>());

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsFalse(wrote);
            Assert.AreEqual(0, source.ActiveIdsForTest.Count);
            AssertMaskBits(source.ContributeMask, false, false, false, false);
        }

        [Test]
        public void ResetToExpressionStack_BlendModeAccumulatesWithoutTransition()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.Blend);

            source.ResetToExpressionStack(new[] { "smile", "angry" });
            source.Tick(0.1f);

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            Assert.AreEqual(1f, buffer[0], 1e-5f);
            Assert.AreEqual(1f, buffer[1], 1e-5f);
            AssertMaskBits(source.ContributeMask, true, true, false, false);
        }

        [Test]
        public void ResetToExpressionStack_ExcessDepthKeepsNewestEntries()
        {
            var source = CreateSource(maxStackDepth: 2);

            source.ResetToExpressionStack(new[] { "smile", "angry", "sad" });

            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
            Assert.AreEqual("sad", source.ActiveIdsForTest[1]);
        }

        [Test]
        public void SuspendTriggerInput_WhenNotSuspended_ReturnsTrueAndSuspends()
        {
            var source = CreateSource();

            Assert.IsFalse(source.IsTriggerInputSuspended);

            bool suspended = source.SuspendTriggerInput();

            Assert.IsTrue(suspended);
            Assert.IsTrue(source.IsTriggerInputSuspended);
        }

        [Test]
        public void SuspendTriggerInput_WhenAlreadySuspended_ReturnsFalseWithoutStateChange()
        {
            var source = CreateSource();
            source.SuspendTriggerInput();

            bool suspendedAgain = source.SuspendTriggerInput();

            Assert.IsFalse(suspendedAgain);
            Assert.IsTrue(source.IsTriggerInputSuspended);

            bool resumed = source.ResumeTriggerInput();

            Assert.IsTrue(resumed);
            Assert.IsFalse(source.IsTriggerInputSuspended);
        }

        [Test]
        public void ResumeTriggerInput_WhenNotSuspended_ReturnsFalseWithoutStateChange()
        {
            var source = CreateSource();

            bool resumed = source.ResumeTriggerInput();

            Assert.IsFalse(resumed);
            Assert.IsFalse(source.IsTriggerInputSuspended);
        }

        [Test]
        public void TriggerOn_WhenSuspended_IgnoresStackMutationAndObserverNotification()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.SuspendTriggerInput();

            source.TriggerOn("smile");

            Assert.AreEqual(0, source.ActiveIdsForTest.Count);
            Assert.AreEqual(0, observer.OnEvents.Count);
            Assert.AreEqual(0, observer.OffEvents.Count);
            Assert.IsFalse(source.TryWriteValues(new float[BlendShapeNames.Length]));
        }

        [Test]
        public void TriggerOff_WhenSuspended_IgnoresStackMutationAndObserverNotification()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.TriggerOn("smile");
            source.Tick(1.0f);
            observer.OnEvents.Clear();
            source.SuspendTriggerInput();

            source.TriggerOff("smile");

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual(0, observer.OnEvents.Count);
            Assert.AreEqual(0, observer.OffEvents.Count);
        }

        [Test]
        public void InjectTriggerOn_WhenSuspended_MutatesStackAndNotifiesObserver()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.SuspendTriggerInput();

            source.InjectTriggerOn("smile");

            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual(1, observer.OnEvents.Count);
            Assert.AreEqual(("input", "smile"), observer.OnEvents[0]);
            Assert.AreEqual(0, observer.OffEvents.Count);
        }

        [Test]
        public void InjectTriggerOff_WhenSuspended_RemovesStackAndNotifiesObserver()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.InjectTriggerOn("smile");
            source.Tick(1.0f);
            observer.OnEvents.Clear();
            source.SuspendTriggerInput();

            source.InjectTriggerOff("smile");

            Assert.AreEqual(0, source.ActiveIdsForTest.Count);
            Assert.AreEqual(0, observer.OnEvents.Count);
            Assert.AreEqual(1, observer.OffEvents.Count);
            Assert.AreEqual(("input", "smile"), observer.OffEvents[0]);
        }

        [Test]
        public void ResumeTriggerInput_WhenInjectedDuringSuspension_PreservesInjectedState()
        {
            var source = CreateSource("input");
            source.SuspendTriggerInput();

            source.InjectTriggerOn("smile");
            source.ResumeTriggerInput();

            var buffer = new float[BlendShapeNames.Length];
            source.Tick(1.0f);
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            Assert.IsFalse(source.IsTriggerInputSuspended);
            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual(1f, buffer[0], 1e-5f);
            Assert.AreEqual(0f, buffer[1], 1e-5f);
        }

        [Test]
        public void ResetToExpressionStack_WhenSuspended_AppliesRequestedState()
        {
            var source = CreateSource("input");
            var observer = new TestTriggerEventObserver();
            source.SetTriggerEventObserver(observer);
            source.TriggerOn("smile");
            source.Tick(1.0f);
            source.SuspendTriggerInput();

            source.ResetToExpressionStack(new[] { "angry" });

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            Assert.AreEqual(1, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
            Assert.AreEqual(0f, buffer[0], 1e-5f);
            Assert.AreEqual(1f, buffer[1], 1e-5f);
            Assert.AreEqual(1, observer.OnEvents.Count);
            Assert.AreEqual(0, observer.OffEvents.Count);
        }

        [Test]
        public void TryWriteValues_EmptyStack_ReturnsFalseAndLeavesOutputUnchanged()
        {
            var source = CreateSource();
            var buffer = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
            var snapshot = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

            bool wrote = source.TryWriteValues(buffer);

            Assert.IsFalse(wrote);
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [Test]
        public void TryWriteValues_AfterTriggerOnAndFullTick_WritesTargetValues()
        {
            var source = CreateSource();

            source.TriggerOn("smile");
            source.Tick(1.0f); // 遷移時間 (0.2s) を超える

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            // smile インデックス 0 が 1.0 に達していること
            Assert.AreEqual(1.0f, buffer[0], 1e-5f);
            Assert.AreEqual(0.0f, buffer[1], 1e-5f);
            Assert.AreEqual(0.0f, buffer[2], 1e-5f);
            Assert.AreEqual(0.0f, buffer[3], 1e-5f);
        }

        [Test]
        public void Tick_ProgressesInterpolationLinearly()
        {
            var source = CreateSource();
            source.TriggerOn("smile");

            source.Tick(0.1f); // 半分進行 (duration=0.2s)

            var buffer = new float[BlendShapeNames.Length];
            source.TryWriteValues(buffer);

            // Linear 補間: 0→1 の 50% = 0.5
            Assert.That(buffer[0], Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void TwoIndependentInstances_DoNotInterfereWhenBothActive()
        {
            // Controller 相当インスタンス: smile をトリガー
            var controllerLike = CreateSource("input");
            // Keyboard 相当インスタンス: angry をトリガー (同じレイヤー前提)
            var keyboardLike = CreateSource("analog-blendshape");

            controllerLike.TriggerOn("smile");
            keyboardLike.TriggerOn("angry");

            // 両者を同時に進行させる
            controllerLike.Tick(1.0f);
            keyboardLike.Tick(1.0f);

            var controllerBuffer = new float[BlendShapeNames.Length];
            var keyboardBuffer = new float[BlendShapeNames.Length];

            Assert.IsTrue(controllerLike.TryWriteValues(controllerBuffer));
            Assert.IsTrue(keyboardLike.TryWriteValues(keyboardBuffer));

            // input 側は smile のみが立つ
            Assert.AreEqual(1.0f, controllerBuffer[0], 1e-5f);
            Assert.AreEqual(0.0f, controllerBuffer[1], 1e-5f);

            // analog-blendshape 側は angry のみが立つ
            Assert.AreEqual(0.0f, keyboardBuffer[0], 1e-5f);
            Assert.AreEqual(1.0f, keyboardBuffer[1], 1e-5f);

            // スタック内容も独立
            Assert.AreEqual(1, controllerLike.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", controllerLike.ActiveIdsForTest[0]);
            Assert.AreEqual(1, keyboardLike.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", keyboardLike.ActiveIdsForTest[0]);
        }

        [Test]
        public void LastWins_WithStackedExpressions_TargetsTopOfStackOnly()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.LastWins);

            source.TriggerOn("smile");
            source.TriggerOn("angry"); // top
            source.Tick(1.0f);

            var buffer = new float[BlendShapeNames.Length];
            source.TryWriteValues(buffer);

            // LastWins: angry のみが立ち smile は 0
            Assert.AreEqual(0.0f, buffer[0], 1e-5f, "smile should be 0 under LastWins");
            Assert.AreEqual(1.0f, buffer[1], 1e-5f, "angry should be 1 under LastWins");
        }

        [Test]
        public void Blend_WithStackedExpressions_AccumulatesAllTargets()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.Blend);

            source.TriggerOn("smile");
            source.TriggerOn("angry");
            source.Tick(1.0f);

            var buffer = new float[BlendShapeNames.Length];
            source.TryWriteValues(buffer);

            // Blend: smile と angry の両者が立つ
            Assert.AreEqual(1.0f, buffer[0], 1e-5f, "smile should be 1 under Blend");
            Assert.AreEqual(1.0f, buffer[1], 1e-5f, "angry should be 1 under Blend");
        }

        // ----- 3.1 ExpressionTrigger の transition union mask  -----

        [Test]
        public void ContributeMask_DuringExpressionTransition_ReturnsOutgoingAndIncomingUnion()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.Tick(1.0f);

            source.TriggerOn("angry");

            AssertMaskBits(
                source.ContributeMask,
                true,  // smile: outgoing expression
                true,  // angry: incoming expression
                false,
                false);
        }

        [Test]
        public void ContributeMask_AfterExpressionTransitionCompletes_ReturnsIncomingExpressionOnly()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.Tick(1.0f);

            source.TriggerOn("angry");
            source.Tick(1.0f);

            AssertMaskBits(
                source.ContributeMask,
                false,
                true,  // angry only after transition completion
                false,
                false);
        }

        [Test]
        public void ContributeMask_SameExpressionActivatedAgain_ReturnsStableMaskReference()
        {
            var source = CreateSource(exclusionMode: ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.Tick(1.0f);
            BitArray firstSmileMask = source.ContributeMask;

            AssertMaskBits(firstSmileMask, true, false, false, false);

            source.TriggerOn("angry");
            AssertMaskBits(source.ContributeMask, true, true, false, false);

            source.Tick(1.0f);
            AssertMaskBits(firstSmileMask, true, false, false, false);

            source.TriggerOff("angry");
            source.Tick(1.0f);

            Assert.That(source.ContributeMask, Is.SameAs(firstSmileMask),
                "同一 Expression の mask 参照は構築後不変で、再アクティブ化時も同じ参照を返す必要がある。");
            AssertMaskBits(source.ContributeMask, true, false, false, false);
        }

        [Test]
        public void StackDepthExceeded_DropsOldestAndCallsHook()
        {
            var source = CreateSource(maxStackDepth: 2);

            source.TriggerOn("smile");
            source.TriggerOn("angry");
            source.TriggerOn("sad"); // 深度 2 超え → smile が drop

            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
            Assert.AreEqual("sad", source.ActiveIdsForTest[1]);
            Assert.Greater(source.StackDepthExceededCount, 0);
        }

        [Test]
        public void TriggerOn_Null_Throws()
        {
            var source = CreateSource();

            Assert.Throws<ArgumentNullException>(() => source.TriggerOn(null));
        }

        [Test]
        public void TriggerOff_Null_Throws()
        {
            var source = CreateSource();

            Assert.Throws<ArgumentNullException>(() => source.TriggerOff(null));
        }

        [Test]
        public void InjectTriggerOn_Null_Throws()
        {
            var source = CreateSource();

            Assert.Throws<ArgumentNullException>(() => source.InjectTriggerOn(null));
        }

        [Test]
        public void InjectTriggerOff_Null_Throws()
        {
            var source = CreateSource();

            Assert.Throws<ArgumentNullException>(() => source.InjectTriggerOff(null));
        }

        [Test]
        public void TryWriteValues_AfterTriggerOffAllAndFullTick_ReturnsFalseWhenFadedOut()
        {
            var source = CreateSource();

            source.TriggerOn("smile");
            source.Tick(1.0f);

            source.TriggerOff("smile"); // スタック空化 + フェードアウト開始
            source.Tick(1.0f);           // フェードアウト完了

            var buffer = new float[BlendShapeNames.Length];
            bool wrote = source.TryWriteValues(buffer);

            Assert.IsFalse(wrote, "フェードアウト完了後は空スタック状態で false を返す");
        }

        // ----- 4.3 スタック深度超過時の最古 drop  -----

        [Test]
        public void StackDepthExceeded_DropsOldestAndKeepsLatestEntries()
        {
            var source = CreateSource(id: "input", maxStackDepth: 2);

            // 超過時は warning が出る（照合はクラスタグと id のみ）。
            LogAssert.Expect(LogType.Warning,
                new Regex("ExpressionTriggerInputSource.*input"));

            source.TriggerOn("smile");
            source.TriggerOn("angry");
            source.TriggerOn("sad"); // 深度 2 超え → smile が drop + warning

            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("angry", source.ActiveIdsForTest[0]);
            Assert.AreEqual("sad", source.ActiveIdsForTest[1]);
        }

        [Test]
        public void StackDepthExceeded_RepeatedOverflow_KeepsLatestEntries()
        {
            var source = CreateSource(id: "input", maxStackDepth: 2);

            LogAssert.Expect(LogType.Warning,
                new Regex("ExpressionTriggerInputSource.*input"));

            source.TriggerOn("smile");
            source.TriggerOn("angry");
            source.TriggerOn("sad"); // 1 回目の超過 (smile が drop)

            source.TriggerOn("smile");  // スタック: [sad, smile] (angry が drop)
            source.TriggerOn("angry");  // スタック: [smile, angry] (sad が drop)

            // スタックは最新 2 件で維持される
            Assert.AreEqual(2, source.ActiveIdsForTest.Count);
            Assert.AreEqual("smile", source.ActiveIdsForTest[0]);
            Assert.AreEqual("angry", source.ActiveIdsForTest[1]);
        }

        [Test]
        public void TryWriteValues_OverlapSemantics_ShorterBufferWritesPrefixOnly()
        {
            var source = CreateSource();
            source.TriggerOn("smile");
            source.Tick(1.0f);

            var shortBuffer = new float[2] { -1f, -1f };
            bool wrote = source.TryWriteValues(shortBuffer);

            Assert.IsTrue(wrote);
            Assert.AreEqual(1.0f, shortBuffer[0], 1e-5f);
            Assert.AreEqual(0.0f, shortBuffer[1], 1e-5f);
        }

        private static void AssertMaskBits(BitArray mask, params bool[] expected)
        {
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]),
                    $"ContributeMask[{i}] ({BlendShapeNames[i]}) が期待値と一致しません。");
            }
        }
    }
}
