using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// レイヤー別 AnimationClip の担当レイヤー決定（<see cref="FacialAnimationClipBuilder.AssignOwnerLayers"/>）の契約（HID-410）。
    /// BlendShape ごとに、値を書いたレイヤーのうち Profile 上で優先度が最も高いレイヤーを 1 つだけ選ぶ。
    /// </summary>
    [SmallTest]
    public sealed class FacialAnimationClipBuilderTests : SizedTestFixture
    {
        private static readonly LayerDefinition[] Layers =
        {
            new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            new LayerDefinition("lipsync", 5, ExclusionMode.Blend),
            new LayerDefinition("eye", 2, ExclusionMode.LastWins),
        };

        [Test]
        public void AssignOwnerLayers_ShapeWrittenByOneLayer_OwnedByThatLayer()
        {
            // shape 0 は emotion だけ、shape 1 は eye だけが書く。
            bool[] contributes = Contributes(
                new[] { true, false },
                new[] { false, false },
                new[] { false, true });

            int[] owners = FacialAnimationClipBuilder.AssignOwnerLayers(contributes, Layers, 2);

            Assert.That(owners, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void AssignOwnerLayers_ShapeWrittenByManyLayers_OwnedByHighestPriority()
        {
            // 3 レイヤーとも書く。優先度は lipsync(5) > eye(2) > emotion(0)。宣言順ではなく優先度で選ぶ。
            bool[] contributes = Contributes(new[] { true }, new[] { true }, new[] { true });

            int[] owners = FacialAnimationClipBuilder.AssignOwnerLayers(contributes, Layers, 1);

            Assert.That(owners, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void AssignOwnerLayers_SamePriority_LaterDeclaredLayerWins()
        {
            // LayerBlender は同じ優先度を宣言順に適用するため、後に宣言したレイヤーが最終値を決める。
            var layers = new[]
            {
                new LayerDefinition("a", 1, ExclusionMode.LastWins),
                new LayerDefinition("b", 1, ExclusionMode.LastWins),
            };
            bool[] contributes = { true, true };

            int[] owners = FacialAnimationClipBuilder.AssignOwnerLayers(contributes, layers, 1);

            Assert.That(owners, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void AssignOwnerLayers_ShapeWrittenByNoLayer_ReturnsNoOwner()
        {
            bool[] contributes = Contributes(new[] { false }, new[] { false }, new[] { false });

            int[] owners = FacialAnimationClipBuilder.AssignOwnerLayers(contributes, Layers, 1);

            Assert.That(owners, Is.EqualTo(new[] { FacialAnimationClipBuilder.NoOwner }));
        }

        /// <summary>レイヤーごとの寄与（[layer][shape]）を AccumulateBlendedContributions と同じ並び（layer * shapeCount + shape）にする。</summary>
        private static bool[] Contributes(params bool[][] perLayer)
        {
            int shapeCount = perLayer[0].Length;
            var flat = new bool[perLayer.Length * shapeCount];
            for (int l = 0; l < perLayer.Length; l++)
            {
                for (int s = 0; s < shapeCount; s++)
                {
                    flat[(l * shapeCount) + s] = perLayer[l][s];
                }
            }

            return flat;
        }
    }
}
