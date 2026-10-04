using Hidano.FacialControl.Rec.Domain;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using System.Linq;

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
            Assert.That(RecInputSourceCoverageCatalog.Entries, Has.Count.EqualTo(20));
            foreach (var entry in RecInputSourceCoverageCatalog.Entries)
            {
                Assert.That(assemblyNames.Contains(entry.AssemblyName), Is.True, entry.TypeFullName);
            }
        }

        [Test]
        public void Entries_ClassificationMatchesInputSourceCoverageTable()
        {
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.Classification == RecInputSourceClassification.Observed), Is.EqualTo(13));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.Classification == RecInputSourceClassification.Excluded), Is.EqualTo(7));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.InjectionSource), Is.EqualTo(3));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.NotRegisteredAtRuntime), Is.EqualTo(2));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.WrappedByObservedSource), Is.EqualTo(1));
            Assert.That(RecInputSourceCoverageCatalog.Entries.Count(entry => entry.ExclusionReason == RecExclusionReason.EditorOnly), Is.EqualTo(1));
        }
    }
}
