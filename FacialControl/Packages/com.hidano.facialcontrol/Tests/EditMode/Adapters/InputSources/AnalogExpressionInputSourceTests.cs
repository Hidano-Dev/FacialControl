using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="AnalogExpressionInputSource"/> の契約テスト。
    /// 構築時に解決した analog source を、<see cref="IRegistryAttachableAnalogConsumer"/> 経由で接続した
    /// registry の Register / Replace / Unregister 通知に追従して差し替えることを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AnalogExpressionInputSourceTests : SizedTestFixture
    {
        private const string SlugText = "pad";
        private const string SourceId = "lt";
        private const string ExpressionId = "smile";
        private const float OriginalValue = 0.2f;

        private static readonly string[] BlendShapeNames = { "MouthSmile", "CheekPuff", "JawOpen" };

        private FakeScalarSource _original;
        private FakeRegistry _registry;
        private AdapterSlug _slug;

        [SetUp]
        public void SetUp()
        {
            _original = new FakeScalarSource(SourceId, OriginalValue);
            _registry = new FakeRegistry();
            _slug = AdapterSlug.Parse(SlugText);
        }

        #region 契約型

        [Test]
        public void Type_ImplementsRegistryAttachableAnalogConsumer()
        {
            Assert.That(
                typeof(IRegistryAttachableAnalogConsumer).IsAssignableFrom(typeof(AnalogExpressionInputSource)),
                Is.True,
                "AnalogExpressionInputSource は IRegistryAttachableAnalogConsumer を実装する必要がある。");
        }

        #endregion

        #region Attach 前

        [Test]
        public void TryWriteValues_WithoutAttach_UsesConstructedSource()
        {
            AnalogExpressionInputSource sut = Build();

            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertOutput(sut, OriginalValue);
        }

        #endregion

        #region Replace 追従

        [Test]
        public void AttachRegistry_ThenReplaceWithAnalogSource_WritesReplacedSourceValue()
        {
            AnalogExpressionInputSource sut = Build();
            bool[] maskBefore = SnapshotMask(sut);

            sut.AttachRegistry(_registry, _slug);
            _registry.Replace(_slug, SourceId, new FakeScalarSource("timeline-lt", 0.8f));

            Assert.That(sut.IsRegistryAttached, Is.True);
            AssertOutput(sut, 0.8f);
            CollectionAssert.AreEqual(maskBefore, SnapshotMask(sut), "ContributeMask は差し替えで変わらない。");
        }

        [Test]
        public void AttachRegistry_ThenUnregister_RestoresConstructedSource()
        {
            AnalogExpressionInputSource sut = Build();
            sut.AttachRegistry(_registry, _slug);
            _registry.Replace(_slug, SourceId, new FakeScalarSource("timeline-lt", 0.8f));
            AssertOutput(sut, 0.8f);

            _registry.Unregister(_slug, SourceId);

            AssertOutput(sut, OriginalValue);
        }

        [Test]
        public void AttachRegistry_ThenReplaceWithNonAnalogSource_IgnoresNotification()
        {
            AnalogExpressionInputSource sut = Build();
            sut.AttachRegistry(_registry, _slug);

            _registry.Replace(_slug, SourceId, new FakeNonAnalogSource("plain"));

            AssertOutput(sut, OriginalValue);
        }

        [Test]
        public void AttachRegistry_OtherIdReplaced_DoesNotAffectConsumer()
        {
            AnalogExpressionInputSource sut = Build();
            sut.AttachRegistry(_registry, _slug);

            _registry.Replace(_slug, "rt", new FakeScalarSource("timeline-rt", 0.9f));
            _registry.Replace(AdapterSlug.Parse("other"), SourceId, new FakeScalarSource("other-lt", 0.9f));

            AssertOutput(sut, OriginalValue);
        }

        #endregion

        #region 冪等 / Detach

        [Test]
        public void AttachRegistry_CalledTwiceWithSameRegistry_DoesNotSubscribeAgain()
        {
            AnalogExpressionInputSource sut = Build();

            sut.AttachRegistry(_registry, _slug);
            int afterFirst = _registry.SubscribeCount;
            sut.AttachRegistry(_registry, _slug);

            Assert.That(afterFirst, Is.EqualTo(1), "解決済み binding 1 件につき購読は 1 回。");
            Assert.That(_registry.SubscribeCount, Is.EqualTo(afterFirst), "同じ registry への 2 回目の Attach は no-op。");
            Assert.That(sut.IsRegistryAttached, Is.True);
        }

        [Test]
        public void AttachRegistry_DifferentRegistry_FollowsOnlyNewRegistry()
        {
            AnalogExpressionInputSource sut = Build();
            var second = new FakeRegistry();
            sut.AttachRegistry(_registry, _slug);

            sut.AttachRegistry(second, _slug);
            _registry.Replace(_slug, SourceId, new FakeScalarSource("old-registry", 0.7f));
            AssertOutput(sut, OriginalValue);

            second.Replace(_slug, SourceId, new FakeScalarSource("new-registry", 0.6f));
            AssertOutput(sut, 0.6f);
        }

        [Test]
        public void DetachRegistry_AfterReplace_RestoresConstructedSourceAndIgnoresLaterNotifications()
        {
            AnalogExpressionInputSource sut = Build();
            sut.AttachRegistry(_registry, _slug);
            _registry.Replace(_slug, SourceId, new FakeScalarSource("timeline-lt", 0.8f));
            AssertOutput(sut, 0.8f);

            sut.DetachRegistry();

            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertOutput(sut, OriginalValue);

            _registry.Replace(_slug, SourceId, new FakeScalarSource("timeline-lt-2", 0.9f));
            AssertOutput(sut, OriginalValue);
        }

        [Test]
        public void DetachRegistry_WithoutAttach_IsNoOp()
        {
            AnalogExpressionInputSource sut = Build();

            Assert.DoesNotThrow(() => sut.DetachRegistry());
            Assert.That(sut.IsRegistryAttached, Is.False);
            AssertOutput(sut, OriginalValue);
        }

        [Test]
        public void AttachRegistry_AfterDetach_SubscribesAgainAndFollowsReplace()
        {
            AnalogExpressionInputSource sut = Build();
            sut.AttachRegistry(_registry, _slug);
            sut.DetachRegistry();

            sut.AttachRegistry(_registry, _slug);
            _registry.Replace(_slug, SourceId, new FakeScalarSource("timeline-lt", 0.5f));

            Assert.That(_registry.SubscribeCount, Is.EqualTo(2));
            AssertOutput(sut, 0.5f);
        }

        [Test]
        public void AttachRegistry_NullRegistry_Throws()
        {
            AnalogExpressionInputSource sut = Build();

            Assert.Throws<ArgumentNullException>(() => sut.AttachRegistry(null, _slug));
        }

        #endregion

        #region ヘルパー / フェイク

        private AnalogExpressionInputSource Build()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var smile = new Expression(
                id: ExpressionId,
                name: "Smile",
                layer: "emotion",
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("MouthSmile", 1f),
                    new BlendShapeMapping("CheekPuff", 0.5f),
                });
            var profile = new FacialProfile("1.0", layers, new[] { smile });

            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { SourceId, _original },
            };
            var bindings = new[]
            {
                new AnalogExpressionBinding(SourceId, 0, ExpressionId, 1f),
            };

            return new AnalogExpressionInputSource(
                InputSourceId.Parse(AnalogExpressionInputSource.ReservedId),
                BlendShapeNames.Length,
                BlendShapeNames,
                profile,
                sources,
                bindings);
        }

        private static void AssertOutput(AnalogExpressionInputSource sut, float scalar)
        {
            Span<float> output = stackalloc float[BlendShapeNames.Length];
            output.Clear();

            bool wrote = sut.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output[0], Is.EqualTo(scalar * 1f).Within(1e-6f), "MouthSmile = scalar x 1.0");
            Assert.That(output[1], Is.EqualTo(scalar * 0.5f).Within(1e-6f), "CheekPuff = scalar x 0.5");
            Assert.That(output[2], Is.EqualTo(0f).Within(1e-6f), "JawOpen は Expression に含まれない。");
        }

        private static bool[] SnapshotMask(AnalogExpressionInputSource sut)
        {
            var mask = new bool[sut.ContributeMask.Length];
            sut.ContributeMask.CopyTo(mask, 0);
            return mask;
        }

        /// <summary>
        /// 値を外から差し替えられる 1 軸フェイク。registry に登録できるよう
        /// <see cref="IInputSource"/> と <see cref="IAnalogInputSource"/> の両方を実装する（InputSystem の wrapper と同形）。
        /// </summary>
        private sealed class FakeScalarSource : IInputSource, IAnalogInputSource
        {
            public FakeScalarSource(string id, float value)
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
