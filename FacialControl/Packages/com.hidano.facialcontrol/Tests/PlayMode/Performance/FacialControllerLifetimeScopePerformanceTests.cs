using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using VContainer;
using VContainer.Unity;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public class FacialControllerLifetimeScopePerformanceTests : SizedTestFixture
    {
        private const int BindingsPerCharacter = 3;
        private const int SourcesPerCharacter = 3;
        private const int LargeCharacterCount = 10;
        private const long OneMillisecondNanoseconds = 1_000_000L;

        private readonly List<FacialControllerLifetimeScope> _scopes =
            new List<FacialControllerLifetimeScope>();
        private readonly List<GameObject> _hostGameObjects = new List<GameObject>();

        private TestAppLifetimeScope _appScope;
        private GameObject _appScopeGameObject;

        [SetUp]
        public void SetUp()
        {
            _appScopeGameObject = new GameObject("FacialControllerLifetimeScopePerformanceTestsAppScope");
            _appScopeGameObject.SetActive(false);
            _appScope = _appScopeGameObject.AddComponent<TestAppLifetimeScope>();
            _appScope.autoRun = false;
            _appScopeGameObject.SetActive(true);
            _appScope.Build();

            // Warm up VContainer, ProfilerRecorder marker handles, and test binding source setup.
            ScopeMeasurement warmup = MeasureScenario(characterCount: 1);
            Assert.That(warmup.BuildMarkerCount, Is.EqualTo(1));
            Assert.That(warmup.DisposeMarkerCount, Is.EqualTo(1));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scopes.Count; i++)
            {
                _scopes[i]?.Dispose();
            }
            _scopes.Clear();

            if (_appScope != null)
            {
                _appScope.DisposeCore();
                _appScope = null;
            }

            if (_appScopeGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_appScopeGameObject);
                _appScopeGameObject = null;
            }

            for (int i = 0; i < _hostGameObjects.Count; i++)
            {
                if (_hostGameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_hostGameObjects[i]);
                }
            }
            _hostGameObjects.Clear();
        }

        [Test]
        public void BuildAndDispose_TenCharactersThreeBindings_ProfilerMarkersStayLinear()
        {
            ScopeMeasurement oneCharacter = MeasureScenario(characterCount: 1);
            ScopeMeasurement fiveCharacters = MeasureScenario(characterCount: 5);
            ScopeMeasurement tenCharacters = MeasureScenario(characterCount: LargeCharacterCount);

            AssertMarkerInvocationCount(oneCharacter);
            AssertMarkerInvocationCount(fiveCharacters);
            AssertMarkerInvocationCount(tenCharacters);

            Assert.That(tenCharacters.BuildAverageNanoseconds, Is.LessThan(OneMillisecondNanoseconds),
                $"Per-FC child scope build exceeded 1ms: {tenCharacters.BuildAverageMilliseconds:F3}ms " +
                $"({LargeCharacterCount} characters, {BindingsPerCharacter} bindings, {SourcesPerCharacter} sources)");

            Assert.That(tenCharacters.DisposeAverageNanoseconds, Is.LessThan(OneMillisecondNanoseconds),
                $"Per-FC child scope dispose exceeded 1ms: {tenCharacters.DisposeAverageMilliseconds:F3}ms " +
                $"({LargeCharacterCount} characters, {BindingsPerCharacter} bindings, {SourcesPerCharacter} sources)");
        }

        private ScopeMeasurement MeasureScenario(int characterCount)
        {
            var profile = new FacialProfile("2.0");
            var blendShapeNames = new[] { "Blink", "Smile", "MouthOpen" };
            var scenarioScopes = new FacialControllerLifetimeScope[characterCount];

            ProfilerRecorderSample buildSample;
            using (ProfilerRecorder recorder = StartMarkerRecorder(
                FacialControllerLifetimeScope.BuildProfilerMarkerName))
            {
                for (int character = 0; character < characterCount; character++)
                {
                    var host = new GameObject(
                        "FacialControllerLifetimeScopePerformanceTestsHost_" + characterCount + "_" + character);
                    _hostGameObjects.Add(host);

                    AdapterBindingBase[] bindings = CreateBindings(character);
                    scenarioScopes[character] = FacialControllerLifetimeScope.Build(
                        _appScope,
                        profile,
                        blendShapeNames,
                        bindings,
                        host,
                        "FacialControllerLifetimeScopePerformanceTestsChild_" + characterCount + "_" + character);
                    _scopes.Add(scenarioScopes[character]);
                }

                recorder.Stop();
                buildSample = ReadMarkerSample(recorder, "build", characterCount);
            }

            ProfilerRecorderSample disposeSample;
            using (ProfilerRecorder recorder = StartMarkerRecorder(
                FacialControllerLifetimeScope.DisposeProfilerMarkerName))
            {
                for (int character = 0; character < scenarioScopes.Length; character++)
                {
                    scenarioScopes[character].Dispose();
                }

                recorder.Stop();
                disposeSample = ReadMarkerSample(recorder, "dispose", characterCount);
            }

            return new ScopeMeasurement(characterCount, buildSample, disposeSample);
        }

        private static AdapterBindingBase[] CreateBindings(int characterIndex)
        {
            var bindings = new AdapterBindingBase[BindingsPerCharacter];
            for (int binding = 0; binding < bindings.Length; binding++)
            {
                bindings[binding] = new RegisteringAdapterBinding
                {
                    Slug = "scope-perf-" + characterIndex + "-" + binding
                };
            }

            return bindings;
        }

        private static ProfilerRecorder StartMarkerRecorder(string markerName)
        {
            return ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                markerName,
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        }

        private static ProfilerRecorderSample ReadMarkerSample(
            ProfilerRecorder recorder,
            string phase,
            int expectedMarkerCount)
        {
            Assert.That(recorder.Count, Is.GreaterThan(0),
                $"ProfilerRecorder did not capture the {phase} marker for {expectedMarkerCount} FCs.");

            return recorder.GetSample(0);
        }

        private static void AssertMarkerInvocationCount(ScopeMeasurement measurement)
        {
            Assert.That(measurement.BuildMarkerCount, Is.EqualTo(measurement.CharacterCount),
                "Build marker count must match per-FC scope build invocations.");
            Assert.That(measurement.DisposeMarkerCount, Is.EqualTo(measurement.CharacterCount),
                "Dispose marker count must match per-FC scope dispose invocations.");
        }

        private sealed class TestAppLifetimeScope : LifetimeScope
        {
            protected override void Configure(IContainerBuilder builder)
            {
                builder.RegisterInstance<ITimeProvider>(new ManualTimeProvider());
            }
        }

        [Serializable]
        private sealed class RegisteringAdapterBinding : AdapterBindingBase
        {
            public override void OnStart(in AdapterBuildContext ctx)
            {
                ctx.InputSourceRegistry.Register(
                    AdapterSlug.Parse(Slug),
                    new FixedValueInputSource(Slug));
            }
        }

        private sealed class FixedValueInputSource : IInputSource
        {
            public FixedValueInputSource(string id)
            {
                Id = id;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(BlendShapeCount);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 3;
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                int count = output.Length < BlendShapeCount ? output.Length : BlendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    output[i] = 0.25f;
                }

                return true;
            }
        }

        private readonly struct ScopeMeasurement
        {
            public readonly int CharacterCount;
            public readonly long BuildTotalNanoseconds;
            public readonly long DisposeTotalNanoseconds;
            public readonly long BuildMarkerCount;
            public readonly long DisposeMarkerCount;

            public ScopeMeasurement(
                int characterCount,
                ProfilerRecorderSample buildSample,
                ProfilerRecorderSample disposeSample)
            {
                CharacterCount = characterCount;
                BuildTotalNanoseconds = buildSample.Value;
                DisposeTotalNanoseconds = disposeSample.Value;
                BuildMarkerCount = buildSample.Count;
                DisposeMarkerCount = disposeSample.Count;
            }

            public long BuildAverageNanoseconds => BuildTotalNanoseconds / CharacterCount;
            public long DisposeAverageNanoseconds => DisposeTotalNanoseconds / CharacterCount;
            public double BuildAverageMilliseconds => BuildAverageNanoseconds * 1e-6;
            public double DisposeAverageMilliseconds => DisposeAverageNanoseconds * 1e-6;
        }
    }
}
