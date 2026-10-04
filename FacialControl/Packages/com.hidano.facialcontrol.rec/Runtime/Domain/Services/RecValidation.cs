using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Validates recording expression ids against the current profile.
    /// </summary>
    public static class RecValidation
    {
        public static IReadOnlyList<string> FindMissingExpressionIds(RecTimeline timeline, FacialProfile profile)
        {
            if (timeline == null)
            {
                return Array.Empty<string>();
            }

            var availableIds = new HashSet<string>(StringComparer.Ordinal);
            var expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                string id = expressions[i].Id;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    availableIds.Add(id);
                }
            }

            var missingIds = new List<string>();
            var seenMissingIds = new HashSet<string>(StringComparer.Ordinal);

            CollectMissingIds(timeline.ExpressionIds, availableIds, seenMissingIds, missingIds);

            IReadOnlyList<RecBaselineState.TriggerEntry> triggerEntries = timeline.Baseline?.TriggerEntries;
            if (triggerEntries != null)
            {
                for (int i = 0; i < triggerEntries.Count; i++)
                {
                    CollectMissingIds(triggerEntries[i].ExpressionIds, availableIds, seenMissingIds, missingIds);
                }
            }

            CollectMissingIds(timeline.Baseline?.ExpressionEntries, availableIds, seenMissingIds, missingIds);

            return missingIds.Count == 0 ? Array.Empty<string>() : missingIds;
        }

        private static void CollectMissingIds(
            IReadOnlyList<string> expressionIds,
            HashSet<string> availableIds,
            HashSet<string> seenMissingIds,
            List<string> missingIds)
        {
            if (expressionIds == null)
            {
                return;
            }

            for (int i = 0; i < expressionIds.Count; i++)
            {
                string expressionId = expressionIds[i];
                if (string.IsNullOrWhiteSpace(expressionId) || availableIds.Contains(expressionId))
                {
                    continue;
                }

                if (seenMissingIds.Add(expressionId))
                {
                    missingIds.Add(expressionId);
                }
            }
        }
    }
}
