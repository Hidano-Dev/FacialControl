using System;
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
    /// <see cref="ExpressionTriggerInputSourceBase"/> のレイヤー別スタック（HID-418）のテスト。
    /// 親（入力源）が受けた TriggerOn / TriggerOff を、Expression.Layer が指すレイヤーの子スタックだけへ渡すこと、
    /// そのレイヤーが入力源を宣言していないときは従来どおり宣言レイヤーへ渡すことを確かめる。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ExpressionTriggerLayerStackTests : SizedTestFixture
    {
        private const string Emotion = "emotion";
        private const string Eye = "eye";
        private const string Mouth = "mouth";

        private static readonly string[] BlendShapeNames = { "mouth_smile", "eye_close", "mouth_open" };

        private sealed class TestTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestTriggerSource(FacialProfile profile, ExclusionMode exclusionMode = ExclusionMode.LastWins, int maxStackDepth = 16)
                : base(
                    InputSourceId.Parse("input"),
                    BlendShapeNames.Length,
                    maxStackDepth,
                    exclusionMode,
                    BlendShapeNames,
                    profile)
            {
            }
        }

        private sealed class RecordingObserver : ITriggerEventObserver
        {
            public readonly List<string> Events = new List<string>();

            public void OnTriggerOn(string sourceId, string expressionId) => Events.Add("on:" + expressionId);

            public void OnTriggerOff(string sourceId, string expressionId) => Events.Add("off:" + expressionId);
        }

        private static FacialProfile BuildProfile()
        {
            var layers = new[]
            {
                new LayerDefinition(Emotion, 0, ExclusionMode.LastWins),
                new LayerDefinition(Eye, 1, ExclusionMode.LastWins),
                new LayerDefinition(Mouth, 2, ExclusionMode.Blend),
            };
            var expressions = new[]
            {
                new Expression("smile", "smile", Emotion, 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("mouth_smile", 1f) }),
                new Expression("eyeclose", "eyeclose", Eye, 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("eye_close", 1f) }),
                new Expression("open", "open", Mouth, 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("mouth_open", 0.5f) }),
            };
            return new FacialProfile("1.0", layers, expressions);
        }

        private static float[] Read(IInputSource source)
        {
            source.Tick(1f);
            var values = new float[BlendShapeNames.Length];
            source.TryWriteValues(values);
            return values;
        }

        [Test]
        public void AcquireLayerStack_TriggerSource_ReturnsDistinctStackPerLayer()
        {
            var source = new TestTriggerSource(BuildProfile());

            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            Assert.AreNotSame(source, emotion);
            Assert.AreNotSame(emotion, eye);
            Assert.AreEqual(source.Id, emotion.Id);
            Assert.AreEqual(source.BlendShapeCount, emotion.BlendShapeCount);
        }

        [Test]
        public void AcquireLayerStack_SameLayerTwice_ReturnsSameStack()
        {
            var source = new TestTriggerSource(BuildProfile());

            IInputSource first = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource second = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);

            Assert.AreSame(first, second);
        }

        [Test]
        public void AcquireLayerStack_NonTriggerOrStack_ReturnsInputAsIs()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource stack = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);

            Assert.AreSame(stack, ExpressionTriggerInputSourceBase.AcquireLayerStack(stack, Eye, ExclusionMode.LastWins));
            Assert.IsNull(ExpressionTriggerInputSourceBase.AcquireLayerStack(null, Emotion, ExclusionMode.LastWins));
        }

        [Test]
        public void TriggerOn_TwoDeclaredLayers_RoutesByExpressionLayer()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            // 共有スタックの LastWins なら smile が消えるが、レイヤー別なら両方残る（笑ったまま目を閉じる）。
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, Read(emotion));
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, Read(eye));
            CollectionAssert.AreEqual(new[] { "smile" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
            // 親のスタックは全レイヤー分の和を保つ（REC の基準捕捉・HUD 向け）。
            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.ActiveExpressionIds);
        }

        [Test]
        public void TriggerOn_ExpressionLayerNotDeclared_FallsBackToDeclaredLayersAndWarnsOnce()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);

            LogAssert.Expect(LogType.Warning, new Regex("Expression 'eyeclose' のレイヤー 'eye'.*\\(emotion\\)"));
            source.TriggerOn("eyeclose");
            source.TriggerOff("eyeclose");
            source.TriggerOn("eyeclose");

            // 未決 1（2026-11-03 決定）: 宣言の無いレイヤーの表情は、従来どおり宣言しているレイヤーに出す。
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, Read(emotion));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TriggerOn_UnknownExpression_FallsBackToAllStacksWithoutWarning()
        {
            var source = new TestTriggerSource(BuildProfile());
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.TriggerOn("missing");

            CollectionAssert.AreEqual(new[] { "missing" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "missing" }, source.GetActiveExpressionIds(Eye));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LayerStack_UsesLayerExclusionMode()
        {
            var profile = BuildProfile();
            var source = new TestTriggerSource(profile, ExclusionMode.LastWins);
            IInputSource mouth = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Mouth, ExclusionMode.Blend);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);

            source.TriggerOn("open");
            source.TriggerOn("missing");

            // 親は LastWins だが、子スタックは mouth レイヤーの Blend で合成する（missing は値を持たない）。
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0.5f }, Read(mouth));
        }

        [Test]
        public void TriggerOff_RemovesFromRoutedStack()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");
            source.TriggerOff("smile");

            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, Read(emotion));
            CollectionAssert.IsEmpty(source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, Read(eye));
        }

        [Test]
        public void SuspendTriggerInput_BlocksLiveButInjectReachesStacks()
        {
            var source = new TestTriggerSource(BuildProfile());
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            source.SuspendTriggerInput();

            source.TriggerOn("smile");
            source.InjectTriggerOn("eyeclose");

            CollectionAssert.IsEmpty(source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
        }

        [Test]
        public void ResetToExpressionStack_DistributesIdsByRouting()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.ResetToExpressionStack(new[] { "smile", "eyeclose", "missing" });

            CollectionAssert.AreEqual(new[] { "smile", "missing" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "eyeclose", "missing" }, source.GetActiveExpressionIds(Eye));
            CollectionAssert.AreEqual(new[] { "smile", "eyeclose", "missing" }, source.ActiveExpressionIds);
            // 即時に確立する（遷移しない）。
            var values = new float[BlendShapeNames.Length];
            Assert.IsTrue(emotion.TryWriteValues(values));
            Assert.AreEqual(0f, values[0], "LastWins の末尾 missing は値を持たない");
            Assert.IsTrue(eye.TryWriteValues(values));
        }

        [Test]
        public void AcquireLayerStack_AfterTriggerOn_SeedsRoutedExpressions()
        {
            var source = new TestTriggerSource(BuildProfile());
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
            CollectionAssert.AreEqual(new[] { "smile" }, source.GetActiveExpressionIds(Emotion));
            var values = new float[BlendShapeNames.Length];
            Assert.IsTrue(eye.TryWriteValues(values));
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, values);
        }

        [Test]
        public void ReleaseLayerStack_LastReference_StopsRoutingToLayer()
        {
            var source = new TestTriggerSource(BuildProfile());
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            Assert.IsTrue(ExpressionTriggerInputSourceBase.ReleaseLayerStack(eye));
            source.TriggerOn("eyeclose");
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye), "参照が残っている間は渡る");
            source.TriggerOff("eyeclose");

            Assert.IsTrue(ExpressionTriggerInputSourceBase.ReleaseLayerStack(eye));
            LogAssert.Expect(LogType.Warning, new Regex("Expression 'eyeclose' のレイヤー 'eye'"));
            source.TriggerOn("eyeclose");

            // eye の子スタックが無くなったので、eyeclose は宣言の残る emotion へ出る。
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, Read(emotion));
            Assert.IsFalse(ExpressionTriggerInputSourceBase.ReleaseLayerStack(source), "子スタック以外は返却しない");
        }

        [Test]
        public void Observer_NotifiedOncePerEvent_EvenWithStacks()
        {
            var source = new TestTriggerSource(BuildProfile());
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            var observer = new RecordingObserver();
            source.SetTriggerEventObserver(observer);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");
            source.TriggerOff("smile");
            source.TriggerOff("smile");

            CollectionAssert.AreEqual(new[] { "on:smile", "on:eyeclose", "off:smile" }, observer.Events);
        }

        [Test]
        public void TriggerOn_TotalAcrossLayersExceedsDepth_OwnStackKeepsWhatLayerStacksOutput()
        {
            // 深さはレイヤーごとに数える。親のスタックは子スタックの和なので、合計が深さを超えても落とさない
            // （REC の基準捕捉・OverrideMask が、出力中の表情を取りこぼさない）。
            var source = new TestTriggerSource(BuildProfile(), maxStackDepth: 1);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            var observer = new RecordingObserver();
            source.SetTriggerEventObserver(observer);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.ActiveExpressionIds);

            source.TriggerOff("smile");

            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.ActiveExpressionIds);
            CollectionAssert.IsEmpty(source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "on:smile", "on:eyeclose", "off:smile" }, observer.Events);
        }

        [Test]
        public void TriggerOn_LayerStackDropsOldest_OwnStackDropsItToo()
        {
            var source = new TestTriggerSource(BuildProfile(), maxStackDepth: 1);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");
            source.TriggerOn("missing");

            // missing は全子スタックへ入り、深さ 1 の emotion から smile、eye から eyeclose が落ちる。
            CollectionAssert.AreEqual(new[] { "missing" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "missing" }, source.GetActiveExpressionIds(Eye));
            CollectionAssert.AreEqual(new[] { "missing" }, source.ActiveExpressionIds);
        }

        [Test]
        public void ResetToExpressionStack_MoreThanDepthAcrossLayers_OwnStackKeepsUnion()
        {
            var source = new TestTriggerSource(BuildProfile(), maxStackDepth: 1);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            source.ResetToExpressionStack(new[] { "smile", "eyeclose" });

            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.ActiveExpressionIds);
        }

        [Test]
        public void AcquireLayerStack_ActiveBeforeConnect_ConnectOrderDoesNotLeaveFallbackInEarlierStack()
        {
            var source = new TestTriggerSource(BuildProfile());
            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.GetActiveExpressionIds(Emotion),
                "eye が無い間は eyeclose も宣言レイヤーへ出る");

            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);

            // eye ができたので、emotion に残っていた eyeclose は eye へ移る。
            CollectionAssert.AreEqual(new[] { "smile" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.GetActiveExpressionIds(Eye));
            var values = new float[BlendShapeNames.Length];
            Assert.IsTrue(emotion.TryWriteValues(values));
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, values);
            Assert.IsTrue(eye.TryWriteValues(values));
        }

        [Test]
        public void ReleaseLayerStack_ActiveExpressions_MoveToRemainingLayer()
        {
            var source = new TestTriggerSource(BuildProfile());
            ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            ExpressionTriggerInputSourceBase.ReleaseLayerStack(eye);

            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.GetActiveExpressionIds(Emotion));
            CollectionAssert.AreEqual(new[] { "smile", "eyeclose" }, source.ActiveExpressionIds);
        }

        [Test]
        public void ReleaseLayerStack_LastStack_TrimsOwnStackToDepth()
        {
            var source = new TestTriggerSource(BuildProfile(), maxStackDepth: 1);
            IInputSource emotion = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);
            IInputSource eye = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Eye, ExclusionMode.LastWins);
            source.TriggerOn("smile");
            source.TriggerOn("eyeclose");

            ExpressionTriggerInputSourceBase.ReleaseLayerStack(eye);
            ExpressionTriggerInputSourceBase.ReleaseLayerStack(emotion);

            // 子スタックが無くなれば、親は自身の深さで単独のスタックに戻る。
            CollectionAssert.AreEqual(new[] { "eyeclose" }, source.ActiveExpressionIds);
        }

        [Test]
        public void GetActiveExpressionIds_NoStackForLayer_ReturnsOwnStack()
        {
            var source = new TestTriggerSource(BuildProfile());
            source.TriggerOn("smile");

            Assert.AreSame(source.ActiveExpressionIds, source.GetActiveExpressionIds(Emotion));
        }

        [Test]
        public void LayerStack_OwnerWithoutBlendShapes_UsesOwnerContributeMask()
        {
            var source = new ZeroBlendShapeSource(BuildProfile(), hostBlendShapeCount: 5);

            IInputSource stack = ExpressionTriggerInputSourceBase.AcquireLayerStack(source, Emotion, ExclusionMode.LastWins);

            Assert.AreEqual(5, stack.ContributeMask.Length);
        }

        private sealed class ZeroBlendShapeSource : ExpressionTriggerInputSourceBase
        {
            private readonly System.Collections.BitArray _mask;

            public ZeroBlendShapeSource(FacialProfile profile, int hostBlendShapeCount)
                : base(InputSourceId.Parse("timeline:emotion"), 0, 16, ExclusionMode.LastWins, Array.Empty<string>(), profile)
            {
                _mask = new System.Collections.BitArray(hostBlendShapeCount, false);
            }

            public override System.Collections.BitArray ContributeMask => _mask;
        }
    }
}
