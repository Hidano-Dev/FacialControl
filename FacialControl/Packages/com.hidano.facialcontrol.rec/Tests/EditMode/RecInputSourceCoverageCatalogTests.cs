using Hidano.FacialControl.Rec.Domain;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public sealed class RecInputSourceCoverageCatalogTests : SizedTestFixture
    {
        [Test]
        public void Entries_AssemblyName_IsDeclaredProductAssembly()
        {
            var assemblyNames = new System.Collections.Generic.HashSet<string>();
            foreach (var assembly in RecInputSourceCoverageCatalog.ProductAssemblies)
            {
                Assert.That(assemblyNames.Add(assembly.Name), Is.True, assembly.Name);
            }

            Assert.That(RecInputSourceCoverageCatalog.ProductAssemblies, Has.Count.EqualTo(20));
            Assert.That(RecInputSourceCoverageCatalog.Entries, Has.Count.EqualTo(21));
            foreach (var entry in RecInputSourceCoverageCatalog.Entries)
            {
                Assert.That(assemblyNames.Contains(entry.AssemblyName), Is.True, entry.TypeFullName);
            }
        }

        [Test]
        public void Entries_ClassificationMatchesInputSourceCoverageTable()
        {
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.Classification == RecInputSourceClassification.Observed), Is.EqualTo(13));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.Classification == RecInputSourceClassification.Excluded), Is.EqualTo(8));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.InjectionSource), Is.EqualTo(4));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.NotRegisteredAtRuntime), Is.EqualTo(2));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.WrappedByObservedSource), Is.EqualTo(1));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.EditorOnly), Is.EqualTo(1));
        }

        [Test]
        public void FindProjectProductAssemblies_CatalogProductAssemblies_AllLoaded()
        {
            var expected = RecInputSourceCoverageCatalog.ProductAssemblies.Select(assembly => assembly.Name);
            var loaded = TestAssemblyCatalog.FindProjectProductAssemblies().Select(assembly => assembly.GetName().Name);

            Assert.That(RecInputSourceCoverageGate.FindAssemblyViolations(expected, loaded), Is.Empty);
        }

        [Test]
        public void FindProjectProductAssemblies_LoadedProductAssembly_IsDeclaredInCatalog()
        {
            var expected = RecInputSourceCoverageCatalog.ProductAssemblies.Select(assembly => assembly.Name);
            var loaded = TestAssemblyCatalog.FindProjectProductAssemblies().Select(assembly => assembly.GetName().Name);

            Assert.That(RecInputSourceCoverageGate.FindAssemblyViolations(expected, loaded), Is.Empty);
        }

        [Test]
        public void Entries_EnumeratedType_IsClassified()
        {
            var assemblies = TestAssemblyCatalog.FindProjectProductAssemblies();
            var types = assemblies.SelectMany(TestAssemblyCatalog.GetLoadableTypes);
            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                types,
                RecInputSourceCoverageCatalog.Entries,
                RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void Entries_DuplicateFullName_Fails()
        {
            var entry = RecInputSourceCoverageCatalog.Entries[0];
            var entries = new[] { entry, entry };

            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                Array.Empty<Type>(), entries, RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations.Any(message => message.Contains("二重分類") && message.Contains(entry.TypeFullName)), Is.True);
        }

        [Test]
        public void Entries_StaleEntry_Fails()
        {
            var stale = new RecInputSourceCoverageEntry(
                "Missing.Type", "Hidano.FacialControl.Domain", RecInputSourceClassification.Observed,
                RecObservationCategory.Trigger, RecExclusionReason.None, "fixture", null,
                Array.Empty<RecAllowedDirectReferrer>(), null);

            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                Array.Empty<Type>(), new[] { stale }, RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations.Any(message => message.Contains("陳腐化") && message.Contains("Missing.Type")), Is.True);
        }

        [Test]
        public void Entries_ExcludedWithEmptyReason_Fails()
        {
            var entry = new RecInputSourceCoverageEntry(
                typeof(FakeInputSource).FullName, "Hidano.FacialControl.Domain", RecInputSourceClassification.Excluded,
                RecObservationCategory.None, RecExclusionReason.InjectionSource, string.Empty, null,
                Array.Empty<RecAllowedDirectReferrer>(), null);

            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                new[] { typeof(FakeInputSource) }, new[] { entry }, RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations.Any(message => message.Contains("理由が空")), Is.True);
        }

        [Test]
        public void Entries_ObservedWithoutCategory_Fails()
        {
            var entry = new RecInputSourceCoverageEntry(
                typeof(FakeInputSource).FullName, "Hidano.FacialControl.Domain", RecInputSourceClassification.Observed,
                RecObservationCategory.None, RecExclusionReason.None, "fixture", null,
                Array.Empty<RecAllowedDirectReferrer>(), null);

            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                new[] { typeof(FakeInputSource) }, new[] { entry }, RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations.Any(message => message.Contains("カテゴリが未分類")), Is.True);
        }

        [Test]
        public void Entries_ExcludedWithoutExclusionReason_Fails()
        {
            var entry = new RecInputSourceCoverageEntry(
                typeof(FakeInputSource).FullName, "Hidano.FacialControl.Domain", RecInputSourceClassification.Excluded,
                RecObservationCategory.None, RecExclusionReason.None, "fixture", null,
                Array.Empty<RecAllowedDirectReferrer>(), null);

            var violations = RecInputSourceCoverageGate.FindEntryViolations(
                new[] { typeof(FakeInputSource) }, new[] { entry }, RecInputSourceCoverageCatalog.ProductAssemblies);

            Assert.That(violations.Any(message => message.Contains("除外区分") && message.Contains("未分類")), Is.True);
        }

        [Test]
        public void Gate_MissingExpectedAssembly_FailsWithAssemblyName()
        {
            const string missingAssembly = "Hidano.FacialControl.Missing";
            var violations = RecInputSourceCoverageGate.FindAssemblyViolations(
                new[] { missingAssembly }, Array.Empty<string>());

            Assert.That(violations.Single(), Does.Contain(missingAssembly));
        }

        [Test]
        public void Gate_UndeclaredLoadedAssembly_FailsWithAssemblyName()
        {
            const string undeclaredAssembly = "Hidano.FacialControl.Unlisted";
            var violations = RecInputSourceCoverageGate.FindAssemblyViolations(
                Array.Empty<string>(), new[] { undeclaredAssembly });

            Assert.That(violations.Single(), Does.Contain(undeclaredAssembly));
        }

        private sealed class FakeInputSource : IInputSource
        {
            public string Id => "fake";
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public System.Collections.BitArray ContributeMask => new System.Collections.BitArray(0);
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
        }
    }
}
