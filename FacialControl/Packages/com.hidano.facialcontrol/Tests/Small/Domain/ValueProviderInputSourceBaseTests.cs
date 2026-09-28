using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// ValueProviderInputSourceBase のテスト 。
    /// </summary>
    /// <remarks>
    /// 観測完了条件:
    /// <list type="bullet">
    ///   <item><c>Tick</c> が no-op であること (内部状態に影響しない)。</item>
    ///   <item>派生クラスが <c>TryWriteValues</c> のみ実装すれば成立すること。</item>
    ///   <item>派生フェイクが true/false を切替えると呼出側がその結果どおり寄与を判定できること。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class ValueProviderInputSourceBaseTests
    {
        /// <summary>
        /// <see cref="ValueProviderInputSourceBase"/> を継承し、<c>TryWriteValues</c> のみ
        /// 実装した最小フェイク。<c>IsValid</c> の切替で true/false を返す。
        /// </summary>
        private sealed class FakeValueProvider : ValueProviderInputSourceBase
        {
            private readonly float _writeValue;

            public FakeValueProvider(InputSourceId id, int blendShapeCount, float writeValue = 0.5f)
                : base(id, blendShapeCount)
            {
                _writeValue = writeValue;
            }

            public bool IsValid { get; set; } = true;

            public int WriteCallCount { get; private set; }

            public override bool TryWriteValues(Span<float> output)
            {
                WriteCallCount++;
                if (!IsValid)
                {
                    return false;
                }

                int len = Math.Min(output.Length, BlendShapeCount);
                for (int i = 0; i < len; i++)
                {
                    output[i] = _writeValue;
                }
                return true;
            }
        }

        [Test]
        public void Ctor_StoresIdTypeAndBlendShapeCount()
        {
            var id = InputSourceId.Parse("osc");
            var provider = new FakeValueProvider(id, blendShapeCount: 52);

            Assert.AreEqual("osc", provider.Id);
            Assert.AreEqual(InputSourceType.ValueProvider, provider.Type);
            Assert.AreEqual(52, provider.BlendShapeCount);
        }

        [Test]
        public void Type_IsAlwaysValueProvider()
        {
            var provider = new FakeValueProvider(InputSourceId.Parse("lipsync"), blendShapeCount: 4);

            IInputSource viaInterface = provider;

            Assert.AreEqual(InputSourceType.ValueProvider, viaInterface.Type);
        }

        [Test]
        public void Tick_IsNoOp_AndDoesNotThrow()
        {
            var provider = new FakeValueProvider(InputSourceId.Parse("osc"), blendShapeCount: 4);

            Assert.DoesNotThrow(() => provider.Tick(0f));
            Assert.DoesNotThrow(() => provider.Tick(0.016f));
            Assert.DoesNotThrow(() => provider.Tick(1.0f));
        }

        [Test]
        public void TryWriteValues_WhenValid_ReturnsTrueAndWritesOverlap()
        {
            var provider = new FakeValueProvider(InputSourceId.Parse("osc"), blendShapeCount: 4, writeValue: 0.75f)
            {
                IsValid = true
            };
            var buffer = new float[] { 0f, 0f, 0f, 0f };

            bool wrote = provider.TryWriteValues(buffer);

            Assert.IsTrue(wrote);
            Assert.AreEqual(1, provider.WriteCallCount);
            CollectionAssert.AreEqual(new[] { 0.75f, 0.75f, 0.75f, 0.75f }, buffer);
        }

        [Test]
        public void TryWriteValues_WhenInvalid_ReturnsFalseAndLeavesOutputUnchanged()
        {
            var provider = new FakeValueProvider(InputSourceId.Parse("lipsync"), blendShapeCount: 4, writeValue: 0.9f)
            {
                IsValid = false
            };
            var buffer = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
            var snapshot = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

            bool wrote = provider.TryWriteValues(buffer);

            Assert.IsFalse(wrote);
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [Test]
        public void TryWriteValues_SwitchingValidity_FlipsReturnValue()
        {
            var provider = new FakeValueProvider(InputSourceId.Parse("osc"), blendShapeCount: 2, writeValue: 1.0f);
            var buffer = new float[2];

            provider.IsValid = true;
            Assert.IsTrue(provider.TryWriteValues(buffer));

            provider.IsValid = false;
            Assert.IsFalse(provider.TryWriteValues(buffer));

            provider.IsValid = true;
            Assert.IsTrue(provider.TryWriteValues(buffer));
        }

        [Test]
        public void BaseContract_IsReachableAsIInputSource()
        {
            IInputSource source = new FakeValueProvider(InputSourceId.Parse("x-sensor"), blendShapeCount: 1);

            Assert.AreEqual("x-sensor", source.Id);
            Assert.AreEqual(InputSourceType.ValueProvider, source.Type);
            Assert.AreEqual(1, source.BlendShapeCount);
        }

        [Test]
        public void Ctor_RejectsNegativeBlendShapeCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FakeValueProvider(InputSourceId.Parse("osc"), blendShapeCount: -1));
        }

        [Test]
        public void Ctor_AllowsZeroBlendShapeCount()
        {
            Assert.DoesNotThrow(() =>
                new FakeValueProvider(InputSourceId.Parse("osc"), blendShapeCount: 0));
        }
    }
}
