using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="IGazeChannelOverrideProvider"/> を実装する binding の上書き (目ボーン path・可動範囲) が
    /// ローカルの <see cref="GazeChannel"/> より優先されること、上書きの変化で目ボーン provider が
    /// 作り直されることを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerGazeChannelOverrideTests : SizedTestFixture
    {
        private const float Tolerance = 0.01f;

        private GameObject _gameObject;
        private FacialControllerGazeChannelTests.GazeChannelProfileSO _profileAsset;
        private Mesh _mesh;
        private Transform _localLeftEye;
        private Transform _localRightEye;
        private Transform _customLeftEye;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }

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
        }

        [UnityTest]
        public IEnumerator LateUpdate_OverridePathAndRange_DrivesOverrideBoneWithOverrideRange()
        {
            var binding = new FakeOverrideBinding { Slug = "fake" };
            binding.SetOverride(new GazeChannelOverride("Head/CustomEye_L", null, true, 40f, 40f, 40f, 40f));
            FacialController controller = CreateController(binding);

            yield return null;

            Assert.That(controller.IsInitialized, Is.True);
            // input (0, 1) は上方向。lookUpAngle 分だけ pitch 軸 (right) まわりに符号反転して回る。
            AssertRotation(Quaternion.AngleAxis(-40f, Vector3.right), _customLeftEye.localRotation);
            AssertRotation(Quaternion.identity, _localLeftEye.localRotation);
            // 右目は path を上書きしていないのでローカル path を、可動範囲は上書き値を使う。
            AssertRotation(Quaternion.AngleAxis(-40f, Vector3.right), _localRightEye.localRotation);
        }

        [UnityTest]
        public IEnumerator LateUpdate_NoOverride_UsesLocalPathAndRange()
        {
            var binding = new FakeOverrideBinding { Slug = "fake" };
            FacialController controller = CreateController(binding);

            yield return null;

            Assert.That(controller.IsInitialized, Is.True);
            AssertRotation(Quaternion.AngleAxis(-15f, Vector3.right), _localLeftEye.localRotation);
            AssertRotation(Quaternion.identity, _customLeftEye.localRotation);
        }

        [UnityTest]
        public IEnumerator LateUpdate_OverrideChangedAfterInitialize_RebuildsAndRestoresPreviousBone()
        {
            var binding = new FakeOverrideBinding { Slug = "fake" };
            CreateController(binding);
            yield return null;
            AssertRotation(Quaternion.AngleAxis(-15f, Vector3.right), _localLeftEye.localRotation);

            binding.SetOverride(new GazeChannelOverride("Head/CustomEye_L", null, false, 0f, 0f, 0f, 0f));
            yield return null;

            AssertRotation(Quaternion.AngleAxis(-15f, Vector3.right), _customLeftEye.localRotation);
            AssertRotation(Quaternion.identity, _localLeftEye.localRotation);
        }

        [UnityTest]
        public IEnumerator LateUpdate_UnresolvableOverridePath_WarnsOnceAndFallsBackToLocalPath()
        {
            var binding = new FakeOverrideBinding { Slug = "fake" };
            binding.SetOverride(new GazeChannelOverride("Head/Missing_L", null, false, 0f, 0f, 0f, 0f));
            LogAssert.Expect(LogType.Warning, new Regex("Head/Missing_L"));

            CreateController(binding);
            yield return null;

            AssertRotation(Quaternion.AngleAxis(-15f, Vector3.right), _localLeftEye.localRotation);
        }

        private FacialController CreateController(FakeOverrideBinding binding)
        {
            // Initialize は Animator と SkinnedMeshRenderer が無いと初期化をスキップする。Avatar は持たせない
            // (非 Humanoid)。目ボーンは path だけで解決させる。
            _gameObject = new GameObject("FacialControllerGazeChannelOverrideTests");
            _gameObject.AddComponent<Animator>();
            var head = new GameObject("Head").transform;
            head.SetParent(_gameObject.transform, false);
            _localLeftEye = CreateChild("LocalEye_L", head);
            _localRightEye = CreateChild("LocalEye_R", head);
            _customLeftEye = CreateChild("CustomEye_L", head);

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_gameObject.transform, false);
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            meshObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
            var controller = _gameObject.AddComponent<FacialController>();

            _profileAsset = UnityEngine.ScriptableObject.CreateInstance<FacialControllerGazeChannelTests.GazeChannelProfileSO>();
            _profileAsset.ProfileToLoad = CreateMinimalProfile();
            _profileAsset.WritableAdapterBindings.Add(binding);
            // 既定チャネル gaze は SO が補完する。ローカル path と既定の可動範囲 (lookUp 15) を設定する。
            GazeChannel gaze = _profileAsset.GazeChannels[0];
            gaze.leftEyeBonePath = "Head/LocalEye_L";
            gaze.rightEyeBonePath = "Head/LocalEye_R";

            controller.CharacterSO = _profileAsset;
            controller.Initialize();
            return controller;
        }

        private static Transform CreateChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static FacialProfile CreateMinimalProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers, Array.Empty<Expression>());
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance),
                $"expected {expected.eulerAngles}, actual {actual.eulerAngles}");
        }

        [Serializable]
        private sealed class FakeOverrideBinding : AdapterBindingBase, IGazeChannelOverrideProvider
        {
            [NonSerialized] private GazeChannelOverride _override;
            [NonSerialized] private bool _hasOverride;
            [NonSerialized] private int _version;

            public int GazeChannelOverrideVersion => _version;

            public void SetOverride(GazeChannelOverride value)
            {
                _override = value;
                _hasOverride = true;
                _version++;
            }

            public bool TryGetGazeChannelOverride(string channelId, out GazeChannelOverride value)
            {
                value = _override;
                return _hasOverride && channelId == GazeSourceIdConvention.DefaultChannelId;
            }

            public override void OnStart(in AdapterBuildContext ctx)
            {
                // 常に上方向 (0, 1) を返す共有 gaze 入力源を fake:gaze として登録する。
                ctx.InputSourceRegistry.Register(
                    AdapterSlug.Parse(Slug),
                    GazeSourceIdConvention.ComposeSub(GazeSourceIdConvention.DefaultChannelId, GazeSide.Shared),
                    new LookUpSource(Slug + ":" + GazeSourceIdConvention.DefaultChannelId));
            }
        }

        private sealed class LookUpSource : IInputSource, IAnalogInputSource
        {
            public LookUpSource(string id)
            {
                Id = id;
                ContributeMask = new BitArray(0);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; }
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
            public bool TryReadScalar(out float value) { value = 0f; return true; }
            public bool TryReadVector2(out float x, out float y) { x = 0f; y = 1f; return true; }
            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0) output[0] = 0f;
                if (output.Length > 1) output[1] = 1f;
                return output.Length >= 2;
            }
        }
    }
}
