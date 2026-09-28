using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="LayerBlender.Blend"/> のレイヤー間ブレンドを検証する。
    /// 全 index が contribute する通常ケースが参照 lerp 実装と一致すること、
    /// および ContributeMask（部分 mask / null mask）が出力の上書き範囲を制御することを確認する。
    /// </summary>
    [TestFixture]
    public class LayerBlenderTests
    {
        private const float Tolerance = 1e-6f;

        // --- 全 index contribute（参照実装との一致） ---

        [Test]
        public void Blend_AllSetMaskAndZeroInitialOutput_MatchesReferenceLerpOutput()
        {
            var layers = new[]
            {
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 0,
                    weight: 0.5f,
                    values: new[] { 1.0f, 0.25f, 0.8f },
                    mask: ContributeMaskTestHelper.AllSetContributeMask(3)),
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 1,
                    weight: 0.25f,
                    values: new[] { 0.0f, 1.0f, 0.4f },
                    mask: ContributeMaskTestHelper.AllSetContributeMask(3)),
            };
            var output = new float[3];
            float[] expected = LayerBlenderTestSupport.ReferenceLerpBlend(
                new[]
                {
                    (priority: 0, weight: 0.5f, values: new[] { 1.0f, 0.25f, 0.8f }),
                    (priority: 1, weight: 0.25f, values: new[] { 0.0f, 1.0f, 0.4f }),
                },
                outputLength: output.Length);

            LayerBlender.Blend(layers, output);

            Assert.That(output, Is.EqualTo(expected).Within(Tolerance));
        }

        // --- ContributeMask ---

        [Test]
        public void Blend_PartialMask_KeepsPreviousOutputForFalseIndexes()
        {
            BitArray mask = new BitArray(new[] { true, false, true, false });
            var layers = new[]
            {
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 0,
                    weight: 1.0f,
                    values: new[] { 1.0f, 1.0f, 0.0f, 0.0f },
                    mask: mask),
            };
            var output = new[] { 0.2f, 0.4f, 0.6f, 0.8f };

            LayerBlender.Blend(layers, output);

            Assert.That(output, Is.EqualTo(new[] { 1.0f, 0.4f, 0.0f, 0.8f }).Within(Tolerance));
        }

        [Test]
        public void Blend_NullMask_TreatsLayerAsAllSet()
        {
            var layers = new[]
            {
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 0,
                    weight: 0.5f,
                    values: new[] { 0.2f, 0.8f, 1.0f },
                    mask: null),
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 1,
                    weight: 0.75f,
                    values: new[] { 1.0f, 0.0f, 0.5f },
                    mask: null),
            };
            var output = new[] { 0.9f, 0.9f, 0.9f };
            float[] expected = LayerBlenderTestSupport.ReferenceLerpBlend(
                new[]
                {
                    (priority: 0, weight: 0.5f, values: new[] { 0.2f, 0.8f, 1.0f }),
                    (priority: 1, weight: 0.75f, values: new[] { 1.0f, 0.0f, 0.5f }),
                },
                outputLength: output.Length);

            LayerBlender.Blend(layers, output);

            Assert.That(output, Is.EqualTo(expected).Within(Tolerance));
        }
    }

    /// <summary>
    /// <see cref="LayerBlender"/> テスト用の補助。
    /// ContributeMask 付き <see cref="LayerBlender.LayerInput"/> の生成と、
    /// mask を考慮しない参照 lerp 実装（期待値算出用）を提供する。
    /// </summary>
    internal static class LayerBlenderTestSupport
    {
        private static readonly Type[] MaskCtorSignature =
        {
            typeof(int),
            typeof(float),
            typeof(float[]),
            typeof(BitArray),
        };

        public static LayerBlender.LayerInput CreateLayerInput(
            int priority,
            float weight,
            float[] values,
            BitArray mask)
        {
            var ctor = typeof(LayerBlender.LayerInput).GetConstructor(MaskCtorSignature);
            Assert.That(ctor, Is.Not.Null,
                "LayerBlender.LayerInput は (int priority, float weight, float[] blendShapeValues, BitArray contributeMask) コンストラクタを公開する必要がある。");

            var property = typeof(LayerBlender.LayerInput).GetProperty(
                "ContributeMask",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null,
                "LayerBlender.LayerInput は ContributeMask プロパティを公開する必要がある。");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(BitArray)));

            var input = (LayerBlender.LayerInput)ctor.Invoke(new object[] { priority, weight, values, mask });
            Assert.That(property.GetValue(input), Is.SameAs(mask),
                "LayerInput に渡した contribute mask 参照が保持されること。");
            return input;
        }

        /// <summary>
        /// mask を考慮しない参照 lerp ブレンド。全 index が contribute する場合の期待値算出に使う。
        /// </summary>
        public static float[] ReferenceLerpBlend(
            (int priority, float weight, float[] values)[] layers,
            int outputLength)
        {
            var output = new float[outputLength];
            if (layers.Length == 0 || output.Length == 0)
            {
                return output;
            }

            Array.Sort(layers, (a, b) => a.priority.CompareTo(b.priority));

            float firstWeight = Clamp01(layers[0].weight);
            int firstLength = Math.Min(output.Length, layers[0].values.Length);
            for (int i = 0; i < firstLength; i++)
            {
                output[i] = Clamp01(layers[0].values[i] * firstWeight);
            }

            for (int layerIndex = 1; layerIndex < layers.Length; layerIndex++)
            {
                float weight = Clamp01(layers[layerIndex].weight);
                int length = Math.Min(output.Length, layers[layerIndex].values.Length);
                for (int i = 0; i < length; i++)
                {
                    output[i] = Clamp01(output[i] + (layers[layerIndex].values[i] - output[i]) * weight);
                }
            }

            return output;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
