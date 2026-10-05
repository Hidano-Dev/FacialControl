using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidano.FacialControl.Rec.Domain;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public sealed class RecWeightWritePathCatalogTests : SizedTestFixture
    {
        [Test]
        public void WeightWritePaths_EveryEntry_TypeAndMemberExist()
        {
            Assert.That(RecInputSourceCoverageCatalog.WeightWritePaths, Has.Count.EqualTo(14));
            var violations = FindViolations(RecInputSourceCoverageCatalog.WeightWritePaths);

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void WeightWritePaths_NoDuplicateTypeMember()
        {
            var duplicate = RecInputSourceCoverageCatalog.WeightWritePaths
                .GroupBy(entry => entry.TypeFullName + "::" + entry.MemberName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.That(duplicate, Is.Empty);
        }

        [Test]
        public void WeightWritePaths_Excluded_HaveReasonAndCategory()
        {
            foreach (var entry in RecInputSourceCoverageCatalog.WeightWritePaths)
            {
                if (entry.Classification != RecWeightWritePathClassification.Excluded)
                {
                    continue;
                }

                Assert.That(entry.ExclusionReason, Is.Not.EqualTo(RecWeightWritePathExclusionReason.None), entry.TypeFullName);
                Assert.That(entry.Reason, Is.Not.Null.And.Not.Empty, entry.TypeFullName);
            }
        }

        [Test]
        public void WeightWritePaths_StaleEntry_FailsWithTypeAndMemberName()
        {
            var stale = new RecWeightWritePathEntry(
                "Missing.WeightWriter", "MissingMember", "Hidano.FacialControl.Domain",
                RecWeightWritePathClassification.Gated,
                RecWeightWritePathExclusionReason.None,
                "fixture");

            var violations = FindViolations(new[] { stale });

            Assert.That(violations, Has.Some.Contains("Missing.WeightWriter").And.Contains("MissingMember"));
            Assert.That(violations, Has.Some.Contains("RecInputSourceCoverageCatalog.WeightWritePaths"));
        }

        [Test]
        public void Entries_OverlayAndInputActionReasons_ReferenceWeightCoverage()
        {
            var entries = RecInputSourceCoverageCatalog.Entries;
            var overlay = entries.Single(entry => entry.TypeFullName.EndsWith(".InputSources.OverlayInputSource", StringComparison.Ordinal));
            var inputAction = entries.Single(entry => entry.TypeFullName.EndsWith("InputActionAnalogSource", StringComparison.Ordinal));
            var inputActionReferrer = inputAction.AllowedDirectReferrers.Single(referrer =>
                referrer.TypeFullName.EndsWith("InputSystemAdapterBinding", StringComparison.Ordinal));

            Assert.That(overlay.Reason, Does.Contain("rec-weight-coverage"));
            Assert.That(inputActionReferrer.Reason,
                Does.Contain("overlay layer weight 駆動（core の weight 遮断面に乗る）"));
        }

        private static IReadOnlyList<string> FindViolations(IEnumerable<RecWeightWritePathEntry> entries)
        {
            var productAssemblies = new HashSet<string>(
                RecInputSourceCoverageCatalog.ProductAssemblies.Select(assembly => assembly.Name),
                StringComparer.Ordinal);
            var violations = new List<string>();
            foreach (var duplicate in entries.GroupBy(entry => entry.TypeFullName + "::" + entry.MemberName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                violations.Add($"duplicate {duplicate.Key}; update RecInputSourceCoverageCatalog.WeightWritePaths");
            }

            foreach (var entry in entries)
            {
                if (!productAssemblies.Contains(entry.AssemblyName))
                {
                    violations.Add($"undeclared assembly {entry.TypeFullName}::{entry.MemberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Reason))
                {
                    violations.Add($"empty reason {entry.TypeFullName}::{entry.MemberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                }

                if (entry.Classification == RecWeightWritePathClassification.Excluded
                    && entry.ExclusionReason == RecWeightWritePathExclusionReason.None)
                {
                    violations.Add($"missing exclusion category {entry.TypeFullName}::{entry.MemberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                }

                if (!TestAssemblyCatalog.TryFindLoadedAssembly(entry.AssemblyName, out var assembly))
                {
                    violations.Add($"assembly not loaded {entry.AssemblyName} for {entry.TypeFullName}::{entry.MemberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                    continue;
                }

                var type = assembly.GetType(entry.TypeFullName);
                if (type == null)
                {
                    violations.Add($"stale type {entry.TypeFullName}::{entry.MemberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                    continue;
                }

                foreach (var memberName in entry.MemberName.Split(new[] { " / " }, StringSplitOptions.None))
                {
                    var member = type.GetMember(memberName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    if (member == null || member.Length == 0)
                    {
                        violations.Add($"stale member {entry.TypeFullName}::{memberName}; update RecInputSourceCoverageCatalog.WeightWritePaths");
                    }
                }
            }

            return violations;
        }
    }
}
