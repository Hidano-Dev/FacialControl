#if UNITY_EDITOR
using System.Collections;
using System.Text;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
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
    /// REC Export の出力 TimelineAsset をそのまま使い、4 手順（Export した Timeline を Director にセット / Receiver を
    /// FacialController と同じ GameObject に置く / 録画時と同じ Profile SO / Play）だけで Expression・Analog・Gaze が
    /// SkinnedMeshRenderer と目ボーンに再現されることを固定する end-to-end テスト（受け入れ条件 (1)(3)(4)）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class TimelinePlaybackEndToEndTests : SizedTestFixture
    {
        /// <summary>renderer の BlendShape weight（0..100 スケール）の許容誤差。</summary>
        private const float WeightTolerance = 0.5f;

        /// <summary>記録（Analog / Gaze の最初のサンプル 0.1 s・trigger 0.2〜0.8 s・遷移 0.1 s）より前。</summary>
        private const double BeforeRecordingSeconds = 0.05d;

        /// <summary>trigger 区間の中（遷移完了後）。</summary>
        private const double MidTriggerSeconds = 0.5d;

        /// <summary>trigger 終了と遷移の後（Analog / Gaze は続く）。</summary>
        private const double AfterTriggerSeconds = 1.2d;

        /// <summary>Clip 移動先の開始時刻（0.2〜0.8 s → 1.0〜1.6 s）。</summary>
        private const double MovedClipStartSeconds = 1.0d;

        /// <summary>移動後の trigger 区間の中（移動前は trigger 後）。</summary>
        private const double MovedMidTriggerSeconds = 1.3d;

        private const string EmotionValueSinkId = "timeline:" + TimelineE2EFixture.EmotionLayer;

        private TimelineE2EFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Export_FixtureRecording_ProducesTracksAndLocatorFindsBake()
        {
            _fixture = TimelineE2EFixture.Create();

            Assert.That(_fixture.ExportResult.Success, Is.True);
            BakeLocateResult located = FacialTimelineBakeLocator.Locate(_fixture.Timeline, null);
            Assert.That(located.Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(located.Bake, Is.SameAs(_fixture.ExportResult.BakeAsset));

            FacialValueTrack analog = FindValueTrack(_fixture.Timeline, RecFixtureWriter.AnalogSourceId);
            FacialValueTrack gaze = FindValueTrack(_fixture.Timeline, RecFixtureWriter.GazeSourceId);
            Assert.That(analog, Is.Not.Null, "前提: osc:lt の Value トラックが Export される");
            Assert.That(analog.ChannelKind, Is.EqualTo(FacialValueChannelKind.Analog));
            Assert.That(gaze, Is.Not.Null, "前提: osc:gaze の Value トラックが Export される");
            Assert.That(gaze.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(
                HasBakedCurve(_fixture.Bake, TimelineE2EFixture.EmotionLayer, TimelineE2EFixture.SmileBlendShape),
                Is.True,
                "前提: Profile の Expression から smile の BlendShape カーブが焼かれる");
        }

        private static bool HasBakedCurve(FacialTimelineBakeAsset bake, string layerName, string blendShapeName)
        {
            if (bake == null || bake.ExpressionBakes == null)
            {
                return false;
            }

            foreach (ExpressionSourceBake expressionBake in bake.ExpressionBakes)
            {
                if (expressionBake == null || expressionBake.LayerName != layerName || expressionBake.Curves == null)
                {
                    continue;
                }

                foreach (BlendShapeCurve curve in expressionBake.Curves)
                {
                    if (curve != null && curve.BlendShapeName == blendShapeName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ================================================================
        // 4 手順だけで Expression / Analog / Gaze が再現される（受け入れ条件 (1)(4)）
        // ================================================================

        [UnityTest]
        public IEnumerator Play_ReceiverAndDirectorOnControllerObject_ReproducesExpressionAnalogAndGaze()
        {
            _fixture = TimelineE2EFixture.Create();
            AssertProfileHasNoTimelineSettings(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return AssertReproducesRecording(character);
        }

        [UnityTest]
        public IEnumerator Play_DirectorOnParentObject_ReproducesSameResult()
        {
            _fixture = TimelineE2EFixture.Create();
            AssertProfileHasNoTimelineSettings(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.DirectorOnParent);

            yield return AssertReproducesRecording(character);
        }

        [UnityTest]
        public IEnumerator DirectorStop_AfterPlayback_RestoresFakeAnalogRegistryAndLayers()
        {
            _fixture = TimelineE2EFixture.Create();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            FacialController controller = character.Controller;
            FakeAnalogAdapterBinding fake = _fixture.FakeBinding;
            Quaternion leftRest = character.LeftEye.localRotation;

            Assert.That(controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, EmotionValueSinkId), Is.False, "前提: 再生前は未接続");
            Assert.That(controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, fake.AnalogExpressionSourceId), Is.True, "前提: 宣言済みの analog expression");

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(ResolveRegistry(controller, fake.AnalogSourceId), Is.Not.SameAs(fake.AnalogSource), "前提: 再生中は osc:lt が乗っ取られる");
            Assert.That(controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, EmotionValueSinkId), Is.True, "前提: 再生中は値 sink が接続される");
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape), Is.GreaterThan(1f), "前提: 再生中は squint が出ている");

            yield return character.StopDirector();

            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(ResolveRegistry(controller, fake.AnalogSourceId), Is.SameAs(fake.AnalogSource), "osc:lt が Fake に戻る");
            Assert.That(ResolveRegistry(controller, fake.GazeSourceId), Is.SameAs(fake.GazeSource), "osc:gaze が Fake に戻る");
            Assert.That(ResolveRegistry(controller, EmotionValueSinkId), Is.Null, "timeline:emotion の登録が外れる");
            Assert.That(controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, EmotionValueSinkId), Is.False, "値 sink の接続が外れる");
            Assert.That(controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, fake.AnalogExpressionSourceId), Is.True, "宣言済みの接続は残る");
            Assert.That(character.Receiver.ConnectedLayerNames, Is.Empty);
            Assert.That(character.Receiver.TakeoverEntries, Is.Empty);

            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape), Is.EqualTo(0f).Within(WeightTolerance), "Fake の値（0）に戻り squint が 0");
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance), "Timeline の値 sink が外れ smile が 0");
            Assert.That(Quaternion.Angle(character.LeftEye.localRotation, leftRest), Is.LessThan(0.01f), "Fake の gaze（0, 0）で目ボーンが初期姿勢に戻る");

            // analog expression の消費者は Fake の値に追従している（乗っ取り前の読み先に戻っている）。
            fake.AnalogSource.SetAxis(0, 0.25f);
            yield return null;
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape),
                Is.EqualTo(TimelineE2EFixture.SquintExpressionValue * 0.25f * 100f).Within(WeightTolerance));
        }

        // ================================================================
        // 手順欠落と復旧、Clip 編集後のタイミング変化（受け入れ条件 (3)）
        // ================================================================

        [UnityTest]
        public IEnumerator ClipMovedAndRebaked_NextPlayback_ShiftsBlendShapeChangeTime()
        {
            _fixture = TimelineE2EFixture.Create();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(WeightTolerance), "前提: 移動前は 0.5 s で smile");
            yield return character.EvaluateAt(MovedMidTriggerSeconds);
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance), "前提: 移動前は 1.3 s で smile なし");
            yield return character.StopDirector();

            // Clip を 0.2〜0.8 s から 1.0〜1.6 s へ移動し、Editor の再ベイクと同じ経路で焼き直す。
            TimelineClip smileClip = FindEmotionClip(_fixture.Timeline);
            smileClip.start = MovedClipStartSeconds;
            _fixture.Rebake();

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance), "移動後は 0.5 s で smile が出ない");
            yield return character.EvaluateAt(MovedMidTriggerSeconds);
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(WeightTolerance), "移動後は 1.3 s で smile が出る");
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.True, DescribeDiagnostics(character));
        }

        [UnityTest]
        public IEnumerator TrackBakeReferenceShifted_Play_FailsWithReferenceConflictAndRebakeRecovers()
        {
            LogAssert.ignoreFailingMessages = true;
            _fixture = TimelineE2EFixture.Create();
            var foreignBake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            try
            {
                // 1 トラック（osc:lt の Value トラック）だけ Bake 参照を別インスタンスにずらす。
                ((IFacialTimelineBakeHolder)FindValueTrack(_fixture.Timeline, RecFixtureWriter.AnalogSourceId)).Bake = foreignBake;
                TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

                yield return character.EvaluateAt(MidTriggerSeconds);
                Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed), DescribeDiagnostics(character));
                Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.True, DescribeDiagnostics(character));
                Assert.That(character.Receiver.Diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Error));
                Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance), "古い / 不整合な Bake を無言で再生しない");
                Assert.That(ResolveRegistry(character.Controller, EmotionValueSinkId), Is.Null, "Failed では何も登録しない");

                // 再ベイクで全トラックが同じ参照に戻り、次のセッションで Active に復旧する。
                _fixture.Rebake();
                Assert.That(FacialTimelineBakeLocator.Locate(_fixture.Timeline, null).Status, Is.EqualTo(BakeLocateStatus.Found));
                yield return character.StopDirector();
                character.Receiver.ReleaseAll();

                yield return character.EvaluateAt(MidTriggerSeconds);
                Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
                Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.False, DescribeDiagnostics(character));
                Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(WeightTolerance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(foreignBake);
            }
        }

        [UnityTest]
        public IEnumerator LegacyStateDeclaration_Play_FailsAndRemovingDeclarationReproduces()
        {
            LogAssert.ignoreFailingMessages = true;
            // 旧 Profile: Layer.inputSources に timeline:{layer}:state の宣言が残っている（その Profile で録画・Export した）。
            _fixture = TimelineE2EFixture.Create(configureProfile: profile =>
                profile.Layers[0].inputSources.Add(new InputSourceDeclarationSerializable { id = EmotionValueSinkId + ":state", weight = 1f }));
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed), DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.True, DescribeDiagnostics(character));
            Assert.That(ResolveRegistry(character.Controller, EmotionValueSinkId), Is.Null, "旧 :state 宣言では何も登録しない");
            Assert.That(character.Controller.IsLayerInputSourceBound(TimelineE2EFixture.EmotionLayer, EmotionValueSinkId), Is.False);
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance));

            // 宣言を除いた Profile（Profile 変更に伴う再ベイク済み）で読み込み直すと再現される。
            _fixture.EmotionInputSources.RemoveAll(declaration => declaration.id == EmotionValueSinkId + ":state");
            _fixture.MarkProfileChanged();
            _fixture.Rebake();
            character.Controller.LoadCharacter(_fixture.ProfileAsset);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle), "前提: 読み込み直しで binding が付け直されセッションが解放される");

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.False, DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True, DescribeDiagnostics(character));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(WeightTolerance));
        }

        [UnityTest]
        public IEnumerator ValueSinkDeclarationOnly_LegacyProfile_PlaysWithDeclaredConnection()
        {
            // 第 1 段のロールバック基準: 値 sink（timeline:{layer}）の宣言だけを持つ旧 Profile はそのまま動く。
            _fixture = TimelineE2EFixture.Create();
            _fixture.EmotionInputSources.Add(new InputSourceDeclarationSerializable { id = EmotionValueSinkId, weight = 1f });
            _fixture.MarkProfileChanged();
            _fixture.Rebake();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(
                character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LayerConnectionSkippedDeclared, TimelineE2EFixture.EmotionLayer),
                Is.True,
                DescribeDiagnostics(character));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(WeightTolerance));
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape),
                Is.EqualTo(TimelineE2EFixture.SquintExpressionValue * _fixture.Recording.AnalogValue * 100f).Within(WeightTolerance));
        }

        [UnityTest]
        public IEnumerator ReceiverOnChildObject_Play_ReportsReceiverNotOnControllerObject()
        {
            LogAssert.ignoreFailingMessages = true;
            _fixture = TimelineE2EFixture.Create();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.ReceiverOnChild);
            Assert.That(character.Receiver.gameObject, Is.Not.SameAs(character.Controller.gameObject), "前提: Receiver は controller と別 GameObject");

            // Start の静的診断（配置の誤り）が Play 開始時に記録される。
            yield return null;
            Assert.That(
                character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ReceiverNotOnControllerObject),
                Is.True,
                DescribeDiagnostics(character));

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed), DescribeDiagnostics(character));
            Assert.That(
                character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ReceiverNotOnControllerObject),
                Is.True,
                DescribeDiagnostics(character));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance));
        }

        [UnityTest]
        public IEnumerator BindingDisabled_Play_ReportsBindingDisabled()
        {
            LogAssert.ignoreFailingMessages = true;
            _fixture = TimelineE2EFixture.Create();
            _fixture.TimelineBinding.Enabled = false;
            _fixture.MarkProfileChanged();
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            yield return null;
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingDisabled), Is.True, DescribeDiagnostics(character));

            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed), DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingDisabled), Is.True, DescribeDiagnostics(character));
            Assert.That(ResolveRegistry(character.Controller, EmotionValueSinkId), Is.Null, "無効時は sink を登録しない");
            Assert.That(ResolveRegistry(character.Controller, _fixture.FakeBinding.AnalogSourceId), Is.SameAs(_fixture.FakeBinding.AnalogSource), "無効時は乗っ取らない");
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance));
        }

        private static TimelineClip FindEmotionClip(TimelineAsset timeline)
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is FacialExpressionTrack && track.name == TimelineE2EFixture.EmotionLayer)
                {
                    foreach (TimelineClip clip in track.GetClips())
                    {
                        return clip;
                    }
                }
            }

            Assert.Fail("前提: emotion の Expression トラックに Clip がある");
            return null;
        }

        private IEnumerator AssertReproducesRecording(TimelineE2ECharacter character)
        {
            Assert.That(character.Controller.IsInitialized, Is.True, "前提: FacialController が Profile SO から初期化される");
            Quaternion leftRest = character.LeftEye.localRotation;
            Quaternion rightRest = character.RightEye.localRotation;

            // 記録の前（trigger 前・analog / gaze サンプル前）は何も出ない。
            yield return character.EvaluateAt(BeforeRecordingSeconds);
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance));
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape), Is.EqualTo(0f).Within(WeightTolerance));

            // trigger 区間の中: smile の Expression 値、Analog クリップ値 × squint の Expression 値、Gaze の目ボーン回転。
            yield return character.EvaluateAt(MidTriggerSeconds);
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape),
                Is.EqualTo(TimelineE2EFixture.SmileExpressionValue * 100f).Within(WeightTolerance),
                "smile のトリガーで Expression 値が出る");
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape),
                Is.EqualTo(TimelineE2EFixture.SquintExpressionValue * _fixture.Recording.AnalogValue * 100f).Within(WeightTolerance),
                "Analog クリップ値 × Expression 値が squint に出る");
            Assert.That(Quaternion.Angle(character.LeftEye.localRotation, leftRest), Is.GreaterThan(1f), "Gaze が左目ボーンの回転に出る");
            Assert.That(Quaternion.Angle(character.RightEye.localRotation, rightRest), Is.GreaterThan(1f), "Gaze が右目ボーンの回転に出る");

            // trigger 終了後: smile は戻り、Analog は続く。
            yield return character.EvaluateAt(AfterTriggerSeconds);
            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(0f).Within(WeightTolerance));
            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SquintBlendShape),
                Is.EqualTo(TimelineE2EFixture.SquintExpressionValue * _fixture.Recording.AnalogValue * 100f).Within(WeightTolerance));

            // 問題なしの状態値が保持される（不要な警告なし）。
            FacialTimelineDiagnostics diagnostics = character.Receiver.Diagnostics;
            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Ok), DescribeDiagnostics(character));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.True, DescribeDiagnostics(character));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True, DescribeDiagnostics(character));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, TimelineE2EFixture.EmotionLayer), Is.True, DescribeDiagnostics(character));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.AnalogTakeoverAttached), Is.True, DescribeDiagnostics(character));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.GazeTakeoverAttached), Is.True, DescribeDiagnostics(character));
        }

        private static void AssertProfileHasNoTimelineSettings(TimelineE2EFixture fixture)
        {
            foreach (var layer in fixture.ProfileAsset.Layers)
            {
                foreach (var declaration in layer.inputSources)
                {
                    Assert.That(declaration.id, Does.Not.StartWith("timeline:"), "前提: Profile に timeline の宣言を書かない");
                }
            }

            Assert.That(fixture.TimelineBinding.HasLegacyFields, Is.False, "前提: Target Layer Names / Channel Definitions を書かない");
            Assert.That(fixture.TimelineBinding.Enabled, Is.True);
        }

        private static IInputSource ResolveRegistry(FacialController controller, string id)
        {
            return controller.InputSourceRegistry.TryResolve(id, out IInputSource source) ? source : null;
        }

        private static string DescribeDiagnostics(TimelineE2ECharacter character)
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

        private static FacialValueTrack FindValueTrack(TimelineAsset timeline, string channelSubId)
        {
            var tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] is FacialValueTrack valueTrack
                    && string.Equals(valueTrack.ChannelSubId, channelSubId, System.StringComparison.Ordinal))
                {
                    return valueTrack;
                }
            }

            return null;
        }
    }
}
#endif
