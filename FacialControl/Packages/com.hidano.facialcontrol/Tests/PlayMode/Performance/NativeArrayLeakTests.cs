using System.Collections;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public class NativeArrayLeakTests : SizedTestFixture
    {
        private GameObject _controllerGameObject;
        private Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_mesh != null)
            {
                Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_controllerGameObject != null)
            {
                Object.DestroyImmediate(_controllerGameObject);
                _controllerGameObject = null;
            }
        }

        [UnityTest]
        public IEnumerator FacialController_Disable_DisposesWeightBufferNativeArrays()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            FacialProfile profile = CreateProfile("smile", "emotion");

            controller.InitializeWithProfile(profile);
            controller.Activate(profile.Expressions.Span[0]);

            yield return null;

            LayerInputSourceWeightBuffer weightBuffer = GetCurrentWeightBuffer(controller);
            Assert.That(weightBuffer, Is.Not.Null);
            AssertNativeArraysCreated(weightBuffer);

            controller.enabled = false;

            yield return null;

            Assert.That(controller.IsInitialized, Is.False);
            AssertNativeArraysDisposed(weightBuffer);
        }

        [UnityTest]
        public IEnumerator FacialController_ReinitializeWithProfile_DisposesPreviousWeightBuffer()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            FacialProfile firstProfile = CreateProfile("smile", "emotion");
            FacialProfile secondProfile = CreateProfile("blink", "eye");

            controller.InitializeWithProfile(firstProfile);
            controller.Activate(firstProfile.Expressions.Span[0]);

            yield return null;

            LayerInputSourceWeightBuffer previousBuffer = GetCurrentWeightBuffer(controller);
            Assert.That(previousBuffer, Is.Not.Null);
            AssertNativeArraysCreated(previousBuffer);

            controller.InitializeWithProfile(secondProfile);
            controller.Activate(secondProfile.Expressions.Span[0]);

            yield return null;

            Assert.That(controller.IsInitialized, Is.True);
            AssertNativeArraysDisposed(previousBuffer);

            LayerInputSourceWeightBuffer currentBuffer = GetCurrentWeightBuffer(controller);
            Assert.That(currentBuffer, Is.Not.Null);
            Assert.That(currentBuffer, Is.Not.SameAs(previousBuffer));
            AssertNativeArraysCreated(currentBuffer);
        }

        [UnityTest]
        public IEnumerator FacialController_RepeatedProfileSwitch_DoesNotKeepOldNativeArraysAlive()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            FacialProfile[] profiles =
            {
                CreateProfile("smile", "emotion"),
                CreateProfile("blink", "eye"),
                CreateProfile("jawOpen", "lipsync")
            };

            LayerInputSourceWeightBuffer previousBuffer = null;
            for (int i = 0; i < profiles.Length; i++)
            {
                controller.InitializeWithProfile(profiles[i]);
                controller.Activate(profiles[i].Expressions.Span[0]);

                yield return null;

                LayerInputSourceWeightBuffer currentBuffer = GetCurrentWeightBuffer(controller);
                Assert.That(currentBuffer, Is.Not.Null, $"profile switch {i} should create a weight buffer");
                AssertNativeArraysCreated(currentBuffer);

                if (previousBuffer != null)
                {
                    AssertNativeArraysDisposed(previousBuffer);
                }

                previousBuffer = currentBuffer;
            }
        }

        private GameObject CreateControllerHost()
        {
            var root = new GameObject("NativeArrayLeakTestsHost");
            root.AddComponent<Animator>();

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(root.transform, false);

            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = CreateMeshWithBlendShapes("smile", "blink", "jawOpen");
            renderer.sharedMesh = _mesh;

            return root;
        }

        private static Mesh CreateMeshWithBlendShapes(params string[] blendShapeNames)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };

            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            return mesh;
        }

        private static FacialProfile CreateProfile(string blendShapeName, string layerName)
        {
            var layers = new[]
            {
                new LayerDefinition(layerName, 0, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    $"expr-{blendShapeName}",
                    blendShapeName,
                    layerName,
                    0.05f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping(blendShapeName, 1.0f)
                    })
            };

            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static LayerInputSourceWeightBuffer GetCurrentWeightBuffer(FacialController controller)
        {
            FieldInfo layerUseCaseField = typeof(FacialController).GetField(
                "_layerUseCase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(layerUseCaseField, Is.Not.Null);

            var layerUseCase = layerUseCaseField.GetValue(controller) as LayerUseCase;
            Assert.That(layerUseCase, Is.Not.Null);

            FieldInfo weightBufferField = typeof(LayerUseCase).GetField(
                "_weightBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(weightBufferField, Is.Not.Null);

            return weightBufferField.GetValue(layerUseCase) as LayerInputSourceWeightBuffer;
        }

        private static void AssertNativeArraysCreated(LayerInputSourceWeightBuffer buffer)
        {
            Assert.That(ReadNativeArrayField(buffer, "_bufferA").IsCreated, Is.True);
            Assert.That(ReadNativeArrayField(buffer, "_bufferB").IsCreated, Is.True);
        }

        private static void AssertNativeArraysDisposed(LayerInputSourceWeightBuffer buffer)
        {
            Assert.That(ReadNativeArrayField(buffer, "_bufferA").IsCreated, Is.False);
            Assert.That(ReadNativeArrayField(buffer, "_bufferB").IsCreated, Is.False);
        }

        private static NativeArray<float> ReadNativeArrayField(
            LayerInputSourceWeightBuffer buffer,
            string fieldName)
        {
            FieldInfo field = typeof(LayerInputSourceWeightBuffer).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            return (NativeArray<float>)field.GetValue(buffer);
        }
    }
}
