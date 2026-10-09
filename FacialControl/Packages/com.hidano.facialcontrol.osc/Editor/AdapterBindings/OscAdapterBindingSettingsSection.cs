using System;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    /// <summary>
    /// OSC Receiver / Sender の drawer が共有する「上級設定」Foldout と、旧形式の設定からの移行 UI。
    /// </summary>
    internal static class OscAdapterBindingSettingsSection
    {
        public const string AdvancedSettingsFieldName = "_advancedSettings";
        public const string LegacySettingsFieldName = "_legacySettings";

        /// <summary>
        /// 上級設定アセットの割り当て欄を Foldout「上級設定」に入れて追加する。
        /// </summary>
        public static void AddAdvancedFoldout(
            VisualElement root,
            SerializedProperty property,
            string foldoutName,
            string fieldElementName,
            string assetTypeName)
        {
            var foldout = new Foldout
            {
                name = foldoutName,
                text = "上級設定",
                value = false,
            };
            root.Add(foldout);

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            if (advancedProp == null)
            {
                foldout.Add(new Label($"<missing field: {AdvancedSettingsFieldName}>"));
                return;
            }

            foldout.Add(new HelpBox(
                "割り当ては任意です。未割り当てなら既定値で動作します。"
                + $"値を変えたい場合は AdapterRuntimeSettingsCollection に {assetTypeName} を追加して割り当ててください。",
                HelpBoxMessageType.Info));

            foldout.Add(new PropertyField(advancedProp, "上級設定アセット")
            {
                name = fieldElementName,
            });
        }

        /// <summary>
        /// 旧形式の設定が割り当てられている間だけ、警告と「旧設定から移行」ボタンを表示する。
        /// </summary>
        public static void AddLegacyMigrationBox(
            VisualElement root,
            SerializedProperty property,
            string containerName,
            string message,
            Func<SerializedProperty, bool> migrate)
        {
            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            if (legacyProp == null)
            {
                return;
            }

            var container = new VisualElement
            {
                name = containerName,
            };
            container.Add(new HelpBox(message, HelpBoxMessageType.Warning));
            container.Add(new Button(() =>
            {
                migrate(property);
            })
            {
                text = "旧設定から移行",
            });
            root.Add(container);

            RefreshLegacyVisibility(container, legacyProp);
            container.TrackPropertyValue(legacyProp, prop => RefreshLegacyVisibility(container, prop));
        }

        /// <summary>
        /// 旧形式の受信設定から、ポートを binding へ、既定値と異なる上級設定を新しい sub-asset へ移す。
        /// </summary>
        /// <returns>移行した場合 true。旧設定が無い、または上級設定を保存できず中止した場合 false。</returns>
        public static bool MigrateReceiver(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();

            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            var legacy = legacyProp?.objectReferenceValue as OscRuntimeSettingsSO;
            if (legacy == null)
            {
                return false;
            }

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            AdapterRuntimeSettingsBase advanced = null;
            if (advancedProp != null && advancedProp.objectReferenceValue == null)
            {
                OscReceiverRuntimeSettingsSO created = OscReceiverRuntimeSettingsSO.CreateFromLegacy(legacy);
                if (!TryPrepareAdvanced(created, created.IsDefault, legacy, out advanced))
                {
                    return false;
                }
            }

            SerializedProperty portProp = property.FindPropertyRelative("_port");
            if (portProp != null)
            {
                portProp.intValue = legacy.ListenPort;
            }

            if (advanced != null)
            {
                advancedProp.objectReferenceValue = advanced;
            }

            legacyProp.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();

            if (!legacy.ReceiverEnabled)
            {
                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] 旧形式の設定 '{legacy.name}' では受信が無効 (receiverEnabled=false) でした。"
                    + "移行後の binding は起動します。受信が不要なら binding を外してください。");
            }

            Debug.Log($"[OscReceiverAdapterBinding] 旧形式の設定 '{legacy.name}' から移行しました (port={legacy.ListenPort})。");
            return true;
        }

        /// <summary>
        /// 旧形式の送信設定から、送信先リストを binding へ、既定値と異なる上級設定を新しい sub-asset へ移す。
        /// 旧設定で送信が無効だった場合は、全送信先を無効 (enabled=false) にして移し、送信しない状態を保つ。
        /// </summary>
        /// <returns>移行した場合 true。旧設定が無い、または上級設定を保存できず中止した場合 false。</returns>
        public static bool MigrateSender(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();

            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            var legacy = legacyProp?.objectReferenceValue as OscRuntimeSettingsSO;
            if (legacy == null)
            {
                return false;
            }

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            AdapterRuntimeSettingsBase advanced = null;
            if (advancedProp != null && advancedProp.objectReferenceValue == null)
            {
                OscSenderRuntimeSettingsSO created = OscSenderRuntimeSettingsSO.CreateFromLegacy(legacy);
                if (!TryPrepareAdvanced(created, created.IsDefault, legacy, out advanced))
                {
                    return false;
                }
            }

            SerializedProperty endpointsProp = property.FindPropertyRelative("_endpoints");
            if (endpointsProp != null && endpointsProp.isArray)
            {
                endpointsProp.ClearArray();
                var source = legacy.Endpoints;
                int count = source != null ? source.Count : 0;
                for (int i = 0; i < count; i++)
                {
                    OscSenderEndpointConfig src = source[i] ?? new OscSenderEndpointConfig();
                    endpointsProp.InsertArrayElementAtIndex(i);
                    SerializedProperty element = endpointsProp.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.endpoint)).stringValue =
                        src.endpoint ?? OscSenderEndpointConfig.DefaultEndpoint;
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.port)).intValue = src.port;
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.enabled)).boolValue =
                        src.enabled && legacy.SenderEnabled;
                }
            }

            if (advanced != null)
            {
                advancedProp.objectReferenceValue = advanced;
            }

            legacyProp.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();

            if (!legacy.SenderEnabled)
            {
                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] 旧形式の設定 '{legacy.name}' では送信が無効 (senderEnabled=false) でした。"
                    + "送信しない状態を保つため、移行した送信先はすべて無効にしています。");
            }

            Debug.Log($"[OscSenderAdapterBinding] 旧形式の設定 '{legacy.name}' から移行しました (送信先 {legacy.Endpoints?.Count ?? 0} 件)。");
            return true;
        }

        /// <summary>
        /// 旧設定から作った上級設定を保存する。既定値のままなら作らない（未割り当てで同じ挙動になる）。
        /// 保存できなかった場合は false を返し、呼び出し側は移行を中止する（旧設定の値を失わないため）。
        /// </summary>
        private static bool TryPrepareAdvanced(
            AdapterRuntimeSettingsBase created,
            bool isDefault,
            OscRuntimeSettingsSO legacy,
            out AdapterRuntimeSettingsBase advanced)
        {
            advanced = null;
            if (isDefault)
            {
                UnityEngine.Object.DestroyImmediate(created);
                return true;
            }

            if (!TryPersistNextTo(legacy, created))
            {
                UnityEngine.Object.DestroyImmediate(created);
                Debug.LogWarning(
                    $"[OscAdapterBinding] 旧形式の設定 '{legacy.name}' の上級設定 ({created.GetType().Name}) を保存できなかったため、移行を中止しました。"
                    + "上級設定アセットを手動で作成して割り当ててから、もう一度移行してください。");
                return false;
            }

            advanced = created;
            return true;
        }

        /// <summary>
        /// 旧設定と同じアセットファイルへ sub-asset として保存する。親が Collection なら一覧にも登録する。
        /// 旧設定が単独のアセットファイルなら、旧アセットを消しても残るよう同じフォルダに別アセットとして保存する。
        /// </summary>
        private static bool TryPersistNextTo(OscRuntimeSettingsSO legacy, AdapterRuntimeSettingsBase created)
        {
            string path = AssetDatabase.GetAssetPath(legacy);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            created.name = created.GetType().Name;
            try
            {
                UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                if (mainAsset == null || ReferenceEquals(mainAsset, legacy))
                {
                    string directory = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                    string newPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{created.name}.asset");
                    AssetDatabase.CreateAsset(created, newPath);
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(created, mainAsset);
                    if (mainAsset is AdapterRuntimeSettingsCollectionSO collection)
                    {
                        var collectionObject = new SerializedObject(collection);
                        SerializedProperty items = collectionObject.FindProperty("_items");
                        if (items != null && items.isArray)
                        {
                            int index = items.arraySize;
                            items.InsertArrayElementAtIndex(index);
                            items.GetArrayElementAtIndex(index).objectReferenceValue = created;
                            collectionObject.ApplyModifiedProperties();
                        }
                    }

                    EditorUtility.SetDirty(mainAsset);
                }

                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OscAdapterBinding] 上級設定アセットの保存に失敗しました: {e.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(created)))
            {
                return false;
            }

            Undo.RegisterCreatedObjectUndo(created, "Migrate OSC Runtime Settings");
            return true;
        }

        private static void RefreshLegacyVisibility(VisualElement container, SerializedProperty legacyProp)
        {
            bool hasLegacy = legacyProp != null && legacyProp.objectReferenceValue != null;
            container.style.display = hasLegacy ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
