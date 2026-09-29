#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEngine;

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
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

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
    }
}
#endif
