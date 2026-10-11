using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.LipSync.Adapters;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterProfileSO = Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO;
using LayerSerializable = Hidano.FacialControl.Adapters.ScriptableObject.Serializable.LayerDefinitionSerializable;

namespace Hidano.FacialControl.LipSync.Editor.Inspector
{
    /// <summary>
    /// uLipSync binding Inspector の発話ゲート節（設定・口まわりの押さえ込み・レイヤー構成の警告・Live Monitor）。
    /// </summary>
    /// <remarks>
    /// レイヤー構成の警告は <see cref="RefreshIntervalMs"/> ごとに取り直し、レイヤー設定の編集から
    /// 数百 ms 以内に反映する。iFacialMocap Receiver の Mappings は lipsync から ifacialmocap へ依存しないよう
    /// SerializedProperty で読む。
    /// </remarks>
    internal static class ULipSyncVoiceGateSection
    {
        public const string SectionName = "ulipsync-adapter-binding-voice-gate-section";
        public const string WarningsContainerName = "ulipsync-adapter-binding-layer-warnings";
        public const string AddMouthButtonName = "ulipsync-adapter-binding-add-capture-mouth-button";
        public const string LiveMonitorFoldoutName = "ulipsync-adapter-binding-live-monitor";
        public const string LiveMonitorLabelName = "ulipsync-adapter-binding-live-monitor-label";

        internal const int RefreshIntervalMs = 250;

        private const string AdapterBindingsPropertyName = "_adapterBindings";
        private const string SuppressNamesPropertyName = "_suppressBlendShapeNames";
        private const string VersionPropertyName = "_voiceGateSettingsVersion";
        private const string IFacialMocapBindingTypeName = "IFacialMocapReceiverAdapterBinding";
        private const string OscReceiverBindingTypeName = "OscReceiverAdapterBinding";
        private const string MappingsPropertyName = "_mappings";
        private const string MappingCaptureNamePropertyName = "ifacialMocapName";
        private const string MappingBlendShapeNamePropertyName = "blendShapeName";

        private static readonly (string path, string label)[] SettingFields =
        {
            ("_voiceGateEnabled", "Voice Gate Enabled"),
            ("_voiceOnThreshold", "Voice On Threshold"),
            ("_voiceOffThreshold", "Voice Off Threshold"),
            ("_voiceHoldTime", "Voice Hold Time"),
            ("_voiceAttackTime", "Voice Attack Time"),
            ("_voiceReleaseTime", "Voice Release Time"),
            ("_voiceStaleTimeout", "Voice Stale Timeout"),
        };

        public static void Add(VisualElement root, SerializedProperty property)
        {
            MigrateLegacySettings(property);

            var section = new VisualElement { name = SectionName };
            section.style.marginTop = 6;
            section.Add(new Label("Voice Gate（iFacialMocap 等との相互排他）"));

            for (int i = 0; i < SettingFields.Length; i++)
            {
                SerializedProperty child = property.FindPropertyRelative(SettingFields[i].path);
                if (child == null)
                {
                    section.Add(new Label($"<missing field: {SettingFields[i].path}>"));
                    continue;
                }

                section.Add(new PropertyField(child, SettingFields[i].label));
            }

            SerializedProperty suppressProperty = property.FindPropertyRelative(SuppressNamesPropertyName);
            if (suppressProperty != null)
            {
                section.Add(new PropertyField(suppressProperty, "Suppress Blend Shape Names"));
                var button = new Button(() => AppendFacialCaptureMouthNames(property))
                {
                    name = AddMouthButtonName,
                    text = "フェイシャルキャプチャの口まわりを追加",
                };
                section.Add(button);
            }

            var warnings = new VisualElement { name = WarningsContainerName };
            warnings.style.marginTop = 4;
            section.Add(warnings);

            var monitor = new Foldout { name = LiveMonitorFoldoutName, text = "Live Monitor", value = false };
            var monitorLabel = new Label { name = LiveMonitorLabelName };
            monitor.Add(monitorLabel);
            section.Add(monitor);

            root.Add(section);

            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;
            var cachedWarnings = new List<string>();
            void Refresh()
            {
                // Undo / 並べ替えで managed reference が作り直されても追従するよう、毎回取り直す。
                ULipSyncAdapterBinding binding = ResolveBinding(serializedObject, propertyPath);
                CharacterProfileSO profile = binding != null ? serializedObject.targetObject as CharacterProfileSO : null;
                RefreshWarnings(warnings, cachedWarnings, profile, binding);
                RefreshLiveMonitor(monitor, monitorLabel, binding);
            }

            Refresh();
            section.schedule.Execute(Refresh).Every(RefreshIntervalMs);
        }

