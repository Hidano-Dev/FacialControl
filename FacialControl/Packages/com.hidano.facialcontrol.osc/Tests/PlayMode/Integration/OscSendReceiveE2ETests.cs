using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// PlayMode E2E: FacialController の post-blend 出力を OSC loopback で送信し、
    /// 受信側 FacialController が OscReceiverAdapterBinding 経由で消費して Renderer に適用することを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscSendReceiveE2ETests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19280;
        private const string BlendShapeName = "smile";
        private const string LayerName = "emotion";
        private const string SourceExpressionId = "source-smile";
        private const string SenderSlug = "osc-sender-e2e";
        private const string ReceiverSlug = "osc-receiver-e2e";
        private const float SourceValue = 0.73f;

        private static int s_portCounter;

        private GameObject _sourceRoot;
        private GameObject _receiverRoot;
        private Mesh _sourceMesh;
        private Mesh _receiverMesh;
        private TestOscE2EProfileSO _sourceProfileSo;
        private TestOscE2EProfileSO _receiverProfileSo;

        [TearDown]
        public void TearDown()
        {
            DeactivateAndDestroy(_sourceRoot);
            _sourceRoot = null;

            DeactivateAndDestroy(_receiverRoot);
            _receiverRoot = null;

            if (_sourceProfileSo != null)
            {
                UnityEngine.Object.DestroyImmediate(_sourceProfileSo);
                _sourceProfileSo = null;
            }

            if (_receiverProfileSo != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverProfileSo);
                _receiverProfileSo = null;
            }

            if (_sourceMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_sourceMesh);
                _sourceMesh = null;
            }

            if (_receiverMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverMesh);
                _receiverMesh = null;
            }
        }

        [UnityTest]
        public IEnumerator FacialControllerPostBlend_UdpLoopback_ReachesReceiverRenderer()
        {
            int port = AllocatePort();
            _sourceMesh = CreateMeshWithBlendShape("OscSendReceiveE2E_SourceMesh", BlendShapeName);
            _receiverMesh = CreateMeshWithBlendShape("OscSendReceiveE2E_ReceiverMesh", BlendShapeName);

            var senderBinding = new OscSenderAdapterBinding
            {
                Slug = SenderSlug,
                SuppressLoopback = false,
                LayoutRefreshIntervalSeconds = 60f
            };
            senderBinding.Configure(Endpoint, port, new[] { BlendShapeName });

            var receiverBinding = new OscReceiverAdapterBinding
            {
                Slug = ReceiverSlug,
                BundleMode = BundleInterpretationMode.AtomicSwap
            };
            receiverBinding.Port = port;

            _sourceProfileSo = CreateProfileSo(
                CreateSourceProfile(),
                senderBinding);
            _receiverProfileSo = CreateProfileSo(
                CreateReceiverProfile(),
                receiverBinding);

            _sourceRoot = CreateControllerRoot(
                "OscSendReceiveE2E_Source",
                _sourceMesh,
                _sourceProfileSo,
                out FacialController sourceController,
                out _);
            _receiverRoot = CreateControllerRoot(
                "OscSendReceiveE2E_Receiver",
                _receiverMesh,
                _receiverProfileSo,
                out FacialController receiverController,
                out SkinnedMeshRenderer receiverRenderer);

            sourceController.Initialize();
            receiverController.Initialize();

            Assert.That(sourceController.IsInitialized, Is.True, "送信側 FacialController の初期化が成功すること。");
            Assert.That(receiverController.IsInitialized, Is.True, "受信側 FacialController の初期化が成功すること。");
            Assert.That(senderBinding.IsStarted, Is.True, "OscSenderAdapterBinding が child scope で起動していること。");
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

                // 受信側の到達は FacialController が Renderer に書き込んだ BlendShape weight（公開 API）で観測する。
                float rendererValue = receiverRenderer.GetBlendShapeWeight(0) / 100f;
                if (rendererValue > 0.01f)
                {
                    Assert.That(rendererValue, Is.EqualTo(SourceValue).Within(0.08f),
                        "送信側 post-blend BlendShape 値が受信側 FacialController 経由で Renderer に適用されること。");
                    reached = true;
                }
            }

            Assert.That(reached, Is.True,
                "FacialController → FacialOutputBus → OscSenderAdapterBinding → 実 UDP loopback → OscReceiverAdapterBinding → 受信側 Renderer の BlendShape 経路で値が到達すること。");
        }

        private static TestOscE2EProfileSO CreateProfileSo(
            FacialProfile profile,
            AdapterBindingBase binding)
        {
            var so = ScriptableObject.CreateInstance<TestOscE2EProfileSO>();
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
                    new LayerDefinition(LayerName, 0, ExclusionMode.LastWins)
                },
                new[]
                {
                    CreateSourceExpression()
                });
        }

        private static FacialProfile CreateReceiverProfile()
        {
            return new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition(LayerName, 0, ExclusionMode.LastWins)
                },
                expressions: null,
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[]
                    {
                        new InputSourceDeclaration(ReceiverSlug, 1f, null)
                    }
                });
        }

        private static Expression CreateSourceExpression()
        {
            return new Expression(
                SourceExpressionId,
                "Source Smile",
                LayerName,
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
            FacialCharacterProfileSO profileSo,
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

        private static Mesh CreateMeshWithBlendShape(string name, string blendShapeName)
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
            mesh.AddBlendShapeFrame(blendShapeName, 100f, zeroDeltas, null, null);
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
            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        public sealed class TestOscE2EProfileSO : FacialCharacterProfileSO
        {
            public FacialProfile Profile;

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public override FacialProfile LoadProfile()
            {
                return Profile;
            }
        }
    }
}
