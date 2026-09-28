using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// LayerInputSourceRegistry の初期化・プール確保・基本アクセサのテスト 。
    /// </summary>
    /// <remarks>
    /// 観測完了条件: 3 layer × 2 source × 200 blendShape の Registry を構築すると、
    /// 各 (layer, source) の scratch が連続・非重複・固定アドレスで取得でき、
    /// <see cref="LayerInputSourceRegistry.Dispose"/> で内部バッファが解放されること。
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class LayerInputSourceRegistryTests : SizedTestFixture
    {
        private sealed class FakeInputSource : IInputSource
        {
            public FakeInputSource(string id, InputSourceType type, int blendShapeCount)
            {
                Id = id;
                Type = type;
                BlendShapeCount = blendShapeCount;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(blendShapeCount);
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
        }

        private static FacialProfile BuildProfile(int layerCount)
        {
            var layers = new LayerDefinition[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new LayerDefinition($"layer{i}", priority: i, ExclusionMode.LastWins);
            }
            return new FacialProfile("1.0", layers: layers);
        }

        private static IReadOnlyList<(int layerIdx, int sourceIdx, IInputSource source)>
            BuildBindings(int layerCount, int sourcesPerLayer, int blendShapeCount)
        {
            var bindings = new List<(int, int, IInputSource)>(layerCount * sourcesPerLayer);
            for (int l = 0; l < layerCount; l++)
            {
                for (int s = 0; s < sourcesPerLayer; s++)
                {
                    var id = $"src-{l}-{s}";
                    bindings.Add((l, s, new FakeInputSource(id, InputSourceType.ValueProvider, blendShapeCount)));
                }
            }
            return bindings;
        }

        [Test]
        public void Constructor_ExposesLayerCount_MaxSourcesPerLayer_BlendShapeCount()
        {
            var profile = BuildProfile(layerCount: 3);
            var bindings = BuildBindings(layerCount: 3, sourcesPerLayer: 2, blendShapeCount: 200);

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 200, bindings);

            Assert.AreEqual(3, registry.LayerCount);
            Assert.AreEqual(2, registry.MaxSourcesPerLayer);
            Assert.AreEqual(200, registry.BlendShapeCount);
        }

        [Test]
        public void GetSource_ForRegisteredSlot_ReturnsRegisteredInstance()
        {
            var profile = BuildProfile(layerCount: 2);
            var a = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            var b = new FakeInputSource("lipsync", InputSourceType.ValueProvider, 4);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, a),
                (1, 0, b),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreSame(a, registry.GetSource(0, 0));
            Assert.AreSame(b, registry.GetSource(1, 0));
        }

        [Test]
        public void GetSource_OutOfRange_ReturnsNull()
        {
            var profile = BuildProfile(layerCount: 1);
            var src = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.IsNull(registry.GetSource(-1, 0));
            Assert.IsNull(registry.GetSource(5, 0));
            Assert.IsNull(registry.GetSource(0, -1));
            Assert.IsNull(registry.GetSource(0, 5));
        }

        [Test]
        public void GetSourceCountForLayer_ReturnsPerLayerCount()
        {
            var profile = BuildProfile(layerCount: 3);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s00", InputSourceType.ValueProvider, 4)),
                (0, 1, new FakeInputSource("s01", InputSourceType.ValueProvider, 4)),
                (1, 0, new FakeInputSource("s10", InputSourceType.ValueProvider, 4)),
                // layer 2 has no source
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreEqual(2, registry.GetSourceCountForLayer(0));
            Assert.AreEqual(1, registry.GetSourceCountForLayer(1));
            Assert.AreEqual(0, registry.GetSourceCountForLayer(2));
        }

        [Test]
        public void GetSourceCountForLayer_OutOfRange_ReturnsZero()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s", InputSourceType.ValueProvider, 4)),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreEqual(0, registry.GetSourceCountForLayer(-1));
            Assert.AreEqual(0, registry.GetSourceCountForLayer(5));
        }

        [Test]
        public void GetScratchBuffer_LengthEqualsBlendShapeCount()
        {
            var profile = BuildProfile(layerCount: 3);
            var bindings = BuildBindings(layerCount: 3, sourcesPerLayer: 2, blendShapeCount: 200);

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 200, bindings);

            for (int l = 0; l < 3; l++)
            {
                for (int s = 0; s < 2; s++)
                {
                    var mem = registry.GetScratchBuffer(l, s);
                    Assert.AreEqual(200, mem.Length,
                        $"Scratch slice for (l={l}, s={s}) length must equal BlendShapeCount (=200)");
                }
            }
        }

        [Test]
        public void GetScratchBuffer_AllSlicesAreContiguous_AndNonOverlapping()
        {
            // 観測完了条件: 3 layer × 2 source × 200 blendShape の scratch が
            // 連続（合計長）・非重複（書込独立）で割り当てられていること。
            var profile = BuildProfile(layerCount: 3);
            var bindings = BuildBindings(layerCount: 3, sourcesPerLayer: 2, blendShapeCount: 200);

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 200, bindings);

            for (int l = 0; l < 3; l++)
            {
                for (int s = 0; s < 2; s++)
                {
                    var span = registry.GetScratchBuffer(l, s).Span;
                    float marker = (l * 2 + s) + 1f;
                    for (int k = 0; k < span.Length; k++)
                    {
                        span[k] = marker;
                    }
                }
            }

            for (int l = 0; l < 3; l++)
            {
                for (int s = 0; s < 2; s++)
                {
                    var span = registry.GetScratchBuffer(l, s).Span;
                    float marker = (l * 2 + s) + 1f;
                    for (int k = 0; k < span.Length; k++)
                    {
                        Assert.AreEqual(marker, span[k],
                            $"Scratch slice (l={l}, s={s}, k={k}) must not overlap with other slices");
                    }
                }
            }
        }

        [Test]
        public void GetScratchBuffer_RepeatedCalls_ReturnSameFixedRegion()
        {
            // 固定アドレス: 同 (layer, source) に対する 2 回の呼出しが同じメモリ領域を指すこと。
            var profile = BuildProfile(layerCount: 2);
            var bindings = BuildBindings(layerCount: 2, sourcesPerLayer: 2, blendShapeCount: 8);

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 8, bindings);

            var first = registry.GetScratchBuffer(1, 1);
            first.Span[3] = 0.42f;

            var second = registry.GetScratchBuffer(1, 1);
            Assert.AreEqual(0.42f, second.Span[3],
                "同 (layer, source) への繰返し取得は同一メモリ領域を返すこと");
        }

        [Test]
        public void GetScratchBuffer_OutOfRange_ReturnsEmpty()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s", InputSourceType.ValueProvider, 4)),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.IsTrue(registry.GetScratchBuffer(-1, 0).IsEmpty);
            Assert.IsTrue(registry.GetScratchBuffer(0, -1).IsEmpty);
            Assert.IsTrue(registry.GetScratchBuffer(9, 0).IsEmpty);
            Assert.IsTrue(registry.GetScratchBuffer(0, 9).IsEmpty);
        }

        [Test]
        public void Constructor_NullBindings_Throws()
        {
            var profile = BuildProfile(layerCount: 1);

            Assert.Throws<ArgumentNullException>(() =>
                new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings: null));
        }

        [Test]
        public void Constructor_NegativeBlendShapeCount_Throws()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LayerInputSourceRegistry(profile, blendShapeCount: -1, bindings));
        }

        [Test]
        public void Constructor_MaxSourcesPerLayer_IsDerivedFromHighestSourceIdx()
        {
            // bindings のうち最大 sourceIdx が 3 なら MaxSourcesPerLayer == 4 になる。
            var profile = BuildProfile(layerCount: 2);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s00", InputSourceType.ValueProvider, 4)),
                (1, 3, new FakeInputSource("s13", InputSourceType.ValueProvider, 4)),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreEqual(4, registry.MaxSourcesPerLayer);
        }

        [Test]
        public void Constructor_EmptyBindings_AllocatesEmptyScratchWithoutThrowing()
        {
            var profile = BuildProfile(layerCount: 2);
            var bindings = new List<(int, int, IInputSource)>();

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreEqual(2, registry.LayerCount);
            Assert.AreEqual(0, registry.MaxSourcesPerLayer);
            Assert.AreEqual(4, registry.BlendShapeCount);
            Assert.AreEqual(0, registry.GetSourceCountForLayer(0));
            Assert.IsNull(registry.GetSource(0, 0));
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s", InputSourceType.ValueProvider, 4)),
            };

            var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            registry.Dispose();
            Assert.DoesNotThrow(() => registry.Dispose());
        }

        [Test]
        public void Dispose_AfterDispose_GetScratchBufferReturnsEmpty()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("s", InputSourceType.ValueProvider, 4)),
            };

            var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);
            registry.Dispose();

            Assert.IsTrue(registry.GetScratchBuffer(0, 0).IsEmpty,
                "Dispose 後は scratch バッファが解放されるため IsEmpty を返すこと");
        }

        // ----- 3.6 低頻度ランタイム API: TryAddSource / TryRemoveSource  -----

        [Test]
        public void TryAddSource_WithinExistingCapacity_ReusesFreedSlot()
        {
            // シナリオ: 2 source 確保済みレイヤーから 1 source を削除し、
            // 新しい source を追加すると MaxSourcesPerLayer の再確保を伴わず
            // 同じスロットに配置される。
            var profile = BuildProfile(layerCount: 1);
            var a = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            var b = new FakeInputSource("lipsync", InputSourceType.ValueProvider, 4);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, a),
                (0, 1, b),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.AreEqual(2, registry.MaxSourcesPerLayer);
            Assert.AreEqual(2, registry.GetSourceCountForLayer(0));

            Assert.IsTrue(registry.TryRemoveSource(0, InputSourceId.Parse("osc")),
                "既存 id の削除は true を返すこと");

            Assert.AreEqual(1, registry.GetSourceCountForLayer(0),
                "削除後は GetSourceCountForLayer が 1 減ること");

            var c = new FakeInputSource("x-added", InputSourceType.ValueProvider, 4);
            Assert.IsTrue(registry.TryAddSource(0, c),
                "追加は true を返すこと");

            Assert.AreEqual(2, registry.MaxSourcesPerLayer,
                "既存スロットに空きがあれば MaxSourcesPerLayer は変化しない");
            Assert.AreEqual(2, registry.GetSourceCountForLayer(0));

            // 追加後に GetSource(layerIdx, newSourceIdx) が新アダプタを返す
            bool foundAdded = false;
            for (int s = 0; s < registry.MaxSourcesPerLayer; s++)
            {
                if (ReferenceEquals(registry.GetSource(0, s), c))
                {
                    foundAdded = true;
                    break;
                }
            }
            Assert.IsTrue(foundAdded, "追加後に GetSource(layerIdx, newSourceIdx) が新アダプタを返すこと");
        }

        [Test]
        public void TryAddSource_ExceedsCapacity_ReallocatesScratchBuffer()
        {
            // 2 source で初期化した Registry に 3 本目を追加すると
            // MaxSourcesPerLayer が増加し scratch が再確保される (既存 Memory 参照は無効化許容)。
            var profile = BuildProfile(layerCount: 2);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("osc", InputSourceType.ValueProvider, 4)),
                (0, 1, new FakeInputSource("lipsync", InputSourceType.ValueProvider, 4)),
                (1, 0, new FakeInputSource("input", InputSourceType.ExpressionTrigger, 4)),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            int initialMax = registry.MaxSourcesPerLayer;
            Assert.AreEqual(2, initialMax);

            // layer 0 に 3 本目を追加
            var added = new FakeInputSource("x-cam", InputSourceType.ValueProvider, 4);
            Assert.IsTrue(registry.TryAddSource(0, added));

            Assert.AreEqual(initialMax + 1, registry.MaxSourcesPerLayer,
                "容量超過で追加すると MaxSourcesPerLayer が増加すること");
            Assert.AreEqual(3, registry.GetSourceCountForLayer(0));

            // 追加後の scratch は再確保されたが各 (layer, source) には
            // BlendShapeCount 長の slice が配られていること。
            for (int l = 0; l < registry.LayerCount; l++)
            {
                for (int s = 0; s < registry.GetSourceCountForLayer(l); s++)
                {
                    var mem = registry.GetScratchBuffer(l, s);
                    Assert.AreEqual(registry.BlendShapeCount, mem.Length,
                        $"再確保後も (l={l}, s={s}) の scratch 長は BlendShapeCount に等しいこと");
                }
            }

            // 追加直後に GetSource が新アダプタを返す
            bool foundAdded = false;
            for (int s = 0; s < registry.MaxSourcesPerLayer; s++)
            {
                if (ReferenceEquals(registry.GetSource(0, s), added))
                {
                    foundAdded = true;
                    break;
                }
            }
            Assert.IsTrue(foundAdded, "容量拡張後に GetSource が新アダプタを返すこと");

            // 既存 source も同じ参照で残っていること (layer 1 の input)
            bool foundExisting = false;
            for (int s = 0; s < registry.MaxSourcesPerLayer; s++)
            {
                var src = registry.GetSource(1, s);
                if (src != null && src.Id == "input")
                {
                    foundExisting = true;
                    break;
                }
            }
            Assert.IsTrue(foundExisting, "容量拡張時に既存 source が保持されること");
        }

        [Test]
        public void TryAddSource_DuplicateId_LogsWarningAndReturnsFalse()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("osc", InputSourceType.ValueProvider, 4)),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceRegistry.*TryAddSource.*osc"));

            var dup = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            Assert.IsFalse(registry.TryAddSource(0, dup),
                "同 id が既に存在するレイヤーへの追加は false を返すこと");

            Assert.AreEqual(1, registry.GetSourceCountForLayer(0),
                "重複追加で count は変化しないこと");
        }

        [Test]
        public void TryAddSource_OutOfRangeLayer_LogsWarningAndReturnsFalse()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceRegistry.*TryAddSource.*layerIdx=5"));

            var src = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            Assert.IsFalse(registry.TryAddSource(5, src));
        }

        [Test]
        public void TryAddSource_NullSource_LogsWarningAndReturnsFalse()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceRegistry.*TryAddSource.*null"));

            Assert.IsFalse(registry.TryAddSource(0, null));
        }

        [Test]
        public void TryRemoveSource_ExistingId_CompactsAndDecrementsCount()
        {
            // 3 sources を登録し、中間の 1 つを削除。
            // 残り 2 source は詰めて配置され GetSourceCountForLayer が 1 減る。
            var profile = BuildProfile(layerCount: 1);
            var a = new FakeInputSource("osc", InputSourceType.ValueProvider, 4);
            var b = new FakeInputSource("lipsync", InputSourceType.ValueProvider, 4);
            var c = new FakeInputSource("input", InputSourceType.ExpressionTrigger, 4);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, a),
                (0, 1, b),
                (0, 2, c),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);
            Assert.AreEqual(3, registry.GetSourceCountForLayer(0));

            Assert.IsTrue(registry.TryRemoveSource(0, InputSourceId.Parse("lipsync")));

            Assert.AreEqual(2, registry.GetSourceCountForLayer(0),
                "削除後は GetSourceCountForLayer が 1 減ること");

            // 残った 2 source が参照可能であること
            var remaining = new HashSet<string>();
            for (int s = 0; s < registry.GetSourceCountForLayer(0); s++)
            {
                var src = registry.GetSource(0, s);
                Assert.NotNull(src, $"compacted slot (0, {s}) は非 null であること");
                remaining.Add(src.Id);
            }
            CollectionAssert.AreEquivalent(new[] { "osc", "input" }, remaining);
        }

        [Test]
        public void TryRemoveSource_NonExistentId_LogsWarningAndReturnsFalse()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("osc", InputSourceType.ValueProvider, 4)),
            };
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceRegistry.*TryRemoveSource.*x-missing"));

            Assert.IsFalse(registry.TryRemoveSource(0, InputSourceId.Parse("x-missing")),
                "存在しない id の削除は false を返すこと");

            Assert.AreEqual(1, registry.GetSourceCountForLayer(0),
                "存在しない id の削除は count を変化させないこと");
            Assert.AreEqual("osc", registry.GetSource(0, 0)?.Id,
                "存在しない id の削除は既存スロットを変更しないこと");
        }

        [Test]
        public void TryRemoveSource_OutOfRangeLayer_LogsWarningAndReturnsFalse()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, new FakeInputSource("osc", InputSourceType.ValueProvider, 4)),
            };
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceRegistry.*TryRemoveSource.*layerIdx=9"));

            Assert.IsFalse(registry.TryRemoveSource(9, InputSourceId.Parse("osc")));
            Assert.AreEqual(1, registry.GetSourceCountForLayer(0));
        }
    }
}