        /// <summary>
        /// 発話ゲート設定を持たない（版 0）アセットの表示を既定値にそろえる。開いただけでアセットを dirty にしない
        /// （ランタイムも起動時に同じ既定値を入れる）。
        /// </summary>
        private static void MigrateLegacySettings(SerializedProperty property)
        {
            SerializedProperty version = property.FindPropertyRelative(VersionPropertyName);
            if (version == null || version.intValue > 0)
            {
                return;
            }

            ULipSyncAdapterBinding binding = GetBinding(property);
            if (binding != null)
            {
                binding.EnsureVoiceGateSettings();
                property.serializedObject.Update();
            }
        }

        private static void AppendFacialCaptureMouthNames(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();
            SerializedProperty root = serializedObject.FindProperty(property.propertyPath) ?? property;
            SerializedProperty suppressProperty = root.FindPropertyRelative(SuppressNamesPropertyName);
            if (suppressProperty == null || !suppressProperty.isArray)
            {
                return;
            }

            var existing = new List<string>(suppressProperty.arraySize);
            for (int i = 0; i < suppressProperty.arraySize; i++)
            {
                existing.Add(suppressProperty.GetArrayElementAtIndex(i).stringValue);
            }

            List<string> updated = ULipSyncMouthSuppressionPresets.AppendFacialCaptureMouthNames(
                existing,
                CollectCaptureMappings(serializedObject),
                CollectReferenceMeshBlendShapeNames(serializedObject.targetObject as CharacterProfileSO));

            if (updated.Count == existing.Count)
            {
                return;
            }

            suppressProperty.arraySize = updated.Count;
            for (int i = existing.Count; i < updated.Count; i++)
            {
                suppressProperty.GetArrayElementAtIndex(i).stringValue = updated[i];
            }

            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        /// <summary>同じキャラクターの iFacialMocap Receiver binding の Mappings（キャプチャ名, 反映先）を集める。</summary>
        internal static List<KeyValuePair<string, string>> CollectCaptureMappings(SerializedObject serializedObject)
        {
            var result = new List<KeyValuePair<string, string>>();
            SerializedProperty bindings = serializedObject.FindProperty(AdapterBindingsPropertyName);
            if (bindings == null || !bindings.isArray)
            {
                return result;
            }

            for (int i = 0; i < bindings.arraySize; i++)
            {
                SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                if (!IsBindingType(element.managedReferenceFullTypename, IFacialMocapBindingTypeName))
                {
                    continue;
                }

                SerializedProperty mappings = element.FindPropertyRelative(MappingsPropertyName);
                if (mappings == null || !mappings.isArray)
                {
                    continue;
                }

                for (int m = 0; m < mappings.arraySize; m++)
                {
                    SerializedProperty mapping = mappings.GetArrayElementAtIndex(m);
                    SerializedProperty captureName = mapping.FindPropertyRelative(MappingCaptureNamePropertyName);
                    SerializedProperty blendShapeName = mapping.FindPropertyRelative(MappingBlendShapeNamePropertyName);
                    if (captureName != null && blendShapeName != null)
                    {
                        result.Add(new KeyValuePair<string, string>(captureName.stringValue, blendShapeName.stringValue));
                    }
                }
            }

            return result;
        }

        private static HashSet<string> CollectReferenceMeshBlendShapeNames(CharacterProfileSO profile)
        {
            GameObject model = profile != null ? profile.ReferenceModel : null;
            if (model == null)
            {
                return null;
            }

            return new HashSet<string>(BlendShapeNameProvider.GetBlendShapeNames(model), StringComparer.Ordinal);
        }

        private static void RefreshWarnings(
            VisualElement container,
            List<string> cachedWarnings,
            CharacterProfileSO profile,
            ULipSyncAdapterBinding binding)
        {
            List<string> warnings = profile != null && binding != null
                ? ULipSyncLayerSetup.CollectWarnings(
                    ULipSyncLayerSetup.AddTargetLayerInputs(BuildLayers(profile.Layers), profile.AdapterBindings),
                    binding.Slug,
                    CollectCaptureSlugs(profile.AdapterBindings))
                : new List<string>();

            if (SameWarnings(cachedWarnings, warnings))
            {
                return;
            }

            cachedWarnings.Clear();
            cachedWarnings.AddRange(warnings);
            container.Clear();
            for (int i = 0; i < warnings.Count; i++)
            {
                container.Add(new HelpBox(warnings[i], HelpBoxMessageType.Warning));
            }
        }

        internal static ULipSyncLayerSetup.Layer[] BuildLayers(IReadOnlyList<LayerSerializable> layers)
        {
            if (layers == null)
            {
                return Array.Empty<ULipSyncLayerSetup.Layer>();
            }

            var result = new ULipSyncLayerSetup.Layer[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                LayerSerializable layer = layers[i];
                if (layer == null)
                {
                    result[i] = new ULipSyncLayerSetup.Layer(null, 0, null);
                    continue;
                }

                var ids = new string[layer.inputSources != null ? layer.inputSources.Count : 0];
                for (int j = 0; j < ids.Length; j++)
                {
                    ids[j] = layer.inputSources[j]?.id;
                }

                result[i] = new ULipSyncLayerSetup.Layer(layer.name, layer.priority, ids);
            }

            return result;
        }

        internal static List<string> CollectCaptureSlugs(IReadOnlyList<AdapterBindingBase> bindings)
        {
            var slugs = new List<string>();
            if (bindings == null)
            {
                return slugs;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null || binding.Disabled || string.IsNullOrEmpty(binding.Slug))
                {
                    continue;
                }

                string typeName = binding.GetType().Name;
                if ((typeName == IFacialMocapBindingTypeName || typeName == OscReceiverBindingTypeName)
                    && !slugs.Contains(binding.Slug))
                {
                    slugs.Add(binding.Slug);
                }
            }

            return slugs;
        }

