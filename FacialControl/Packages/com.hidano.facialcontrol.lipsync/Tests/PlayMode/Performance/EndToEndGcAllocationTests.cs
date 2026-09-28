using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public class EndToEndGcAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 1000;
        private const int BlendShapeCount = 4;
        private const string PhonemeId = "A";

        private GameObject _hostGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        [Test]
        public void UnityEventToAggregator_OneThousandFrames_AllocatesZeroBytes()
        {
            var snapshots = new[]
            {
                new PhonemeSnapshot(PhonemeId, new[] { 0.75f, 0.25f, 0.50f, 0.10f }),
            };

            AssertUnityEventToAggregatorAllocatesZeroBytes(snapshots, "direct");
        }

        [Test]
        public void UnityEventToAggregator_WithBlendShapeEntrySnapshots_OneThousandFrames_AllocatesZeroBytes()
        {
            _hostGameObject = CreateHostGameObject();
            AddFaceRenderer(_hostGameObject);
            var binding = CreateBinding(
                new BlendShapePhonemeEntry
                {
                    PhonemeId = PhonemeId,
                    BlendShapeName = "Mouth_A",
                    MaxWeight = 75f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(_hostGameObject, CreateProfileWithoutExpressions()));

            AssertUnityEventToAggregatorAllocatesZeroBytes(snapshots, "blendshape");
        }

        [Test]
        public void UnityEventToAggregator_WithAnimationClipEntrySnapshots_OneThousandFrames_AllocatesZeroBytes()
        {
            _hostGameObject = CreateHostGameObject();
            AddFaceRenderer(_hostGameObject);
            var clip = new AnimationClip();
            clip.SetCurve(
                "FaceMesh",
                typeof(SkinnedMeshRenderer),
                "blendShape.Mouth_A",
                AnimationCurve.Constant(0f, 0.01f, 75f));
            var binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = PhonemeId,
                    Clip = clip,
                    MaxWeight = 100f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(_hostGameObject, CreateProfileWithoutExpressions()));

            AssertUnityEventToAggregatorAllocatesZeroBytes(snapshots, "animationclip");
        }

        [Test]
        public void UnityEventToAggregator_WithExpressionPhonemeEntrySnapshots_OneThousandFrames_AllocatesZeroBytes()
        {
            _hostGameObject = CreateHostGameObject();
            var expressionEntry = new ExpressionPhonemeEntry
            {
                PhonemeId = PhonemeId,
                MaxWeight = 100f,
            };
            SetPrivateField(expressionEntry, "_expressionId", "expr-a");
            var binding = CreateBinding(expressionEntry);

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(_hostGameObject, CreateProfileWithExpression()));

            AssertUnityEventToAggregatorAllocatesZeroBytes(snapshots, "expression");
        }

        private void AssertUnityEventToAggregatorAllocatesZeroBytes(
            PhonemeSnapshot[] snapshots,
            string scenario)
        {
            if (_hostGameObject == null)
            {
                _hostGameObject = CreateHostGameObject();
            }

            Assert.That(snapshots, Has.Length.EqualTo(1), scenario);
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo(PhonemeId), scenario);

            var analyzer = _hostGameObject.AddComponent<uLipSync.uLipSync>();
            var blendShape = _hostGameObject.AddComponent<FacialControlULipSyncBlendShape>();
            blendShape.ConfigurePhonemes(new[] { PhonemeId });
            analyzer.onLipSyncUpdate.AddListener(blendShape.OnLipSyncUpdate);
            using var provider = new ULipSyncProvider(blendShape, snapshots, BlendShapeCount);
            var inputSource = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:a"),
                PhonemeId,
                provider,
                BlendShapeCount);

            var profile = new FacialProfile(
                "1.0",
                new[]
                {
                    new LayerDefinition("lipSync", 0, ExclusionMode.LastWins),
                });
            var bindings = new (int layerIdx, int sourceIdx, IInputSource source)[]
            {
                (0, 0, inputSource),
            };
            using var registry = new LayerInputSourceRegistry(profile, BlendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount,
                registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, BlendShapeCount);
            var priorities = new[] { 0 };
            var layerWeights = new[] { 1f };
            var finalOutput = new float[BlendShapeCount];
            var info = new uLipSync.LipSyncInfo
            {
                phoneme = PhonemeId,
                volume = 1f,
                rawVolume = 1f,
                phonemeRatios = new Dictionary<string, float>(1)
                {
                    { PhonemeId, 1f },
                },
            };

            WarmUp(analyzer, aggregator, priorities, layerWeights, finalOutput, info);
            ForceFullCollection();

            long before = ReadAllocatedBytes();
            for (int frame = 0; frame < FrameCount; frame++)
            {
                analyzer.onLipSyncUpdate.Invoke(info);
                aggregator.AggregateAndBlend(1f / 60f, priorities, layerWeights, finalOutput);
            }
            long after = ReadAllocatedBytes();
            long allocatedBytes = after - before;

            Assert.That(allocatedBytes, Is.EqualTo(0L),
                "uLipSync UnityEvent -> ULipSyncProvider -> LipSyncPhonemeOverlayInputSource -> " +
                "LayerInputSourceAggregator hot path allocated " + allocatedBytes +
                " bytes. scenario=" + scenario);
            Assert.That(finalOutput[0], Is.EqualTo(0.75f).Within(1e-6f),
                "End-to-end phoneme weight did not reach final BlendShape output. scenario=" + scenario);
        }

        private static void WarmUp(
            uLipSync.uLipSync analyzer,
            LayerInputSourceAggregator aggregator,
            int[] priorities,
            float[] layerWeights,
            float[] finalOutput,
            uLipSync.LipSyncInfo info)
        {
            for (int frame = 0; frame < 128; frame++)
            {
                analyzer.onLipSyncUpdate.Invoke(info);
                aggregator.AggregateAndBlend(1f / 60f, priorities, layerWeights, finalOutput);
            }
        }

        private static long ReadAllocatedBytes()
        {
            return AllocatedBytesReader.Read();
        }

        private static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static GameObject CreateHostGameObject()
        {
            return new GameObject("EndToEndGcAllocationTestsHost");
        }

        private static void AddFaceRenderer(GameObject host)
        {
            var face = new GameObject("FaceMesh");
            face.transform.SetParent(host.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("Mouth_A", 100f, CreateDeltaVertices(0.01f), null, null);
            mesh.AddBlendShapeFrame("Mouth_I", 100f, CreateDeltaVertices(0.02f), null, null);
            mesh.AddBlendShapeFrame("Mouth_U", 100f, CreateDeltaVertices(0.03f), null, null);
            mesh.AddBlendShapeFrame("Mouth_O", 100f, CreateDeltaVertices(0.04f), null, null);
            renderer.sharedMesh = mesh;
        }

        private static Vector3[] CreateDeltaVertices(float x)
        {
            return new[]
            {
                new Vector3(x, 0f, 0f),
                new Vector3(x, 0f, 0f),
                new Vector3(x, 0f, 0f),
            };
        }

        private static ULipSyncAdapterBinding CreateBinding(params PhonemeEntryBase[] entries)
        {
            var binding = new ULipSyncAdapterBinding();
            binding.Configure(default, null, entries);
            return binding;
        }

        private static AdapterBuildContext CreateContext(GameObject host, FacialProfile profile)
        {
            return new AdapterBuildContext(
                profile,
                new List<string> { "Mouth_A", "Mouth_I", "Mouth_U", "Mouth_O" },
                new InputSourceRegistry(),
                new FacialOutputBus(),
                new UnityTimeProvider(),
                host,
                null);
        }

        private static FacialProfile CreateProfileWithoutExpressions()
        {
            return new FacialProfile("1.0");
        }

        private static FacialProfile CreateProfileWithExpression()
        {
            return new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "expr-a",
                        "A",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.75f),
                            new BlendShapeMapping("Mouth_I", 0.25f),
                            new BlendShapeMapping("Mouth_U", 0.50f),
                            new BlendShapeMapping("Mouth_O", 0.10f),
                        }),
                });
        }

        private static PhonemeSnapshot[] BuildSnapshots(
            ULipSyncAdapterBinding binding,
            AdapterBuildContext ctx)
        {
            MethodInfo method = typeof(ULipSyncAdapterBinding).GetMethod(
                "BuildSnapshots",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            try
            {
                return (PhonemeSnapshot[])method.Invoke(binding, new object[] { ctx });
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }

                throw;
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private delegate long TotalAllocatedBytesReader(bool forceFullCollection);

        private static class AllocatedBytesReader
        {
            private static readonly Func<long> Reader = CreateReader();

            public static long Read()
            {
                return Reader();
            }

            private static Func<long> CreateReader()
            {
                var method = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
                if (method != null)
                {
                    var reader = (TotalAllocatedBytesReader)Delegate.CreateDelegate(
                        typeof(TotalAllocatedBytesReader),
                        method);
                    return () => reader(true);
                }

                return () => GC.GetTotalMemory(false);
            }
        }
    }
}
