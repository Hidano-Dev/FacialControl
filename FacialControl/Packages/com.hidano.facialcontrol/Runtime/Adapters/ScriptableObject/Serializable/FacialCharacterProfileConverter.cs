using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// SO Serializable → Domain <see cref="FacialProfile"/> 変換器。
    /// <see cref="ExpressionSerializable.cachedSnapshot"/> から BlendShape 値 / 遷移メタを
    /// 展開（snapshot 展開ロジック）して Domain Expression を構築する。
    /// </summary>
    public static class FacialCharacterProfileConverter
    {
        public static FacialProfile ToFacialProfile(
            string schemaVersion,
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<ExpressionSerializable> expressions,
            IReadOnlyList<string> rendererPaths)
        {
            return ToFacialProfile(
                schemaVersion,
                layers,
                expressions,
                rendererPaths,
                defaultOverlays: null,
                slots: null);
        }

        public static FacialProfile ToFacialProfile(
            string schemaVersion,
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<ExpressionSerializable> expressions,
            IReadOnlyList<string> rendererPaths,
            IReadOnlyList<OverlaySlotBindingSerializable> defaultOverlays)
        {
            return ToFacialProfile(
                schemaVersion,
                layers,
                expressions,
                rendererPaths,
                defaultOverlays,
                slots: null);
        }

        public static FacialProfile ToFacialProfile(
            string schemaVersion,
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<ExpressionSerializable> expressions,
            IReadOnlyList<string> rendererPaths,
            IReadOnlyList<OverlaySlotBindingSerializable> defaultOverlays,
            IReadOnlyList<string> slots,
            IReadOnlyList<BlendShapeSnapshotDto> baseExpression = null)
        {
            string version = string.IsNullOrWhiteSpace(schemaVersion)
                ? SystemTextJsonParser.SchemaVersionV2
                : schemaVersion;
            var uniqueLayers = RemoveDuplicateLayers(layers);
            var layerArr = ConvertLayers(uniqueLayers);
            var inputSourceArr = ConvertLayerInputSources(uniqueLayers);
            var expressionArr = ConvertExpressions(expressions, uniqueLayers);
            var rendererArr = ConvertStrings(rendererPaths);
            var defaultOverlayArr = ConvertOverlays(defaultOverlays);
            var slotArr = ConvertStrings(slots);
            var baseExpressionArr = ConvertBlendShapeSnapshots(baseExpression);
            return new FacialProfile(
                schemaVersion: version,
                layers: layerArr,
                expressions: expressionArr,
                rendererPaths: rendererArr,
                layerInputSources: inputSourceArr,
                defaultOverlays: defaultOverlayArr,
                slots: slotArr,
                baseExpression: baseExpressionArr);
        }

        public static ProfileSnapshotDto ToProfileSnapshotDto(FacialProfile profile)
        {
            var dto = new ProfileSnapshotDto
            {
                schemaVersion = string.IsNullOrEmpty(profile.SchemaVersion)
                    ? SystemTextJsonParser.SchemaVersionV2
                    : profile.SchemaVersion,
                slots = new List<string>(),
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string>(),
                gaze = new GazeSectionDto { channels = new List<GazeChannelDto>() },
                defaultOverlays = BuildOverlaySlotBindingDtoList(profile.DefaultOverlays.Span),
            };

            var slotsSpan = profile.Slots.Span;
            for (int i = 0; i < slotsSpan.Length; i++)
            {
                dto.slots.Add(slotsSpan[i] ?? string.Empty);
            }

            var rendererPathSpan = profile.RendererPaths.Span;
            for (int i = 0; i < rendererPathSpan.Length; i++)
            {
                dto.rendererPaths.Add(rendererPathSpan[i] ?? string.Empty);
            }

            var layerSpan = profile.Layers.Span;
            var inputSourcesSpan = profile.LayerInputSources.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                dto.layers.Add(new LayerDefinitionDto
                {
                    name = layerSpan[i].Name,
                    priority = layerSpan[i].Priority,
                    exclusionMode = SerializeExclusionMode(layerSpan[i].ExclusionMode),
                    inputSources = BuildInputSourceDtoList(i < inputSourcesSpan.Length ? inputSourcesSpan[i] : null),
                });
            }

            var expressionSpan = profile.Expressions.Span;
            for (int i = 0; i < expressionSpan.Length; i++)
            {
                dto.expressions.Add(BuildExpressionDto(expressionSpan[i]));
            }

            dto.baseExpression = BuildBaseExpressionSnapshotDto(profile.BaseExpression.Span);

            return dto;
        }

        /// <summary>
        /// profile.json の gaze セクションを SO の GazeChannels に復元する。
        /// gaze が欠落している場合は新規 JSON の最小形として既定チャネルを補完する。
        /// </summary>
        public static List<GazeChannel> ToGazeChannels(ProfileSnapshotDto dto)
        {
            return ToGazeChannels(dto != null ? dto.gaze : null);
        }

        public static List<GazeChannel> ToGazeChannels(GazeSectionDto section)
        {
            var result = new List<GazeChannel>();
            var channels = section != null ? section.channels : null;
            if (channels != null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < channels.Count; i++)
                {
                    var source = channels[i];
                    if (source == null || !IsValidGazeChannel(source.id))
                    {
                        Debug.LogWarning("[FacialControl] gaze.channels の不正なチャネルを読み捨てました。id は規約に従う必要があります。");
                        continue;
                    }

                    if (!ids.Add(source.id))
                    {
                        Debug.LogWarning($"[FacialControl] gaze.channels のチャネル id '{source.id}' が重複しているため、後続エントリを読み捨てました。");
                        continue;
                    }

                    result.Add(ToGazeChannel(source));
                }
            }

            int defaultIndex = result.FindIndex(c => string.Equals(c.id, GazeSourceIdConvention.DefaultChannelId, StringComparison.Ordinal));
            if (defaultIndex < 0)
            {
                Debug.LogWarning("[FacialControl] gaze.channels に既定チャネルがないため、id 'gaze' を補完しました。");
                result.Insert(0, new GazeChannel { id = GazeSourceIdConvention.DefaultChannelId });
            }
            else if (defaultIndex > 0)
            {
                var defaultChannel = result[defaultIndex];
                result.RemoveAt(defaultIndex);
                result.Insert(0, defaultChannel);
            }

            return result;
        }

        public static List<GazeChannelDto> ToGazeChannelDtos(IReadOnlyList<GazeChannel> channels)
        {
            var result = new List<GazeChannelDto>();
            if (channels == null) return result;
            for (int i = 0; i < channels.Count; i++)
            {
                if (channels[i] != null) result.Add(ToGazeChannelDto(channels[i]));
            }
            return result;
        }

        public static GazeChannelDto ToGazeChannelDto(GazeChannel source)
        {
            if (source == null) return null;
            return new GazeChannelDto
            {
                id = source.id,
                providerSlug = source.providerSlug ?? string.Empty,
                useDistinctLeftRight = source.useDistinctLeftRight,
                sourceIdLeft = source.sourceIdLeft ?? string.Empty,
                sourceIdRight = source.sourceIdRight ?? string.Empty,
                leftEyeBonePath = source.leftEyeBonePath ?? string.Empty,
                leftEyeInitialRotation = source.leftEyeInitialRotation,
                leftEyeYawAxisLocal = source.leftEyeYawAxisLocal,
                leftEyePitchAxisLocal = source.leftEyePitchAxisLocal,
                rightEyeBonePath = source.rightEyeBonePath ?? string.Empty,
                rightEyeInitialRotation = source.rightEyeInitialRotation,
                rightEyeYawAxisLocal = source.rightEyeYawAxisLocal,
                rightEyePitchAxisLocal = source.rightEyePitchAxisLocal,
                lookUpAngle = source.lookUpAngle,
                lookDownAngle = source.lookDownAngle,
                outerYawAngle = source.outerYawAngle,
                innerYawAngle = source.innerYawAngle,
            };
        }

        private static GazeChannel ToGazeChannel(GazeChannelDto source)
        {
            return new GazeChannel
            {
                id = source.id,
                providerSlug = source.providerSlug ?? string.Empty,
                useDistinctLeftRight = source.useDistinctLeftRight,
                sourceIdLeft = source.sourceIdLeft ?? string.Empty,
                sourceIdRight = source.sourceIdRight ?? string.Empty,
                leftEyeBonePath = source.leftEyeBonePath ?? string.Empty,
                leftEyeInitialRotation = source.leftEyeInitialRotation,
                leftEyeYawAxisLocal = source.leftEyeYawAxisLocal,
                leftEyePitchAxisLocal = source.leftEyePitchAxisLocal,
                rightEyeBonePath = source.rightEyeBonePath ?? string.Empty,
                rightEyeInitialRotation = source.rightEyeInitialRotation,
                rightEyeYawAxisLocal = source.rightEyeYawAxisLocal,
                rightEyePitchAxisLocal = source.rightEyePitchAxisLocal,
                lookUpAngle = source.lookUpAngle,
                lookDownAngle = source.lookDownAngle,
                outerYawAngle = source.outerYawAngle,
                innerYawAngle = source.innerYawAngle,
            };
        }

        private static bool IsValidGazeChannel(string id)
        {
            return !string.IsNullOrEmpty(id) && GazeSourceIdConvention.IsValidChannelId(id);
        }

        /// <summary>
        /// ベース表情 (<see cref="FacialProfile.BaseExpression"/>) を JSON DTO へ変換する。
        /// AnimationClip 参照は SO 内のみで保持し、DTO には bake 済み BlendShape 値のみを載せる。
        /// </summary>
        private static ExpressionSnapshotDto BuildBaseExpressionSnapshotDto(
            ReadOnlySpan<BlendShapeSnapshot> blendShapes)
        {
            var dto = new ExpressionSnapshotDto
            {
                transitionDuration = 0f,
                transitionCurvePreset = SerializeTransitionCurvePreset(TransitionCurvePreset.Linear),
                blendShapes = new List<BlendShapeSnapshotDto>(blendShapes.Length),
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string>(),
            };

            for (int i = 0; i < blendShapes.Length; i++)
            {
                var snapshot = blendShapes[i];
                dto.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = snapshot.RendererPath,
                    name = snapshot.Name,
                    value = snapshot.Value,
                });

                if (!string.IsNullOrEmpty(snapshot.RendererPath)
                    && !dto.rendererPaths.Contains(snapshot.RendererPath))
                {
                    dto.rendererPaths.Add(snapshot.RendererPath);
                }
            }

            return dto;
        }

        /// <summary>
        /// JSON root の gaze channels DTO を channel リストへ変換する。
        /// Domain <see cref="FacialProfile"/> には gaze を載せず、SO ルートの sidecar data として扱う。
        /// </summary>
        public static List<GazeChannel> ToLegacyGazeChannels(ProfileSnapshotDto dto)
        {
            // Keep this overload as a compatibility bridge for callers that have
            // not migrated from the old root-list API yet.  The source of truth
            // is the new gaze.channels section; never read the obsolete
            // ProfileSnapshotDto.gazeConfigs property here.
            if (dto == null || dto.gaze == null || dto.gaze.channels == null || dto.gaze.channels.Count == 0)
                return new List<GazeChannel>();

            var channels = ToGazeChannels(dto.gaze);
            var result = new List<GazeChannel>(channels.Count);
            for (int i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null) continue;

                result.Add(new GazeChannel
                {
                    id = channel.id,
                    useDistinctLeftRight = channel.useDistinctLeftRight,
                    sourceIdLeft = channel.sourceIdLeft ?? string.Empty,
                    sourceIdRight = channel.sourceIdRight ?? string.Empty,
                    leftEyeBonePath = channel.leftEyeBonePath,
                    leftEyeInitialRotation = channel.leftEyeInitialRotation,
                    leftEyeYawAxisLocal = channel.leftEyeYawAxisLocal,
                    leftEyePitchAxisLocal = channel.leftEyePitchAxisLocal,
                    rightEyeBonePath = channel.rightEyeBonePath,
                    rightEyeInitialRotation = channel.rightEyeInitialRotation,
                    rightEyeYawAxisLocal = channel.rightEyeYawAxisLocal,
                    rightEyePitchAxisLocal = channel.rightEyePitchAxisLocal,
                    lookUpAngle = channel.lookUpAngle,
                    lookDownAngle = channel.lookDownAngle,
                    outerYawAngle = channel.outerYawAngle,
                    innerYawAngle = channel.innerYawAngle,
                });
            }

            return result;
        }

        /// <summary>
        /// JSON root の gaze channels DTO を channel リストへ変換する。
        /// </summary>
        public static List<GazeChannel> ToLegacyGazeChannels(IReadOnlyList<GazeChannelDto> dtoList)
        {
            if (dtoList == null || dtoList.Count == 0)
                return new List<GazeChannel>();

            var result = new List<GazeChannel>(dtoList.Count);
            for (int i = 0; i < dtoList.Count; i++)
            {
                var src = dtoList[i];
                if (src == null) continue;

                result.Add(new GazeChannel
                {
                    id = src.id,
                    useDistinctLeftRight = src.useDistinctLeftRight,
                    sourceIdLeft = src.sourceIdLeft ?? string.Empty,
                    sourceIdRight = src.sourceIdRight ?? string.Empty,
                    leftEyeBonePath = src.leftEyeBonePath,
                    leftEyeInitialRotation = src.leftEyeInitialRotation,
                    leftEyeYawAxisLocal = src.leftEyeYawAxisLocal,
                    leftEyePitchAxisLocal = src.leftEyePitchAxisLocal,
                    rightEyeBonePath = src.rightEyeBonePath,
                    rightEyeInitialRotation = src.rightEyeInitialRotation,
                    rightEyeYawAxisLocal = src.rightEyeYawAxisLocal,
                    rightEyePitchAxisLocal = src.rightEyePitchAxisLocal,
                    lookUpAngle = src.lookUpAngle,
                    lookDownAngle = src.lookDownAngle,
                    outerYawAngle = src.outerYawAngle,
                    innerYawAngle = src.innerYawAngle,
                });
            }
            return result;
        }

        public static List<GazeChannelDto> ToLegacyGazeChannelDtos(IReadOnlyList<GazeChannel> configs)
        {
            if (configs == null || configs.Count == 0)
                return new List<GazeChannelDto>();

            var result = new List<GazeChannelDto>(configs.Count);
            for (int i = 0; i < configs.Count; i++)
            {
                var src = configs[i];
                if (src == null) continue;

                result.Add(ToLegacyGazeChannelDto(src));
            }
            return result;
        }

        public static GazeChannelDto ToLegacyGazeChannelDto(GazeChannel src)
        {
            if (src == null)
                return null;

            return new GazeChannelDto
            {
                id = src.id,
                useDistinctLeftRight = src.useDistinctLeftRight,
                sourceIdLeft = src.sourceIdLeft ?? string.Empty,
                sourceIdRight = src.sourceIdRight ?? string.Empty,
                leftEyeBonePath = src.leftEyeBonePath,
                leftEyeInitialRotation = src.leftEyeInitialRotation,
                leftEyeYawAxisLocal = src.leftEyeYawAxisLocal,
                leftEyePitchAxisLocal = src.leftEyePitchAxisLocal,
                rightEyeBonePath = src.rightEyeBonePath,
                rightEyeInitialRotation = src.rightEyeInitialRotation,
                rightEyeYawAxisLocal = src.rightEyeYawAxisLocal,
                rightEyePitchAxisLocal = src.rightEyePitchAxisLocal,
                lookUpAngle = src.lookUpAngle,
                lookDownAngle = src.lookDownAngle,
                outerYawAngle = src.outerYawAngle,
                innerYawAngle = src.innerYawAngle,
            };
        }

        /// <summary>
        /// 指定 Layer 名に対応する Layer 定義の <c>layerOverrideMask</c> を返す。
        /// Layer 側に mask が定義されていれば最優先、空であれば呼出側の Expression mask に委ねる。
        /// </summary>
        public static IReadOnlyList<string> ResolveLayerMask(
            IReadOnlyList<LayerDefinitionSerializable> layers,
            string layerName)
        {
            if (layers == null || string.IsNullOrEmpty(layerName)) return null;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l == null) continue;
                if (string.Equals(l.name, layerName, StringComparison.Ordinal))
                {
                    return l.layerOverrideMask;
                }
            }
            return null;
        }

        public static AnalogInputBindingProfile ToAnalogProfile(
            string version,
            IReadOnlyList<AnalogBindingEntrySerializable> bindings)
        {
            if (bindings == null || bindings.Count == 0)
                return new AnalogInputBindingProfile(version ?? string.Empty, Array.Empty<AnalogBindingEntry>());
            var entries = new List<AnalogBindingEntry>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                var src = bindings[i];
                if (src == null || string.IsNullOrWhiteSpace(src.targetIdentifier)) continue;
                AnalogBindingEntry entry;
                try
                {
                    entry = new AnalogBindingEntry(src.inputActionRef ?? string.Empty, 0, AnalogBindingTargetKind.BlendShape, src.targetIdentifier, src.targetAxis);
                }
                catch (ArgumentOutOfRangeException) { continue; }
                catch (ArgumentException) { continue; }
                entries.Add(entry);
            }
            return new AnalogInputBindingProfile(version ?? string.Empty, entries.ToArray());
        }

        private static LayerDefinition[] ConvertLayers(IReadOnlyList<LayerDefinitionSerializable> layers)
        {
            if (layers == null || layers.Count == 0) return Array.Empty<LayerDefinition>();
            var result = new List<LayerDefinition>(layers.Count);
            for (int i = 0; i < layers.Count; i++)
            {
                var src = layers[i];
                if (src == null || string.IsNullOrWhiteSpace(src.name)) continue;
                int priority = src.priority < 0 ? 0 : src.priority;
                result.Add(new LayerDefinition(src.name, priority, src.exclusionMode));
            }
            return result.ToArray();
        }

        private static List<LayerDefinitionSerializable> RemoveDuplicateLayers(
            IReadOnlyList<LayerDefinitionSerializable> layers)
        {
            if (layers == null || layers.Count == 0)
                return new List<LayerDefinitionSerializable>();

            var accepted = new List<LayerDefinitionSerializable>(layers.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool warned = false;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer != null && !string.IsNullOrWhiteSpace(layer.name) && !names.Add(layer.name))
                {
                    if (!warned)
                    {
                        Debug.LogWarning(
                            "[FacialControl] layers にレイヤー名の重複があるため、後続の重複レイヤーを読み捨てました。");
                        warned = true;
                    }
                    continue;
                }

                accepted.Add(layer);
            }

            return accepted;
        }

        private static InputSourceDeclaration[][] ConvertLayerInputSources(IReadOnlyList<LayerDefinitionSerializable> layers)
        {
            if (layers == null || layers.Count == 0) return Array.Empty<InputSourceDeclaration[]>();
            var result = new List<InputSourceDeclaration[]>(layers.Count);
            for (int i = 0; i < layers.Count; i++)
            {
                var src = layers[i];
                if (src == null || string.IsNullOrWhiteSpace(src.name)) continue;
                if (src.inputSources == null || src.inputSources.Count == 0) { result.Add(Array.Empty<InputSourceDeclaration>()); continue; }
                var arr = new List<InputSourceDeclaration>(src.inputSources.Count);
                for (int j = 0; j < src.inputSources.Count; j++)
                {
                    var s = src.inputSources[j];
                    if (s == null || string.IsNullOrWhiteSpace(s.id)) continue;
                    string options = string.IsNullOrEmpty(s.optionsJson) ? null : s.optionsJson;
                    arr.Add(new InputSourceDeclaration(s.id, s.weight, options));
                }
                result.Add(arr.ToArray());
            }
            return result.ToArray();
        }

        private static Expression[] ConvertExpressions(
            IReadOnlyList<ExpressionSerializable> expressions,
            IReadOnlyList<LayerDefinitionSerializable> _layers)
        {
            if (expressions == null || expressions.Count == 0) return Array.Empty<Expression>();

            // OverrideMask の bit position ↔ layer 名の対応表（_layers の宣言順）。
            // LayerOverrideMaskSerializable.ToMask が orderedLayerNames の index を bit position とする。
            List<string> orderedLayerNames = null;
            if (_layers != null)
            {
                orderedLayerNames = new List<string>(_layers.Count);
                for (int li = 0; li < _layers.Count; li++)
                {
                    orderedLayerNames.Add(_layers[li]?.name);
                }
            }

            var result = new List<Expression>(expressions.Count);
            for (int i = 0; i < expressions.Count; i++)
            {
                var src = expressions[i];
                if (src == null || string.IsNullOrWhiteSpace(src.id) || string.IsNullOrWhiteSpace(src.name) || string.IsNullOrWhiteSpace(src.layer)) continue;

                // 遷移時間は Inspector スライダー (src.transitionDuration) を常に正とする。
                // Why: cachedSnapshot.transitionDuration は AnimationEvent 由来のベイク値で、
                // Inspector で編集した値が反映されない問題があった (Inspector 操作が silent に無視される)。
                float duration = src.transitionDuration;
                TransitionCurve curve;
                BlendShapeMapping[] blendShapes;

                if (src.cachedSnapshot != null)
                {
                    curve = ConvertTransitionCurvePreset(src.cachedSnapshot.transitionCurvePreset);
                    blendShapes = ConvertSnapshotBlendShapes(src.cachedSnapshot.blendShapes);
                }
                else
                {
                    curve = ConvertTransitionCurve(src.transitionCurve);
                    blendShapes = ConvertBlendShapes(src.blendShapeValues);
                }

                var overlays = ConvertOverlays(src.overlays);
                var overrideMask = LayerOverrideMaskSerializable.ToMask(src.layerOverrideMask, orderedLayerNames);
                result.Add(new Expression(src.id, src.name, src.layer, duration, curve, blendShapes, overlays, overrideMask));
            }
            return result.ToArray();
        }

        private static OverlaySlotBinding[] ConvertOverlays(IReadOnlyList<OverlaySlotBindingSerializable> source)
        {
            if (source == null || source.Count == 0) return null;
            var list = new List<OverlaySlotBinding>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var s = source[i];
                if (s == null || string.IsNullOrWhiteSpace(s.slot)) continue;

                if (s.suppress)
                {
                    list.Add(new OverlaySlotBinding(s.slot, suppress: true, snapshot: null));
                    continue;
                }

                ExpressionSnapshot? snapshot = IsSnapshotEmpty(s.cachedSnapshot)
                    ? (ExpressionSnapshot?)null
                    : ConvertExpressionSnapshot(s.cachedSnapshot, s.slot);
                list.Add(new OverlaySlotBinding(s.slot, suppress: false, snapshot: snapshot));
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static BlendShapeMapping[] ConvertBlendShapes(IReadOnlyList<BlendShapeMappingSerializable> mappings)
        {
            if (mappings == null || mappings.Count == 0) return Array.Empty<BlendShapeMapping>();
            var result = new List<BlendShapeMapping>(mappings.Count);
            for (int i = 0; i < mappings.Count; i++)
            {
                var src = mappings[i];
                if (src == null || string.IsNullOrEmpty(src.name)) continue;
                string renderer = string.IsNullOrEmpty(src.renderer) ? null : src.renderer;
                result.Add(new BlendShapeMapping(src.name, src.value, renderer));
            }
            return result.ToArray();
        }

        private static BlendShapeMapping[] ConvertSnapshotBlendShapes(IReadOnlyList<BlendShapeSnapshotDto> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return Array.Empty<BlendShapeMapping>();
            var result = new List<BlendShapeMapping>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var src = snapshots[i];
                if (src == null || string.IsNullOrEmpty(src.name)) continue;
                string renderer = string.IsNullOrEmpty(src.rendererPath) ? null : src.rendererPath;
                result.Add(new BlendShapeMapping(src.name, src.value, renderer));
            }
            return result.ToArray();
        }

        private static ExpressionSnapshot ConvertExpressionSnapshot(OverlaySnapshotDto dto, string fallbackId)
        {
            return new ExpressionSnapshot(
                fallbackId ?? string.Empty,
                dto != null ? dto.transitionDuration : Expression.DefaultTransitionDuration,
                ConvertSnapshotTransitionCurvePreset(dto != null ? dto.transitionCurvePreset : null),
                ConvertBlendShapeSnapshots(dto != null ? dto.blendShapes : null),
                ConvertBoneSnapshots(dto != null ? dto.bones : null),
                ConvertSnapshotRendererPaths(dto != null ? dto.rendererPaths : null));
        }

        private static BlendShapeSnapshot[] ConvertBlendShapeSnapshots(IReadOnlyList<BlendShapeSnapshotDto> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return Array.Empty<BlendShapeSnapshot>();
            var result = new List<BlendShapeSnapshot>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var src = snapshots[i];
                if (src == null || string.IsNullOrEmpty(src.name)) continue;
                result.Add(new BlendShapeSnapshot(src.rendererPath, src.name, src.value));
            }
            return result.Count == 0 ? Array.Empty<BlendShapeSnapshot>() : result.ToArray();
        }

        private static BoneSnapshot[] ConvertBoneSnapshots(IReadOnlyList<BoneSnapshotDto> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return Array.Empty<BoneSnapshot>();
            var result = new List<BoneSnapshot>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var src = snapshots[i];
                if (src == null || string.IsNullOrEmpty(src.bonePath)) continue;
                result.Add(new BoneSnapshot(
                    src.bonePath,
                    src.position.x,
                    src.position.y,
                    src.position.z,
                    src.rotationEuler.x,
                    src.rotationEuler.y,
                    src.rotationEuler.z,
                    src.scale.x,
                    src.scale.y,
                    src.scale.z));
            }
            return result.Count == 0 ? Array.Empty<BoneSnapshot>() : result.ToArray();
        }

        private static string[] ConvertSnapshotRendererPaths(IReadOnlyList<string> rendererPaths)
        {
            if (rendererPaths == null || rendererPaths.Count == 0) return Array.Empty<string>();
            var result = new string[rendererPaths.Count];
            for (int i = 0; i < rendererPaths.Count; i++)
            {
                result[i] = rendererPaths[i] ?? string.Empty;
            }
            return result;
        }

        private static bool IsSnapshotEmpty(OverlaySnapshotDto snapshot)
        {
            return snapshot == null
                || ((snapshot.blendShapes == null || snapshot.blendShapes.Count == 0)
                    && (snapshot.bones == null || snapshot.bones.Count == 0));
        }

        private static TransitionCurvePreset ConvertSnapshotTransitionCurvePreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
                return TransitionCurvePreset.Linear;

            return preset.Trim() switch
            {
                "Linear" => TransitionCurvePreset.Linear,
                "EaseIn" => TransitionCurvePreset.EaseIn,
                "EaseOut" => TransitionCurvePreset.EaseOut,
                "EaseInOut" => TransitionCurvePreset.EaseInOut,
                _ => TransitionCurvePreset.Linear,
            };
        }

        private static ExpressionDto BuildExpressionDto(Expression expression)
        {
            var dto = new ExpressionDto
            {
                id = expression.Id,
                name = expression.Name,
                layer = expression.Layer,
                layerOverrideMask = new List<string>(),
                snapshot = new ExpressionSnapshotDto
                {
                    transitionDuration = expression.TransitionDuration,
                    transitionCurvePreset = SerializeTransitionCurvePreset(expression.TransitionCurve),
                    blendShapes = new List<BlendShapeSnapshotDto>(),
                    bones = new List<BoneSnapshotDto>(),
                    rendererPaths = new List<string>(),
                    overlays = BuildOverlaySlotBindingDtoList(expression.Overlays.Span),
                },
            };

            var blendShapeSpan = expression.BlendShapeValues.Span;
            for (int i = 0; i < blendShapeSpan.Length; i++)
            {
                dto.snapshot.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = blendShapeSpan[i].Renderer ?? string.Empty,
                    name = blendShapeSpan[i].Name,
                    value = blendShapeSpan[i].Value,
                });
            }

            return dto;
        }

        private static List<OverlaySlotBindingDto> BuildOverlaySlotBindingDtoList(
            ReadOnlySpan<OverlaySlotBinding> bindings)
        {
            var list = new List<OverlaySlotBindingDto>(bindings.Length);
            for (int i = 0; i < bindings.Length; i++)
            {
                list.Add(new OverlaySlotBindingDto
                {
                    slot = bindings[i].Slot,
                    suppress = bindings[i].Suppress,
                    snapshot = bindings[i].Snapshot.HasValue
                        ? BuildExpressionSnapshotDto(bindings[i].Snapshot.Value)
                        : null,
                });
            }
            return list;
        }

        private static OverlaySnapshotDto BuildExpressionSnapshotDto(ExpressionSnapshot snapshot)
        {
            var dto = new OverlaySnapshotDto
            {
                transitionDuration = snapshot.TransitionDuration,
                transitionCurvePreset = SerializeTransitionCurvePreset(snapshot.TransitionCurvePreset),
                blendShapes = new List<BlendShapeSnapshotDto>(),
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string>(),
            };

            var blendShapeSpan = snapshot.BlendShapes.Span;
            for (int i = 0; i < blendShapeSpan.Length; i++)
            {
                dto.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = blendShapeSpan[i].RendererPath ?? string.Empty,
                    name = blendShapeSpan[i].Name ?? string.Empty,
                    value = blendShapeSpan[i].Value,
                });
            }

            var boneSpan = snapshot.Bones.Span;
            for (int i = 0; i < boneSpan.Length; i++)
            {
                var src = boneSpan[i];
                dto.bones.Add(new BoneSnapshotDto
                {
                    bonePath = src.BonePath ?? string.Empty,
                    position = new Vector3(src.PositionX, src.PositionY, src.PositionZ),
                    rotationEuler = new Vector3(src.EulerX, src.EulerY, src.EulerZ),
                    scale = new Vector3(src.ScaleX, src.ScaleY, src.ScaleZ),
                });
            }

            var rendererPathSpan = snapshot.RendererPaths.Span;
            for (int i = 0; i < rendererPathSpan.Length; i++)
            {
                dto.rendererPaths.Add(rendererPathSpan[i] ?? string.Empty);
            }

            return dto;
        }

        private static List<InputSourceDto> BuildInputSourceDtoList(InputSourceDeclaration[] declarations)
        {
            if (declarations != null && declarations.Length > 0)
            {
                var list = new List<InputSourceDto>(declarations.Length);
                for (int i = 0; i < declarations.Length; i++)
                {
                    var declaration = declarations[i];
                    list.Add(new InputSourceDto
                    {
                        id = declaration.Id,
                        weight = declaration.Weight,
                        optionsJson = string.IsNullOrEmpty(declaration.OptionsJson) ? null : declaration.OptionsJson,
                    });
                }
                return list;
            }

            return new List<InputSourceDto>
            {
                new InputSourceDto { id = "input", weight = 1.0f },
            };
        }

        private static string SerializeExclusionMode(ExclusionMode mode)
        {
            return mode switch
            {
                ExclusionMode.LastWins => "lastWins",
                ExclusionMode.Blend => "blend",
                _ => "lastWins",
            };
        }

        private static string SerializeTransitionCurvePreset(TransitionCurve curve)
        {
            return curve.Type switch
            {
                TransitionCurveType.Linear => "Linear",
                TransitionCurveType.EaseIn => "EaseIn",
                TransitionCurveType.EaseOut => "EaseOut",
                TransitionCurveType.EaseInOut => "EaseInOut",
                _ => "Linear",
            };
        }

        private static string SerializeTransitionCurvePreset(TransitionCurvePreset preset)
        {
            return preset switch
            {
                TransitionCurvePreset.Linear => "Linear",
                TransitionCurvePreset.EaseIn => "EaseIn",
                TransitionCurvePreset.EaseOut => "EaseOut",
                TransitionCurvePreset.EaseInOut => "EaseInOut",
                _ => "Linear",
            };
        }

        private static TransitionCurve ConvertTransitionCurve(TransitionCurveSerializable curve)
        {
            if (curve == null) return TransitionCurve.Linear;
            CurveKeyFrame[] keys;
            if (curve.keys == null || curve.keys.Count == 0) { keys = Array.Empty<CurveKeyFrame>(); }
            else
            {
                keys = new CurveKeyFrame[curve.keys.Count];
                for (int i = 0; i < curve.keys.Count; i++)
                {
                    var k = curve.keys[i] ?? new CurveKeyFrameSerializable();
                    keys[i] = new CurveKeyFrame(k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight, k.weightedMode);
                }
            }
            return new TransitionCurve(curve.type, keys);
        }

        private static TransitionCurve ConvertTransitionCurvePreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
                return TransitionCurve.Linear;

            return preset.Trim() switch
            {
                "Linear"    => new TransitionCurve(TransitionCurveType.Linear),
                "EaseIn"    => new TransitionCurve(TransitionCurveType.EaseIn),
                "EaseOut"   => new TransitionCurve(TransitionCurveType.EaseOut),
                "EaseInOut" => new TransitionCurve(TransitionCurveType.EaseInOut),
                _ => TransitionCurve.Linear
            };
        }

        private static string[] ConvertStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return Array.Empty<string>();
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i] ?? string.Empty;
            return result;
        }
    }
}
