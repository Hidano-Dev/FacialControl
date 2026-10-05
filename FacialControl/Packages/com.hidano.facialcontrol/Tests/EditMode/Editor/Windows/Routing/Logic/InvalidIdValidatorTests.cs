using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEngine;
#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using Hidano.FacialControl.LipSync.Adapters;
#endif

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class InvalidIdValidatorTests : SizedTestFixture
    {
        private TestFacialCharacterProfileSO _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null)
            {
                UnityEngine.Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

#if FACIALCONTROL_HAS_LIPSYNC_MODULE
        [Test]
        public void Validate_LegacySlugAndUnknownIds_ReturnsLayerAndDeclarationIndexes()
        {
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "lipsync-overlay:a", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "ulipsync:a", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "unknown:ghost", weight = 0.5f },
                },
            });

            ISet<string> validCanonicalIds = new SourcePortEnumerator().EnumerateCanonicalIds(
                new AdapterBindingBase[] { new ULipSyncAdapterBinding() },
                new[] { "overlay" });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(_profile, validCanonicalIds);

            CollectionAssert.AreEqual(
                new[]
                {
                    new InvalidDeclarationRef(0, 1, "ulipsync:a"),
                    new InvalidDeclarationRef(0, 2, "unknown:ghost"),
                },
                invalidDeclarations);
            Assert.That(_profile.Layers[0].inputSources[1].id, Is.EqualTo("ulipsync:a"));
            Assert.That(_profile.Layers[0].inputSources[2].id, Is.EqualTo("unknown:ghost"));
        }

        [Test]
        public void Validate_AllIdsKnown_ReturnsEmpty()
        {
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "lipsync-overlay:a", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "ulipsync", weight = 1f },
                },
            });

            ISet<string> validCanonicalIds = new SourcePortEnumerator().EnumerateCanonicalIds(
                new AdapterBindingBase[] { new ULipSyncAdapterBinding() },
                new[] { "overlay" });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(_profile, validCanonicalIds);

            Assert.That(invalidDeclarations, Is.Empty);
        }
#endif

        #region 動的 id binding の prefix 許容

        [Test]
        public void Validate_DynamicInputsBindingSlugPrefix_AcceptsMatchingIds()
        {
            _profile.WritableAdapterBindings.Add(new DynamicInputsBinding { Slug = "timeline" });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "timeline:emotion", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "timeline:layer0", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "unknown:ghost", weight = 0.5f },
                },
            });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(_profile, new HashSet<string>(StringComparer.Ordinal));

            CollectionAssert.AreEqual(
                new[] { new InvalidDeclarationRef(0, 2, "unknown:ghost") },
                invalidDeclarations,
                "マーカー binding の `{Slug}:` prefix に一致する宣言 id は有効扱いになる必要がある。");
        }

        [Test]
        public void Validate_DynamicInputsBindingDifferentSlug_ReportsUnmatchedPrefixAsInvalid()
        {
            _profile.WritableAdapterBindings.Add(new DynamicInputsBinding { Slug = "timeline" });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "rec:emotion", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "timeline", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "timeline:", weight = 1f },
                },
            });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(_profile, new HashSet<string>(StringComparer.Ordinal));

            CollectionAssert.AreEqual(
                new[]
                {
                    new InvalidDeclarationRef(0, 0, "rec:emotion"),
                    new InvalidDeclarationRef(0, 1, "timeline"),
                    new InvalidDeclarationRef(0, 2, "timeline:"),
                },
                invalidDeclarations,
                "slug が異なる id・prefix だけで sub が無い id は従来どおり不正扱いになる必要がある。");
        }

        [Test]
        public void Validate_BindingWithoutMarkerHasSamePrefix_ReportsAsInvalid()
        {
            _profile.WritableAdapterBindings.Add(new PlainBinding { Slug = "timeline" });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "timeline:emotion", weight = 1f },
                },
            });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(_profile, new HashSet<string>(StringComparer.Ordinal));

            CollectionAssert.AreEqual(
                new[] { new InvalidDeclarationRef(0, 0, "timeline:emotion") },
                invalidDeclarations,
                "マーカーを実装しない binding の slug は prefix 許容の対象にならない。");
        }

        [Test]
        public void Validate_DynamicInputsBindingWithEmptySlug_DoesNotAcceptAnyPrefix()
        {
            _profile.WritableAdapterBindings.Add(new DynamicInputsBinding { Slug = string.Empty });
            _profile.WritableAdapterBindings.Add(null);
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = ":emotion", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "known", weight = 1f },
                },
            });

            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                new InvalidIdValidator().Validate(
                    _profile,
                    new HashSet<string>(StringComparer.Ordinal) { "known" });

            CollectionAssert.AreEqual(
                new[] { new InvalidDeclarationRef(0, 0, ":emotion") },
                invalidDeclarations,
                "slug が空のマーカー binding や null 要素は prefix 許容を生まず、既知 id の判定も変わらない。");
        }

        [Serializable]
        private sealed class DynamicInputsBinding : AdapterBindingBase, IAdapterBindingDynamicInputs
        {
        }

        [Serializable]
        private sealed class PlainBinding : AdapterBindingBase
        {
        }

        #endregion
    }
}
