using System.Linq;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    /// <summary>
    /// 同梱サンプル MicLipSyncDemo の Profile アセットが README に書かれた構成
    /// （slot a/i/u/e/o の宣言、overlay レイヤーの overlay:/lipsync-overlay: 入力、smile の slot a Override）を保っていることを固定する。
    /// </summary>
    [TestFixture]
    public sealed class MicLipSyncDemoProfileAssetTests
    {
        private const string AssetPath =
            "Packages/com.hidano.facialcontrol.lipsync/Samples~/MicLipSyncDemo/Profiles/MicLipSyncDemoProfile.asset";

        [Test]
        public void Asset_MicLipSyncDemoProfile_HasFivePhonemeSlotsDeclared()
        {
            FacialCharacterProfileSO asset = LoadAsset();

            CollectionAssert.AreEqual(
                new[]
                {
                    PhonemeOverlaySlots.A,
                    PhonemeOverlaySlots.I,
                    PhonemeOverlaySlots.U,
                    PhonemeOverlaySlots.E,
                    PhonemeOverlaySlots.O
                },
                asset.Slots.ToArray());

            FacialProfile profile = asset.BuildFallbackProfile();
            CollectionAssert.AreEqual(asset.Slots.ToArray(), profile.Slots.ToArray());
        }

        [Test]
        public void Asset_SmileExpression_HasPhonemeOverlayBinding()
        {
            FacialCharacterProfileSO asset = LoadAsset();

            ExpressionSerializable smile = asset.Expressions.Single(expression => expression.id == "smile");
            OverlaySlotBindingSerializable binding = smile.overlays.Single(overlay => overlay.slot == PhonemeOverlaySlots.A);

            Assert.That(binding.suppress, Is.False);
            Assert.That(binding.cachedSnapshot, Is.Not.Null);
            Assert.That(binding.cachedSnapshot.blendShapes, Has.Count.EqualTo(1));
            Assert.That(binding.cachedSnapshot.blendShapes[0].rendererPath, Is.EqualTo("Face"));
            Assert.That(binding.cachedSnapshot.blendShapes[0].name, Does.EndWith("smile"));
            Assert.That(binding.cachedSnapshot.blendShapes[0].value, Is.EqualTo(1f).Within(1e-6f));

            FacialProfile profile = asset.BuildFallbackProfile();
            Expression domainSmile = profile.FindExpressionById("smile").Value;
            OverlaySlotBinding domainBinding = domainSmile.Overlays.ToArray().Single(overlay => overlay.Slot == PhonemeOverlaySlots.A);

            Assert.That(domainBinding.Suppress, Is.False);
            Assert.That(domainBinding.Snapshot.HasValue, Is.True);
            Assert.That(domainBinding.Snapshot.Value.BlendShapes.Span[0].RendererPath, Is.EqualTo("Face"));
            Assert.That(
                domainBinding.Snapshot.Value.BlendShapes.Span[0].Name,
                Is.EqualTo(binding.cachedSnapshot.blendShapes[0].name));
            Assert.That(domainBinding.Snapshot.Value.BlendShapes.Span[0].Value, Is.EqualTo(1f).Within(1e-6f));

            var overlayLayer = profile.FindLayerByName("overlay");
            Assert.That(overlayLayer.HasValue, Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    "overlay:a",
                    "lipsync-overlay:a",
                    "overlay:i",
                    "lipsync-overlay:i",
                    "overlay:u",
                    "lipsync-overlay:u",
                    "overlay:e",
                    "lipsync-overlay:e",
                    "overlay:o",
                    "lipsync-overlay:o"
                },
                profile.LayerInputSources.Span[0].Select(input => input.Id).ToArray());
        }

        private static FacialCharacterProfileSO LoadAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(AssetPath);
            if (asset == null)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, AssetPath.Replace('/', Path.DirectorySeparatorChar)));
                asset = InternalEditorUtility
                    .LoadSerializedFileAndForget(absolutePath)
                    .OfType<FacialCharacterProfileSO>()
                    .SingleOrDefault();
            }

            Assert.That(asset, Is.Not.Null, $"{AssetPath} could not be loaded.");
            return asset;
        }
    }
}
