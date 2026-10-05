using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
#if FACIALCONTROL_HAS_OSC_MODULE
using UnityEngine;
using Hidano.FacialControl.Adapters.OSC;
#endif

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="AnalogBlendShapeInputSource"/> の契約テスト。
    /// <see cref="AnalogBindingDirection"/> / <see cref="AnalogBindingEntry.Scale"/> による
    /// 1 軸入力の振り分け（gaze 4 系統 LookLeft / LookRight / LookUp / LookDown）と、
    /// binding map から導出される <c>ContributeMask</c> が束縛先 BlendShape index 集合と一致することを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AnalogBlendShapeInputSourceTests : SizedTestFixture
    {
#if FACIALCONTROL_HAS_OSC_MODULE
        private GameObject _receiverObject;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }

            if (_receiverObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverObject);
                _receiverObject = null;
            }

            _receiver = null;
        }
#endif

        #region Direction / Scale 適用

        [Test]
        public void TryWriteValues_BipolarDirection_AddsRawValueDirectly()
        {
            var fake = new FakeVector2Source("gaze", x: 0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "EyeLook", AnalogTargetAxis.X);
            var src = BuildSourceWithFake(fake, new[] { "EyeLook" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            bool wrote = src.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.5f, output[0], 1e-6f, "Bipolar の場合は raw 値が無加工で加算される (Scale=1, default ctor)。");
        }

        [Test]
        public void TryWriteValues_BipolarDirectionNegativeRaw_AddsNegativeValue()
        {
            var fake = new FakeVector2Source("gaze", x: -0.7f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "EyeLook", AnalogTargetAxis.X);
            var src = BuildSourceWithFake(fake, new[] { "EyeLook" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(-0.7f, output[0], 1e-6f, "Bipolar は符号もそのまま反映する。");
        }

        [Test]
        public void TryWriteValues_PositiveDirectionNegativeRaw_AddsZero()
        {
            var fake = new FakeVector2Source("gaze", x: -0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X,
                scale: 1f, direction: AnalogBindingDirection.Positive);
            var src = BuildSourceWithFake(fake, new[] { "LookRight" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "Positive direction は raw < 0 を無視して 0 を加算する。");
        }

        [Test]
        public void TryWriteValues_PositiveDirectionPositiveRaw_AppliesScale()
        {
            var fake = new FakeVector2Source("gaze", x: 0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X,
                scale: 80f, direction: AnalogBindingDirection.Positive);
            var src = BuildSourceWithFake(fake, new[] { "LookRight" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(40f, output[0], 1e-5f, "Positive direction は raw>0 のとき raw * scale (= 0.5 * 80 = 40)。");
        }

        [Test]
        public void TryWriteValues_NegativeDirectionNegativeRaw_AppliesAbsoluteScale()
        {
            var fake = new FakeVector2Source("gaze", x: -0.25f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X,
                scale: 100f, direction: AnalogBindingDirection.Negative);
            var src = BuildSourceWithFake(fake, new[] { "LookLeft" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(25f, output[0], 1e-5f, "Negative direction は raw<0 のとき |raw| * scale (= 0.25 * 100 = 25)。");
        }

        [Test]
        public void TryWriteValues_NegativeDirectionPositiveRaw_AddsZero()
        {
            var fake = new FakeVector2Source("gaze", x: 0.9f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X,
                scale: 100f, direction: AnalogBindingDirection.Negative);
            var src = BuildSourceWithFake(fake, new[] { "LookLeft" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "Negative direction は raw>0 を無視して 0 を加算する。");
        }

        [Test]
        public void TryWriteValues_FourDirectionsVectorXPositive_DrivesOnlyLookRight()
        {
            // Gaze 4 系統相当: input.x=+0.6, input.y=0 → LookRight だけが反応すべき。
            var fake = new FakeVector2Source("gaze", x: 0.6f, y: 0f);
            var bsNames = new[] { "LookLeft", "LookRight", "LookUp", "LookDown" };
            var bindings = new[]
            {
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookUp", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookDown", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
            };
            var src = BuildSourceWithFake(fake, bsNames, bindings);

            Span<float> output = stackalloc float[bsNames.Length];
            for (int i = 0; i < output.Length; i++) output[i] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "LookLeft は raw>0 で反応しない。");
            Assert.AreEqual(60f, output[1], 1e-5f, "LookRight は 0.6 * 100 = 60。");
            Assert.AreEqual(0f, output[2], 1e-6f, "LookUp は input.y=0 で反応しない。");
            Assert.AreEqual(0f, output[3], 1e-6f, "LookDown は input.y=0 で反応しない。");
        }

        [Test]
        public void TryWriteValues_FourDirectionsVectorYNegative_DrivesOnlyLookDown()
        {
            var fake = new FakeVector2Source("gaze", x: 0f, y: -0.4f);
            var bsNames = new[] { "LookLeft", "LookRight", "LookUp", "LookDown" };
            var bindings = new[]
            {
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookUp", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookDown", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
            };
            var src = BuildSourceWithFake(fake, bsNames, bindings);

            Span<float> output = stackalloc float[bsNames.Length];
            for (int i = 0; i < output.Length; i++) output[i] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.AreEqual(0f, output[2], 1e-6f, "LookUp は raw<0 で反応しない。");
            Assert.AreEqual(40f, output[3], 1e-5f, "LookDown は |raw| * scale = 0.4 * 100 = 40。");
        }

        #endregion

        #region ContributeMask（binding map 由来）

        [Test]
        public void ContributeMask_BindingMapBlendShapeIndexes_MatchesTrueIndexSet()
        {
            var source = new FixedAnalogSource("manual", axisCount: 2);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { source.Id, source },
            };
            var blendShapeNames = new[]
            {
                "BrowInnerUp",
                "EyeBlinkLeft",
                "JawOpen",
                "MouthSmileLeft",
                "CheekPuff",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 1, AnalogBindingTargetKind.BlendShape, "EyeBlinkLeft", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BonePose, "Head", AnalogTargetAxis.Y),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 1, 2);
        }

#if FACIALCONTROL_HAS_OSC_MODULE
        [Test]
        public void ContributeMask_OscScalarBindingScenario_MatchesBoundBlendShapeIndexes()
        {
            OscReceiver receiver = CreateReceiver();
            using var oscSource = new OscFloatAnalogSource(
                InputSourceId.Parse("osc-jaw"),
                receiver,
                "/avatar/parameters/JawOpen",
                stalenessSeconds: 0f);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { oscSource.Id, oscSource },
            };
            var blendShapeNames = new[]
            {
                "JawOpen",
                "MouthFunnel",
                "EyeBlinkLeft",
                "MouthPucker",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("osc-jaw", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("osc-jaw", 0, AnalogBindingTargetKind.BlendShape, "MouthPucker", AnalogTargetAxis.X),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 0, 3);
        }

        [Test]
        public void ContributeMask_ArKitBindingScenario_MatchesBoundBlendShapeIndexes()
        {
            OscReceiver receiver = CreateReceiver();
            var arkitNames = new[] { "jawOpen", "eyeBlinkLeft", "mouthSmileLeft", "browInnerUp" };
            using var arkitSource = new ArKitOscAnalogSource(
                InputSourceId.Parse("arkit"),
                receiver,
                arkitNames,
                stalenessSeconds: 0f);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { arkitSource.Id, arkitSource },
            };
            var blendShapeNames = new[]
            {
                "JawOpen",
                "EyeBlinkLeft",
                "MouthSmileLeft",
                "BrowInnerUp",
                "NoseSneerLeft",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("arkit", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("arkit", 2, AnalogBindingTargetKind.BlendShape, "MouthSmileLeft", AnalogTargetAxis.X),
                new AnalogBindingEntry("arkit", 3, AnalogBindingTargetKind.BlendShape, "BrowInnerUp", AnalogTargetAxis.X),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 0, 2, 3);
        }

        private OscReceiver CreateReceiver()
        {
            _receiverObject = new GameObject("AnalogBlendShapeInputSourceTests_OscReceiver");
            _receiver = _receiverObject.AddComponent<OscReceiver>();
            _buffer = new OscDoubleBuffer(0);
            _receiver.Initialize(_buffer, Array.Empty<OscMapping>());
            return _receiver;
        }
#endif

        private static void AssertMaskMatchesIndexes(BitArray mask, int blendShapeCount, params int[] expectedTrueIndexes)
        {
            Assert.That(mask, Is.Not.Null,
                "AnalogBlendShapeInputSource は binding map 由来の ContributeMask を公開する必要がある。");
            Assert.That(mask.Length, Is.EqualTo(blendShapeCount),
                "ContributeMask.Length は BlendShapeCount と一致する必要がある。");

            var expected = new bool[blendShapeCount];
            for (int i = 0; i < expectedTrueIndexes.Length; i++)
            {
                expected[expectedTrueIndexes[i]] = true;
            }

            for (int i = 0; i < blendShapeCount; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]),
                    $"ContributeMask[{i}] は binding map の BlendShape index 集合と一致する必要がある。");
            }
        }

        #endregion

        #region registry 再解決（IRegistryAttachableAnalogConsumer）

        private const string ReattachSlugText = "pad";
        private const string ReattachSourceId = "lt";
        private const float ReattachOriginalValue = 0.2f;
        private static readonly string[] ReattachBlendShapeNames = { "JawOpen", "MouthFunnel" };

        [Test]
        public void Type_ImplementsRegistryAttachableAnalogConsumer()
        {
            Assert.That(
                typeof(IRegistryAttachableAnalogConsumer).IsAssignableFrom(typeof(AnalogBlendShapeInputSource)),
                Is.True,
                "AnalogBlendShapeInputSource は IRegistryAttachableAnalogConsumer を実装する必要がある。");
        }

        [Test]
        public void TryWriteValues_WithoutAttach_UsesConstructedSource()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);

            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertReattachOutput(sut, ReattachOriginalValue);
        }

        [Test]
        public void AttachRegistry_ThenReplaceWithAnalogSource_WritesReplacedSourceValue()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);
            var registry = new FakeRegistry();
            AdapterSlug slug = AdapterSlug.Parse(ReattachSlugText);
            var maskBefore = new bool[sut.ContributeMask.Length];
            sut.ContributeMask.CopyTo(maskBefore, 0);

            sut.AttachRegistry(registry, slug);
            registry.Replace(slug, ReattachSourceId, new RegistrableScalarSource("timeline-lt", 0.8f));

            Assert.That(sut.IsRegistryAttached, Is.True);
            AssertReattachOutput(sut, 0.8f);
            var maskAfter = new bool[sut.ContributeMask.Length];
            sut.ContributeMask.CopyTo(maskAfter, 0);
            CollectionAssert.AreEqual(maskBefore, maskAfter, "ContributeMask は差し替えで変わらない。");
        }

        [Test]
        public void AttachRegistry_ThenUnregister_RestoresConstructedSource()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);
            var registry = new FakeRegistry();
            AdapterSlug slug = AdapterSlug.Parse(ReattachSlugText);
            sut.AttachRegistry(registry, slug);
            registry.Replace(slug, ReattachSourceId, new RegistrableScalarSource("timeline-lt", 0.8f));
            AssertReattachOutput(sut, 0.8f);

            registry.Unregister(slug, ReattachSourceId);

            AssertReattachOutput(sut, ReattachOriginalValue);
        }

        [Test]
        public void AttachRegistry_ThenReplaceWithNonAnalogSource_IgnoresNotification()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);
            var registry = new FakeRegistry();
            AdapterSlug slug = AdapterSlug.Parse(ReattachSlugText);
            sut.AttachRegistry(registry, slug);

            registry.Replace(slug, ReattachSourceId, new FakeNonAnalogSource("plain"));

            AssertReattachOutput(sut, ReattachOriginalValue);
        }

        [Test]
        public void AttachRegistry_CalledTwiceWithSameRegistry_DoesNotSubscribeAgain()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);
            var registry = new FakeRegistry();
            AdapterSlug slug = AdapterSlug.Parse(ReattachSlugText);

            sut.AttachRegistry(registry, slug);
            int afterFirst = registry.SubscribeCount;
            sut.AttachRegistry(registry, slug);

            Assert.That(afterFirst, Is.EqualTo(1), "解決済み binding 1 件につき購読は 1 回。");
            Assert.That(registry.SubscribeCount, Is.EqualTo(afterFirst), "同じ registry への 2 回目の Attach は no-op。");
        }

        [Test]
        public void DetachRegistry_AfterReplace_RestoresConstructedSourceAndIgnoresLaterNotifications()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);
            var registry = new FakeRegistry();
            AdapterSlug slug = AdapterSlug.Parse(ReattachSlugText);
            sut.AttachRegistry(registry, slug);
            registry.Replace(slug, ReattachSourceId, new RegistrableScalarSource("timeline-lt", 0.8f));
            AssertReattachOutput(sut, 0.8f);

            sut.DetachRegistry();

            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertReattachOutput(sut, ReattachOriginalValue);

            registry.Replace(slug, ReattachSourceId, new RegistrableScalarSource("timeline-lt-2", 0.9f));
            AssertReattachOutput(sut, ReattachOriginalValue);
        }

        [Test]
        public void DetachRegistry_WithoutAttach_IsNoOp()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);

            Assert.DoesNotThrow(() => sut.DetachRegistry());
            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertReattachOutput(sut, ReattachOriginalValue);
        }

        [Test]
        public void AttachRegistry_NullRegistry_Throws()
        {
            AnalogBlendShapeInputSource sut = BuildReattachTarget(out _);

            Assert.Throws<ArgumentNullException>(() => sut.AttachRegistry(null, AdapterSlug.Parse(ReattachSlugText)));
        }

        /// <summary>`lt` 1 軸 → JawOpen（Bipolar, scale 1）だけを束縛した対象を構築する。</summary>
        private static AnalogBlendShapeInputSource BuildReattachTarget(out RegistrableScalarSource original)
        {
            original = new RegistrableScalarSource(ReattachSourceId, ReattachOriginalValue);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { original.Id, original },
            };
            var bindings = new[]
            {
                new AnalogBindingEntry(ReattachSourceId, 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
            };
            return BuildSource(ReattachBlendShapeNames, sources, bindings);
        }

        private static void AssertReattachOutput(AnalogBlendShapeInputSource sut, float expected)
        {
            Span<float> output = stackalloc float[ReattachBlendShapeNames.Length];
            output.Clear();

            bool wrote = sut.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output[0], Is.EqualTo(expected).Within(1e-6f), "JawOpen = 読む先 source の値 x scale 1。");
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-6f), "MouthFunnel は束縛されていない。");
        }

        #endregion

        #region ヘルパー / フェイク

        /// <summary>単一のフェイク source だけを登録した <see cref="AnalogBlendShapeInputSource"/> を構築する。</summary>
        private static AnalogBlendShapeInputSource BuildSourceWithFake(
            FakeVector2Source fakeSource,
            IReadOnlyList<string> blendShapeNames,
            params AnalogBindingEntry[] bindings)
        {
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { fakeSource.Id, fakeSource },
            };
            return BuildSource(blendShapeNames, sources, bindings);
        }

        private static AnalogBlendShapeInputSource BuildSource(
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyDictionary<string, IAnalogInputSource> sources,
            IReadOnlyList<AnalogBindingEntry> bindings)
        {
            return new AnalogBlendShapeInputSource(
                InputSourceId.Parse(AnalogBlendShapeInputSource.ReservedId),
                blendShapeNames.Count,
                blendShapeNames,
                sources,
                bindings);
        }

        /// <summary>固定値を返すフェイク <see cref="IAnalogInputSource"/>。Vector2 想定。</summary>
        private sealed class FakeVector2Source : IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;
            public FakeVector2Source(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
            }

            public string Id { get; }
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length >= 1) output[0] = _x;
                if (output.Length >= 2) output[1] = _y;
                return true;
            }
        }

        /// <summary>全軸 1.0 を返すフェイク <see cref="IAnalogInputSource"/>。軸数は可変。</summary>
        private sealed class FixedAnalogSource : IAnalogInputSource
        {
            public FixedAnalogSource(string id, int axisCount)
            {
                Id = id;
                AxisCount = axisCount;
            }

            public string Id { get; }
            public bool IsValid => true;
            public int AxisCount { get; }

            public void Tick(float deltaTime) { }

            public bool TryReadScalar(out float value)
            {
                value = 1f;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = 1f;
                y = 1f;
                return AxisCount >= 2;
            }

            public bool TryReadAxes(Span<float> output)
            {
                int copyLength = output.Length < AxisCount ? output.Length : AxisCount;
                for (int i = 0; i < copyLength; i++)
                {
                    output[i] = 1f;
                }
                return copyLength > 0;
            }
        }

        /// <summary>
        /// registry に登録できる 1 軸フェイク。<see cref="IInputSource"/> と <see cref="IAnalogInputSource"/> の両方を実装する
        /// （InputSystem の wrapper と同形）。
        /// </summary>
        private sealed class RegistrableScalarSource : IInputSource, IAnalogInputSource
        {
            public RegistrableScalarSource(string id, float value)
            {
                Id = id;
                Value = value;
            }

            public string Id { get; }
            public float Value { get; set; }
            public bool IsValid => true;
            public int AxisCount => 1;
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; } = new BitArray(0);
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;

            public bool TryReadScalar(out float value)
            {
                value = Value;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = Value;
                y = 0f;
                return false;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length >= 1) output[0] = Value;
                return output.Length >= 1;
            }
        }

        /// <summary><see cref="IAnalogInputSource"/> ではない <see cref="IInputSource"/>。</summary>
        private sealed class FakeNonAnalogSource : IInputSource
        {
            public FakeNonAnalogSource(string id)
            {
                Id = id;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; } = new BitArray(0);
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
        }

        /// <summary>
        /// Subscribe 回数を記録し、Register / Replace / Unregister で購読者へ同期通知する最小 registry。
        /// 実 <see cref="InputSourceRegistry"/> と同じ通知契約（Register / Replace は新 source、Unregister は null）。
        /// </summary>
        private sealed class FakeRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<Action<IInputSource>>> _handlers =
                new Dictionary<string, List<Action<IInputSource>>>(StringComparer.Ordinal);
            private readonly List<string> _ids = new List<string>();

            public int SubscribeCount { get; private set; }

            public IReadOnlyList<string> RegisteredIds => _ids;

            public void Register(AdapterSlug slug, IInputSource source) => Set(slug.Value, source);

            public void Replace(AdapterSlug slug, IInputSource source) => Set(slug.Value, source);

            public void Register(AdapterSlug slug, string sub, IInputSource source) => Set(slug.Value + ":" + sub, source);

            public void Replace(AdapterSlug slug, string sub, IInputSource source) => Set(slug.Value + ":" + sub, source);

            public void Unregister(AdapterSlug slug) => Remove(slug.Value);

            public void Unregister(AdapterSlug slug, string sub) => Remove(slug.Value + ":" + sub);

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                if (string.IsNullOrEmpty(layerInputSourceId))
                {
                    source = null;
                    return false;
                }

                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
                if (string.IsNullOrEmpty(id) || handler == null)
                {
                    return;
                }

                SubscribeCount++;
                if (!_handlers.TryGetValue(id, out List<Action<IInputSource>> list))
                {
                    list = new List<Action<IInputSource>>();
                    _handlers[id] = list;
                }

                list.Add(handler);
            }

            private void Set(string key, IInputSource source)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (!_entries.ContainsKey(key)) _ids.Add(key);
                _entries[key] = source;
                Notify(key, source);
            }

            private void Remove(string key)
            {
                if (!_entries.Remove(key)) return;
                _ids.Remove(key);
                Notify(key, null);
            }

            private void Notify(string key, IInputSource source)
            {
                if (!_handlers.TryGetValue(key, out List<Action<IInputSource>> list)) return;
                for (int i = 0; i < list.Count; i++)
                {
                    list[i](source);
                }
            }
        }

        #endregion
    }
}
