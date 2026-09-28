using System;
using System.Collections;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class IInputSourceContractTests
    {
        /// <summary>
        /// 契約検証用の最小 Fake。<see cref="IInputSource.TryWriteValues"/> が常に false を返し、
        /// output を書き換えない挙動を持つ。Tick / TryWriteValues の呼び出し可否を検証するための型付きモック。
        /// </summary>
        private sealed class NoOpInvalidInputSource : IInputSource
        {
            public NoOpInvalidInputSource(string id, InputSourceType type, int blendShapeCount)
            {
                Id = id;
                Type = type;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, true);
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public int TickCallCount { get; private set; }
            public float LastDeltaTime { get; private set; }

            public void Tick(float deltaTime)
            {
                TickCallCount++;
                LastDeltaTime = deltaTime;
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }
        }

        [Test]
        public void InputSourceType_HasExpressionTriggerAndValueProviderMembers()
        {
            Assert.AreEqual(0, (int)InputSourceType.ExpressionTrigger);
            Assert.AreEqual(1, (int)InputSourceType.ValueProvider);
        }

        [Test]
        public void Id_Type_BlendShapeCount_AreReadableFromContract()
        {
            IInputSource source = new NoOpInvalidInputSource("input", InputSourceType.ExpressionTrigger, 52);

            Assert.AreEqual("input", source.Id);
            Assert.AreEqual(InputSourceType.ExpressionTrigger, source.Type);
            Assert.AreEqual(52, source.BlendShapeCount);
        }

        [Test]
        public void ContributeMask_Length_MatchesBlendShapeCount()
        {
            IInputSource source = new NoOpInvalidInputSource("input", InputSourceType.ExpressionTrigger, 52);

            Assert.IsNotNull(source.ContributeMask);
            Assert.AreEqual(source.BlendShapeCount, source.ContributeMask.Length);
        }

        [Test]
        public void ContributeMask_Length_RemainsStableForSameSource()
        {
            IInputSource source = new NoOpInvalidInputSource("input", InputSourceType.ValueProvider, 4);
            int initialLength = source.ContributeMask.Length;
            Span<float> output = stackalloc float[4];

            source.Tick(0.016f);
            source.TryWriteValues(output);

            Assert.AreEqual(source.BlendShapeCount, initialLength);
            Assert.AreEqual(initialLength, source.ContributeMask.Length);
        }

        [Test]
        public void Tick_IsInvocableWithZeroDeltaTime()
        {
            var fake = new NoOpInvalidInputSource("osc", InputSourceType.ValueProvider, 4);
            IInputSource source = fake;

            source.Tick(0f);

            Assert.AreEqual(1, fake.TickCallCount);
            Assert.AreEqual(0f, fake.LastDeltaTime);
        }

        [Test]
        public void TryWriteValues_InvalidSource_ReturnsFalseAndLeavesOutputUnchanged()
        {
            IInputSource source = new NoOpInvalidInputSource("lipsync", InputSourceType.ValueProvider, 4);
            var buffer = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
            var snapshot = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

            bool wrote = source.TryWriteValues(buffer);

            Assert.IsFalse(wrote);
            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [Test]
        public void TryWriteValues_AcceptsStackAllocatedSpan()
        {
            IInputSource source = new NoOpInvalidInputSource("x-test", InputSourceType.ValueProvider, 2);
            Span<float> output = stackalloc float[2];
            output[0] = 0.5f;
            output[1] = 0.25f;

            bool wrote = source.TryWriteValues(output);

            Assert.IsFalse(wrote);
            Assert.AreEqual(0.5f, output[0]);
            Assert.AreEqual(0.25f, output[1]);
        }
    }
}
