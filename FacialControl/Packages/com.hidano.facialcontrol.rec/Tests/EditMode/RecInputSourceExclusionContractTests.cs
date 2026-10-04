using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public sealed class RecInputSourceExclusionContractTests : SizedTestFixture
    {
        [Test]
        public void ExclusionContract_InjectionSource_ImplementsIInjectedInputSource()
        {
            foreach (var entry in Entries(RecExclusionReason.InjectionSource))
            {
                Assert.That(typeof(IInjectedInputSource).IsAssignableFrom(ResolveType(entry)), Is.True,
                    Violation(entry, "IInjectedInputSource を実装していません"));
            }
        }

        [Test]
        public void ExclusionContract_EditorOnly_AssemblyIsDeclaredEditorOnly()
        {
            foreach (var entry in Entries(RecExclusionReason.EditorOnly))
            {
                var declaration = FindAssemblyDeclaration(entry);
                Assert.That(declaration, Is.Not.Null, Violation(entry, "product アセンブリ宣言がありません"));
                Assert.That(declaration.IsEditorOnly, Is.True, Violation(entry, "Editor 専用として宣言されていません"));
            }
        }

        [Test]
        public void ExclusionContract_EditorOnly_NoRuntimeAssemblyReferencesEditorAssembly()
        {
            var editorAssemblyNames = new HashSet<string>(
                RecInputSourceCoverageCatalog.ProductAssemblies
                    .Where(assembly => assembly.IsEditorOnly)
                    .Select(assembly => assembly.Name), StringComparer.Ordinal);

            foreach (var assembly in TestAssemblyCatalog.FindProjectProductAssemblies())
            {
                var declaration = RecInputSourceCoverageCatalog.ProductAssemblies
                    .Single(item => string.Equals(item.Name, assembly.GetName().Name, StringComparison.Ordinal));
                if (declaration.IsEditorOnly) continue;

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Assert.That(editorAssemblyNames.Contains(reference.Name), Is.False,
                        $"EditorOnly の契約違反: {assembly.GetName().Name} が {reference.Name} を参照しています。修正先: product アセンブリの参照設定");
                }
            }
        }

        [Test]
        public void ExclusionContract_NotRegisteredAtRuntime_DoesNotImplementIInputSource()
        {
            foreach (var entry in Entries(RecExclusionReason.NotRegisteredAtRuntime))
            {
                Assert.That(typeof(IInputSource).IsAssignableFrom(ResolveType(entry)), Is.False,
                    Violation(entry, "IInputSource を実装しているため非登録契約を満たしません"));
            }
        }

        [Test]
        public void ExclusionContract_WrappedByObservedSource_WrapperIsObservedEntry()
        {
            foreach (var entry in Entries(RecExclusionReason.WrappedByObservedSource))
            {
                Assert.That(entry.WrapperTypeFullName, Is.Not.Null.And.Not.Empty, Violation(entry, "wrapper 型が未指定です"));
                var wrapperEntry = RecInputSourceCoverageCatalog.Entries.SingleOrDefault(
                    candidate => string.Equals(candidate.TypeFullName, entry.WrapperTypeFullName, StringComparison.Ordinal));
                Assert.That(wrapperEntry, Is.Not.Null, Violation(entry, "wrapper 型がカタログにありません"));
                Assert.That(wrapperEntry.Classification, Is.EqualTo(RecInputSourceClassification.Observed), Violation(entry, "wrapper が Observed ではありません"));
                Assert.That(wrapperEntry.Category, Is.EqualTo(RecObservationCategory.Analog), Violation(entry, "wrapper が Analog ではありません"));
                Assert.That(typeof(IInputSource).IsAssignableFrom(ResolveType(wrapperEntry)), Is.True, Violation(entry, "wrapper が IInputSource を実装していません"));
                Assert.That(typeof(IAnalogInputSource).IsAssignableFrom(ResolveType(wrapperEntry)), Is.True, Violation(entry, "wrapper が IAnalogInputSource を実装していません"));
            }
        }

        [Test]
        public void ExclusionContract_RuntimeRegistrationContract_IsDeclaredAndExists()
        {
            foreach (var entry in RecInputSourceCoverageCatalog.Entries.Where(IsRuntimeRegistrationContractEntry))
            {
                Assert.That(entry.RuntimeRegistrationContractTest, Is.Not.Null.And.Not.Empty, Violation(entry, "主契約テストが未指定です"));
                Assert.That(FindContractMethod(entry.RuntimeRegistrationContractTest), Is.Not.Null,
                    Violation(entry, "主契約テストがロード済みテストアセンブリに存在しません"));
            }
        }

        [Test]
        public void ExclusionContract_AllowedReferrer_ReasonIsNotEmpty()
        {
            foreach (var entry in RecInputSourceCoverageCatalog.Entries.Where(entry => entry.Classification == RecInputSourceClassification.Excluded))
            foreach (var referrer in entry.AllowedDirectReferrers)
            {
                Assert.That(referrer.Reason, Is.Not.Null.And.Not.Empty, Violation(entry, $"許容参照元 {referrer.TypeFullName} の理由が空です"));
            }
        }

        [Test]
        public void ExclusionContract_EveryExclusionReason_HasContract()
        {
            var excluded = RecInputSourceCoverageCatalog.Entries
                .Where(entry => entry.Classification == RecInputSourceClassification.Excluded).ToList();
            foreach (var reason in Enum.GetValues(typeof(RecExclusionReason)).Cast<RecExclusionReason>().Where(reason => reason != RecExclusionReason.None))
            {
                Assert.That(excluded.Any(entry => entry.ExclusionReason == reason), Is.True,
                    $"除外区分の契約違反: {reason} のエントリがありません。修正先: RecInputSourceCoverageCatalog");
            }
        }

        [Test]
        public void ExclusionContract_NotRegisteredAtRuntime_DirectReferrersWithinAllowList()
        {
            AssertDirectReferrersWithinAllowList(RecExclusionReason.NotRegisteredAtRuntime);
        }

        [Test]
        public void ExclusionContract_WrappedByObservedSource_DirectReferrersWithinAllowList()
        {
            AssertDirectReferrersWithinAllowList(RecExclusionReason.WrappedByObservedSource);
        }

        private static IEnumerable<RecInputSourceCoverageEntry> Entries(RecExclusionReason reason)
        {
            return RecInputSourceCoverageCatalog.Entries.Where(entry => entry.ExclusionReason == reason);
        }

        private static bool IsRuntimeRegistrationContractEntry(RecInputSourceCoverageEntry entry)
        {
            return entry.ExclusionReason == RecExclusionReason.NotRegisteredAtRuntime
                || entry.ExclusionReason == RecExclusionReason.WrappedByObservedSource;
        }

        private static Type ResolveType(RecInputSourceCoverageEntry entry)
        {
            var type = TestAssemblyCatalog.FindProjectProductAssemblies()
                .Where(assembly => string.Equals(assembly.GetName().Name, entry.AssemblyName, StringComparison.Ordinal))
                .SelectMany(TestAssemblyCatalog.GetLoadableTypes)
                .SingleOrDefault(candidate => string.Equals(candidate.FullName, entry.TypeFullName, StringComparison.Ordinal));
            Assert.That(type, Is.Not.Null, Violation(entry, "カタログ型がロード済み product アセンブリに存在しません"));
            return type;
        }

        private static RecProductAssemblyDeclaration FindAssemblyDeclaration(RecInputSourceCoverageEntry entry)
        {
            return RecInputSourceCoverageCatalog.ProductAssemblies
                .SingleOrDefault(assembly => string.Equals(assembly.Name, entry.AssemblyName, StringComparison.Ordinal));
        }

        private static MethodInfo FindContractMethod(string contract)
        {
            var separator = contract.IndexOf("::", StringComparison.Ordinal);
            if (separator <= 0 || separator == contract.Length - 2) return null;
            var typeName = contract.Substring(0, separator);
            var methodName = contract.Substring(separator + 2);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
            foreach (var assemblyName in new[]
            {
                "Hidano.FacialControl.Osc.Tests.EditMode",
                "Hidano.FacialControl.InputSystem.Tests.PlayMode"
            })
            {
                if (assemblies.Any(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))) continue;
                try { assemblies.Add(Assembly.Load(new AssemblyName(assemblyName))); }
                catch (FileNotFoundException) { }
            }

            return assemblies
                .SelectMany(GetLoadableTypes)
                .Where(type => string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .SingleOrDefault(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type != null); }
        }

        private static void AssertDirectReferrersWithinAllowList(RecExclusionReason reason)
        {
            var entries = Entries(reason).ToList();
            var references = ProductAssemblyIlScanner.Scan(TestAssemblyCatalog.FindProjectProductAssemblies());
            foreach (var entry in entries)
            {
                var allowed = new HashSet<string>(entry.AllowedDirectReferrers.Select(referrer => referrer.TypeFullName), StringComparer.Ordinal);
                foreach (var reference in references.Where(reference => string.Equals(reference.ReferencedType.FullName, entry.TypeFullName, StringComparison.Ordinal)))
                {
                    var referrerType = GetOutermostType(reference.ReferrerType);
                    if (string.Equals(referrerType.FullName, entry.TypeFullName, StringComparison.Ordinal)) continue;
                    Assert.That(allowed.Contains(referrerType.FullName), Is.True,
                        Violation(entry, $"許容されていない直接参照元 {referrerType.FullName} が検出されました"));
                }
            }
        }

        private static Type GetOutermostType(Type type)
        {
            while (type.DeclaringType != null) type = type.DeclaringType;
            return type;
        }

        private static string Violation(RecInputSourceCoverageEntry entry, string detail)
        {
            return $"除外区分の契約違反: {entry.ExclusionReason} / {entry.TypeFullName}: {detail}。修正先: RecInputSourceCoverageCatalog または契約テスト";
        }
    }
}
