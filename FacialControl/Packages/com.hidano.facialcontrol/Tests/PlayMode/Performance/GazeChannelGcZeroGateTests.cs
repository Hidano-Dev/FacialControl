using System;
using System.Collections;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class GazeChannelGcZeroGateTests : SizedTestFixture
    {
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 100;
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            _root = null;
        }

        [Test]
        public void GazeSnapshotAndBoneApply_100Frames_AreGcZero()
        {
            _root = new GameObject("GazeChannelGcZeroGateTestsRoot");
            var left = new GameObject("LeftEye").transform;
            left.SetParent(_root.transform, false);
            var right = new GameObject("RightEye").transform;
            right.SetParent(_root.transform, false);
            var source = new FixedGazeSource(0.35f, -0.2f);
            var channel = new GazeChannel
            {
                id = GazeSourceIdConvention.DefaultChannelId,
                leftEyeBonePath = "LeftEye",
                rightEyeBonePath = "RightEye"
            };

            using (var provider = new GazeBonePoseProvider(
                new BoneTransformResolver(_root.transform),
                new[] { new GazeBoneBinding(channel, source, source) }))
            {
                for (int i = 0; i < WarmupFrames; i++) provider.Apply();
                Assert.That(Quaternion.Angle(Quaternion.identity, left.localRotation), Is.GreaterThan(0.1f));
                Assert.That(Quaternion.Angle(Quaternion.identity, right.localRotation), Is.GreaterThan(0.1f));

                using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.SumAllSamplesInFrame
                        | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    for (int i = 0; i < MeasurementFrames; i++) provider.Apply();
                    Assert.That(recorder.LastValue, Is.EqualTo(0L),
                        "gaze snapshot/bone apply steady-state path must allocate zero GC bytes");
                }
            }
        }

        private sealed class FixedGazeSource : IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;
            public FixedGazeSource(float x, float y) { Id = "test:gaze"; _x = x; _y = y; }
            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }
            public bool TryReadScalar(out float value) { value = _x; return true; }
            public bool TryReadVector2(out float x, out float y) { x = _x; y = _y; return true; }
            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0) output[0] = _x;
                if (output.Length > 1) output[1] = _y;
                return output.Length >= 2;
            }
        }
    }
}
