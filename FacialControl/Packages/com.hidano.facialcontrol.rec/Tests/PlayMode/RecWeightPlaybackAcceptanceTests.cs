using System.Collections;
using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.PlayMode
{
    /// <summary>
    /// rec-weight-coverage の受け入れ（実 <see cref="FacialController"/> + <see cref="RecCharacterBinding"/>）。
    /// レイヤー weight（SetLayerWeight）と入力源 weight（@expression スロット = sourceIdx 0）を記録・再生し、
    /// BlendShape 出力で再現・遮断・停止後の挙動を確かめる。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RecWeightPlaybackAcceptanceTests : SizedTestFixture
    {
        private const string TestAssetName = "RecWeightPlaybackAcceptanceTestsAsset";
        private const int EyesLayerIndex = 1;
        private const int ExpressionSlotIndex = 0;
        // batchmode の deltaTime は極小のため秒ではなくフレーム数で区切る。
        private const int FramesBeforeChange = 30;
        private const int MaxWaitFrames = 600;
        private const float Tolerance = 0.01f;

        private const float FrameDeltaTime = 1f / 60f;

        private GameObject _host;
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            // 記録クロックと再生 Tick を同じフレーム刻みにそろえる（batchmode の deltaTime は極小で不安定）。
            _previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameDeltaTime;
        }
        private Mesh _mesh;
        private WeightTestCharacterProfileSO _characterSo;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }

            if (_mesh != null)
            {
                Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_characterSo != null)
            {
                Object.DestroyImmediate(_characterSo);
                _characterSo = null;
            }

            DeleteGeneratedRecordingAssets();
        }

        [UnityTest]
        public IEnumerator RecordAndPlay_LayerAndSlotWeights_ReproducesFromFrameZeroBlocksLiveWritesAndKeepsWeightsAfterStop()
        {
            Setup(out FacialController controller, out RecCharacterBinding binding, out SkinnedMeshRenderer renderer);

            // 記録開始時点で既定値以外の weight を持つ構成（基準の確立と、再生がライブ値を引き継がないことの確認用）。
            controller.SetLayerWeight("emotion", 0.6f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 0.3f);
            yield return null;
            yield return null;
            float[] start = Snapshot(renderer);
            Assert.That(start[0], Is.LessThan(99f), "layer weight must affect the output for this test to be meaningful");
            Assert.That(start[1], Is.LessThan(99f), "slot weight must affect the output for this test to be meaningful");

            Assert.That(binding.StartRecording("weights"), Is.True);
            yield return WaitFrames(FramesBeforeChange);
            double changedAt = binding.ElapsedSeconds;
            Assert.That(changedAt, Is.GreaterThan(0d));
            controller.SetLayerWeight("emotion", 0.2f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 0.9f);
            yield return null;
            yield return null;
            float[] end = Snapshot(renderer);
            Assert.That(end[0], Is.Not.EqualTo(start[0]).Within(Tolerance));
            Assert.That(end[1], Is.Not.EqualTo(start[1]).Within(Tolerance));
            binding.StopRecording();

            // 再生前にライブ値を別の値へずらす（再生が基準を確立しライブ値を引き継がないことの確認）。
            controller.SetLayerWeight("emotion", 1f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 1f);
            yield return null;

            Assert.That(binding.LoadRecording(binding.LastRecordingName), Is.True);
            Assert.That(binding.StartPlayback(), Is.True);
            yield return null;
            AssertOutput(renderer, start, "frame 0 of playback must reproduce the recorded baseline");

            // 再生中のスクリプト書込（単発・入力源・バルク）は出力に影響しない。
            controller.SetLayerWeight("emotion", 0f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 0f);
            using (var bulk = controller.BeginInputSourceWeightBatch())
            {
                bulk.SetWeight(EyesLayerIndex, ExpressionSlotIndex, 0f);
            }

            yield return null;
            Assert.That(binding.ElapsedSeconds, Is.LessThan(changedAt), "the live-write check must happen before the recorded change");
            AssertOutput(renderer, start, "live weight writes during playback must not affect the output");

            yield return WaitUntilCompleted(binding);
            yield return null;
            AssertOutput(renderer, end, "playback must reach the recorded final weights");

            // 停止後は停止時点の weight を維持し、以後のライブ書込が反映される。
            binding.StopPlayback();
            yield return null;
            AssertOutput(renderer, end, "stopping playback must keep the weights at the stop point");

            controller.SetLayerWeight("emotion", 0f);
            yield return null;
            Assert.That(renderer.GetBlendShapeWeight(0), Is.LessThan(end[0] - 1f), "live writes must apply again after playback stops");
        }

        [UnityTest]
        public IEnumerator StartPlaybackWithOffset_AfterWeightChange_EstablishesFoldedWeightsAtFrameZero()
        {
            Setup(out FacialController controller, out RecCharacterBinding binding, out SkinnedMeshRenderer renderer);
            yield return null;

            Assert.That(binding.StartRecording("offset"), Is.True);
            yield return WaitFrames(FramesBeforeChange);
            double changedAt = binding.ElapsedSeconds;
            controller.SetLayerWeight("emotion", 0.3f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 0.4f);
            yield return null;
            yield return null;
            float[] changed = Snapshot(renderer);
            yield return WaitFrames(FramesBeforeChange);
            double recordedEnd = binding.ElapsedSeconds;
            binding.StopRecording();

            controller.SetLayerWeight("emotion", 1f);
            controller.SetInputSourceWeight(EyesLayerIndex, ExpressionSlotIndex, 1f);
            yield return null;

            Assert.That(binding.LoadRecording(binding.LastRecordingName), Is.True);
            Assert.That(binding.StartPlayback((changedAt + recordedEnd) * 0.5d), Is.True);
            yield return null;

            AssertOutput(renderer, changed, "seeking past the weight change must establish the folded weights at frame 0");
            binding.StopPlayback();
        }

        private void Setup(out FacialController controller, out RecCharacterBinding binding, out SkinnedMeshRenderer renderer)
        {
            _host = new GameObject("RecWeightPlaybackAcceptanceHost");
            _host.AddComponent<Animator>();

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_host.transform, false);
            renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            _mesh.AddBlendShapeFrame("blink", 100f, new Vector3[3], null, null);
            renderer.sharedMesh = _mesh;

            _characterSo = ScriptableObject.CreateInstance<WeightTestCharacterProfileSO>();
            _characterSo.name = TestAssetName;

            controller = _host.AddComponent<FacialController>();
            controller.CharacterSO = _characterSo;
            controller.Initialize();
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(controller.WeightInjectionGate, Is.Not.Null);

            binding = _host.AddComponent<RecCharacterBinding>();
            binding.FacialController = controller;
            binding.RecordingClock = new FrameRecClock();

            FacialProfile profile = WeightTestCharacterProfileSO.CreateProfile();
            controller.Activate(profile.FindExpressionById("smile").Value);
            controller.Activate(profile.FindExpressionById("blink").Value);
        }

        private static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntilCompleted(RecCharacterBinding binding)
        {
            for (int i = 0; i < MaxWaitFrames && binding.PlaybackState != RecPlaybackState.Completed; i++)
            {
                yield return null;
            }

            Assert.That(binding.PlaybackState, Is.EqualTo(RecPlaybackState.Completed), "timed out waiting for playback completion");
        }

        private static float[] Snapshot(SkinnedMeshRenderer renderer)
        {
            return new[] { renderer.GetBlendShapeWeight(0), renderer.GetBlendShapeWeight(1) };
        }

        private static void AssertOutput(SkinnedMeshRenderer renderer, float[] expected, string message)
        {
            float[] actual = Snapshot(renderer);
            Assert.That(actual[0], Is.EqualTo(expected[0]).Within(Tolerance), message + " (smile)");
            Assert.That(actual[1], Is.EqualTo(expected[1]).Within(Tolerance), message + " (blink)");
        }

        private static void DeleteGeneratedRecordingAssets()
        {
            // テスト専用のキャラクターフォルダごと（.meta を含めて）消し、生成物をリポジトリに残さない。
            string assetDirectory = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                TestAssetName);
            if (Directory.Exists(assetDirectory))
            {
                Directory.Delete(assetDirectory, true);
            }

            string assetDirectoryMeta = assetDirectory + ".meta";
            if (File.Exists(assetDirectoryMeta))
            {
                File.Delete(assetDirectoryMeta);
            }
        }

        /// <summary>経過時間をフレーム数 × 固定刻みで返す記録クロック（再生 Tick の captureDeltaTime と一致させる）。</summary>
        private sealed class FrameRecClock : IRecClock
        {
            private int _startFrame;

            public double ElapsedSeconds => (Time.frameCount - _startFrame) * (double)FrameDeltaTime;

            public void Reset()
            {
                _startFrame = Time.frameCount;
            }
        }

        private sealed class WeightTestCharacterProfileSO : FacialCharacterProfileSO
        {
            public static FacialProfile CreateProfile()
            {
                return new FacialProfile(
                    "1.0.0",
                    new[]
                    {
                        new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                        new LayerDefinition("eyes", 1, ExclusionMode.LastWins),
                    },
                    new[]
                    {
                        new Expression("smile", "Smile", "emotion", 0f, TransitionCurve.Linear, new[] { new BlendShapeMapping("smile", 1f) }),
                        new Expression("blink", "Blink", "eyes", 0f, TransitionCurve.Linear, new[] { new BlendShapeMapping("blink", 1f) }),
                    });
            }

            public override FacialProfile LoadProfile()
            {
                return CreateProfile();
            }
        }
    }
}
