using System.Collections;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// S-3: Activate → Deactivate → ゼロ復帰の経路を「実フレーム同期 (MonoBehaviour
    /// ライフサイクル + <see cref="Time.deltaTime"/>)」で踏むリグレッションテスト。
    /// </summary>
    /// <remarks>
    /// EditMode 側 (<c>LayerUseCaseTests.UpdateWeights_AfterDeactivate_TransitionsBackToZero</c>)
    /// は <see cref="LayerUseCase.UpdateWeights"/> を 1 度だけ呼ぶ単発検証で、
    /// PlayerLoop 経由の <c>UpdateExpressions(empty)</c> 連続呼び出しまでは保証していなかった。
    /// 本テストは <see cref="LayerUseCaseHostBehaviour"/> 経由で各フレーム
    /// <see cref="LayerUseCase.UpdateWeights"/> を呼びつつ Activate / Deactivate を踏む。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class LayerLifecycleZeroFadeTests : SizedTestFixture
    {
        private GameObject _host;
        private LayerUseCase _useCase;
        private ExpressionUseCase _expressionUseCase;

        [TearDown]
        public void TearDown()
        {
            _useCase?.Dispose();
            _useCase = null;
            _expressionUseCase = null;

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [UnityTest]
        public IEnumerator ActivateThenDeactivate_DrivenByMonoBehaviourTick_FadesToZero()
        {
            const int maxTransitionFrames = 240;
            string[] blendShapeNames = { "bs_smile", "bs_sad" };

            var layers = new[]
            {
                new LayerDefinition("emotion", priority: 0, exclusionMode: ExclusionMode.LastWins)
            };
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f)
            };
            // 遷移時間 0.05 秒。Time.deltaTime が ~16ms 想定でも 4 フレーム程度で完了する。
            var expressions = new[]
            {
                new Expression(
                    "expr-smile", "smile", "emotion",
                    transitionDuration: 0.05f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: blendShapes)
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            _expressionUseCase = new ExpressionUseCase(profile);
            _useCase = new LayerUseCase(profile, _expressionUseCase, blendShapeNames);

            _host = new GameObject(nameof(LayerLifecycleZeroFadeTests));
            var tickHost = _host.AddComponent<LayerUseCaseHostBehaviour>();
            tickHost.Bind(_useCase);

            // ---- Activate: 実時間 + フレームで target = 1.0 に到達することを確認 ----
            // batchmode では yield return null だけだと Time.deltaTime が安定して累積しないため、
            // WaitForSeconds で実時間を進めたうえで、最大 maxTransitionFrames まで残りフレームで polling する。
            _expressionUseCase.Activate(profile.Expressions.Span[0]);
            yield return new WaitForSeconds(0.1f);
            for (int i = 0; i < maxTransitionFrames; i++)
            {
                var output = _useCase.BlendedOutputSpan;
                if (output.Length > 0 && Mathf.Abs(output[0] - 1.0f) <= 0.01f)
                {
                    break;
                }

                yield return null;
            }

            float[] activatedOutput = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, activatedOutput[0], 0.01f,
                "実時間経過後も target = 1.0 に達していない (transitionDuration=0.05s)。");
            Assert.AreEqual(0.0f, activatedOutput[1], 0.01f);

            // ---- Deactivate: 実時間 + フレームで rest = 0.0 へ補間されることを確認 ----
            _expressionUseCase.Deactivate(profile.Expressions.Span[0]);
            yield return new WaitForSeconds(0.1f);
            for (int i = 0; i < maxTransitionFrames; i++)
            {
                var output = _useCase.BlendedOutputSpan;
                if (output.Length > 0 && Mathf.Abs(output[0] - 0.0f) <= 0.01f)
                {
                    break;
                }

                yield return null;
            }

            float[] deactivatedOutput = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.0f, deactivatedOutput[0], 0.01f,
                "Deactivate 後、実時間経過でゼロに戻っていない (latched バグの再発)。");
            Assert.AreEqual(0.0f, deactivatedOutput[1], 0.01f);
        }

        [UnityTest]
        public IEnumerator ActivateThenDeactivate_MidTransition_ReachesZeroOverFrames()
        {
            // 遷移途中で Deactivate しても、最終的にゼロまで補間されることを実フレームで確認する。
            string[] blendShapeNames = { "bs_smile" };

            var layers = new[]
            {
                new LayerDefinition("emotion", priority: 0, exclusionMode: ExclusionMode.LastWins)
            };
            var blendShapes = new[] { new BlendShapeMapping("bs_smile", 1.0f) };
            var expressions = new[]
            {
                new Expression(
                    "expr-smile", "smile", "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: blendShapes)
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            _expressionUseCase = new ExpressionUseCase(profile);
            _useCase = new LayerUseCase(profile, _expressionUseCase, blendShapeNames);

            _host = new GameObject(nameof(LayerLifecycleZeroFadeTests));
            var tickHost = _host.AddComponent<LayerUseCaseHostBehaviour>();
            tickHost.Bind(_useCase);

            _expressionUseCase.Activate(profile.Expressions.Span[0]);

            // 遷移途中 (~半分) で Deactivate する。
            yield return new WaitForSeconds(0.1f);
            float midValue = _useCase.GetBlendedOutput()[0];
            Assert.Greater(midValue, 0f);
            Assert.Less(midValue, 1f, "前提: Deactivate 時に遷移途中であること。");

            _expressionUseCase.Deactivate(profile.Expressions.Span[0]);

            // 遷移時間 + バッファで完全にゼロまで補間されるはず。
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(0.0f, _useCase.GetBlendedOutput()[0], 0.01f,
                "遷移途中の Deactivate からゼロ復帰が実フレームで完了していない。");
        }

        /// <summary>
        /// PlayerLoop の Update phase で <see cref="LayerUseCase.UpdateWeights"/> を
        /// <see cref="Time.deltaTime"/> 込みで毎フレーム呼ぶテスト用ホスト。
        /// </summary>
        private sealed class LayerUseCaseHostBehaviour : MonoBehaviour
        {
            private LayerUseCase _useCase;

            public void Bind(LayerUseCase useCase)
            {
                _useCase = useCase;
            }

            private void Update()
            {
                _useCase?.UpdateWeights(Time.deltaTime);
            }
        }
    }
}
