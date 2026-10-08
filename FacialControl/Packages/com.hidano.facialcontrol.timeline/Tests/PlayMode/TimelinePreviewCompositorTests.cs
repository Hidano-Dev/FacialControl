#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// Edit プレビューの合成（<see cref="TimelinePreviewCompositor"/>）が Director 再生（Play）と同じ値を
    /// renderer と目ボーンへ出すことを、D9 の比較時刻集合と許容誤差で固定する（Req 7.1 / 7.6 / 11.5）。
    /// </summary>
    /// <remarks>
    /// 比較は「Timeline 以外の live 入力が無く、レイヤー weight が既定」の条件で行う。Analog チャネル経由の出力
    /// （Analog Value トラック → analog 消費者 → BlendShape）も、Edit は Profile の binding 宣言から同じ消費者をオフラインに
    /// 組んで再現する（HID-148）。<see cref="Evaluate_AnalogChannelNonZero_MatchesDirectorPlaybackAtComparisonTimes"/> で固定する。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class TimelinePreviewCompositorTests : SizedTestFixture
    {
        /// <summary>renderer の BlendShape weight（0..100 スケール。正規化 1e-4 相当）の許容誤差。</summary>
        private const float RendererTolerance = 0.01f;

        /// <summary>目ボーン localRotation の各成分の許容誤差。</summary>
        private const float GazeTolerance = 1e-3f;

        private const double FrameSeconds = 1d / 60d;

        private static readonly string[] ComparedBlendShapes =
        {
            TimelineE2EFixture.SmileBlendShape,
            TimelineE2EFixture.SquintBlendShape,
            TimelineE2EFixture.BlinkBlendShape,
            TimelineE2EFixture.JawOpenBlendShape,
            TimelineE2EFixture.EyeWideBlendShape,
        };

        /// <summary>JSON だけを書き換えるときの smile の Expression 値（Bake は 1.0 で焼いてある）。</summary>
        private const float ChangedSmileValue = 0.6f;

        /// <summary>smile の trigger 区間の中（遷移完了後）。</summary>
        private const double MidTriggerSeconds = 0.5d;

        /// <summary>描画しないことの確認に使う renderer の初期値。</summary>
        private const float SentinelWeight = 42f;

        private TimelineE2EFixture _fixture;
        private TimelinePreviewCompositor _compositor;
        private readonly List<FacialTimelinePreviewEyeTarget> _gazeBuffer = new List<FacialTimelinePreviewEyeTarget>();
        private TimelineEditChangeWatcher _watcher;
        private Func<bool> _originalPlayProbe;
        private Action _originalRefresh;

        [TearDown]
        public void TearDown()
        {
            FacialTimelineEditorPreview.ClearCache();
            if (_watcher != null)
            {
                _watcher.DiscardPending();
                _watcher.IsPlayModeTransition = _originalPlayProbe;
                _watcher.RefreshTimelineWindow = _originalRefresh;
                _watcher = null;
            }

            _compositor?.Dispose();
            _compositor = null;
            _fixture?.Dispose();
            _fixture = null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator Evaluate_SameSnapshot_MatchesDirectorPlaybackAtComparisonTimes()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f });
            PrepareSameSnapshot(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            FacialProfile profile = TimelineProfileSource.Resolve(_fixture.ProfileAsset);
            _compositor = new TimelinePreviewCompositor(
                character.Controller,
                _fixture.ProfileAsset,
                profile,
                null,
                _fixture.Timeline);
            Assert.That(_compositor.CanRender, Is.True, "前提: Export 直後の Timeline は Bake を Found で解決できる");
            Assert.That(_compositor.ProfileCheck, Is.EqualTo(TimelineDiagnosticCode.Ok), "前提: 同一スナップショットで焼いた Bake");

            var result = new ComparisonResult();
            yield return AssertEditMatchesPlay(character, _compositor, result);

            Assert.That(result.MaxSmile, Is.GreaterThan(50f), "前提: 比較時刻に smile が出ている時刻を含む");
            Assert.That(result.SawGaze, Is.True, "前提: 比較時刻に Gaze が出ている時刻を含む");
        }

        /// <summary>
        /// Analog 値が 0 でない記録でも、Edit プレビュー（Compositor）は Profile の binding 宣言から Play と同じ analog 消費者を組み、
        /// Analog Value トラックの値で駆動するため、squint を含めて比較時刻ごとに Play と一致する（HID-148）。
        /// </summary>
        [UnityTest]
        public IEnumerator Evaluate_AnalogChannelNonZero_MatchesDirectorPlaybackAtComparisonTimes()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0.5f });
            PrepareSameSnapshot(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            _compositor = CreateCompositor(character);
            Assert.That(_compositor.CanRender, Is.True, "前提: Bake を解決できる");

            var result = new ComparisonResult();
            yield return AssertEditMatchesPlay(character, _compositor, result);

            Assert.That(result.MaxSquint, Is.GreaterThan(1f), "前提: 比較時刻に Analog 消費者経由の squint が出ている時刻を含む");
        }

        /// <summary>
        /// 値提供型（REC の kind 7 / 8）の記録でも、Edit プレビューは値提供型 Value トラックの sink を Profile のレイヤー宣言
        /// （<c>face</c> レイヤーの <c>ifm</c>）どおりに合成し、比較時刻ごとに Play と一致する（HID-178）。
        /// </summary>
        [UnityTest]
        public IEnumerator Evaluate_ValueProviderChannel_MatchesDirectorPlaybackAtComparisonTimes()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f, IncludeValueProvider = true });
            PrepareSameSnapshot(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            _compositor = CreateCompositor(character);
            Assert.That(_compositor.CanRender, Is.True, "前提: Bake を解決できる");

            var result = new ComparisonResult();
            yield return AssertEditMatchesPlay(character, _compositor, result);

            Assert.That(result.MaxValueProvider, Is.GreaterThan(1f), "前提: 比較時刻に値提供型の BlendShape が出ている時刻を含む");
        }

        [UnityTest]
        public IEnumerator Evaluate_ProfileJsonDiffersFromBake_ReportsMismatchRendersBakeAndMatchesPlayThenRebakeRestoresOk()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f });
            PrepareSameSnapshot(_fixture);
            RewriteProfileJsonSmileValue(_fixture, ChangedSmileValue);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            // Edit: profile.json（smile 0.6）と Bake（smile 1.0 で焼いた）が食い違う。描画は止めず Bake の値を出す。
            _compositor = CreateCompositor(character);
            Assert.That(_compositor.ProfileCheck, Is.EqualTo(TimelineDiagnosticCode.ProfileMismatch));
            Assert.That(_compositor.CanRender, Is.True, "ProfileMismatch でも描画は続ける");

            var mismatched = new ComparisonResult();
            yield return AssertEditMatchesPlay(character, _compositor, mismatched);
            Assert.That(mismatched.MaxSmile, Is.EqualTo(100f).Within(RendererTolerance), "不一致中は Edit / Play とも Bake の値（smile 1.0）を出す");

            // Play: 同じ fixture のセッションは ProfileMismatch（Warning）で Active のまま。
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True, DescribeDiagnostics(character));
            Assert.That(FindSeverity(character, TimelineDiagnosticCode.ProfileMismatch), Is.EqualTo(TimelineDiagnosticSeverity.Warning));

            // MarkDirty(ProfileMismatch) → 再ベイク（Watcher の既定の再ベイク口）。
            TimelineEditChangeWatcher watcher = AcquireWatcherForPlayModeTest();
            Assert.That(watcher.MarkDirty(_fixture.Timeline, TimelineDirtyReason.ProfileMismatch), Is.EqualTo(MarkDirtyResult.Queued));
            watcher.FlushNow();
            Assert.That(watcher.IsPending(_fixture.Timeline), Is.False);

            // 再ベイク後: Edit の Compositor（作り直し）も Play のセッション（張り直し）も Ok になり、値が一致する。
            _compositor.Dispose();
            _compositor = CreateCompositor(character);
            Assert.That(_compositor.ProfileCheck, Is.EqualTo(TimelineDiagnosticCode.Ok));
            yield return character.StopDirector();
            character.Receiver.ReleaseAll();

            var rebaked = new ComparisonResult();
            yield return AssertEditMatchesPlay(character, _compositor, rebaked);
            Assert.That(rebaked.MaxSmile, Is.EqualTo(ChangedSmileValue * 100f).Within(RendererTolerance), "再ベイク後は profile.json の値（smile 0.6）");
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True, DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.False, DescribeDiagnostics(character));
        }

        [UnityTest]
        public IEnumerator ApplyPreview_ProfileMismatch_RendersBakeWritesWarningDiagnosticAndMarksDirty()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f });
            PrepareSameSnapshot(_fixture);
            RewriteProfileJsonSmileValue(_fixture, ChangedSmileValue);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            yield return null;
            TimelineEditChangeWatcher watcher = AcquireWatcherForPlayModeTest();

            FacialTimelineEditorPreview.ApplyPreview(character.Receiver, _fixture.Timeline, MidTriggerSeconds);

            Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(100f).Within(RendererTolerance), "Bake の値で描く");
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True, DescribeDiagnostics(character));
            Assert.That(FindSeverity(character, TimelineDiagnosticCode.ProfileMismatch), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(watcher.IsPending(_fixture.Timeline), Is.True, "ProfileMismatch で自動再ベイクを予約する");
            Assert.That(character.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle), "Edit プレビューはセッションを開始しない");

            // 再ベイク（BakeUpdated）でキャッシュを破棄し、次のプレビューは新しい Bake・Profile 一致で描く。
            watcher.FlushNow();
            FacialTimelineEditorPreview.ApplyPreview(character.Receiver, _fixture.Timeline, MidTriggerSeconds);

            Assert.That(
                character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape),
                Is.EqualTo(ChangedSmileValue * 100f).Within(RendererTolerance),
                "再ベイク後の Bake で描く");
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.False, DescribeDiagnostics(character));
            Assert.That(character.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True, DescribeDiagnostics(character));
            Assert.That(watcher.IsPending(_fixture.Timeline), Is.False);
        }

        [UnityTest]
        public IEnumerator ApplyPreview_BakeReferenceConflict_DoesNotRenderAndMarksDirty()
        {
            // Play 側の Receiver が Start の静的診断で BakeReferenceConflict（Error）を Console に出す。
            LogAssert.ignoreFailingMessages = true;
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f });
            var foreignBake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            try
            {
                ((IFacialTimelineBakeHolder)FindRootTrack<FacialValueTrack>(_fixture.Timeline)).Bake = foreignBake;
                TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);
                yield return null;
                TimelineEditChangeWatcher watcher = AcquireWatcherForPlayModeTest();
                int smileIndex = character.Renderer.sharedMesh.GetBlendShapeIndex(TimelineE2EFixture.SmileBlendShape);
                character.Renderer.SetBlendShapeWeight(smileIndex, SentinelWeight);

                FacialTimelineEditorPreview.ApplyPreview(character.Receiver, _fixture.Timeline, MidTriggerSeconds);

                Assert.That(character.GetBlendShapeWeight(TimelineE2EFixture.SmileBlendShape), Is.EqualTo(SentinelWeight), "Bake が一意に決まらないので描かない");
                Assert.That(watcher.IsPending(_fixture.Timeline), Is.True, "参照不整合で自動再ベイクを予約する");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(foreignBake);
            }
        }

        private TimelinePreviewCompositor CreateCompositor(TimelineE2ECharacter character)
        {
            TimelineProfileSource.InvalidateCache(_fixture.ProfileAsset);
            return new TimelinePreviewCompositor(
                character.Controller,
                _fixture.ProfileAsset,
                TimelineProfileSource.Resolve(_fixture.ProfileAsset),
                null,
                _fixture.Timeline);
        }

        /// <summary>
        /// 比較時刻ごとに Director を評価（Play）して renderer / 目ボーンを読み、同じ時刻を Compositor（Edit）で描いて比べる。
        /// </summary>
        private IEnumerator AssertEditMatchesPlay(
            TimelineE2ECharacter character,
            TimelinePreviewCompositor compositor,
            ComparisonResult result)
        {
            var resolver = new BoneTransformResolver(character.Controller.transform);
            GazeEyeBoneFallback fallback = GazeEyeBoneFallback.FromAnimator(character.Controller.GetComponent<Animator>());
            foreach (double time in CollectComparisonTimes(_fixture.Timeline))
            {
                yield return character.EvaluateAt(time);
                float[] played = ReadWeights(character);
                Quaternion playedLeft = character.LeftEye.localRotation;
                Quaternion playedRight = character.RightEye.localRotation;

                compositor.Evaluate(time);
                compositor.EvaluateGaze(time, _fixture.ProfileAsset.GazeChannels, resolver, fallback, _gazeBuffer);
                float[] composed = ReadWeights(character);

                for (int i = 0; i < ComparedBlendShapes.Length; i++)
                {
                    Assert.That(
                        composed[i],
                        Is.EqualTo(played[i]).Within(RendererTolerance),
                        $"t={time:F4} {ComparedBlendShapes[i]}: Edit={composed[i]} Play={played[i]}");
                }

                AssertRotation(playedLeft, character.LeftEye.localRotation, $"t={time:F4} LeftEye");
                AssertRotation(playedRight, character.RightEye.localRotation, $"t={time:F4} RightEye");

                result.MaxSmile = Math.Max(result.MaxSmile, played[0]);
                result.MaxSquint = Math.Max(result.MaxSquint, played[1]);
                for (int i = 2; i < played.Length; i++)
                {
                    result.MaxValueProvider = Math.Max(result.MaxValueProvider, played[i]);
                }
                result.SawGaze |= Quaternion.Angle(playedLeft, Quaternion.identity) > 1f;
            }
        }

        /// <summary>
        /// profile.json の smile の Expression 値だけを書き換える（SO と Bake は元の値のまま）。
        /// SO の値を一時的に変えて AutoExporter の冪等入口で書き出し、SO を元に戻して保存する。
        /// </summary>
        private static void RewriteProfileJsonSmileValue(TimelineE2EFixture fixture, float value)
        {
            var snapshot = fixture.ProfileAsset.Expressions[0].cachedSnapshot;
            float original = snapshot.blendShapes[0].value;
            snapshot.blendShapes[0].value = value;
            Assert.That(FacialCharacterProfileAutoExporter.ExportIfEnabled(fixture.ProfileAsset), Is.True, "前提: profile.json が書き換わる");
            snapshot.blendShapes[0].value = original;
            fixture.MarkProfileChanged();
        }

        /// <summary>
        /// Editor の変更検知 Watcher を PlayMode テストで使えるようにする（Play 遷移中判定を外し、TearDown で戻す）。
        /// </summary>
        private TimelineEditChangeWatcher AcquireWatcherForPlayModeTest()
        {
            TimelineEditorServices.EnsureInitialized();
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            _watcher = watcher;
            _originalPlayProbe = watcher.IsPlayModeTransition;
            _originalRefresh = watcher.RefreshTimelineWindow;
            watcher.IsPlayModeTransition = () => false;
            watcher.RefreshTimelineWindow = () => { };
            return watcher;
        }

        private static TimelineDiagnosticSeverity FindSeverity(TimelineE2ECharacter character, TimelineDiagnosticCode code)
        {
            IReadOnlyList<TimelineDiagnosticItem> items = character.Receiver.Diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Code == code)
                {
                    return items[i].Severity;
                }
            }

            Assert.Fail($"{code} が診断に無い: {DescribeDiagnostics(character)}");
            return default;
        }

        private static T FindRootTrack<T>(TimelineAsset timeline)
            where T : TrackAsset
        {
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (track is T typed)
                {
                    return typed;
                }
            }

            Assert.Fail($"前提: {typeof(T).Name} が Timeline にある");
            return null;
        }

        private static string DescribeDiagnostics(TimelineE2ECharacter character)
        {
            var builder = new StringBuilder();
            builder.Append("state=").Append(character.Receiver.SessionState).Append(" items=[");
            IReadOnlyList<TimelineDiagnosticItem> items = character.Receiver.Diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                builder.Append(items[i].Severity).Append(':').Append(items[i].Code).Append('(').Append(items[i].Subject).Append(") ");
            }

            return builder.Append(']').ToString();
        }

        private sealed class ComparisonResult
        {
            public float MaxSmile { get; set; }

            public float MaxSquint { get; set; }

            /// <summary>値提供型が動かす BlendShape（Blink / JawOpen / EyeWide）の Play 側の最大値。</summary>
            public float MaxValueProvider { get; set; }

            public bool SawGaze { get; set; }
        }

        /// <summary>
        /// 同一スナップショット: SO 保存 → profile.json 書き出し（AutoExporter の冪等入口）→ Profile 読込キャッシュ無効化 → 再ベイク。
        /// </summary>
        private static void PrepareSameSnapshot(TimelineE2EFixture fixture)
        {
            fixture.MarkProfileChanged();
            FacialCharacterProfileAutoExporter.ExportIfEnabled(fixture.ProfileAsset);
            TimelineProfileSource.InvalidateCache(fixture.ProfileAsset);
            fixture.Rebake();
        }

        /// <summary>
        /// D9 の比較時刻集合: 0 / 各 Clip の start・end ±1/60 s / 中点 / duration（[0, duration] に収め昇順・重複除去）。
        /// </summary>
        private static List<double> CollectComparisonTimes(TimelineAsset timeline)
        {
            double duration = timeline.duration;
            var times = new SortedSet<double> { 0d, duration };
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!(track is FacialExpressionTrack) && !(track is FacialValueTrack))
                {
                    continue;
                }

                foreach (TimelineClip clip in track.GetClips())
                {
                    AddClamped(times, clip.start - FrameSeconds, duration);
                    AddClamped(times, clip.start + FrameSeconds, duration);
                    AddClamped(times, clip.end - FrameSeconds, duration);
                    AddClamped(times, clip.end + FrameSeconds, duration);
                    AddClamped(times, (clip.start + clip.end) * 0.5d, duration);
                }
            }

            return new List<double>(times);
        }

        private static void AddClamped(SortedSet<double> times, double time, double duration)
        {
            times.Add(Math.Max(0d, Math.Min(duration, time)));
        }

        private static float[] ReadWeights(TimelineE2ECharacter character)
        {
            var weights = new float[ComparedBlendShapes.Length];
            for (int i = 0; i < ComparedBlendShapes.Length; i++)
            {
                weights[i] = character.GetBlendShapeWeight(ComparedBlendShapes[i]);
            }

            return weights;
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual, string label)
        {
            // q と -q は同じ回転なので符号を揃えて成分を比べる。
            float sign = Quaternion.Dot(expected, actual) < 0f ? -1f : 1f;
            var message = new StringBuilder(label).Append(": Edit=").Append(actual.ToString("F5"))
                .Append(" Play=").Append(expected.ToString("F5")).ToString();
            Assert.That(actual.x * sign, Is.EqualTo(expected.x).Within(GazeTolerance), message);
            Assert.That(actual.y * sign, Is.EqualTo(expected.y).Within(GazeTolerance), message);
            Assert.That(actual.z * sign, Is.EqualTo(expected.z).Within(GazeTolerance), message);
            Assert.That(actual.w * sign, Is.EqualTo(expected.w).Within(GazeTolerance), message);
        }
    }
}
#endif
