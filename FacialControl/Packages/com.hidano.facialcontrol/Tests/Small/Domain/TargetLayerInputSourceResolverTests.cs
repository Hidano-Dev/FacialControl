using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="TargetLayerInputSourceResolver"/> が binding の入力源を既存レイヤーへ補う規則を守る
    /// （手動宣言の優先・二重合成の防止・未指定時の既定・存在しないレイヤーの警告・Profile 非破壊）。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class TargetLayerInputSourceResolverTests : SizedTestFixture
    {
        [Test]
        public void Resolve_TargetLayerNamed_AppendsSlugToThatLayer()
        {
            FacialProfile profile = CreateProfile(
                new[] { "emotion", "lipsync" },
                new[] { new[] { Decl("input") }, new[] { Decl("lipsync") } });
            var bindings = new List<AdapterBindingBase> { new TargetBinding("osc", "lipsync") };

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(profile, bindings, new List<string>());

            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[0]));
            CollectionAssert.AreEqual(new[] { "lipsync", "osc" }, Ids(result[1]));
            Assert.AreEqual(TargetLayerInputSourceResolver.AutoDeclarationWeight, result[1][1].Weight);
            Assert.IsNull(result[1][1].OptionsJson);
        }

        [Test]
        public void Resolve_TargetLayerUnspecified_AppendsSlugToFirstLayer()
        {
            FacialProfile profile = CreateProfile(
                new[] { "emotion", "eye" },
                new[] { new[] { Decl("input") }, new[] { Decl("input") } });
            var warnings = new List<string>();

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", null), },
                warnings);

            CollectionAssert.AreEqual(new[] { "input", "osc" }, Ids(result[0]));
            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[1]));
            Assert.IsEmpty(warnings, "未指定は既定（先頭レイヤー）で動くので警告しない。");
        }

        [Test]
        public void Resolve_SlugAlreadyDeclaredOnAnotherLayer_KeepsManualDeclarationOnly()
        {
            FacialProfile profile = CreateProfile(
                new[] { "emotion", "eye" },
                new[] { new[] { Decl("input") }, new[] { Decl("osc", 0.5f) } });
            var warnings = new List<string>();

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "emotion") },
                warnings);

            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[0]), "手動宣言があれば別レイヤーへ二重に足さない。");
            CollectionAssert.AreEqual(new[] { "osc" }, Ids(result[1]));
            Assert.AreEqual(0.5f, result[1][0].Weight, "手動宣言の weight を変えない。");
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Resolve_TargetLayerMissing_DoesNotAppendAndWarns()
        {
            FacialProfile profile = CreateProfile(new[] { "emotion" }, new[] { new[] { Decl("input") } });
            var warnings = new List<string>();

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "face") },
                warnings);

            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[0]));
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("'face'", warnings[0]);
            StringAssert.Contains("'osc'", warnings[0]);
        }

        [Test]
        public void Resolve_NoLayers_WarnsAndReturnsEmpty()
        {
            FacialProfile profile = CreateProfile(new string[0], new InputSourceDeclaration[0][]);
            var warnings = new List<string>();

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", null) },
                warnings);

            Assert.AreEqual(0, result.Length);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void Resolve_SameSlugFromTwoBindings_AppendsOnce()
        {
            FacialProfile profile = CreateProfile(
                new[] { "emotion", "eye" },
                new[] { new[] { Decl("input") }, new[] { Decl("input") } });

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "emotion"), new TargetBinding("osc", "eye") },
                new List<string>());

            CollectionAssert.AreEqual(new[] { "input", "osc" }, Ids(result[0]));
            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[1]));
        }

        [Test]
        public void Resolve_DisabledNullOrPlainBindings_AreIgnored()
        {
            FacialProfile profile = CreateProfile(new[] { "emotion" }, new[] { new[] { Decl("input") } });
            var bindings = new List<AdapterBindingBase>
            {
                null,
                new TargetBinding("osc", "emotion") { Disabled = true },
                new PlainBinding { Slug = "plain" },
                new TargetBinding(" ", "emotion"),
            };

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(profile, bindings, new List<string>());

            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[0]));
        }

        [Test]
        public void Resolve_DeclarationsShorterThanLayers_PadsAndAppends()
        {
            FacialProfile profile = CreateProfile(new[] { "emotion", "eye" }, new[] { new[] { Decl("input") } });

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "eye") },
                new List<string>());

            Assert.AreEqual(2, result.Length);
            CollectionAssert.AreEqual(new[] { "osc" }, Ids(result[1]));
        }

        [Test]
        public void Resolve_Appended_DoesNotModifyProfile()
        {
            FacialProfile profile = CreateProfile(new[] { "emotion" }, new[] { new[] { Decl("input") } });

            TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "emotion") },
                new List<string>());

            CollectionAssert.AreEqual(new[] { "input" }, Ids(profile.LayerInputSources.Span[0]));
        }

        [Test]
        public void Resolve_NullBindingsAndWarnings_ReturnsProfileDeclarations()
        {
            FacialProfile profile = CreateProfile(new[] { "emotion" }, new[] { new[] { Decl("input") } });

            InputSourceDeclaration[][] result = TargetLayerInputSourceResolver.Resolve(profile, null, null);

            CollectionAssert.AreEqual(new[] { "input" }, Ids(result[0]));
            Assert.DoesNotThrow(() => TargetLayerInputSourceResolver.Resolve(
                profile,
                new List<AdapterBindingBase> { new TargetBinding("osc", "missing") },
                null));
        }

        private static FacialProfile CreateProfile(string[] layerNames, InputSourceDeclaration[][] declarations)
        {
            var layers = new LayerDefinition[layerNames.Length];
            for (int i = 0; i < layerNames.Length; i++)
            {
                layers[i] = new LayerDefinition(layerNames[i], i, ExclusionMode.LastWins);
            }

            return new FacialProfile("1.0.0", layers, layerInputSources: declarations);
        }

        private static InputSourceDeclaration Decl(string id, float weight = 1f)
        {
            return new InputSourceDeclaration(id, weight, null);
        }

        private static string[] Ids(InputSourceDeclaration[] declarations)
        {
            var ids = new string[declarations.Length];
            for (int i = 0; i < declarations.Length; i++)
            {
                ids[i] = declarations[i].Id;
            }

            return ids;
        }

        private sealed class TargetBinding : AdapterBindingBase, IAdapterBindingTargetLayerInput
        {
            private readonly string _targetLayer;

            public TargetBinding(string slug, string targetLayer)
            {
                Slug = slug;
                _targetLayer = targetLayer;
            }

            public string TargetLayerName => _targetLayer;

            public string TargetLayerInputSourceId => Slug;
        }

        private sealed class PlainBinding : AdapterBindingBase
        {
        }
    }
}
