using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// 録画開始時点の入力状態（トリガースタック・アナログ値・値提供型の mask と値・系1のアクティブ表情）を
    /// <see cref="RecBaselineState"/> として捕捉する。
    /// </summary>
    public static class RecBaselineCapture
    {
        /// <param name="registry">現在登録されている入力源。</param>
        /// <param name="expressionGate">系1のアクティブ表情。null なら系1の基準は空。</param>
        /// <param name="blendShapeCount">コントローラの BlendShape 数。値提供型はこの数と一致するものだけ捕捉する。</param>
        public static RecBaselineState Capture(
            IInputSourceRegistry registry,
            IExpressionActivationGate expressionGate,
            int blendShapeCount)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var triggerEntries = new List<RecBaselineState.TriggerEntry>();
            var analogEntries = new List<RecBaselineState.AnalogEntry>();
            var valueProviderEntries = new List<RecBaselineState.ValueProviderEntry>();
            var expressionEntries = new List<string>();

            if (expressionGate != null)
            {
                expressionGate.CollectActiveExpressionIds(expressionEntries);
            }

            int maskByteCount = blendShapeCount > 0 ? (blendShapeCount + 7) / 8 : 0;
            var valueScratch = blendShapeCount > 0 ? new float[blendShapeCount] : Array.Empty<float>();

            IReadOnlyList<string> registeredIds = registry.RegisteredIds ?? Array.Empty<string>();
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string sourceId = registeredIds[i];
                if (!registry.TryResolve(sourceId, out IInputSource source) || source == null)
                {
                    continue;
                }

                if (source is ExpressionTriggerInputSourceBase triggerSource)
                {
                    IReadOnlyList<string> activeExpressionIds = triggerSource.ActiveExpressionIds;
                    if (activeExpressionIds.Count > 0)
                    {
                        triggerEntries.Add(new RecBaselineState.TriggerEntry(sourceId, activeExpressionIds));
                    }

                    continue;
                }

                if (source is ValueProviderInputSourceBase valueProvider)
                {
                    if (valueProvider.BlendShapeCount != blendShapeCount)
                    {
                        Debug.LogWarning(
                            $"REC baseline skipped value-provider '{sourceId}' because BlendShapeCount " +
                            $"({valueProvider.BlendShapeCount}) did not match the controller ({blendShapeCount}).");
                        continue;
                    }

                    Array.Clear(valueScratch, 0, valueScratch.Length);
                    bool isValid = valueProvider.TryWriteValues(valueScratch);
                    var maskBytes = new byte[maskByteCount];
                    BitArray mask = valueProvider.ContributeMask;
                    int setBitCount = 0;
                    if (mask != null)
                    {
                        int maskLength = Math.Min(mask.Length, blendShapeCount);
                        for (int maskIndex = 0; maskIndex < maskLength; maskIndex++)
                        {
                            if (mask[maskIndex])
                            {
                                maskBytes[maskIndex >> 3] |= (byte)(1 << (maskIndex & 7));
                                setBitCount++;
                            }
                        }
                    }

                    // 記録・再生とも値は「mask が立っている位置だけを mask 順に詰めた」形式。
                    // 全長で保存すると疎な入力源（OSC 等）の基準注入が値数不一致で拒否される。
                    var packedValues = new float[setBitCount];
                    int packedIndex = 0;
                    if (mask != null)
                    {
                        int maskLength = Math.Min(mask.Length, blendShapeCount);
                        for (int maskIndex = 0; maskIndex < maskLength; maskIndex++)
                        {
                            if (mask[maskIndex])
                            {
                                packedValues[packedIndex++] = valueScratch[maskIndex];
                            }
                        }
                    }

                    valueProviderEntries.Add(new RecBaselineState.ValueProviderEntry(
                        sourceId,
                        isValid,
                        maskBytes,
                        packedValues));
                    continue;
                }

                if (source is not IAnalogInputSource analogSource
                    || !analogSource.IsValid
                    || analogSource.AxisCount <= 0)
                {
                    continue;
                }

                var axes = new float[analogSource.AxisCount];
                if (!analogSource.TryReadAxes(axes))
                {
                    continue;
                }

                analogEntries.Add(new RecBaselineState.AnalogEntry(sourceId, axes));
            }

            return new RecBaselineState(triggerEntries, analogEntries, valueProviderEntries, expressionEntries);
        }
    }
}
