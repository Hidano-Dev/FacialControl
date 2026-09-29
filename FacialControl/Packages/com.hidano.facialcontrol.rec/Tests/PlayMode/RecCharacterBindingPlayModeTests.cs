using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.PlayMode
{
    [TestFixture]
    public class RecCharacterBindingPlayModeTests
    {
        private const string TestAssetName = "RecCharacterBindingPlayModeTestsAsset";

        private GameObject _gameObject;
        private TestCharacterProfileSO _characterSo;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }

            if (_characterSo != null)
            {
                UnityEngine.Object.DestroyImmediate(_characterSo);
                _characterSo = null;
            }

            DeleteGeneratedRecordingAssets();
        }

        [UnityTest]
        public IEnumerator RecordingAndPlayback_EndToEnd_ReplaysIntoTriggerAndAnalogSources()
        {
            SetupHarness(out FacialController controller, out RecCharacterBinding binding, out FakeObservationBus bus, out FakeInputSourceRegistry registry, out TestTriggerSource triggerSource, out FakeAnalogSource analogSource);

            Assert.That(binding.StartRecording("session"), Is.True);

            analogSource.Publish(0.25f, -0.5f);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            bus.PublishAnalog(analogSource.Id, 0.25f, -0.5f);
            binding.StopRecording();

            Assert.That(binding.IsRecording, Is.False);
            Assert.That(binding.LastRecordingPath, Is.Not.Null.And.Not.Empty);
            Assert.That(RecFileReader.TryRead(binding.LastRecordingPath, out _), Is.True);

            triggerSource.TriggerOff("smile");
            analogSource.Publish(0f, 0f);

            Assert.That(binding.LoadRecording(binding.LastRecordingName), Is.True);

            bool completed = false;
            binding.Completed += () => completed = true;
            Assert.That(binding.StartPlayback(), Is.True);

            yield return null;

            Assert.That(triggerSource.ActiveExpressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(registry.TryResolve(analogSource.Id, out IInputSource resolvedAnalogSource), Is.True);
            Assert.That(resolvedAnalogSource, Is.Not.SameAs(analogSource));
            Assert.That(resolvedAnalogSource, Is.AssignableTo<IAnalogInputSource>());

            var playbackAnalogSource = (IAnalogInputSource)resolvedAnalogSource;
            Assert.That(playbackAnalogSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(-0.5f).Within(1e-5f));
            Assert.That(binding.PlaybackState, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(completed, Is.True);

            binding.StopPlayback();

            Assert.That(binding.PlaybackState, Is.EqualTo(RecPlaybackState.Idle));
            Assert.That(registry.TryResolve(analogSource.Id, out IInputSource restoredSource), Is.True);
            Assert.That(restoredSource, Is.SameAs(analogSource));
        }

        [UnityTest]
        public IEnumerator StartRecording_SameNameTwice_KeepsBothFilesUnderDistinctPaths()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out _, out TestTriggerSource triggerSource, out _);

            Assert.That(binding.StartRecording("same"), Is.True);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            binding.StopRecording();
            string firstPath = binding.LastRecordingPath;
            byte[] firstBytes = File.ReadAllBytes(firstPath);

            Assert.That(binding.StartRecording("same"), Is.True);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            binding.StopRecording();
            string secondPath = binding.LastRecordingPath;
            yield return null;

            Assert.That(secondPath, Is.Not.EqualTo(firstPath));
            Assert.That(binding.LastRecordingName, Is.EqualTo("same-2"));
            Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(firstBytes));
            Assert.That(RecFileReader.TryRead(firstPath, out _), Is.True);
            Assert.That(RecFileReader.TryRead(secondPath, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator LoadRecording_WithoutName_LoadsTheTakeThatWasJustRecorded()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out _, out TestTriggerSource triggerSource, out _);

            Assert.That(binding.StartRecording("reload"), Is.True);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            binding.StopRecording();

            Assert.That(binding.StartRecording("reload"), Is.True);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            binding.StopRecording();
            yield return null;

            Assert.That(binding.LoadRecording(), Is.True);
            Assert.That(binding.LoadedRecordingPath, Is.EqualTo(binding.LastRecordingPath));
        }

        [UnityTest]
        public IEnumerator StartRecording_WhenOutputFileCannotBeCreated_StopsRecordingAndKeepsPreviousTake()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out _, out TestTriggerSource triggerSource, out _);

            Assert.That(binding.StartRecording("kept"), Is.True);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            binding.StopRecording();
            string keptPath = binding.LastRecordingPath;

            // 出力ファイルと同名のディレクトリを置き、ライタースレッドでのオープンを失敗させる。
            Assert.That(RecSidecarPath.TryBuildRecordingFilePath(TestAssetName, "blocked", out string blockedPath, out _), Is.True);
            Directory.CreateDirectory(blockedPath);
            LogAssert.Expect(LogType.Error, new Regex("could not open"));
            LogAssert.Expect(LogType.Warning, new Regex("could not be created"));

            Assert.That(binding.StartRecording("blocked"), Is.True);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (binding.IsRecording && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(binding.IsRecording, Is.False);
            Assert.That(binding.LastRecordingPath, Is.EqualTo(keptPath));
            Assert.That(binding.LastRecordingName, Is.EqualTo("kept"));
        }

        [UnityTest]
        public IEnumerator OnDisable_WhileRecording_FinalizesTheRecordingFile()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out _, out TestTriggerSource triggerSource, out FakeAnalogSource analogSource);

            Assert.That(binding.StartRecording("disable-stop"), Is.True);
            analogSource.Publish(0.1f, -0.2f);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            bus.PublishAnalog(analogSource.Id, 0.1f, -0.2f);

            _gameObject.SetActive(false);
            yield return null;

            Assert.That(binding.IsRecording, Is.False);
            Assert.That(binding.LastRecordingPath, Is.Not.Null.And.Not.Empty);
            Assert.That(RecFileReader.TryRead(binding.LastRecordingPath, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator OnDestroy_WhileRecording_FinalizesTheRecordingFile()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out _, out TestTriggerSource triggerSource, out FakeAnalogSource analogSource);

            Assert.That(binding.StartRecording("destroy-stop"), Is.True);
            analogSource.Publish(0.35f, -0.15f);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            bus.PublishAnalog(analogSource.Id, 0.35f, -0.15f);

            GameObject host = _gameObject;
            _gameObject = null;
            UnityEngine.Object.Destroy(host);
            yield return null;

            // 出力ファイルはライタースレッドが開くため、パスは録画を閉じた後に確定している。
            string recordingPath = binding.LastRecordingPath;
            Assert.That(recordingPath, Is.Not.Null.And.Not.Empty);
            Assert.That(RecFileReader.TryRead(recordingPath, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator StopPlayback_PreservesActiveTriggerAndRestoresOriginalAnalogSource()
        {
            SetupHarness(out _, out RecCharacterBinding binding, out FakeObservationBus bus, out FakeInputSourceRegistry registry, out TestTriggerSource triggerSource, out FakeAnalogSource analogSource);

            Assert.That(binding.StartRecording("stop-preserve"), Is.True);
            analogSource.Publish(0.25f, -0.5f);
            bus.PublishTriggerOn(triggerSource.Id, "smile");
            bus.PublishAnalog(analogSource.Id, 0.25f, -0.5f);
            binding.StopRecording();

            triggerSource.TriggerOff("smile");
            analogSource.Publish(0.9f, -0.1f);

            Assert.That(binding.LoadRecording("stop-preserve"), Is.True);
            Assert.That(binding.StartPlayback(), Is.True);

            yield return null;

            Assert.That(triggerSource.ActiveExpressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(registry.TryResolve(analogSource.Id, out IInputSource injectedSource), Is.True);
            Assert.That(injectedSource, Is.Not.SameAs(analogSource));

            binding.StopPlayback();

            Assert.That(triggerSource.ActiveExpressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(registry.TryResolve(analogSource.Id, out IInputSource restoredSource), Is.True);
            Assert.That(restoredSource, Is.SameAs(analogSource));
            Assert.That(analogSource.TryReadVector2(out float restoredX, out float restoredY), Is.True);
            Assert.That(restoredX, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(restoredY, Is.EqualTo(-0.1f).Within(1e-5f));

            triggerSource.TriggerOff("smile");

            Assert.That(triggerSource.ActiveExpressionIds, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AnalogInjector_EndInjection_WithoutOriginal_UnregistersInjectedSource()
        {
            SetupHarness(out _, out _, out _, out FakeInputSourceRegistry registry, out _, out _);

            var injector = new RecAnalogInjector(registry);
            RecBaselineState baseline = CreateAnalogOnlyBaseline("input:orphan", 0.4f, -0.2f);

            injector.BeginInjection(baseline);
            yield return null;

            Assert.That(registry.TryResolve("input:orphan", out IInputSource injectedSource), Is.True);
            Assert.That(injectedSource, Is.InstanceOf<IInjectedInputSource>());

            injector.EndInjection();
            yield return null;

            Assert.That(registry.TryResolve("input:orphan", out _), Is.False);
        }

        [UnityTest]
        public IEnumerator AnalogInjector_BeginInjection_WhenAnotherInjectedSourceAlreadyOccupiesId_LogsWarningAndSkips()
        {
            SetupHarness(out _, out _, out _, out FakeInputSourceRegistry registry, out _, out _);

            var occupiedSource = new StubInjectedInputSource("input:occupied", replacedSource: null);
            registry.AddSource(occupiedSource);

            var injector = new RecAnalogInjector(registry);
            RecBaselineState baseline = CreateAnalogOnlyBaseline("input:occupied", 0.6f, -0.3f);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("analog.*'input:occupied'"));

            injector.BeginInjection(baseline);
            yield return null;

            Assert.That(registry.TryResolve("input:occupied", out IInputSource resolvedSource), Is.True);
            Assert.That(resolvedSource, Is.SameAs(occupiedSource));
        }

        [UnityTest]
        public IEnumerator AnalogInjector_EndInjection_WhenCurrentEntryWasReplaced_PreservesLaterOccupant()
        {
            SetupHarness(out _, out _, out _, out FakeInputSourceRegistry registry, out _, out _);

            var originalSource = new FakeAnalogSource("input:analog", 2);
            registry.AddSource(originalSource);

            var injectorA = new RecAnalogInjector(registry);
            RecBaselineState baseline = CreateAnalogOnlyBaseline("input:analog", 0.1f, -0.4f);

            injectorA.BeginInjection(baseline);
            Assert.That(registry.TryResolve("input:analog", out IInputSource injectorASource), Is.True);
            Assert.That(injectorASource, Is.InstanceOf<IInjectedInputSource>());

            var injectorBSource = new StubInjectedInputSource("input:analog", originalSource);
            registry.Replace(AdapterSlug.Parse("input"), "analog", injectorBSource);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("analog.*'input:analog'"));

            injectorA.EndInjection();
            yield return null;

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolvedSource), Is.True);
            Assert.That(resolvedSource, Is.SameAs(injectorBSource));
        }

        private void SetupHarness(
            out FacialController controller,
            out RecCharacterBinding binding,
            out FakeObservationBus bus,
            out FakeInputSourceRegistry registry,
            out TestTriggerSource triggerSource,
            out FakeAnalogSource analogSource)
        {
            _gameObject = new GameObject("RecCharacterBindingPlayModeTestsHost");
            _gameObject.AddComponent<Animator>();

            controller = _gameObject.AddComponent<FacialController>();
            binding = _gameObject.AddComponent<RecCharacterBinding>();
            binding.FacialController = controller;

            _characterSo = ScriptableObject.CreateInstance<TestCharacterProfileSO>();
            _characterSo.name = TestAssetName;
            controller.CharacterSO = _characterSo;

            FacialProfile profile = CreateProfile();
            bus = new FakeObservationBus();
            registry = new FakeInputSourceRegistry();
            triggerSource = new TestTriggerSource("input:trigger");
            analogSource = new FakeAnalogSource("input:gaze", 2);
            registry.AddSource(triggerSource);
            registry.AddSource(analogSource);

            SetControllerPrivateField(controller, "_isInitialized", true);
            SetControllerPrivateField(controller, "_currentProfile", (FacialProfile?)profile);
            SetControllerPrivateField(controller, "_inputObservationBus", bus);
            SetControllerPrivateField(controller, "_inputSourceRegistry", registry);
        }

        private static void DeleteGeneratedRecordingAssets()
        {
            string recordingsDirectory = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                TestAssetName,
                RecSidecarPath.RecordingsFolderName);
            if (!Directory.Exists(recordingsDirectory))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(recordingsDirectory))
            {
                File.Delete(filePath);

                string metaFilePath = filePath + ".meta";
                if (File.Exists(metaFilePath))
                {
                    File.Delete(metaFilePath);
                }
            }

            // オープン失敗を再現するために置いた「出力ファイルと同名のディレクトリ」も消す。
            foreach (string directoryPath in Directory.GetDirectories(recordingsDirectory))
            {
                Directory.Delete(directoryPath, true);
            }
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "1.0.0",
                new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                new[] { new Expression("smile", "Smile", "emotion") });
        }

        private static RecBaselineState CreateAnalogOnlyBaseline(string sourceId, params float[] axes)
        {
            return new RecBaselineState(
                Array.Empty<RecBaselineState.TriggerEntry>(),
                new[]
                {
                    new RecBaselineState.AnalogEntry(sourceId, axes),
                });
        }

        /// <summary>
        /// FacialController の初期化済み状態を公開 API だけで再現する手段（CharacterSO からの実初期化を
        /// 伴わない差し替え）が無いため、テスト準備としてのみ private フィールドを注入する。assert は公開 API の観測結果に限る。
        /// </summary>
        private static void SetControllerPrivateField(FacialController controller, string fieldName, object value)
        {
            FieldInfo field = typeof(FacialController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
            field.SetValue(controller, value);
        }

        private sealed class FakeObservationBus : Hidano.FacialControl.Domain.Adapters.IFacialInputObservationBus
        {
            private Hidano.FacialControl.Domain.Adapters.IFacialInputObserver _observer;

            public bool HasObservers => _observer != null;

            public void Subscribe(Hidano.FacialControl.Domain.Adapters.IFacialInputObserver observer)
            {
                _observer = observer;
            }

            public void Unsubscribe(Hidano.FacialControl.Domain.Adapters.IFacialInputObserver observer)
            {
                if (ReferenceEquals(_observer, observer))
                {
                    _observer = null;
                }
            }

            public void OnTriggerOn(string sourceId, string expressionId)
            {
                _observer?.OnTriggerOn(sourceId, expressionId);
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
                _observer?.OnTriggerOff(sourceId, expressionId);
            }

            public void PublishAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                _observer?.OnAnalogSample(sourceId, axes);
            }

            public void PublishTriggerOn(string sourceId, string expressionId)
            {
                OnTriggerOn(sourceId, expressionId);
            }

            public void PublishAnalog(string sourceId, params float[] axes)
            {
                PublishAnalogSample(sourceId, axes);
            }
        }

        private sealed class FakeInputSourceRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _sources = new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<Action<IInputSource>>> _handlers = new Dictionary<string, List<Action<IInputSource>>>(StringComparer.Ordinal);
            private readonly List<string> _registeredIds = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _registeredIds;

            public void AddSource(IInputSource source)
            {
                _sources[source.Id] = source;
                if (!_registeredIds.Contains(source.Id))
                {
                    _registeredIds.Add(source.Id);
                }
            }

            public void Register(AdapterSlug slug, IInputSource source)
            {
                Register(slug, string.Empty, source);
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
                Replace(slug, string.Empty, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                Set(ComposeId(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                Set(ComposeId(slug, sub), source);
            }

            public void Unregister(AdapterSlug slug)
            {
                Unregister(slug, string.Empty);
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
                string id = ComposeId(slug, sub);
                _sources.Remove(id);
                _registeredIds.Remove(id);
                Publish(id, null);
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                return _sources.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
                if (string.IsNullOrEmpty(id) || handler == null)
                {
                    return;
                }

                if (!_handlers.TryGetValue(id, out List<Action<IInputSource>> handlers))
                {
                    handlers = new List<Action<IInputSource>>();
                    _handlers.Add(id, handlers);
                }

                handlers.Add(handler);
            }

            private void Set(string id, IInputSource source)
            {
                _sources[id] = source;
                if (!_registeredIds.Contains(id))
                {
                    _registeredIds.Add(id);
                }

                Publish(id, source);
            }

            private void Publish(string id, IInputSource source)
            {
                if (!_handlers.TryGetValue(id, out List<Action<IInputSource>> handlers))
                {
                    return;
                }

                for (int i = 0; i < handlers.Count; i++)
                {
                    handlers[i]?.Invoke(source);
                }
            }

            private static string ComposeId(AdapterSlug slug, string sub)
            {
                return string.IsNullOrEmpty(sub)
                    ? slug.Value
                    : slug.Value + ":" + sub;
            }
        }

        private sealed class FakeAnalogSource : IInputSource, IAnalogInputSource
        {
            private readonly float[] _axes;

            public FakeAnalogSource(string id, int axisCount)
            {
                Id = id;
                Type = InputSourceType.ValueProvider;
                BlendShapeCount = 0;
                AxisCount = axisCount;
                _axes = new float[axisCount];
                IsValid = true;
            }

            public string Id { get; }

            public InputSourceType Type { get; }

            public int BlendShapeCount { get; }

            public BitArray ContributeMask { get; } = new BitArray(0);

            public bool IsValid { get; private set; }

            public int AxisCount { get; }

            public void Publish(params float[] axes)
            {
                for (int i = 0; i < _axes.Length; i++)
                {
                    _axes[i] = i < axes.Length ? axes[i] : 0f;
                }
            }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }

            public bool TryReadScalar(out float value)
            {
                if (!IsValid || _axes.Length == 0)
                {
                    value = 0f;
                    return false;
                }

                value = _axes[0];
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                if (!IsValid || _axes.Length < 2)
                {
                    x = 0f;
                    y = 0f;
                    return false;
                }

                x = _axes[0];
                y = _axes[1];
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (!IsValid || output.Length < _axes.Length)
                {
                    return false;
                }

                _axes.AsSpan().CopyTo(output);
                return true;
            }
        }

        private sealed class StubInjectedInputSource : IInputSource, IInjectedInputSource
        {
            public StubInjectedInputSource(string id, IInputSource replacedSource)
            {
                Id = id;
                ReplacedSource = replacedSource;
            }

            public string Id { get; }

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public BitArray ContributeMask { get; } = new BitArray(0);

            public IInputSource ReplacedSource { get; }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }
        }

        private sealed class TestTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestTriggerSource(string id)
                : base(
                    InputSourceId.Parse(id),
                    0,
                    4,
                    ExclusionMode.LastWins,
                    Array.Empty<string>(),
                    CreateProfile())
            {
            }

            private static FacialProfile CreateProfile()
            {
                return new FacialProfile(
                    "1.0.0",
                    new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                    new[] { new Expression("smile", "Smile", "emotion") });
            }
        }

        private sealed class TestCharacterProfileSO : FacialCharacterProfileSO
        {
            public override FacialProfile LoadProfile()
            {
                return CreateProfile();
            }
        }
    }
}
