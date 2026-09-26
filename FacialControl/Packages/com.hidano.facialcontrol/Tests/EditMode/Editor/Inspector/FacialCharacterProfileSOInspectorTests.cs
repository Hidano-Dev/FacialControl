using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSOInspector"/> の smoke テスト。
    /// UI ツリーの細部ではなく、次の 3 点と実機で発生した不具合の再発だけを守る。
    /// <list type="bullet">
    /// <item>生成できる: <c>CreateInspectorGUI()</c> が例外なく VisualElement を返す</item>
    /// <item>保存が通る: Overlay 編集が SerializedObject のラウンドトリップを経ても保持される
    /// （Suppress が Play 突入で Default に戻る不具合 / Default Overlays の clip が外れる不具合）</item>
    /// <item>破棄・再構築で例外を出さず、破棄時に自動保存と Foldout 状態を取りこぼさない
    /// （編集直後に別オブジェクトを選択すると保存が失われる不具合 / panel 未接続で ChangeEvent が
    /// 届かない Foldout 状態を OnDisable の一括スイープで保存する仕組み）</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSOInspectorTests
    {
        private const string ProfileName = "FacialCharacterProfileSOInspectorTestProfile";
        private const string EmotionLayerName = "Emotion";
        private const string BlinkSlotName = "blink";

        private FacialCharacterProfileSO _so;
        private UnityEditor.Editor _editor;
        private readonly List<Object> _tracked = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                // overlay 操作で予約された自動保存 delayCall を解除してから Editor を破棄する
                // （破棄時の同期フラッシュで StreamingAssets へ書き出されるのを防ぐ）。
                CancelPendingAutoSave(_editor);
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i] != null)
                {
                    Object.DestroyImmediate(_tracked[i]);
                }
            }
            _tracked.Clear();

            if (_so != null)
            {
                Object.DestroyImmediate(_so);
                _so = null;
            }

            // 自動保存フラッシュ検証テストが StreamingAssets へ書き出した profile.json を掃除する。
            DeleteStreamingAssetsExport(ProfileName);
        }

        // ====================================================================
        // smoke 1: 生成できる
        // ====================================================================

        [Test]
        public void CreateInspectorGUI_PopulatedProfile_ReturnsRootWithoutThrowing()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.BaseExpression.animationClip = CreateClip("BaseExpression_Clip");
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            VisualElement root = null;
            Assert.DoesNotThrow(() => root = BuildInspectorRoot());

            Assert.That(root, Is.Not.Null);
            Assert.That(root.Q<TabView>(FacialCharacterProfileSOInspector.TabViewName), Is.Not.Null);
        }

        // ====================================================================
        // smoke 2: 保存が通る
        // ====================================================================

        [Test]
        public void OverlayStateDropdownSelection_Suppress_SurvivesSerializedObjectRoundTrip()
        {
            // 回帰テスト（Suppress 設定が Play 突入で .asset 上 1→0 に戻る不具合）。
            // bound な ListView / ObjectField は SerializedObject の内部キャッシュを保持し、
            // Domain Reload 直前に ApplyModifiedProperties で書き戻す。Suppress 切替が
            // SerializedProperty 経由で確定されていないと、その書き戻しで suppress が
            // 旧値(false) に巻き戻る。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);

            dropdown.value = "Suppress";

            Assert.That(_so.Expressions[0].overlays[0].suppress, Is.True,
                "Suppress 切替直後の managed モデルで suppress=true である必要があります。");

            // bound UI が保持する SerializedObject の書き戻しを模す。
            _editor.serializedObject.ApplyModifiedProperties();
            Assert.That(_so.Expressions[0].overlays[0].suppress, Is.True,
                "Inspector serializedObject の ApplyModifiedProperties 後に suppress が巻き戻りました。");

            // .asset へ書かれる値（新規 SerializedObject 読み出し）でも保持されること。
            var fresh = new SerializedObject(_so);
            var suppressProp = fresh
                .FindProperty("_expressions")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("overlays")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("suppress");
            Assert.That(suppressProp, Is.Not.Null);
            Assert.That(suppressProp.boolValue, Is.True,
                "永続化対象の SerializedObject で suppress=true が保持されていません。");
        }

        [Test]
        public void DefaultOverlayClipSelection_SurvivesSerializedObjectRoundTrip()
        {
            // 回帰テスト（Default Overlays の clip が Play 突入で .asset 上から外れる不具合）。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = BlinkSlotName });

            var root = BuildInspectorRoot();
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.DefaultOverlayAnimationClipFieldName);
            Assert.That(clipField, Is.Not.Null);

            var clip = CreateClip("DefaultOverlayClipSelection_RoundTripClip");
            clipField.value = clip;

            Assert.That(_so.DefaultOverlays[0].animationClip, Is.SameAs(clip),
                "clip 割当直後の managed モデルで animationClip が保持される必要があります。");

            _editor.serializedObject.ApplyModifiedProperties();
            Assert.That(_so.DefaultOverlays[0].animationClip, Is.SameAs(clip),
                "Inspector serializedObject の ApplyModifiedProperties 後に animationClip が巻き戻りました。");

            var fresh = new SerializedObject(_so);
            var clipProp = fresh
                .FindProperty("_defaultOverlays")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("animationClip");
            Assert.That(clipProp, Is.Not.Null);
            Assert.That(clipProp.objectReferenceValue, Is.SameAs(clip),
                "永続化対象の SerializedObject で animationClip が保持されていません。");
        }

        // ====================================================================
        // smoke 3: 破棄・再構築で例外を出さない / 破棄時の取りこぼし防止
        // ====================================================================

        [Test]
        public void DestroyEditor_ThenRebuildForAnotherProfile_DoesNotThrow()
        {
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));
            BuildInspectorRoot();

            var other = CreateProfileWithSlots(BlinkSlotName);
            other.name = ProfileName + "_Other";
            _tracked.Add(other);

            Assert.DoesNotThrow(() =>
            {
                Object.DestroyImmediate(_editor);
                _editor = UnityEditor.Editor.CreateEditor(other, typeof(FacialCharacterProfileSOInspector));
                Assert.That(_editor.CreateInspectorGUI(), Is.Not.Null);
                Object.DestroyImmediate(_editor);
                _editor = null;
            });
        }

        [Test]
        public void DestroyEditor_AutoSavePending_FlushesAutoSaveOnDisable()
        {
            // 回帰テスト（編集直後に別オブジェクトを選択すると保存が失われる不具合）。
            // ScheduleAutoSave は EditorApplication.delayCall へ保存を予約するが、発火前に
            // Inspector (Editor) が破棄されると delayCall 側の FlushAutoSave は target == null で
            // 何もせず、.asset / profile.json が未保存のまま残る。破棄（OnDisable）時点で
            // 保留中の自動保存を同期確定する必要がある。
            _so = CreateProfileWithSlots(BlinkSlotName);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable { slot = BlinkSlotName }));

            var root = BuildInspectorRoot();
            var dropdown = root.Q<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            Assert.That(dropdown, Is.Not.Null);

            dropdown.value = "Suppress";

            Object.DestroyImmediate(_editor);
            _editor = null;

            string jsonPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(_so.name);
            Assert.That(File.Exists(jsonPath), Is.True,
                "Inspector 破棄時に保留中の自動保存が確定されていません（profile.json 未出力）。"
                + "破棄後の delayCall では target が null となり保存できないため、"
                + "OnDisable で FlushAutoSave を実行する必要があります。");
        }

        [Test]
        public void SectionFoldoutCollapse_PersistsAcrossInspectorRebuild()
        {
            // panel 未接続の Foldout は value 変更で ChangeEvent を発火しないため、
            // OnDisable の一括スイープ（SaveFoldoutViewStates）で SessionState へ保存される必要がある。
            _so = CreateProfileWithSlots(BlinkSlotName);

            var root = BuildInspectorRoot();
            var foldout = root.Q<Foldout>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName);
            Assert.That(foldout, Is.Not.Null);
            Assert.That(foldout.value, Is.True, "前提: Default Overlays セクションは既定で展開されています。");

            foldout.value = false;

            Object.DestroyImmediate(_editor);
            _editor = null;
            root = BuildInspectorRoot();
            foldout = root.Q<Foldout>(FacialCharacterProfileSOInspector.DefaultOverlaysFoldoutName);

            Assert.That(foldout.value, Is.False,
                "Inspector 再構築後も直前の Foldout 折りたたみ状態が復元される必要があります。");
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private static FacialCharacterProfileSO CreateProfileWithSlots(params string[] slots)
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.name = ProfileName;
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = EmotionLayerName,
                priority = 0,
            });
            SetSlots(so, slots);
            return so;
        }

        private static ExpressionSerializable CreateExpression(OverlaySlotBindingSerializable binding)
        {
            return new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = EmotionLayerName,
                overlays = new List<OverlaySlotBindingSerializable> { binding },
            };
        }

        private AnimationClip CreateClip(string name)
        {
            var clip = new AnimationClip { name = name };
            _tracked.Add(clip);
            return clip;
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serialized = new SerializedObject(so);
            serialized.Update();
            var slotsProperty = serialized.FindProperty("_slots");
            Assert.That(slotsProperty, Is.Not.Null, "_slots SerializedProperty が見つかりません。");
            slotsProperty.ClearArray();

            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.InsertArrayElementAtIndex(i);
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// テスト後始末専用: overlay 操作で予約された自動保存を解除し、破棄時の同期フラッシュで
        /// StreamingAssets へ書き出されないようにする（private への reflection は後始末目的のみ）。
        /// </summary>
        private static void CancelPendingAutoSave(UnityEditor.Editor editor)
        {
            if (editor == null) return;

            const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
            var flush = typeof(FacialCharacterProfileSOInspector).GetMethod("FlushAutoSave", instanceNonPublic);
            if (flush != null)
            {
                var del = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                    typeof(EditorApplication.CallbackFunction), editor, flush);
                EditorApplication.delayCall -= del;
            }

            var pending = typeof(FacialCharacterProfileSOInspector).GetField("_autoSavePending", instanceNonPublic);
            pending?.SetValue(editor, false);
        }

        private static void DeleteStreamingAssetsExport(string profileName)
        {
            string exportDir = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                profileName);
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }

            string metaPath = exportDir + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }
}
