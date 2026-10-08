using System.Collections;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// PlayMode E2E: OSC Receiver の対象レイヤー（<see cref="OscReceiverAdapterBinding.TargetLayer"/>）だけを選べば、
    /// レイヤーの inputSources を手で編集しなくても実 UDP の受信値が Renderer に届くこと、
    /// 手動宣言があるプロファイルでは同じ入力源を 2 回合成しないことを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscReceiverTargetLayerE2ETests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19760;
        private const string BlendShapeName = "smile";
        private const string BaseLayerName = "emotion";
        private const string TargetLayerName = "face-capture";
        private const string SourceExpressionId = "source-smile";
        private const string SenderSlug = "osc-sender-target-layer";
        private const string ReceiverSlug = "osc-receiver-target-layer";
        private const float SourceValue = 0.4f;

        private static int s_portCounter;

        private GameObject _sourceRoot;
        private GameObject _receiverRoot;
        private Mesh _sourceMesh;
        private Mesh _receiverMesh;
        private OscSendReceiveE2ETests.TestOscE2EProfileSO _sourceProfileSo;
        private OscSendReceiveE2ETests.TestOscE2EProfileSO _receiverProfileSo;

        [TearDown]
        public void TearDown()
        {
            DeactivateAndDestroy(_sourceRoot);
            _sourceRoot = null;
            DeactivateAndDestroy(_receiverRoot);
            _receiverRoot = null;
            DestroyImmediateIfAlive(_sourceProfileSo);
            _sourceProfileSo = null;
            DestroyImmediateIfAlive(_receiverProfileSo);
            _receiverProfileSo = null;
            DestroyImmediateIfAlive(_sourceMesh);
            _sourceMesh = null;
            DestroyImmediateIfAlive(_receiverMesh);
            _receiverMesh = null;
        }

        [UnityTest]
        public IEnumerator Receive_NoManualDeclarationWithTargetLayer_ReachesReceiverRenderer()
        {
            // 受信側プロファイルのどのレイヤーにも受信 slug を宣言しない。対象レイヤーの選択だけで繋がること。
            FacialProfile receiverProfile = new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition(BaseLayerName, 0, ExclusionMode.LastWins),
                    new LayerDefinition(TargetLayerName, 1, ExclusionMode.LastWins),
                });

            yield return RunLoopback(receiverProfile, TargetLayerName, value => Assert.That(
                value,
                Is.EqualTo(SourceValue).Within(0.08f),
                "対象レイヤーへ自動で宣言された受信入力源の値が Renderer に適用されること。"));

            CollectionAssert.IsEmpty(
                _receiverProfileSo.Profile.LayerInputSources.ToArray(),
                "自動宣言はランタイムの解決結果だけに効き、Profile を書き換えない。");
        }

        [UnityTest]
        public IEnumerator Receive_ManualDeclarationAndTargetLayer_DoesNotComposeTwice()
        {
            // 手動宣言（weight 1）と対象レイヤーが両方ある既存プロファイル。二重に合成すると 2 倍（clamp で 1.0）になる。
            FacialProfile receiverProfile = new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition(BaseLayerName, 0, ExclusionMode.LastWins),
                },
                expressions: null,
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration(ReceiverSlug, 1f, null) },
                });

            yield return RunLoopback(receiverProfile, BaseLayerName, value => Assert.That(
                value,
                Is.EqualTo(SourceValue).Within(0.08f),
                "手動宣言済みの受信入力源は自動宣言で二重に合成されないこと。"));
        }

        private IEnumerator RunLoopback(
            FacialProfile receiverProfile,
            string targetLayer,
            System.Action<float> assertReached)
        {
            int port = AllocatePort();
            _sourceMesh = CreateMeshWithBlendShape("OscTargetLayerE2E_SourceMesh");
            _receiverMesh = CreateMeshWithBlendShape("OscTargetLayerE2E_ReceiverMesh");

            var senderBinding = new OscSenderAdapterBinding
            {
                Slug = SenderSlug,
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f
            };
            senderBinding.Configure(Endpoint, port, new[] { BlendShapeName });

            var receiverBinding = new OscReceiverAdapterBinding
            {
                Slug = ReceiverSlug,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                TargetLayer = targetLayer
            };
            receiverBinding.Configure(
                Endpoint,
                port,
                new[]
                {
                    new OscMapping("/avatar/parameters/" + BlendShapeName, BlendShapeName, targetLayer)
                });

            _sourceProfileSo = CreateProfileSo(CreateSourceProfile(), senderBinding);
            _receiverProfileSo = CreateProfileSo(receiverProfile, receiverBinding);

            _sourceRoot = CreateControllerRoot(
                "OscTargetLayerE2E_Source",
                _sourceMesh,
                _sourceProfileSo,
                out FacialController sourceController,
                out _);
            _receiverRoot = CreateControllerRoot(
                "OscTargetLayerE2E_Receiver",
                _receiverMesh,
                _receiverProfileSo,
                out FacialController receiverController,
                out SkinnedMeshRenderer receiverRenderer);

            sourceController.Initialize();
            receiverController.Initialize();

            Assert.That(sourceController.IsInitialized, Is.True, "送信側 FacialController の初期化が成功すること。");
            Assert.That(receiverController.IsInitialized, Is.True, "受信側 FacialController の初期化が成功すること。");
            Assert.That(receiverBinding.IsStarted, Is.True, "OscReceiverAdapterBinding が child scope で起動していること。");

            sourceController.Activate(CreateSourceExpression());

            yield return null;
            yield return new WaitForSecondsRealtime(0.2f);

            bool reached = false;
            for (int attempt = 0; attempt < 20 && !reached; attempt++)
            {
                senderBinding.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);

                receiverBinding.OnFixedTick(0.02f);
                yield return null;

                float rendererValue = receiverRenderer.GetBlendShapeWeight(0) / 100f;
                if (rendererValue > 0.01f)
                {
                    // 届いた直後の 1 フレームで判定せず、値が落ち着くまで数フレーム回してから確かめる。
                    for (int settle = 0; settle < 3; settle++)
                    {
                        senderBinding.OnLateTick(0.016f);
                        yield return new WaitForSecondsRealtime(0.05f);
                        receiverBinding.OnFixedTick(0.02f);
                        yield return null;
                    }

                    assertReached(receiverRenderer.GetBlendShapeWeight(0) / 100f);
                    reached = true;
                }
            }

            Assert.That(reached, Is.True,
                "inputSources を手で編集しなくても、実 UDP loopback → OscReceiverAdapterBinding → 対象レイヤー → 受信側 Renderer の経路で値が到達すること。");
        }

        private static OscSendReceiveE2ETests.TestOscE2EProfileSO CreateProfileSo(
            FacialProfile profile,
            Hidano.FacialControl.Domain.Adapters.AdapterBindingBase binding)
        {
            var so = ScriptableObject.CreateInstance<OscSendReceiveE2ETests.TestOscE2EProfileSO>();
            so.Profile = profile;
            so.WritableAdapterBindings.Add(binding);
            return so;
        }

        private static FacialProfile CreateSourceProfile()
        {
            return new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition(BaseLayerName, 0, ExclusionMode.LastWins)
                },
                new[]
                {
                    CreateSourceExpression()
                });
        }

        private static Expression CreateSourceExpression()
        {
            return new Expression(
                SourceExpressionId,
                "Source Smile",
                BaseLayerName,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(BlendShapeName, SourceValue)
                });
        }

        private static GameObject CreateControllerRoot(
            string name,
            Mesh mesh,
            Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO profileSo,
            out FacialController controller,
            out SkinnedMeshRenderer renderer)
        {
            var root = new GameObject(name);
            root.AddComponent<Animator>();

            var meshObject = new GameObject("Mesh");
            meshObject.transform.SetParent(root.transform);
            renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;

            controller = root.AddComponent<FacialController>();
            controller.CharacterSO = profileSo;
            controller.SkinnedMeshRenderers = new[] { renderer };
            return root;
        }

        private static Mesh CreateMeshWithBlendShape(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            mesh.triangles = new[] { 0, 1, 2 };

            var zeroDeltas = new[]
            {
                Vector3.zero,
                Vector3.zero,
                Vector3.zero
            };
            mesh.AddBlendShapeFrame(BlendShapeName, 100f, zeroDeltas, null, null);
            return mesh;
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }

        private static void DeactivateAndDestroy(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.SetActive(false);
            Object.DestroyImmediate(gameObject);
        }

        private static void DestroyImmediateIfAlive(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
