using System;
using System.Collections.Generic;
using System.Linq;

using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    internal static class RecInputSourceCoverageGate
    {
        public static IReadOnlyList<string> FindAssemblyViolations(
            IEnumerable<string> expectedAssemblyNames,
            IEnumerable<string> loadedAssemblyNames)
        {
            var expected = expectedAssemblyNames.ToList();
            var loaded = new HashSet<string>(loadedAssemblyNames, StringComparer.Ordinal);
            var violations = new List<string>();

            foreach (var duplicate in expected.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                violations.Add($"重複した期待アセンブリ: {duplicate.Key}。修正先: RecInputSourceCoverageCatalog");
            }

            foreach (var missing in expected.Distinct(StringComparer.Ordinal).Except(loaded, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
            {
                violations.Add($"期待アセンブリが未ロード: {missing}。修正先: RecInputSourceCoverageCatalog");
            }

            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
            foreach (var undeclared in loaded.Except(expectedSet, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
            {
                violations.Add($"ロード済みアセンブリが未宣言: {undeclared}。修正先: RecInputSourceCoverageCatalog");
            }

            return violations;
        }

        public static IReadOnlyList<string> FindEntryViolations(
            IEnumerable<Type> enumeratedTypes,
            IEnumerable<RecInputSourceCoverageEntry> entries,
            IEnumerable<RecProductAssemblyDeclaration> productAssemblies)
        {
            var types = enumeratedTypes
                .Where(IsEnumeratedInputSourceType)
                .ToDictionary(type => type.FullName, StringComparer.Ordinal);
            var catalogEntries = entries.ToList();
            var violations = new List<string>();

            foreach (var duplicate in catalogEntries.GroupBy(entry => entry.TypeFullName, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                violations.Add($"カタログの型が二重分類: {duplicate.Key}。修正先: RecInputSourceCoverageCatalog");
            }

            var productAssemblyNames = new HashSet<string>(
                productAssemblies.Select(assembly => assembly.Name), StringComparer.Ordinal);
            foreach (var entry in catalogEntries)
            {
                if (!productAssemblyNames.Contains(entry.AssemblyName))
                {
                    violations.Add($"カタログのアセンブリが未宣言: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (!string.IsNullOrWhiteSpace(entry.Reason) == false)
                {
                    violations.Add($"カタログの理由が空: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (entry.Classification == RecInputSourceClassification.Observed && entry.Category == RecObservationCategory.None)
                {
                    violations.Add($"Observed 型のカテゴリが未分類: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (entry.Classification == RecInputSourceClassification.Excluded && entry.ExclusionReason == RecExclusionReason.None)
                {
                    violations.Add($"Excluded 型の除外区分が未分類: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (entry.Classification == RecInputSourceClassification.Observed && entry.ExclusionReason != RecExclusionReason.None)
                {
                    violations.Add($"Observed 型に除外区分がある: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (entry.Classification == RecInputSourceClassification.Excluded && entry.ExclusionReason == RecExclusionReason.None)
                {
                    violations.Add($"Excluded 型の除外区分が空: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }

                if (!types.ContainsKey(entry.TypeFullName))
                {
                    violations.Add($"カタログに陳腐化した型がある: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }
                else if (!string.Equals(types[entry.TypeFullName].Assembly.GetName().Name, entry.AssemblyName, StringComparison.Ordinal))
                {
                    violations.Add($"カタログのアセンブリが型と不一致: {entry.TypeFullName} ({entry.AssemblyName})。修正先: RecInputSourceCoverageCatalog");
                }
            }

            var classifiedNames = new HashSet<string>(catalogEntries.Select(entry => entry.TypeFullName), StringComparer.Ordinal);
            foreach (var type in types.Values)
            {
                if (!classifiedNames.Contains(type.FullName))
                {
                    violations.Add($"入力源型が未分類: {type.FullName} ({type.Assembly.GetName().Name})。修正先: RecInputSourceCoverageCatalog");
                }
            }

            return violations;
        }

        public static bool IsEnumeratedInputSourceType(Type type)
        {
            return type != null
                && !type.IsAbstract
                && !type.IsInterface
                && !type.IsGenericTypeDefinition
                && (typeof(IInputSource).IsAssignableFrom(type)
                    || (typeof(IAnalogInputSource).IsAssignableFrom(type)
                        && !typeof(IInputSource).IsAssignableFrom(type)));
        }
    }
}
