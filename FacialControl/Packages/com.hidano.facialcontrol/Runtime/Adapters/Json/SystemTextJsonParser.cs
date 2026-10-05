using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Json
{
    /// <summary>
    /// IJsonParser の実装。Unity の JsonUtility をベースに、
    /// プロファイル JSON 専用 DTO 群（<see cref="ProfileSnapshotDto"/> 等）を介して
    /// ドメインモデルとの変換を行う。
    /// <para>
    /// schemaVersion <c>"1.0"</c> 以外を
    /// <see cref="Debug.LogError(object)"/> + <see cref="NotSupportedException"/> で拒否する。
    /// </para>
    /// </summary>
    public sealed class SystemTextJsonParser : IJsonParser
    {
        /// <summary>
        /// プロファイル JSON のサポートバージョン文字列（"1.0"）。
        /// </summary>
        public const string SchemaVersionV2 = "1.0";

        /// <summary>
        /// 設定 JSON のサポートバージョン。Profile JSON とは別系統で v1.0 を維持する。
        /// </summary>
        private const string SupportedConfigSchemaVersion = "1.0";

        /// <inheritdoc/>
        public FacialProfile ParseProfile(string json)
        {
            var dto = ParseProfileSnapshotV2Internal(json, out string preprocessed);
            var inputSources = ExtractInputSources(dto);
            return ConvertToProfile(dto, inputSources);
        }

        /// <summary>
        /// <c>layers[].inputSources[]</c> をパースして、レイヤー順に <see cref="InputSourceDto"/> 配列を返す。
        /// schemaVersion は <see cref="SchemaVersionV2"/> を要求する。
        /// </summary>
        public InputSourceDto[][] ParseLayerInputSources(string json)
        {
            var dto = ParseProfileSnapshotV2Internal(json, out _);
            return ExtractInputSources(dto);
        }

        /// <summary>
        /// プロファイル JSON の専用パース経路。
        /// <c>schemaVersion</c> が <see cref="SchemaVersionV2"/> 以外の場合は
        /// <see cref="Debug.LogError(object)"/> + <see cref="NotSupportedException"/> で拒否する。
        /// 欠落 / null の snapshot は既定値（<c>transitionDuration=1/15</c>, <c>transitionCurvePreset="Linear"</c>,
        /// 各配列空）に正規化される。
        /// </summary>
        public ProfileSnapshotDto ParseProfileSnapshotV2(string json)
        {
            return ParseProfileSnapshotV2Internal(json, out _);
        }

        private ProfileSnapshotDto ParseProfileSnapshotV2Internal(string json, out string preprocessed)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON 文字列を空にすることはできません。", nameof(json));

            LegacyOverlayFieldDetector.RejectLegacyExpressionIdInOverlays(json);
            WarnIfLegacyGazeConfigsKey(json);
            preprocessed = PreprocessInputSourceOptions(json);

            ProfileSnapshotDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProfileSnapshotDto>(preprocessed);
            }
            catch (Exception ex)
            {
                throw new FormatException("プロファイル JSON (v1.0) のパースに失敗しました。", ex);
            }

            if (dto == null)
                throw new FormatException("プロファイル JSON (v1.0) のパースに失敗しました。結果が null です。");

            if (string.IsNullOrEmpty(dto.schemaVersion) || dto.schemaVersion != SchemaVersionV2)
            {
                var actual = string.IsNullOrEmpty(dto.schemaVersion) ? "<missing>" : dto.schemaVersion;
                Debug.LogError(
                    $"SystemTextJsonParser: プロファイル JSON schema v1.0 の strict チェックに失敗しました。" +
                    $"期待値 '{SchemaVersionV2}'、実際 '{actual}'。未サポートの schema は拒否されます。");
                throw new NotSupportedException(
                    $"サポートされていないスキーマバージョンです: '{actual}' (期待値 '{SchemaVersionV2}')。");
            }

            NormalizeProfileSnapshotDto(dto);
            ValidateLayerInputSources(dto);
            return dto;
        }

        /// <summary>
        /// 後処理: null collection を空 collection に、欠落 snapshot を既定値の
        /// <see cref="ExpressionSnapshotDto"/> に正規化する。
        /// </summary>
        private static void NormalizeProfileSnapshotDto(ProfileSnapshotDto dto)
        {
            if (dto.layers == null)
                dto.layers = new List<LayerDefinitionDto>();
            else
                dto.layers = RemoveDuplicateLayers(dto.layers);
            if (dto.expressions == null)
                dto.expressions = new List<ExpressionDto>();
            if (dto.slots == null)
                dto.slots = new List<string>();
            if (dto.rendererPaths == null)
                dto.rendererPaths = new List<string>();
            // gaze は新スキーマのセクションとして常に利用可能な形にする。
            // JSON にセクションまたは channels がない場合も、後段の Converter が
            // 既定チャネルを補完できるよう null をここで空コレクションへ正規化する。
            if (dto.gaze == null)
                dto.gaze = new GazeSectionDto();
            if (dto.gaze.channels == null)
                dto.gaze.channels = new List<GazeChannelDto>();
            if (dto.defaultOverlays == null)
                dto.defaultOverlays = new List<OverlaySlotBindingDto>();
            NormalizeOverlaySlotBindingDtos(dto.defaultOverlays);
            dto.baseExpression = NormalizeExpressionSnapshotDto(dto.baseExpression);

            for (int i = 0; i < dto.expressions.Count; i++)
            {
                var expr = dto.expressions[i];
                if (expr == null)
                    continue;

                if (expr.layerOverrideMask == null)
                    expr.layerOverrideMask = new List<string>();

                expr.snapshot = NormalizeExpressionSnapshotDto(expr.snapshot);
                NormalizeOverlaySlotBindingDtos(expr.snapshot.overlays);
            }
        }

        private static List<LayerDefinitionDto> RemoveDuplicateLayers(List<LayerDefinitionDto> layers)
        {
            var accepted = new List<LayerDefinitionDto>(layers.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool warned = false;

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer != null && !names.Add(layer.name))
                {
                    if (!warned)
                    {
                        Debug.LogWarning(
                            "SystemTextJsonParser: layers にレイヤー名の重複があるため、後続の重複レイヤーを読み捨てました。");
                        warned = true;
                    }
                    continue;
                }

                accepted.Add(layer);
            }

            return accepted;
        }

        private static void NormalizeOverlaySlotBindingDtos(List<OverlaySlotBindingDto> overlays)
        {
            if (overlays == null)
                return;

            for (int i = 0; i < overlays.Count; i++)
            {
                var overlay = overlays[i];
                if (overlay == null)
                    continue;

                if (IsEffectivelyEmptyOverlaySnapshot(overlay.snapshot))
                {
                    overlay.snapshot = null;
                    continue;
                }

                overlay.snapshot = NormalizeOverlaySnapshotDto(overlay.snapshot);
            }
        }

        private static ExpressionSnapshotDto NormalizeExpressionSnapshotDto(ExpressionSnapshotDto snapshot)
        {
            if (snapshot == null)
            {
                snapshot = new ExpressionSnapshotDto
                {
                    transitionDuration = Expression.DefaultTransitionDuration,
                    transitionCurvePreset = "Linear",
                };
            }

            NormalizeCommonSnapshotFields(snapshot);
            if (snapshot.overlays == null)
                snapshot.overlays = new List<OverlaySlotBindingDto>();

            return snapshot;
        }

        /// <summary>
        /// overlay binding 用の snapshot（再帰終端型 <see cref="OverlaySnapshotDto"/>）を正規化する。
        /// expression 用の <see cref="NormalizeExpressionSnapshotDto"/> と異なり overlays フィールドを持たない。
        /// </summary>
        private static OverlaySnapshotDto NormalizeOverlaySnapshotDto(OverlaySnapshotDto snapshot)
        {
            if (snapshot == null)
            {
                snapshot = new OverlaySnapshotDto
                {
                    transitionDuration = Expression.DefaultTransitionDuration,
                    transitionCurvePreset = "Linear",
                };
            }

            NormalizeCommonSnapshotFields(snapshot);
            return snapshot;
        }

        /// <summary>
        /// <see cref="OverlaySnapshotDto"/>（および派生 <see cref="ExpressionSnapshotDto"/>）共通フィールドの
        /// null collection / 空 preset を正規化する。
        /// </summary>
        private static void NormalizeCommonSnapshotFields(OverlaySnapshotDto snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.transitionCurvePreset))
                snapshot.transitionCurvePreset = "Linear";
            if (snapshot.blendShapes == null)
                snapshot.blendShapes = new List<BlendShapeSnapshotDto>();
            if (snapshot.bones == null)
                snapshot.bones = new List<BoneSnapshotDto>();
            if (snapshot.rendererPaths == null)
                snapshot.rendererPaths = new List<string>();
        }

        // preview 破壊的変更 (D-5): inputSources は必須フィールド。欠落 / 空配列はエラー。
        private static void ValidateLayerInputSources(ProfileSnapshotDto dto)
        {
            if (dto.layers == null)
                return;

            for (int i = 0; i < dto.layers.Count; i++)
            {
                var layer = dto.layers[i];
                if (layer == null)
                    continue;

                if (layer.inputSources == null || layer.inputSources.Count == 0)
                {
                    throw new FormatException(
                        $"レイヤー '{layer.name}' に必須フィールド 'inputSources' が欠落しています。" +
                        "preview 段階の破壊的変更 (D-5) により、inputSources は各レイヤー必須かつ非空配列である必要があります。");
                }
            }
        }

        // inputSources エントリの識別子検証と重複解決。
        private static InputSourceDto[][] ExtractInputSources(ProfileSnapshotDto dto)
        {
            if (dto.layers == null || dto.layers.Count == 0)
                return Array.Empty<InputSourceDto[]>();

            var result = new InputSourceDto[dto.layers.Count][];
            var lastValidIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicateWarned = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < dto.layers.Count; i++)
            {
                var layer = dto.layers[i];
                var entries = layer.inputSources;
                lastValidIndexById.Clear();
                duplicateWarned.Clear();

                var validSchema = new bool[entries.Count];
                for (int j = 0; j < entries.Count; j++)
                {
                    var src = entries[j];
                    var rawId = src.id;

                    if (!InputSourceId.TryParse(rawId, out _))
                    {
                        Debug.LogWarning(
                            $"SystemTextJsonParser: レイヤー '{layer.name}' の inputSources[{j}] に不正な識別子 '{rawId ?? "<null>"}' が指定されました。" +
                            "識別子は [a-zA-Z0-9_.\\-:]{1,64} を満たす必要があります (D-5 により 'legacy' は受理されません)。スキップします。");
                        continue;
                    }

                    validSchema[j] = true;
                    lastValidIndexById[rawId] = j;
                }

                var accepted = new List<InputSourceDto>(entries.Count);
                for (int j = 0; j < entries.Count; j++)
                {
                    if (!validSchema[j])
                        continue;

                    var src = entries[j];
                    if (lastValidIndexById[src.id] != j)
                    {
                        if (duplicateWarned.Add(src.id))
                        {
                            Debug.LogWarning(
                                $"SystemTextJsonParser: レイヤー '{layer.name}' に同一識別子 '{src.id}' の inputSources エントリが重複しています。最後の出現を採用します (last-wins)。");
                        }
                        continue;
                    }

                    accepted.Add(new InputSourceDto
                    {
                        id = src.id,
                        weight = src.weight,
                        optionsJson = string.IsNullOrEmpty(src.optionsJson) ? null : src.optionsJson
                    });
                }

                result[i] = accepted.ToArray();
            }
            return result;
        }

        /// <inheritdoc/>
        public string SerializeProfile(FacialProfile profile)
        {
            var dto = ConvertToProfileSnapshotDto(profile);
            NormalizeProfileSnapshotDto(dto);
            var raw = JsonUtility.ToJson(dto, true);
            return PostprocessInputSourceOptions(raw);
        }

        /// <summary>
        /// 既に組み立て済みの <see cref="ProfileSnapshotDto"/> を schema v1.0 互換 JSON 文字列にシリアライズする。
        /// AutoExporter が AnimationClip サンプリング結果を直接 JSON へ書き出す経路で使用する。
        /// </summary>
        /// <param name="dto">トップレベル DTO。<see cref="ProfileSnapshotDto.schemaVersion"/> が空の場合は <see cref="SchemaVersionV2"/> を補完する。</param>
        /// <returns>JsonUtility 整形済み JSON 文字列（<c>options</c> フィールドは生 JSON ブロックへ復元済）。</returns>
        public string SerializeProfileSnapshot(ProfileSnapshotDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrEmpty(dto.schemaVersion))
                dto.schemaVersion = SchemaVersionV2;
            NormalizeProfileSnapshotDto(dto);
            var raw = JsonUtility.ToJson(dto, true);
            return PostprocessInputSourceOptions(raw);
        }

        /// <inheritdoc/>
        public FacialControlConfig ParseConfig(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON 文字列を空にすることはできません。", nameof(json));

            ConfigDto dto;
            try
            {
                dto = JsonUtility.FromJson<ConfigDto>(json);
            }
            catch (Exception ex)
            {
                throw new FormatException("設定 JSON のパースに失敗しました。", ex);
            }

            if (dto == null)
                throw new FormatException("設定 JSON のパースに失敗しました。結果が null です。");

            if (string.IsNullOrEmpty(dto.schemaVersion))
                throw new FormatException("schemaVersion が指定されていません。");

            if (dto.schemaVersion != SupportedConfigSchemaVersion)
                throw new FormatException(
                    $"サポートされていない設定スキーマバージョンです: {dto.schemaVersion}（サポート対象: {SupportedConfigSchemaVersion}）");

            return ConvertToConfig(dto);
        }

        /// <inheritdoc/>
        public string SerializeConfig(FacialControlConfig config)
        {
            var dto = ConvertToConfigDto(config);
            return JsonUtility.ToJson(dto, true);
        }

        // ====================================================================
        // options 抽出ヘルパー（PreprocessInputSourceOptions / PostprocessInputSourceOptions）
        // ====================================================================

        private static string PreprocessInputSourceOptions(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            const string OptionsKey = "\"options\"";
            var sb = new StringBuilder(json.Length + 64);
            int i = 0;

            while (i < json.Length)
            {
                int keyStart = json.IndexOf(OptionsKey, i, StringComparison.Ordinal);
                if (keyStart < 0)
                {
                    sb.Append(json, i, json.Length - i);
                    break;
                }

                sb.Append(json, i, keyStart - i);

                int afterKey = keyStart + OptionsKey.Length;
                int colonIdx = afterKey;
                while (colonIdx < json.Length && char.IsWhiteSpace(json[colonIdx]))
                    colonIdx++;

                if (colonIdx >= json.Length || json[colonIdx] != ':')
                {
                    sb.Append(json, keyStart, afterKey - keyStart);
                    i = afterKey;
                    continue;
                }

                int valueStart = colonIdx + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                    valueStart++;

                if (valueStart >= json.Length || json[valueStart] != '{')
                {
                    sb.Append(json, keyStart, valueStart - keyStart);
                    i = valueStart;
                    continue;
                }

                int objectEnd = FindMatchingBrace(json, valueStart);
                if (objectEnd < 0)
                {
                    sb.Append(json, keyStart, json.Length - keyStart);
                    i = json.Length;
                    break;
                }

                string rawOptions = json.Substring(valueStart, objectEnd - valueStart + 1);
                sb.Append("\"optionsJson\":\"");
                AppendJsonEscaped(sb, rawOptions);
                sb.Append('"');

                i = objectEnd + 1;
            }

            return sb.ToString();
        }

        private bool _legacyGazeWarningIssued;

        private void WarnIfLegacyGazeConfigsKey(string json)
        {
            if (_legacyGazeWarningIssued || !ContainsJsonKey(json, "gaze_configs"))
                return;

            _legacyGazeWarningIssued = true;
            Debug.LogWarning("[FacialControl] 旧 profile.json の gaze データを検出しました。新しい gaze.channels スキーマへ移行してください。");
        }

        private static bool ContainsJsonKey(string json, string key)
        {
            string quotedKey = "\"" + key + "\"";
            int cursor = 0;
            while ((cursor = json.IndexOf(quotedKey, cursor, StringComparison.Ordinal)) >= 0)
            {
                int after = cursor + quotedKey.Length;
                while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
                if (after < json.Length && json[after] == ':') return true;
                cursor = after;
            }
            return false;
        }

        private static int FindMatchingBrace(string json, int openIndex)
        {
            int depth = 1;
            int p = openIndex + 1;
            bool inString = false;
            bool escaped = false;

            while (p < json.Length)
            {
                char c = json[p];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                }
                else
                {
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return p;
                    }
                }
                p++;
            }
            return -1;
        }

        private static void AppendJsonEscaped(StringBuilder sb, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
        }

        private static List<InputSourceDto> BuildInputSourceDtoList(InputSourceDeclaration[] declarations)
        {
            if (declarations != null && declarations.Length > 0)
            {
                var list = new List<InputSourceDto>(declarations.Length);
                for (int j = 0; j < declarations.Length; j++)
                {
                    var d = declarations[j];
                    list.Add(new InputSourceDto
                    {
                        id = d.Id,
                        weight = d.Weight,
                        optionsJson = string.IsNullOrEmpty(d.OptionsJson) ? null : d.OptionsJson
                    });
                }
                return list;
            }

            return new List<InputSourceDto>
            {
                new InputSourceDto { id = "input", weight = 1.0f }
            };
        }

        private static string PostprocessInputSourceOptions(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            const string KeyQuoted = "\"optionsJson\"";
            var sb = new StringBuilder(json.Length);
            int cursor = 0;

            while (cursor < json.Length)
            {
                int keyStart = json.IndexOf(KeyQuoted, cursor, StringComparison.Ordinal);
                if (keyStart < 0)
                {
                    sb.Append(json, cursor, json.Length - cursor);
                    break;
                }

                int afterKey = keyStart + KeyQuoted.Length;
                int colonIdx = afterKey;
                while (colonIdx < json.Length && char.IsWhiteSpace(json[colonIdx]))
                    colonIdx++;

                if (colonIdx >= json.Length || json[colonIdx] != ':')
                {
                    sb.Append(json, cursor, afterKey - cursor);
                    cursor = afterKey;
                    continue;
                }

                int valueStart = colonIdx + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                    valueStart++;

                if (valueStart >= json.Length || json[valueStart] != '"')
                {
                    sb.Append(json, cursor, valueStart - cursor);
                    cursor = valueStart;
                    continue;
                }

                int valueEnd = FindMatchingQuote(json, valueStart);
                if (valueEnd < 0)
                {
                    sb.Append(json, cursor, json.Length - cursor);
                    cursor = json.Length;
                    break;
                }

                string escaped = json.Substring(valueStart + 1, valueEnd - valueStart - 1);

                if (escaped.Length == 0)
                {
                    int removeFromBack = keyStart;
                    int p = keyStart - 1;
                    while (p >= 0 && char.IsWhiteSpace(json[p]))
                        p--;

                    int afterValue = valueEnd + 1;
                    if (p >= 0 && json[p] == ',')
                    {
                        removeFromBack = p;
                    }
                    else
                    {
                        int q = afterValue;
                        while (q < json.Length && char.IsWhiteSpace(json[q]))
                            q++;
                        if (q < json.Length && json[q] == ',')
                            afterValue = q + 1;
                    }

                    sb.Append(json, cursor, removeFromBack - cursor);
                    cursor = afterValue;
                }
                else
                {
                    sb.Append(json, cursor, keyStart - cursor);
                    sb.Append("\"options\":");
                    sb.Append(UnescapeJsonString(escaped));
                    cursor = valueEnd + 1;
                }
            }

            return sb.ToString();
        }

        private static int FindMatchingQuote(string json, int openIndex)
        {
            int p = openIndex + 1;
            bool escaped = false;
            while (p < json.Length)
            {
                char c = json[p];
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { return p; }
                p++;
            }
            return -1;
        }

        private static string UnescapeJsonString(string escaped)
        {
            var sb = new StringBuilder(escaped.Length);
            int i = 0;
            while (i < escaped.Length)
            {
                char c = escaped[i];
                if (c == '\\' && i + 1 < escaped.Length)
                {
                    char next = escaped[i + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '"': sb.Append('"'); i += 2; break;
                        case 'b': sb.Append('\b'); i += 2; break;
                        case 'f': sb.Append('\f'); i += 2; break;
                        case 'n': sb.Append('\n'); i += 2; break;
                        case 'r': sb.Append('\r'); i += 2; break;
                        case 't': sb.Append('\t'); i += 2; break;
                        case 'u':
                            if (i + 5 < escaped.Length)
                            {
                                string hex = escaped.Substring(i + 2, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
                                {
                                    sb.Append((char)code);
                                    i += 6;
                                    break;
                                }
                            }
                            sb.Append(c);
                            i++;
                            break;
                        default:
                            sb.Append(c);
                            i++;
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        // ====================================================================
        // Profile 変換 (DTO → Domain)
        // ====================================================================

        private static FacialProfile ConvertToProfile(ProfileSnapshotDto dto, InputSourceDto[][] inputSourceDtos)
        {
            var layers = ConvertLayers(dto.layers);
            var expressions = ConvertExpressions(dto.expressions, dto.layers);
            var rendererPaths = ConvertRendererPaths(dto.rendererPaths);
            var layerInputSources = ConvertLayerInputSources(inputSourceDtos);
            var defaultOverlays = ConvertOverlaySlotBindings(dto.defaultOverlays);
            var slots = ConvertStringList(dto.slots);
            // ベース表情は bake 済み BlendShape 値のみを運ぶ（AnimationClip 参照は JSON に載らない）。
            var baseExpression = ConvertBlendShapeSnapshots(dto.baseExpression?.blendShapes);
            return new FacialProfile(
                dto.schemaVersion,
                layers,
                expressions,
                rendererPaths,
                layerInputSources,
                defaultOverlays,
                slots: slots,
                baseExpression: baseExpression);
        }

        private static OverlaySlotBinding[] ConvertOverlaySlotBindings(List<OverlaySlotBindingDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return null;

            var list = new List<OverlaySlotBinding>(dtos.Count);
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                if (d == null || string.IsNullOrWhiteSpace(d.slot))
                    continue;

                var snapshotDto = IsEffectivelyEmptyOverlaySnapshot(d.snapshot) ? null : d.snapshot;
                if (d.suppress && snapshotDto != null)
                {
                    throw new FormatException(
                        $"OverlaySlotBinding slot='{d.slot}' has invalid state: suppress=true and snapshot is not null.");
                }

                try
                {
                    var snapshot = snapshotDto != null
                        ? ConvertExpressionSnapshot(snapshotDto, d.slot)
                        : (ExpressionSnapshot?)null;
                    list.Add(new OverlaySlotBinding(d.slot, d.suppress, snapshot));
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException(
                        $"OverlaySlotBinding slot='{d.slot}' の変換に失敗しました (suppress={d.suppress}, snapshot={(d.snapshot == null ? "null" : "present")})。",
                        ex);
                }
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static bool IsEffectivelyEmptyOverlaySnapshot(OverlaySnapshotDto snapshot)
        {
            if (snapshot == null)
                return true;

            return IsEmptyTransitionDuration(snapshot.transitionDuration)
                && (string.IsNullOrEmpty(snapshot.transitionCurvePreset) || snapshot.transitionCurvePreset == "Linear")
                && IsNullOrEffectivelyEmpty(snapshot.blendShapes)
                && IsNullOrEffectivelyEmpty(snapshot.bones)
                && IsNullOrEffectivelyEmpty(snapshot.rendererPaths);
        }

        private static bool IsEmptyTransitionDuration(float value)
        {
            return Math.Abs(value) <= 0.0001f
                || Math.Abs(value - Expression.DefaultTransitionDuration) <= 0.0001f;
        }

        private static bool IsNullOrEffectivelyEmpty(List<BlendShapeSnapshotDto> list)
        {
            if (list == null || list.Count == 0)
                return true;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && !string.IsNullOrEmpty(item.name))
                    return false;
            }

            return true;
        }

        private static bool IsNullOrEffectivelyEmpty(List<BoneSnapshotDto> list)
        {
            if (list == null || list.Count == 0)
                return true;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && !string.IsNullOrEmpty(item.bonePath))
                    return false;
            }

            return true;
        }

        private static bool IsNullOrEffectivelyEmpty(List<string> list)
        {
            if (list == null || list.Count == 0)
                return true;

            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrEmpty(list[i]))
                    return false;
            }

            return true;
        }

        private static ExpressionSnapshot ConvertExpressionSnapshot(OverlaySnapshotDto dto, string fallbackId)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            return new ExpressionSnapshot(
                fallbackId ?? string.Empty,
                dto.transitionDuration,
                ConvertTransitionCurvePreset(dto.transitionCurvePreset),
                ConvertBlendShapeSnapshots(dto.blendShapes),
                ConvertBoneSnapshots(dto.bones),
                ConvertStringList(dto.rendererPaths));
        }

        private static BlendShapeSnapshot[] ConvertBlendShapeSnapshots(List<BlendShapeSnapshotDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<BlendShapeSnapshot>();

            var snapshots = new List<BlendShapeSnapshot>(dtos.Count);
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                if (d == null)
                    continue;
                if (string.IsNullOrEmpty(d.name))
                    continue;

                snapshots.Add(new BlendShapeSnapshot(d.rendererPath, d.name, d.value));
            }

            return snapshots.Count == 0 ? Array.Empty<BlendShapeSnapshot>() : snapshots.ToArray();
        }

        private static BoneSnapshot[] ConvertBoneSnapshots(List<BoneSnapshotDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<BoneSnapshot>();

            var snapshots = new List<BoneSnapshot>(dtos.Count);
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                if (d == null)
                    continue;
                if (string.IsNullOrEmpty(d.bonePath))
                    continue;

                snapshots.Add(new BoneSnapshot(
                    d.bonePath,
                    d.position.x,
                    d.position.y,
                    d.position.z,
                    d.rotationEuler.x,
                    d.rotationEuler.y,
                    d.rotationEuler.z,
                    d.scale.x,
                    d.scale.y,
                    d.scale.z));
            }

            return snapshots.Count == 0 ? Array.Empty<BoneSnapshot>() : snapshots.ToArray();
        }

        private static string[] ConvertStringList(List<string> values)
        {
            if (values == null || values.Count == 0)
                return Array.Empty<string>();

            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i] ?? string.Empty;
            }

            return result;
        }

        private static InputSourceDeclaration[][] ConvertLayerInputSources(InputSourceDto[][] dtos)
        {
            if (dtos == null || dtos.Length == 0)
                return null;

            var result = new InputSourceDeclaration[dtos.Length][];
            for (int i = 0; i < dtos.Length; i++)
            {
                var inner = dtos[i];
                if (inner == null || inner.Length == 0)
                {
                    result[i] = Array.Empty<InputSourceDeclaration>();
                    continue;
                }
                var arr = new InputSourceDeclaration[inner.Length];
                for (int j = 0; j < inner.Length; j++)
                {
                    arr[j] = new InputSourceDeclaration(inner[j].id, inner[j].weight, inner[j].optionsJson);
                }
                result[i] = arr;
            }
            return result;
        }

        private static string[] ConvertRendererPaths(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return null;

            return paths.ToArray();
        }

        private static LayerDefinition[] ConvertLayers(List<LayerDefinitionDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<LayerDefinition>();

            var layers = new LayerDefinition[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                var exclusionMode = ParseExclusionMode(d.exclusionMode);
                layers[i] = new LayerDefinition(d.name, d.priority, exclusionMode);
            }
            return layers;
        }

        private static Expression[] ConvertExpressions(List<ExpressionDto> dtos, List<LayerDefinitionDto> layerDtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<Expression>();

            // OverrideMask の bit position ↔ layer 名の対応表（layers の宣言順）。
            List<string> orderedLayerNames = null;
            if (layerDtos != null)
            {
                orderedLayerNames = new List<string>(layerDtos.Count);
                for (int li = 0; li < layerDtos.Count; li++)
                {
                    orderedLayerNames.Add(layerDtos[li]?.name);
                }
            }

            var expressions = new Expression[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                var snapshot = d.snapshot;
                float duration = snapshot != null ? snapshot.transitionDuration : Expression.DefaultTransitionDuration;
                var curve = ConvertTransitionCurve(snapshot != null ? snapshot.transitionCurvePreset : "Linear");
                var blendShapes = ConvertBlendShapeMappings(snapshot != null ? snapshot.blendShapes : null);
                var overlays = ConvertOverlaySlotBindings(snapshot != null ? snapshot.overlays : null);
                var overrideMask = ToOverrideMask(d.layerOverrideMask, orderedLayerNames);

                expressions[i] = new Expression(
                    d.id,
                    d.name,
                    d.layer,
                    duration,
                    curve,
                    blendShapes,
                    overlays,
                    overrideMask);
            }
            return expressions;
        }

        /// <summary>
        /// expression の layerOverrideMask（layer 名配列）を <see cref="LayerOverrideMask"/> へ変換する。
        /// bit position は orderedLayerNames（layers の宣言順）の index に対応する。
        /// 未登録の名前および 32 番目以降の bit はサイレントに無視する。
        /// </summary>
        private static LayerOverrideMask ToOverrideMask(List<string> layerNames, List<string> orderedLayerNames)
        {
            if (layerNames == null || layerNames.Count == 0
                || orderedLayerNames == null || orderedLayerNames.Count == 0)
            {
                return LayerOverrideMask.None;
            }
            int mask = 0;
            for (int i = 0; i < layerNames.Count; i++)
            {
                int idx = orderedLayerNames.IndexOf(layerNames[i]);
                if (idx >= 0 && idx < 32)
                {
                    mask |= (1 << idx);
                }
            }
            return (LayerOverrideMask)mask;
        }

        private static BlendShapeMapping[] ConvertBlendShapeMappings(List<BlendShapeSnapshotDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<BlendShapeMapping>();

            var mappings = new BlendShapeMapping[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                mappings[i] = new BlendShapeMapping(
                    d.name,
                    d.value,
                    string.IsNullOrEmpty(d.rendererPath) ? null : d.rendererPath);
            }
            return mappings;
        }

        private static TransitionCurve ConvertTransitionCurve(string preset)
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

        private static ExclusionMode ParseExclusionMode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return ExclusionMode.LastWins;

            return value.ToLowerInvariant() switch
            {
                "lastwins" => ExclusionMode.LastWins,
                "blend" => ExclusionMode.Blend,
                _ => throw new FormatException($"不正な ExclusionMode 値: {value}")
            };
        }

        // ====================================================================
        // Profile → DTO 変換 (Domain → DTO)
        // ====================================================================

        private static ProfileSnapshotDto ConvertToProfileSnapshotDto(FacialProfile profile)
        {
            var dto = new ProfileSnapshotDto
            {
                schemaVersion = SchemaVersionV2,
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string>(),
                gaze = new GazeSectionDto { channels = new List<GazeChannelDto>() },
                defaultOverlays = BuildOverlaySlotBindingDtoList(profile.DefaultOverlays.Span),
                slots = new List<string>(),
            };

            var slotsSpan = profile.Slots.Span;
            for (int i = 0; i < slotsSpan.Length; i++)
            {
                dto.slots.Add(slotsSpan[i]);
            }

            var rendererPathsSpan = profile.RendererPaths.Span;
            for (int i = 0; i < rendererPathsSpan.Length; i++)
            {
                dto.rendererPaths.Add(rendererPathsSpan[i]);
            }

            var layerSpan = profile.Layers.Span;
            var lisSpan = profile.LayerInputSources.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                dto.layers.Add(new LayerDefinitionDto
                {
                    name = layerSpan[i].Name,
                    priority = layerSpan[i].Priority,
                    exclusionMode = SerializeExclusionMode(layerSpan[i].ExclusionMode),
                    inputSources = BuildInputSourceDtoList(i < lisSpan.Length ? lisSpan[i] : null)
                });
            }

            var exprSpan = profile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                dto.expressions.Add(ConvertToExpressionDto(exprSpan[i]));
            }

            dto.baseExpression = BuildBaseExpressionSnapshotDto(profile.BaseExpression.Span);

            return dto;
        }

        /// <summary>
        /// ベース表情 (<see cref="FacialProfile.BaseExpression"/>) を JSON DTO へ変換する。
        /// 通常 Expression と同じ <see cref="ExpressionSnapshotDto"/> を流用するが、
        /// 遷移メタ / bones / overlays は持たず blendShapes と rendererPaths のみを出力する。
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

        private static ExpressionDto ConvertToExpressionDto(Expression expr)
        {
            var dto = new ExpressionDto
            {
                id = expr.Id,
                name = expr.Name,
                layer = expr.Layer,
                layerOverrideMask = new List<string>(),
                snapshot = new ExpressionSnapshotDto
                {
                    transitionDuration = expr.TransitionDuration,
                    transitionCurvePreset = SerializeTransitionCurvePreset(expr.TransitionCurve),
                    blendShapes = new List<BlendShapeSnapshotDto>(),
                    bones = new List<BoneSnapshotDto>(),
                    rendererPaths = new List<string>(),
                    overlays = BuildOverlaySlotBindingDtoList(expr.Overlays.Span),
                }
            };

            var bsSpan = expr.BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                dto.snapshot.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = bsSpan[i].Renderer ?? string.Empty,
                    name = bsSpan[i].Name,
                    value = bsSpan[i].Value
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

        private static ExpressionSnapshotDto BuildExpressionSnapshotDto(ExpressionSnapshot snapshot)
        {
            var dto = new ExpressionSnapshotDto
            {
                transitionDuration = snapshot.TransitionDuration,
                transitionCurvePreset = SerializeTransitionCurvePreset(snapshot.TransitionCurvePreset),
                blendShapes = new List<BlendShapeSnapshotDto>(),
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string>(),
                overlays = new List<OverlaySlotBindingDto>(),
            };

            var blendShapeSpan = snapshot.BlendShapes.Span;
            for (int i = 0; i < blendShapeSpan.Length; i++)
            {
                dto.blendShapes.Add(new BlendShapeSnapshotDto
                {
                    rendererPath = blendShapeSpan[i].RendererPath,
                    name = blendShapeSpan[i].Name,
                    value = blendShapeSpan[i].Value,
                });
            }

            var boneSpan = snapshot.Bones.Span;
            for (int i = 0; i < boneSpan.Length; i++)
            {
                dto.bones.Add(new BoneSnapshotDto
                {
                    bonePath = boneSpan[i].BonePath,
                    position = new Vector3(
                        boneSpan[i].PositionX,
                        boneSpan[i].PositionY,
                        boneSpan[i].PositionZ),
                    rotationEuler = new Vector3(
                        boneSpan[i].EulerX,
                        boneSpan[i].EulerY,
                        boneSpan[i].EulerZ),
                    scale = new Vector3(
                        boneSpan[i].ScaleX,
                        boneSpan[i].ScaleY,
                        boneSpan[i].ScaleZ),
                });
            }

            var rendererPathSpan = snapshot.RendererPaths.Span;
            for (int i = 0; i < rendererPathSpan.Length; i++)
            {
                dto.rendererPaths.Add(rendererPathSpan[i]);
            }

            return dto;
        }

        private static string SerializeExclusionMode(ExclusionMode mode)
        {
            return mode switch
            {
                ExclusionMode.LastWins => "lastWins",
                ExclusionMode.Blend => "blend",
                _ => "lastWins"
            };
        }

        private static string SerializeTransitionCurvePreset(TransitionCurve curve)
        {
            return curve.Type switch
            {
                TransitionCurveType.Linear    => "Linear",
                TransitionCurveType.EaseIn    => "EaseIn",
                TransitionCurveType.EaseOut   => "EaseOut",
                TransitionCurveType.EaseInOut => "EaseInOut",
                _ => "Linear"
            };
        }

        private static TransitionCurvePreset ConvertTransitionCurvePreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
                return TransitionCurvePreset.Linear;

            return preset.Trim() switch
            {
                "Linear"    => TransitionCurvePreset.Linear,
                "EaseIn"    => TransitionCurvePreset.EaseIn,
                "EaseOut"   => TransitionCurvePreset.EaseOut,
                "EaseInOut" => TransitionCurvePreset.EaseInOut,
                _ => TransitionCurvePreset.Linear
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
                _ => "Linear"
            };
        }

        // ====================================================================
        // Config 変換
        // ====================================================================

        private static FacialControlConfig ConvertToConfig(ConfigDto dto)
        {
            var oscMappings = ConvertOscMappings(dto.osc?.mapping);
            var osc = new OscConfiguration(
                dto.osc?.sendPort ?? OscConfiguration.DefaultSendPort,
                dto.osc?.receivePort ?? OscConfiguration.DefaultReceivePort,
                dto.osc?.preset ?? OscConfiguration.DefaultPreset,
                oscMappings);

            var cache = new CacheConfiguration(
                dto.cache?.animationClipLruSize ?? CacheConfiguration.DefaultAnimationClipLruSize);

            return new FacialControlConfig(dto.schemaVersion, osc, cache);
        }

        private static OscMapping[] ConvertOscMappings(List<OscMappingDto> dtos)
        {
            if (dtos == null || dtos.Count == 0)
                return Array.Empty<OscMapping>();

            var mappings = new OscMapping[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                mappings[i] = new OscMapping(d.oscAddress, d.blendShapeName, d.layer);
            }
            return mappings;
        }

        private static ConfigDto ConvertToConfigDto(FacialControlConfig config)
        {
            var dto = new ConfigDto
            {
                schemaVersion = config.SchemaVersion,
                osc = new OscConfigurationDto
                {
                    sendPort = config.Osc.SendPort,
                    receivePort = config.Osc.ReceivePort,
                    preset = config.Osc.Preset,
                    mapping = new List<OscMappingDto>()
                },
                cache = new CacheConfigurationDto
                {
                    animationClipLruSize = config.Cache.AnimationClipLruSize
                }
            };

            var mappingSpan = config.Osc.Mapping.Span;
            for (int i = 0; i < mappingSpan.Length; i++)
            {
                dto.osc.mapping.Add(new OscMappingDto
                {
                    oscAddress = mappingSpan[i].OscAddress,
                    blendShapeName = mappingSpan[i].BlendShapeName,
                    layer = mappingSpan[i].Layer
                });
            }

            return dto;
        }

        // ====================================================================
        // Config DTO 定義（JsonUtility 用 Serializable クラス）
        // ====================================================================

        [Serializable]
        private class ConfigDto
        {
            public string schemaVersion;
            public OscConfigurationDto osc;
            public CacheConfigurationDto cache;
        }

        [Serializable]
        private class OscConfigurationDto
        {
            public int sendPort = OscConfiguration.DefaultSendPort;
            public int receivePort = OscConfiguration.DefaultReceivePort;
            public string preset = OscConfiguration.DefaultPreset;
            public List<OscMappingDto> mapping;
        }

        [Serializable]
        private class OscMappingDto
        {
            public string oscAddress;
            public string blendShapeName;
            public string layer;
        }

        [Serializable]
        private class CacheConfigurationDto
        {
            public int animationClipLruSize = CacheConfiguration.DefaultAnimationClipLruSize;
        }
    }
}
