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

        [Test]
        public void AssignOwnerLayers_ContributesShorterThanLayersTimesShapes_Throws()
        {
            // 長さ不足を「書いていない」と扱うと、評価器とレイヤー構成がずれたときに全 BlendShape が無言で Base に落ちる。
            bool[] contributes = new bool[(Layers.Length * 2) - 1];

            Assert.Throws<System.ArgumentException>(() => FacialAnimationClipBuilder.AssignOwnerLayers(contributes, Layers, 2));
        }

        [Test]
        public void AssignLayerClipNames_UniqueNamesNotReserved_KeepsLayerNames()
        {
            string[] names = FacialAnimationClipBuilder.AssignLayerClipNames(Layers);

            Assert.That(names, Is.EqualTo(new[] { "emotion", "lipsync", "eye" }));
        }

        [Test]
        public void AssignLayerClipNames_ReservedOrDuplicateNames_AddsLayerIndexSuffix()
        {
            // 予約名（Base / Gaze）と、大文字小文字だけ違う重複はファイル名・トラック名が衝突するので区別する。
            var layers = new[]
            {
                new LayerDefinition("Base", 0, ExclusionMode.LastWins),
                new LayerDefinition("emotion", 1, ExclusionMode.LastWins),
                new LayerDefinition("Emotion", 2, ExclusionMode.LastWins),
                new LayerDefinition("gaze", 3, ExclusionMode.LastWins),
                new LayerDefinition("eye", 4, ExclusionMode.LastWins),
            };

            string[] names = FacialAnimationClipBuilder.AssignLayerClipNames(layers);

            Assert.That(names, Is.EqualTo(new[] { "Base_L0", "emotion_L1", "Emotion_L2", "gaze_L3", "eye" }));
        }

        [Test]
        public void AssignLayerClipNames_SuffixedNameTakenByAnotherLayer_StaysUnique()
        {
            var layers = new[]
            {
                new LayerDefinition("Base", 0, ExclusionMode.LastWins),
                new LayerDefinition("Base_L0", 1, ExclusionMode.LastWins),
            };

            string[] names = FacialAnimationClipBuilder.AssignLayerClipNames(layers);

            Assert.That(names[1], Is.EqualTo("Base_L0"));
            Assert.That(names[0], Is.Not.EqualTo("Base_L0").IgnoreCase.And.Not.EqualTo("Base").IgnoreCase);
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
