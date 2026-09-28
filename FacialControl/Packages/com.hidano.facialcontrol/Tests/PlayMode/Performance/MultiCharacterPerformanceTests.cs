using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// 10 体同時動作の性能非回帰を、旧 graph 直叩きではなく
    /// FacialController のライブ経路 (Activate -> LateUpdate -> writer) で検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class MultiCharacterPerformanceTests : SizedTestFixture
    {
        private const int CharacterCount = 10;
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 120;
        private const double FrameBudgetMs = 3.0;

        private static readonly Action<FacialController> InvokeLateUpdate = CreateLateUpdateDelegate();

        private readonly List<Object> _trackedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _trackedObjects.Count - 1; i >= 0; i--)
            {
                if (_trackedObjects[i] != null)
                {
                    Object.DestroyImmediate(_trackedObjects[i]);
                }
            }

            _trackedObjects.Clear();
        }

        [Test]
        public void TenCharacters_LivePathInitialization_AllControllersBecomeActive()
        {
            var profile = CreateProfile();
            var controllers = CreateControllers(profile);

            Assert.That(controllers, Has.Length.EqualTo(CharacterCount));
            for (int i = 0; i < controllers.Length; i++)
            {
                Assert.That(controllers[i].IsInitialized, Is.True,
                    $"Character_{i} の FacialController が初期化されていません。");
            }
        }

        [Test]
        public void TenCharacters_LivePathUpdate_CompletesWithinFrameBudget()
        {
            var profile = CreateProfile();
            var controllers = CreateControllers(profile);

            Warmup(controllers, WarmupFrames);

            var stopwatch = Stopwatch.StartNew();
            RunFrames(controllers, MeasurementFrames);
            stopwatch.Stop();

            double msPerFrame = stopwatch.Elapsed.TotalMilliseconds / MeasurementFrames;
            Assert.That(msPerFrame, Is.LessThan(FrameBudgetMs),
                $"10 体同時動作のライブ経路更新が 1 フレームあたり {msPerFrame:F3}ms かかりました（上限 {FrameBudgetMs:F1}ms）。");
        }

        [Test]
        public void TenCharacters_LivePathUpdate_AfterWarmup_AllocatesZeroGC()
        {
            var profile = CreateProfile();
            var controllers = CreateControllers(profile);

            Warmup(controllers, WarmupFrames);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            RunFrames(controllers, MeasurementFrames);

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                $"10 体同時動作のライブ経路更新で GC.Alloc が検出されました: {recorder.LastValue} bytes");
        }

        private FacialController[] CreateControllers(FacialProfile profile)
        {
            var controllers = new FacialController[CharacterCount];
            for (int i = 0; i < controllers.Length; i++)
            {
                var root = Track(new GameObject($"Character_{i}"));
                root.AddComponent<Animator>();

                var meshObject = Track(new GameObject("FaceMesh"));
                meshObject.transform.SetParent(root.transform, false);

                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = CreateMeshWithBlendShapes();

                var controller = root.AddComponent<FacialController>();
                controller.InitializeWithProfile(profile);
                ActivateAllProfileExpressions(controller, profile);
                controllers[i] = controller;
            }

            return controllers;
        }

        private void Warmup(FacialController[] controllers, int frames)
        {
            RunFrames(controllers, frames);

            for (int i = 0; i < controllers.Length; i++)
            {
                var renderer = controllers[i].GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.GreaterThan(0f),
                    $"Character_{i} の warmup 後に BlendShape 出力が反映されていません。");
            }
        }

        private static void RunFrames(FacialController[] controllers, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                for (int i = 0; i < controllers.Length; i++)
                {
                    InvokeLateUpdate(controllers[i]);
                }
            }
        }

        private static void ActivateAllProfileExpressions(FacialController controller, FacialProfile profile)
        {
            var expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                controller.Activate(expressions[i]);
            }
        }

        private Mesh CreateMeshWithBlendShapes()
        {
            var mesh = Track(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };

            var zeroDeltas = new Vector3[3];
            mesh.AddBlendShapeFrame("smile", 100f, zeroDeltas, null, null);
            mesh.AddBlendShapeFrame("jawOpen", 100f, zeroDeltas, null, null);
            mesh.AddBlendShapeFrame("blink", 100f, zeroDeltas, null, null);
            return mesh;
        }

        private static FacialProfile CreateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    "expr-happy",
                    "Happy",
                    "emotion",
                    0f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression(
                    "expr-aa",
                    "AA",
                    "lipsync",
                    0f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("jawOpen", 0.8f)
                    }),
                new Expression(
                    "expr-blink",
                    "Blink",
                    "eye",
                    0f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("blink", 0.6f)
                    })
            };

            return new FacialProfile("1.0.0", layers, expressions);
        }

        private T Track<T>(T obj)
            where T : Object
        {
            _trackedObjects.Add(obj);
            return obj;
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
