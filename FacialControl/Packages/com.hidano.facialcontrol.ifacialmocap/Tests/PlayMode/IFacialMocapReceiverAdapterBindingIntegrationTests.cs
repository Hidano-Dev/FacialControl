using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.IFacialMocap;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.PlayMode
{
    /// <summary>
    /// 実 UDP loopback で <see cref="IFacialMocapReceiverAdapterBinding"/> の OnStart →
    /// 入力源登録 → OnFixedTick による BlendShape 反映、および Dispose での後始末を検証する。
    /// </summary>
    [MediumTest]
    public class IFacialMocapReceiverAdapterBindingIntegrationTests : SizedTestFixture
    {
        private static int s_port = 19430;

        private GameObject _hostGo;
        private InputSourceRegistry _registry;
        private IFacialMocapReceiverAdapterBinding _binding;
        private IFacialMocapRuntimeSettingsSO _settings;
        private UdpClient _sender;
        private bool _started;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGo = new GameObject("IFM_BindingHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _started)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
                    // TearDown では握り潰してテスト本体の assertion を優先。
                }
            }

            _binding = null;
            _started = false;

            if (_sender != null)
            {
                _sender.Dispose();
                _sender = null;
            }

            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
            }

            if (_hostGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGo);
                _hostGo = null;
            }
        }

        private AdapterBuildContext CreateContext(IReadOnlyList<string> blendShapeNames)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0"),
                blendShapeNames: blendShapeNames,
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGo,
                lipSyncProvider: null);
        }

        private IFacialMocapRuntimeSettingsSO CreateSettings(int port)
        {
            var so = ScriptableObject.CreateInstance<IFacialMocapRuntimeSettingsSO>();
            so.FromJson(new IFacialMocapOptionsDto
            {
                listenPort = port,
                sendHandshake = false,
                enableGaze = true,
                enableHead = true,
                includeHeadPosition = false,
                stalenessSeconds = 0f,
            }.ToJson());
            return so;
        }

        [UnityTest]
        public IEnumerator OnStart_RegistersSources_AndLoopbackUpdatesBlendShape()
        {
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm" };
            _binding.Configure(_settings);
            AdapterBuildContext ctx = CreateContext(new List<string> { "jawOpen", "mouthSmileLeft" });

            _binding.OnStart(in ctx);
            _started = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_registry.TryResolve("ifm", out IInputSource blendShapeSource), Is.True);
            Assert.That(_registry.TryResolve("ifm:gaze.left", out _), Is.True);
            Assert.That(_registry.TryResolve("ifm:gaze.right", out _), Is.True);
            Assert.That(_registry.TryResolve("ifm:head", out _), Is.True);

            yield return new WaitForSeconds(0.2f);

            _sender = new UdpClient();
            byte[] data = Encoding.ASCII.GetBytes(
                "jawOpen-100|mouthSmile_L-50|=head#10,20,30,0,0,0|rightEye#0,15,0|leftEye#0,-15,0|");
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);

            var output = new float[2];
            bool got = false;
            for (int attempt = 0; attempt < 20 && !got; attempt++)
            {
                _sender.Send(data, data.Length, endpoint);
                yield return new WaitForSeconds(0.05f);
                _binding.OnFixedTick(0.02f);

                Array.Clear(output, 0, output.Length);
                if (blendShapeSource.TryWriteValues(output) && output[0] > 0.01f)
                {
                    got = true;
                }
            }

            Assert.That(got, Is.True, "loopback の jawOpen が BlendShape 入力源へ届くべき。");
            Assert.That(output[0], Is.EqualTo(1f).Within(0.02f));
            Assert.That(output[1], Is.EqualTo(0.5f).Within(0.02f));

            Assert.That(_binding.GazeLeftSource.TryReadVector2(out _, out _), Is.True);
        }

        [Test]
        public void OnStart_MeshHasTrailingUnmappedBlendShape_ContributeMaskLengthMatchesMeshCount()
        {
            // ARKit マッピング対象がメッシュ末尾に無いモデルでは、mask 長が
            // 「最大 mapped index + 1」に縮み、LayerInputSourceAggregator の
            // BitArray.Or (長さ完全一致要求) が ArgumentException を投げる回帰の再現。
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm3" };
            _binding.Configure(_settings);
            var meshNames = new List<string> { "jawOpen", "customExtraShape" };
            AdapterBuildContext ctx = CreateContext(meshNames);

            _binding.OnStart(in ctx);
            _started = true;

            Assert.That(_registry.TryResolve("ifm3", out IInputSource source), Is.True);
            Assert.That(source.ContributeMask.Length, Is.EqualTo(meshNames.Count),
                "ContributeMask 長はメッシュの BlendShape 総数と一致すべき。");
            Assert.That(source.BlendShapeCount, Is.EqualTo(meshNames.Count));
            Assert.That(source.ContributeMask[0], Is.True, "jawOpen は ARKit マッピング対象。");
            Assert.That(source.ContributeMask[1], Is.False, "customExtraShape は非マッピング対象。");

            // Aggregator と同じ長さ要求で Or できること（長さ不一致なら ArgumentException）。
            var layerMask = new System.Collections.BitArray(meshNames.Count);
            Assert.DoesNotThrow(() => layerMask.Or(source.ContributeMask));
        }

        [Test]
        public void OnStart_DisabledMapping_IsExcludedFromContributeMask()
        {
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm4" };
            _binding.Configure(_settings, new List<IFacialMocapBlendShapeMapping>
            {
                new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen"),
                new IFacialMocapBlendShapeMapping("mouthSmile_L", "mouthSmileLeft", false, 0f, 1f, 1f),
                // 同名の後続マッピングへ出力先が移らないこと
                new IFacialMocapBlendShapeMapping("mouthSmile_L", "mouthSmileAlt"),
            });
            AdapterBuildContext ctx = CreateContext(new List<string> { "jawOpen", "mouthSmileLeft", "mouthSmileAlt" });

            _binding.OnStart(in ctx);
            _started = true;

            Assert.That(_registry.TryResolve("ifm4", out IInputSource source), Is.True);
            Assert.That(source.ContributeMask[0], Is.True, "有効なマッピングは出力対象。");
            Assert.That(source.ContributeMask[1], Is.False, "オフのマッピングは値を出力しない。");
            Assert.That(source.ContributeMask[2], Is.False, "オフにした名前の後続マッピングは採用しない。");
        }

        [Test]
        public void OnStart_DisabledMappingTargetMissingOnMesh_StillBlocksLaterSameNameMapping()
        {
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm4b" };
            _binding.Configure(_settings, new List<IFacialMocapBlendShapeMapping>
            {
                new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen"),
                // モデル差し替え・リネームで出力先がメッシュに無くなったオフの行
                new IFacialMocapBlendShapeMapping("mouthSmile_L", "renamedShape", false, 0f, 1f, 1f),
                new IFacialMocapBlendShapeMapping("mouthSmile_L", "mouthSmileAlt"),
            });
            AdapterBuildContext ctx = CreateContext(new List<string> { "jawOpen", "mouthSmileAlt" });

            _binding.OnStart(in ctx);
            _started = true;

            Assert.That(_registry.TryResolve("ifm4b", out IInputSource source), Is.True);
            Assert.That(source.ContributeMask[0], Is.True);
            Assert.That(source.ContributeMask[1], Is.False, "出力先が解決できなくても、オフにした名前は後続マッピングへ移さない。");
        }

        [UnityTest]
        public IEnumerator OnFixedTick_TunedMapping_AppliesRangeThenWeight()
        {
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm5" };
            _binding.Configure(_settings, new List<IFacialMocapBlendShapeMapping>
            {
                // 範囲 0〜0.5 を 0〜1 に再マップ → ×0.5: 受信 0.25 → 0.5 → 0.25
                new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen", true, 0f, 0.5f, 0.5f),
                // 旧アセット相当（tuningVersion 0）は従来どおり素通し: 受信 0.5 → 0.5
                new IFacialMocapBlendShapeMapping { ifacialMocapName = "mouthSmile_L", blendShapeName = "mouthSmileLeft" },
            });
            AdapterBuildContext ctx = CreateContext(new List<string> { "jawOpen", "mouthSmileLeft" });

            _binding.OnStart(in ctx);
            _started = true;
            Assert.That(_registry.TryResolve("ifm5", out IInputSource blendShapeSource), Is.True);

            yield return new WaitForSeconds(0.2f);

            _sender = new UdpClient();
            byte[] data = Encoding.ASCII.GetBytes("jawOpen-25|mouthSmile_L-50|");
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);

            var output = new float[2];
            bool got = false;
            for (int attempt = 0; attempt < 20 && !got; attempt++)
            {
                _sender.Send(data, data.Length, endpoint);
                yield return new WaitForSeconds(0.05f);
                _binding.OnFixedTick(0.02f);

                Array.Clear(output, 0, output.Length);
                if (blendShapeSource.TryWriteValues(output) && output[1] > 0.01f)
                {
                    got = true;
                }
            }

            Assert.That(got, Is.True, "loopback の値が BlendShape 入力源へ届くべき。");
            Assert.That(output[0], Is.EqualTo(0.25f).Within(0.02f));
            Assert.That(output[1], Is.EqualTo(0.5f).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysHost_AndUnregistersSources()
        {
            int port = ++s_port;
            _settings = CreateSettings(port);
            _binding = new IFacialMocapReceiverAdapterBinding { Slug = "ifm2" };
            _binding.Configure(_settings);
            AdapterBuildContext ctx = CreateContext(new List<string> { "jawOpen" });

            _binding.OnStart(in ctx);
            _started = true;

            IFacialMocapReceiverHost host = _hostGo.GetComponent<IFacialMocapReceiverHost>();
            Assert.That(host, Is.Not.Null);

            _binding.Dispose();
            _started = false;

            yield return null;

            Assert.That(host == null, Is.True, "Dispose で host MonoBehaviour が破棄されるべき。");
            Assert.That(_registry.TryResolve("ifm2", out _), Is.False);
        }
    }
}
