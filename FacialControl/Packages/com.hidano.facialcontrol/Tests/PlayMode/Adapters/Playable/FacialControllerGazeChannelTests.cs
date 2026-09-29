using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialController.Initialize"/> が CharacterSO の Gaze チャネル id 一覧を
    /// <see cref="IGazeChannelConsumer"/> を実装する adapter binding へ配布する公開契約を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerGazeChannelTests : SizedTestFixture
    {
        private GameObject _gameObject;
        private GazeChannelProfileSO _profileAsset;
        private Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_profileAsset != null)
            {
                UnityEngine.Object.DestroyImmediate(_profileAsset);
                _profileAsset = null;
            }

            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        [Test]
        public void Initialize_BindingImplementsGazeChannelConsumer_ReceivesConfiguredChannelIds()
        {
            // Initialize は Animator と BlendShape 付き SkinnedMeshRenderer が無いと初期化をスキップする。
            _gameObject = new GameObject("FacialControllerGazeChannelTests");
            _gameObject.AddComponent<Animator>();
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_gameObject.transform, false);
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            meshObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
            var controller = _gameObject.AddComponent<FacialController>();

            var binding = new FakeGazeConsumer { Slug = "fake" };
            _profileAsset = UnityEngine.ScriptableObject.CreateInstance<GazeChannelProfileSO>();
            _profileAsset.ProfileToLoad = CreateMinimalProfile();
            _profileAsset.WritableAdapterBindings.Add(binding);
            _profileAsset.WritableGazeChannels.Add(new GazeChannel { id = "camera" });

            controller.CharacterSO = _profileAsset;
            controller.Initialize();

            Assert.That(controller.IsInitialized, Is.True);
            // 既定チャネル "gaze" は SO 側で常に先頭へ補完されるため、追加分 "camera" と合わせて 2 件届く。
            CollectionAssert.AreEqual(new[] { "gaze", "camera" }, binding.ReceivedIds);
        }

        private static FacialProfile CreateMinimalProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers, Array.Empty<Expression>());
        }

        /// <summary>
        /// テスト用 SO。protected な binding / gaze チャネルリストへ書き込めるようにし、
        /// <see cref="LoadProfile"/> は与えられた in-memory プロファイルを返す。
        /// </summary>
        public sealed class GazeChannelProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public List<GazeChannel> WritableGazeChannels => _gazeChannels;

            public FacialProfile ProfileToLoad;

            public override FacialProfile LoadProfile()
            {
                return ProfileToLoad;
            }
        }

        [Serializable]
        private sealed class FakeGazeConsumer : AdapterBindingBase, IGazeChannelConsumer
        {
            [NonSerialized] public List<string> ReceivedIds;

            public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
            {
                ReceivedIds = new List<string>(channelIds);
            }
        }
    }
}
