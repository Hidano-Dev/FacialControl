using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// OSC の系統数（送信先の数 = 受信キャラの数）を増やしたときに、送信側・受信側のコストがどう伸びるかの現状調査（Large）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構成は 3 通り × 系統数 N = 1..4。どれも送信キャラ 1 体 + 受信キャラ N 体の FacialController を置く。
    /// <list type="bullet">
    /// <item><c>no-osc</c>: OSC binding なし（FacialController 自体のコスト）</item>
    /// <item><c>send-only</c>: 送信キャラだけ送信先 N 個の OSC 送信 binding を持つ（受信キャラは OSC なし）</item>
    /// <item><c>send-receive</c>: 送信先 N 個 + 受信キャラ N 体がそれぞれ別ポートで受信</item>
    /// </list>
    /// send-only − no-osc が送信側、send-receive − send-only が受信側のコストの目安になる。
    /// </para>
    /// <para>
    /// 実時間で 60fps を目標に回し、指標はメインスレッドの CPU 時間（Windows のみ。GetThreadTimes）、プロセス全体の
    /// CPU 時間からメインスレッド分を引いた残り（バックグラウンドスレッド）、フレームの実時間（中央値・p95・最大）。
    /// 宛先・BlendShape 数・計測秒数は環境変数 <c>FACIALCONTROL_OSC_SCALING_ENDPOINT</c> /
    /// <c>FACIALCONTROL_OSC_SCALING_BLENDSHAPES</c> / <c>FACIALCONTROL_OSC_SCALING_SECONDS</c> で変えられる
    /// （宛先にサブネットのブロードキャストを指定すると、同じ PC の受信キャラにも届く）。結果は Markdown で
    /// <c>test-results/osc-scaling/survey.md</c>（環境変数 <c>FACIALCONTROL_OSC_SCALING_REPORT_DIR</c> で変更可）と
    /// ログに出す。合否は付けない（計測の仕組み自体が壊れた場合だけ失敗する）。
    /// </para>
    /// </remarks>
    [TestFixture]
    [LargeTest]
    public sealed class OscScalingSurveyTests : SizedTestFixture
    {
        private const string DefaultEndpoint = "127.0.0.1";
        private const int PortBase = 39900;
        private const int DefaultBlendShapeCount = 52;
        private const int MaxChannels = 4;
        private const float WarmUpSeconds = 1.5f;
        private const float DefaultMeasureSeconds = 5f;
        private const string EndpointVariable = "FACIALCONTROL_OSC_SCALING_ENDPOINT";
        private const string BlendShapeCountVariable = "FACIALCONTROL_OSC_SCALING_BLENDSHAPES";
        private const string MeasureSecondsVariable = "FACIALCONTROL_OSC_SCALING_SECONDS";

        private static string Endpoint => ReadEnv(EndpointVariable, DefaultEndpoint);
        private static int BlendShapeCount => int.Parse(ReadEnv(BlendShapeCountVariable, DefaultBlendShapeCount.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        private static float MeasureSeconds => float.Parse(ReadEnv(MeasureSecondsVariable, DefaultMeasureSeconds.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        private const string LayerName = "emotion";
        private const string SenderSlug = "osc-scaling-sender";
        private const string ReceiverSlugPrefix = "osc-scaling-receiver-";
        private const string SourceExpressionId = "osc-scaling-source";
        private const string ReportDirectoryVariable = "FACIALCONTROL_OSC_SCALING_REPORT_DIR";

        private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private int _previousTargetFrameRate;
        private int _previousVSyncCount;

        private enum ScenarioMode
        {
            NoOsc,
            SendOnly,
            SendReceive
        }

        private struct ScenarioResult
        {
            public ScenarioMode Mode;
            public int Channels;
            public double MainThreadCpuMsPerFrame;
            public double OtherThreadsCpuMsPerFrame;
            public double FrameWallMedianMs;
            public double FrameWallP95Ms;
            public double FrameWallMaxMs;
            public int FrameCount;
            public int FramesOver33Ms;
            public int ReceiversReached;
            public bool MainThreadCpuAvailable;
        }

        [SetUp]
        public void SetUp()
        {
            // 実機と同じく 60fps を目標に実時間で回す（対応表の再要求など実時間で動く処理を計測に含めるため）。
            _previousTargetFrameRate = UnityEngine.Application.targetFrameRate;
            _previousVSyncCount = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = 60;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Application.targetFrameRate = _previousTargetFrameRate;
            QualitySettings.vSyncCount = _previousVSyncCount;
            DestroyScenarioObjects();
        }

        [UnityTest]
        public IEnumerator Survey_ChannelCounts_WritesReport()
        {
            var results = new List<ScenarioResult>();
            ScenarioMode[] modes = { ScenarioMode.NoOsc, ScenarioMode.SendOnly, ScenarioMode.SendReceive };
            for (int channels = 1; channels <= MaxChannels; channels++)
            {
                for (int m = 0; m < modes.Length; m++)
                {
                    var holder = new ScenarioResult[1];
                    yield return RunScenario(modes[m], channels, results.Count, holder);
                    results.Add(holder[0]);
                    DestroyScenarioObjects();

                    // ソケットの close と受信スレッドの終了を待ってから次の構成へ進む。
                    for (int i = 0; i < 10; i++)
                    {
                        yield return null;
                    }

                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }

            string report = BuildReport(results);
            string directory = ResolveReportDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "survey.md");
            File.WriteAllText(path, report, new UTF8Encoding(false));
            Debug.Log("[OscScalingSurvey] report written to " + path + "\n" + report);

            Assert.That(results.Count, Is.EqualTo(MaxChannels * modes.Length));
        }

        private IEnumerator RunScenario(ScenarioMode mode, int channels, int scenarioIndex, ScenarioResult[] holder)
        {
            string[] blendShapeNames = CreateBlendShapeNames(BlendShapeCount);
            int[] ports = AllocatePorts(channels, PortBase + scenarioIndex * 10);

            OscSenderAdapterBinding senderBinding = null;
            if (mode != ScenarioMode.NoOsc)
            {
                var endpoints = new OscSenderEndpointConfig[channels];
                for (int i = 0; i < channels; i++)
                {
                    endpoints[i] = new OscSenderEndpointConfig(Endpoint, ports[i], true);
                }

                senderBinding = new OscSenderAdapterBinding
                {
                    Slug = SenderSlug,
                    SuppressLoopback = false
                };
                senderBinding.ConfigureEndpoints(endpoints, blendShapeNames);
            }

            FacialController sourceController = CreateController(
                "OscScaling_Source",
                blendShapeNames,
                CreateSourceProfile(blendShapeNames),
                senderBinding,
                out _);

            var receiverRenderers = new SkinnedMeshRenderer[channels];
            var receiverControllers = new FacialController[channels];
            for (int i = 0; i < channels; i++)
            {
                OscReceiverAdapterBinding receiverBinding = null;
                string slug = ReceiverSlugPrefix + i.ToString(CultureInfo.InvariantCulture);
                if (mode == ScenarioMode.SendReceive)
                {
                    receiverBinding = new OscReceiverAdapterBinding
                    {
                        Slug = slug,
                        BundleMode = BundleInterpretationMode.AtomicSwap
                    };
                    receiverBinding.Port = ports[i];
                }

                receiverControllers[i] = CreateController(
                    "OscScaling_Receiver" + i.ToString(CultureInfo.InvariantCulture),
                    blendShapeNames,
                    CreateReceiverProfile(mode == ScenarioMode.SendReceive ? slug : null),
                    receiverBinding,
                    out receiverRenderers[i]);
            }

            sourceController.Initialize();
            for (int i = 0; i < channels; i++)
            {
                receiverControllers[i].Initialize();
            }

            Assert.That(sourceController.IsInitialized, Is.True, "送信キャラの FacialController が初期化されること。");
            if (senderBinding != null)
            {
                Assert.That(senderBinding.IsStarted, Is.True, "OSC 送信 binding が起動していること。");
                Assert.That(senderBinding.HelperSenderCount, Is.EqualTo(channels), "送信先の数だけ送信器が立っていること。");
            }

            sourceController.Activate(CreateSourceExpression(blendShapeNames));

            // 対応表の要求・応答と遷移が落ち着くまで回す。
            var warmUp = Stopwatch.StartNew();
            while (warmUp.Elapsed.TotalSeconds < WarmUpSeconds)
            {
                yield return null;
            }

            var frameWallList = new List<double>(1024);
            bool mainCpuAvailable = TryGetMainThreadCpu100ns(out long mainCpuStart);
            TimeSpan processCpuStart = GetProcessCpu();
            var window = Stopwatch.StartNew();
            long previousTicks = 0;
            double measureSeconds = MeasureSeconds;
            while (window.Elapsed.TotalSeconds < measureSeconds)
            {
                yield return null;
                long now = window.ElapsedTicks;
                frameWallList.Add((now - previousTicks) * 1000.0 / Stopwatch.Frequency);
                previousTicks = now;
            }

            window.Stop();
            double[] frameWallMs = frameWallList.ToArray();
            int frameCount = frameWallMs.Length;
            int over33 = 0;
            for (int i = 0; i < frameCount; i++)
            {
                if (frameWallMs[i] > 33.3)
                {
                    over33++;
                }
            }
            mainCpuAvailable &= TryGetMainThreadCpu100ns(out long mainCpuEnd);
            TimeSpan processCpuEnd = GetProcessCpu();

            double mainCpuMs = mainCpuAvailable ? (mainCpuEnd - mainCpuStart) / 10000.0 : 0.0;
            double processCpuMs = (processCpuEnd - processCpuStart).TotalMilliseconds;

            int reached = 0;
            if (mode == ScenarioMode.SendReceive)
            {
                for (int i = 0; i < channels; i++)
                {
                    if (receiverRenderers[i].GetBlendShapeWeight(0) > 1f)
                    {
                        reached++;
                    }
                }
            }

            Array.Sort(frameWallMs);
            holder[0] = new ScenarioResult
            {
                Mode = mode,
                Channels = channels,
                MainThreadCpuAvailable = mainCpuAvailable,
                MainThreadCpuMsPerFrame = mainCpuMs / frameCount,
                OtherThreadsCpuMsPerFrame = mainCpuAvailable
                    ? (processCpuMs - mainCpuMs) / frameCount
                    : processCpuMs / frameCount,
                FrameWallMedianMs = frameWallMs[frameCount / 2],
                FrameWallP95Ms = frameWallMs[(int)(frameCount * 0.95)],
                FrameWallMaxMs = frameWallMs[frameCount - 1],
                FrameCount = frameCount,
                FramesOver33Ms = over33,
                ReceiversReached = reached
            };
        }

        private FacialController CreateController(
            string name,
            string[] blendShapeNames,
            FacialProfile profile,
            AdapterBindingBase binding,
            out SkinnedMeshRenderer renderer)
        {
            Mesh mesh = CreateMesh(name + "_Mesh", blendShapeNames);
            _ownedObjects.Add(mesh);

            var so = ScriptableObject.CreateInstance<ScalingProfileSO>();
            so.Profile = profile;
            if (binding != null)
            {
                so.WritableAdapterBindings.Add(binding);
            }

            _ownedObjects.Add(so);

            var root = new GameObject(name);
            _roots.Add(root);
            root.AddComponent<Animator>();

            var meshObject = new GameObject("Mesh");
            meshObject.transform.SetParent(root.transform);
            renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;

            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = so;
            controller.SkinnedMeshRenderers = new[] { renderer };
            return controller;
        }

        private void DestroyScenarioObjects()
        {
            for (int i = 0; i < _roots.Count; i++)
            {
                if (_roots[i] != null)
                {
                    _roots[i].SetActive(false);
                    UnityEngine.Object.DestroyImmediate(_roots[i]);
                }
            }

            _roots.Clear();
            for (int i = 0; i < _ownedObjects.Count; i++)
            {
                if (_ownedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_ownedObjects[i]);
                }
            }

            _ownedObjects.Clear();
        }

        private static string BuildReport(List<ScenarioResult> results)
        {
            var sb = new StringBuilder();
            sb.Append("# OSC 系統数スケーリング調査\n\n");
            sb.Append("送信キャラ 1 体 + 受信キャラ N 体、宛先 ").Append(Endpoint)
                .Append("、BlendShape ").Append(BlendShapeCount)
                .Append(" 個、計測 ").Append(MeasureSeconds.ToString("0.#", CultureInfo.InvariantCulture))
                .Append(" 秒（targetFrameRate = 60）。\n");
            sb.Append("CPU 時間は 1 フレームあたりの ms。「他スレッド」はプロセス全体の CPU 時間からメインスレッド分を引いた残り。\n\n");
            sb.Append("| N | 構成 | メイン CPU ms/f | 他スレッド CPU ms/f | フレーム実時間 中央値 ms | p95 ms | 最大 ms | 33ms 超 / 全フレーム | 受信到達 |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|\n");
            for (int i = 0; i < results.Count; i++)
            {
                ScenarioResult r = results[i];
                sb.Append("| ").Append(r.Channels)
                    .Append(" | ").Append(ModeName(r.Mode))
                    .Append(" | ").Append(r.MainThreadCpuAvailable ? Format(r.MainThreadCpuMsPerFrame) : "n/a")
                    .Append(" | ").Append(Format(r.OtherThreadsCpuMsPerFrame))
                    .Append(" | ").Append(Format(r.FrameWallMedianMs))
                    .Append(" | ").Append(Format(r.FrameWallP95Ms))
                    .Append(" | ").Append(Format(r.FrameWallMaxMs))
                    .Append(" | ").Append(r.FramesOver33Ms.ToString(CultureInfo.InvariantCulture))
                    .Append(" / ").Append(r.FrameCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(r.Mode == ScenarioMode.SendReceive
                        ? r.ReceiversReached.ToString(CultureInfo.InvariantCulture) + "/" + r.Channels.ToString(CultureInfo.InvariantCulture)
                        : "-")
                    .Append(" |\n");
            }

            sb.Append("\n## 差分（系統数ごと）\n\n");
            sb.Append("| N | 送信側 メイン ms/f | 送信側 他スレッド ms/f | 受信側 メイン ms/f | 受信側 他スレッド ms/f |\n");
            sb.Append("|---|---|---|---|---|\n");
            for (int n = 1; n <= MaxChannels; n++)
            {
                if (!TryFind(results, ScenarioMode.NoOsc, n, out ScenarioResult none)
                    || !TryFind(results, ScenarioMode.SendOnly, n, out ScenarioResult send)
                    || !TryFind(results, ScenarioMode.SendReceive, n, out ScenarioResult both))
                {
                    continue;
                }

                bool main = none.MainThreadCpuAvailable && send.MainThreadCpuAvailable && both.MainThreadCpuAvailable;
                sb.Append("| ").Append(n)
                    .Append(" | ").Append(main ? Format(send.MainThreadCpuMsPerFrame - none.MainThreadCpuMsPerFrame) : "n/a")
                    .Append(" | ").Append(Format(send.OtherThreadsCpuMsPerFrame - none.OtherThreadsCpuMsPerFrame))
                    .Append(" | ").Append(main ? Format(both.MainThreadCpuMsPerFrame - send.MainThreadCpuMsPerFrame) : "n/a")
                    .Append(" | ").Append(Format(both.OtherThreadsCpuMsPerFrame - send.OtherThreadsCpuMsPerFrame))
                    .Append(" |\n");
            }

            return sb.ToString();
        }

        private static bool TryFind(List<ScenarioResult> results, ScenarioMode mode, int channels, out ScenarioResult found)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Mode == mode && results[i].Channels == channels)
                {
                    found = results[i];
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static string ModeName(ScenarioMode mode)
        {
            switch (mode)
            {
                case ScenarioMode.NoOsc: return "no-osc";
                case ScenarioMode.SendOnly: return "send-only";
                default: return "send-receive";
            }
        }

        private static string Format(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string ResolveReportDirectory()
        {
            string overridden = Environment.GetEnvironmentVariable(ReportDirectoryVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            // <repo>/FacialControl/Assets → <repo>/test-results/osc-scaling
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "test-results", "osc-scaling"));
        }

        private static int[] AllocatePorts(int count, int start)
        {
            var ports = new int[count];
            int candidate = start;
            for (int i = 0; i < count; i++)
            {
                int port = OscPortResolver.ResolveAvailablePort(candidate, 50);
                Assert.That(port, Is.GreaterThan(0), "計測用の空きポートが見つかること。");
                ports[i] = port;
                candidate = port + 1;
            }

            return ports;
        }

        private static string ReadEnv(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static TimeSpan GetProcessCpu()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return process.TotalProcessorTime;
            }
        }

        private static bool TryGetMainThreadCpu100ns(out long total)
        {
            total = 0;
            if (UnityEngine.Application.platform != RuntimePlatform.WindowsEditor
                && UnityEngine.Application.platform != RuntimePlatform.WindowsPlayer)
            {
                return false;
            }

            try
            {
                if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user))
                {
                    return false;
                }

                total = kernel + user;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(
            IntPtr thread,
            out long creationTime,
            out long exitTime,
            out long kernelTime,
            out long userTime);

        private static string[] CreateBlendShapeNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = "shape" + i.ToString("00", CultureInfo.InvariantCulture);
            }

            return names;
        }

        private static FacialProfile CreateSourceProfile(string[] blendShapeNames)
        {
            return new FacialProfile(
                "2.0",
                new[] { new LayerDefinition(LayerName, 0, ExclusionMode.LastWins) },
                new[] { CreateSourceExpression(blendShapeNames) });
        }

        private static FacialProfile CreateReceiverProfile(string receiverSlug)
        {
            InputSourceDeclaration[][] layerInputSources = receiverSlug != null
                ? new[] { new[] { new InputSourceDeclaration(receiverSlug, 1f, null) } }
                : null;
            return new FacialProfile(
                "2.0",
                new[] { new LayerDefinition(LayerName, 0, ExclusionMode.LastWins) },
                expressions: null,
                rendererPaths: null,
                layerInputSources: layerInputSources);
        }

        private static Expression CreateSourceExpression(string[] blendShapeNames)
        {
            var mappings = new BlendShapeMapping[blendShapeNames.Length];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mappings[i] = new BlendShapeMapping(blendShapeNames[i], 0.2f + 0.6f * i / blendShapeNames.Length);
            }

            return new Expression(
                SourceExpressionId,
                "Scaling Source",
                LayerName,
                transitionDuration: 0f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: mappings);
        }

        private static Mesh CreateMesh(string name, string[] blendShapeNames)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var zeroDeltas = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, zeroDeltas, null, null);
            }

            return mesh;
        }

        public sealed class ScalingProfileSO : FacialCharacterProfileSO
        {
            public FacialProfile Profile;

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public override FacialProfile LoadProfile()
            {
                return Profile;
            }
        }
    }
}
