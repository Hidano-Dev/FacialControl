using System;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public class FacialControllerGcZeroGateTests : SizedTestFixture
    {
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 120;

        private static readonly Action<FacialController> InvokeLateUpdate = CreateLateUpdateDelegate();

        private GameObject _controllerGameObject;
        private Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
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

        [Test]
        public void LateUpdateSteadyState_AfterWarmup_WholeWeightUpdateCallTree_AllocatesZeroGC()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            FacialProfile profile = CreateProfileWithExpression();

            controller.InitializeWithProfile(profile);
            Assert.That(controller.IsInitialized, Is.True);

            controller.Activate(profile.Expressions.Span[0]);

            for (int i = 0; i < WarmupFrames; i++)
            {
                InvokeLateUpdate(controller);
            }

            var renderer = controller.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.GreaterThan(0f),
                "warmup 後に BlendShape 出力が実際に流れている必要がある");

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int i = 0; i < MeasurementFrames; i++)
            {
                InvokeLateUpdate(controller);
            }

            long gcAllocBytes = recorder.LastValue;
            Assert.That(gcAllocBytes, Is.EqualTo(0L),
                "warmup 後の steady-state weight-update call tree 全体で GC.Alloc は 0 であるべき");
        }

        [Test]
        public void Initialize_WiresExpressionObserverAndExposesActivationGate()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            var profile = CreateProfileWithExpression();
            var observer = new ExpressionObservationSpy();

            Assert.That(controller.ExpressionActivationGate, Is.Null,
                "未初期化時の系1遮断面は null であるべき");

            controller.InitializeWithProfile(profile);
            controller.InputObservationBus.Subscribe(observer);
            controller.Activate(profile.Expressions.Span[0]);

            Assert.That(controller.ExpressionActivationGate, Is.Not.Null,
                "初期化後は系1遮断面が公開されるべき");
            Assert.That(observer.ActivatedSourceId, Is.EqualTo("@expression"));
            Assert.That(observer.ActivatedExpressionId, Is.EqualTo("expr-happy"));
        }

        private GameObject CreateControllerHost()
        {
            var root = new GameObject("FacialControllerGcZeroGateTestsHost");
            root.AddComponent<Animator>();

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

        private sealed class ExpressionObservationSpy : IFacialInputObserver
        {
            public string ActivatedSourceId { get; private set; }
            public string ActivatedExpressionId { get; private set; }

            public void OnExpressionActivated(string sourceId, string expressionId)
            {
                ActivatedSourceId = sourceId;
                ActivatedExpressionId = expressionId;
            }

            public void OnExpressionDeactivated(string sourceId, string expressionId) { }
            public void OnTriggerOn(string sourceId, string expressionId) { }
            public void OnTriggerOff(string sourceId, string expressionId) { }
            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes) { }
            public void OnValueProviderSample(string sourceId, in ValueProviderSample sample) { }
        }

        private static Action<FacialController> CreateLateUpdateDelegate()
        {
            MethodInfo lateUpdate = typeof(FacialController).GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdate, Is.Not.Null);

            return (Action<FacialController>)Delegate.CreateDelegate(
                typeof(Action<FacialController>),
                method: lateUpdate);
        }
    }
}