        private static void RefreshLiveMonitor(Foldout monitor, Label label, ULipSyncAdapterBinding binding)
        {
            if (!monitor.value)
            {
                return;
            }

            ULipSyncVoiceGate gate = binding != null ? binding.VoiceGate : null;
            if (!EditorApplication.isPlaying || gate == null)
            {
                label.text = "Play 中に起動した binding の発話ゲートを表示します。";
                return;
            }

            var builder = new StringBuilder();
            builder.Append("Voice Gate: ").Append(binding.VoiceGateEnabled ? "ON" : "OFF").Append('\n');
            builder.Append("activity: ").Append(gate.Activity.ToString("0.000"));
            if (gate.IsStale)
            {
                builder.Append("（途絶）");
            }

            builder.Append('\n');
            builder.Append("Speaking: ").Append(gate.IsSpeaking ? "true" : "false").Append('\n');
            builder.Append("weight: ").Append(gate.Weight.ToString("0.000")).Append('\n');
            IReadOnlyList<string> layers = binding.GateLayerNames;
            builder.Append("対象レイヤー: ").Append(layers.Count == 0 ? "（なし）" : string.Join(", ", layers));
            label.text = builder.ToString();
        }

        private static ULipSyncAdapterBinding ResolveBinding(SerializedObject serializedObject, string propertyPath)
        {
            try
            {
                if (serializedObject == null || serializedObject.targetObject == null)
                {
                    return null;
                }

                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                return property != null ? GetBinding(property) : null;
            }
            catch (Exception)
            {
                // Inspector を閉じた後に破棄済みの SerializedObject を触った場合。
                return null;
            }
        }

        private static ULipSyncAdapterBinding GetBinding(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ManagedReference
                ? property.managedReferenceValue as ULipSyncAdapterBinding
                : null;
        }

        private static bool IsBindingType(string managedReferenceFullTypename, string typeName)
        {
            if (string.IsNullOrEmpty(managedReferenceFullTypename))
            {
                return false;
            }

            // 形式: "<assembly> <namespace>.<type>"
            return managedReferenceFullTypename.EndsWith("." + typeName, StringComparison.Ordinal)
                || managedReferenceFullTypename.EndsWith(" " + typeName, StringComparison.Ordinal);
        }

        private static bool SameWarnings(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
