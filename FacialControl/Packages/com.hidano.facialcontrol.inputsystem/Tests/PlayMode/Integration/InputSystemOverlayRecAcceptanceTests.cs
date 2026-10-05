#if FACIALCONTROL_HAS_REC
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Application.UseCases;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.TestTools;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public sealed class InputSystemOverlayRecAcceptanceTests : SizedTestFixture
    {
        private const string TestAssetName = "InputSystemOverlayRecAcceptanceTestsAsset";
        private const float FrameDeltaTime = 1f / 60f;
        private const float Tolerance = 0.01f;
        private GameObject _host;
        private Mesh _mesh;
        private OverlayProfileSO _profile;
        private InputActionAsset _actionAsset;
        private Gamepad _gamepad;
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameDeltaTime;
            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_profile != null) UnityEngine.Object.DestroyImmediate(_profile);
            if (_mesh != null) UnityEngine.Object.DestroyImmediate(_mesh);
            if (_actionAsset != null) UnityEngine.Object.DestroyImmediate(_actionAsset);
            if (_gamepad != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(_gamepad);
            DeleteGeneratedAssets();
        }

        [UnityTest]
        public IEnumerator OverlayTrigger_RecordingPlaybackBlocksLiveInputAndResumesAfterStop()
        {
            CreateCharacter(out FacialController controller, out RecCharacterBinding recording, out SkinnedMeshRenderer renderer);
            yield return null;

            SetTrigger(0.25f);
            yield return WaitFrames(20);
            float[] initial = Snapshot(renderer);
            Assert.That(initial[0], Is.EqualTo(100f).Within(1f));
            Assert.That(initial[1], Is.EqualTo(25f).Within(1f));

            Assert.That(recording.StartRecording("overlay"), Is.True);
            yield return WaitFrames(30);
            SetTrigger(0.75f);
            yield return WaitFrames(3);
            float[] changed = Snapshot(renderer);
            Assert.That(changed[0], Is.EqualTo(100f).Within(1f));
            Assert.That(changed[1], Is.EqualTo(75f).Within(1f));
            recording.StopRecording();

            SetTrigger(0f);
            yield return null;
            Assert.That(recording.LoadRecording(recording.LastRecordingName), Is.True);
            Assert.That(recording.StartPlayback(), Is.True);
            yield return null;

            SetTrigger(1f);
            yield return null;
            Assert.That(Snapshot(renderer)[0], Is.EqualTo(initial[0]).Within(Tolerance));
            Assert.That(Snapshot(renderer)[1], Is.EqualTo(initial[1]).Within(Tolerance));

            yield return WaitUntilCompleted(recording);
            yield return null;
            Assert.That(Snapshot(renderer)[0], Is.EqualTo(changed[0]).Within(Tolerance));
            Assert.That(Snapshot(renderer)[1], Is.EqualTo(changed[1]).Within(Tolerance));

            // Completed 状態からの StopPlayback は既に停止済みなので、再度 Playing にして
            // 停止経路そのものも受け入れ対象にする。
            Assert.That(recording.StartPlayback(), Is.True);
            yield return null;
            recording.StopPlayback();
            yield return WaitFrames(2);
            SetTrigger(0.5f);
            yield return WaitFrames(3);
            Assert.That(Snapshot(renderer)[0], Is.EqualTo(changed[0]).Within(Tolerance));
            Assert.That(Snapshot(renderer)[1], Is.EqualTo(50f).Within(1f));
        }

        private void CreateCharacter(out FacialController controller, out RecCharacterBinding recording, out SkinnedMeshRenderer renderer)
        {
            _host = new GameObject("InputSystemOverlayRecAcceptanceHost");
            _host.AddComponent<Animator>();
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_host.transform, false);
            renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            _mesh.AddBlendShapeFrame("overlay", 100f, new Vector3[3], null, null);
            renderer.sharedMesh = _mesh;

            _profile = ScriptableObject.CreateInstance<OverlayProfileSO>();
            _profile.name = TestAssetName;
            _profile.WritableAdapterBindings.Add(CreateBinding());
            controller = _host.AddComponent<FacialController>();
            controller.CharacterSO = _profile;
            controller.Initialize();
            Assert.That(controller.IsInitialized, Is.True);
            recording = _host.AddComponent<RecCharacterBinding>();
            recording.FacialController = controller;
            recording.RecordingClock = new FrameRecClock();
            controller.Activate(OverlayProfileSO.CreateProfile().FindExpressionById("smile").Value);
        }

        private InputSystemAdapterBinding CreateBinding()
        {
            _actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _actionAsset.AddActionMap("Expression");
            var action = map.AddAction("OverlayWeight", InputActionType.Value);
            action.AddBinding("<Gamepad>/rightTrigger");
            var binding = new InputSystemAdapterBinding { Slug = "overlay-input" };
            binding.Configure(_actionAsset, "Expression", new[]
            {
                new ExpressionBindingEntry
                {
                    bindingMode = BindingMode.Overlay,
                    actionName = "OverlayWeight",
                    overlaySlot = "overlay",
                    overlayTargetLayer = "overlay"
                }
            });
            return binding;
        }

        private void SetTrigger(float value)
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(_gamepad, new GamepadState { rightTrigger = value });
            UnityEngine.InputSystem.InputSystem.Update();
        }

        private static float[] Snapshot(SkinnedMeshRenderer renderer)
            => new[] { renderer.GetBlendShapeWeight(0), renderer.GetBlendShapeWeight(1) };

        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator WaitUntilCompleted(RecCharacterBinding recording)
        {
            for (int i = 0; i < 600 && recording.PlaybackState != RecPlaybackState.Completed; i++) yield return null;
            Assert.That(recording.PlaybackState, Is.EqualTo(RecPlaybackState.Completed));
        }

        private static void DeleteGeneratedAssets()
        {
            string directory = Path.Combine(UnityEngine.Application.streamingAssetsPath, FacialCharacterProfileSO.StreamingAssetsRootFolder, TestAssetName);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".meta")) File.Delete(directory + ".meta");
        }

        private sealed class FrameRecClock : IRecClock
        {
            private int _startFrame;
            public double ElapsedSeconds => (Time.frameCount - _startFrame) / 60.0;
            public void Reset() => _startFrame = Time.frameCount;
        }

        private sealed class OverlayProfileSO : FacialCharacterProfileSO
        {
            public List<Domain.Adapters.AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
            public static FacialProfile CreateProfile() => new FacialProfile("1.0.0",
                new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins), new LayerDefinition("overlay", 1, ExclusionMode.LastWins) },
                new[] { new Expression("smile", "Smile", "emotion", 0f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("smile", 1f) },
                    new[] { new OverlaySlotBinding("overlay", suppress: false, snapshot: new ExpressionSnapshot(
                        "overlay-expression", Expression.DefaultTransitionDuration, TransitionCurvePreset.Linear,
                        new[] { new BlendShapeSnapshot(string.Empty, "overlay", 1f) }, null, null)) }) },
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration("overlay-input", 1f, null) },
                    new[] { new InputSourceDeclaration("overlay-input:overlay:overlay", 1f, null) }
                },
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding("overlay", suppress: false, snapshot: new ExpressionSnapshot(
                        "overlay-default", Expression.DefaultTransitionDuration, TransitionCurvePreset.Linear,
                        new[] { new BlendShapeSnapshot(string.Empty, "overlay", 1f) }, null, null))
                },
                slots: new[] { "overlay" });
            public override FacialProfile LoadProfile() => CreateProfile();
        }
    }
}
#endif
