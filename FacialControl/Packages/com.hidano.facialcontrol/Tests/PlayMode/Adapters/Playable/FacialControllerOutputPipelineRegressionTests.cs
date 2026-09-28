using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// 実機不具合の再発防止: LateUpdate における「BlendShape writer → FacialOutputBus publish → bone writer」の
    /// 順序が崩れると、出力バスの購読者（OSC 送信等）が 1 フレーム古い BlendShape 値を受け取り、
    /// かつ publish 時点で目ボーン回転が先行適用される症状が出た。
    /// 順序を観測するために private な <c>_outputWriter</c> を reflection で差し替えているが、
    /// 検証対象は「購読者が観測する値と順序」という公開契約である。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerOutputPipelineRegressionTests : SizedTestFixture
    {
        private GameObject _controllerGameObject;
        private Mesh _mesh;
        private UnityEngine.ScriptableObject _profileAsset;

        [TearDown]
        public void TearDown()
        {
            if (_profileAsset != null)
            {
                UnityEngine.Object.DestroyImmediate(_profileAsset);
                _profileAsset = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_controllerGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_controllerGameObject);
                _controllerGameObject = null;
            }
        }

        [UnityTest]
        public IEnumerator LateUpdate_WriterPublishAndBoneWriterOrder_IsPreserved()
        {
            _controllerGameObject = CreateControllerHost(out Transform jawBone);

            var controller = _controllerGameObject.AddComponent<FacialController>();
            var profile = CreateProfileWithExpression();
            var profileAsset = ScriptableObject.CreateInstance<TestableOutputPipelineProfileSO>();
            var binding = new OutputPipelineOrderBinding
            {
                Slug = "output-pipeline-order",
                BoneToObserve = jawBone
            };

            profileAsset.ProfileToLoad = profile;
            profileAsset.WritableAdapterBindings.Add(binding);
            _profileAsset = profileAsset;

            controller.CharacterSO = profileAsset;
            controller.Initialize();

            Assert.That(controller.IsInitialized, Is.True);

            var writer = new TrackingBlendShapeOutputWriter();
            binding.Writer = writer;
            ReplaceOutputWriter(controller, writer);

            Quaternion initialJawRotation = jawBone.localRotation;
            controller.SetActiveBoneSnapshots(new ReadOnlyMemory<BoneSnapshot>(new[]
            {
                new BoneSnapshot("Head/Jaw", 0f, 0f, 0f, 12f, 0f, 0f, 1f, 1f, 1f)
            }));
            controller.Activate(profile.Expressions.Span[0]);

            yield return null;

            Assert.That(writer.WriteCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(writer.LastWeights, Has.Length.EqualTo(1));
            Assert.That(writer.LastWeights[0], Is.GreaterThan(0f));

            Assert.That(binding.PublishCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(binding.WriterWasCalledBeforePublish, Is.True);
            Assert.That(binding.PublishedWeight, Is.EqualTo(writer.LastWeights[0]).Within(0.0001f));
            Assert.That(Quaternion.Angle(initialJawRotation, binding.BoneRotationDuringPublish), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(initialJawRotation, jawBone.localRotation), Is.GreaterThan(1f));
        }

        private GameObject CreateControllerHost(out Transform jawBone)
        {
            var root = new GameObject("FacialControllerOutputPipelineRegressionTestsHost");
            root.AddComponent<Animator>();

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);

            var jaw = new GameObject("Jaw");
            jaw.transform.SetParent(head.transform, false);
            jawBone = jaw.transform;

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = CreateMeshWithBlendShape("smile");
            renderer.sharedMesh = _mesh;

            return root;
        }

        private static Mesh CreateMeshWithBlendShape(string blendShapeName)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame(blendShapeName, 100f, new Vector3[3], null, null);
            return mesh;
        }

        private static FacialProfile CreateProfileWithExpression()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    "expr-happy",
                    "Happy",
                    "emotion",
                    0.05f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    })
            };

            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static void ReplaceOutputWriter(FacialController controller, IBlendShapeOutputWriter replacement)
        {
            FieldInfo field = typeof(FacialController).GetField(
                "_outputWriter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            if (field.GetValue(controller) is IBlendShapeOutputWriter existingWriter)
            {
                existingWriter.Dispose();
            }

            field.SetValue(controller, replacement);
        }

        public sealed class TestableOutputPipelineProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public FacialProfile ProfileToLoad;

            public override FacialProfile LoadProfile()
            {
                return ProfileToLoad;
            }
        }

        [Serializable]
        private sealed class OutputPipelineOrderBinding : AdapterBindingBase, IFacialOutputObserver
        {
            [NonSerialized] public Transform BoneToObserve;
            [NonSerialized] public TrackingBlendShapeOutputWriter Writer;
            [NonSerialized] public int PublishCount;
            [NonSerialized] public bool WriterWasCalledBeforePublish;
            [NonSerialized] public float PublishedWeight;
            [NonSerialized] public Quaternion BoneRotationDuringPublish;

            private IFacialOutputBus _bus;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                _bus = ctx.FacialOutputBus;
                _bus.Subscribe(this);
            }

            public void OnFacialOutputPublished(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                PublishCount++;
                WriterWasCalledBeforePublish = Writer != null && Writer.WriteCount > 0;
                PublishedWeight = postBlendValues.Length > 0 ? postBlendValues[0] : 0f;
                BoneRotationDuringPublish = BoneToObserve != null
                    ? BoneToObserve.localRotation
                    : Quaternion.identity;
            }

            public override void Dispose()
            {
                _bus?.Unsubscribe(this);
                _bus = null;
            }
        }

        private sealed class TrackingBlendShapeOutputWriter : IBlendShapeOutputWriter
        {
            public int WriteCount { get; private set; }

            public float[] LastWeights { get; private set; } = Array.Empty<float>();

            public void Write(ReadOnlySpan<float> normalizedWeights)
            {
                WriteCount++;
                LastWeights = new float[normalizedWeights.Length];
                normalizedWeights.CopyTo(LastWeights);
            }

            public void Dispose()
            {
            }
        }
    }
}
