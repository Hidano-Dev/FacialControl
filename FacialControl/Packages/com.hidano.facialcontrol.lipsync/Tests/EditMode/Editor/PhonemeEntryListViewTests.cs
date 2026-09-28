using System;
using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    /// <summary>
    /// <see cref="PhonemeEntryListView"/> のテスト。
    /// UI ツリーは「生成できる」smoke のみ。残りは公開 API（AddEntry / SetEntryKind / MoveEntry / RemoveEntryAt）
    /// が SerializedProperty へ正しく書き込むことを SerializedObject 経由で観測する（UI 非依存）。
    /// </summary>
    [SmallTest]
    public class PhonemeEntryListViewTests : SizedTestFixture
    {
        private PhonemeEntryListViewTestAsset _asset;
        private SerializedObject _serializedObject;
        private SerializedProperty _entriesProperty;

        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();

            _asset = ScriptableObject.CreateInstance<PhonemeEntryListViewTestAsset>();
            _serializedObject = new SerializedObject(_asset);
            _entriesProperty =
                _serializedObject.FindProperty(nameof(PhonemeEntryListViewTestAsset.Entries));
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject.Dispose();
            UnityEngine.Object.DestroyImmediate(_asset);
            Undo.ClearAll();
        }

        // ====================================================================
        // smoke: 生成できる
        // ====================================================================

        [Test]
        public void Construct_ArrayProperty_BuildsListViewWithoutThrowing()
        {
            _asset.Entries.Add(new BlendShapePhonemeEntry { PhonemeId = "A", BlendShapeName = string.Empty });
            _asset.Entries.Add(new AnimationClipPhonemeEntry { PhonemeId = "O" });
            _asset.Entries.Add(new ExpressionPhonemeEntry { PhonemeId = "I" });
            _serializedObject.Update();

            PhonemeEntryListView view = null;
            Assert.DoesNotThrow(() => view = CreateView());

            Assert.That(view.Q<ListView>(PhonemeEntryListView.ListViewName), Is.Not.Null);
        }

        // ====================================================================
        // 公開 API → SerializedProperty 書き込み
        // ====================================================================

        [Test]
        public void AddEntry_BlendShapeAndAnimationClip_AppendsConcreteManagedReferences()
        {
            var view = CreateView();

            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(2));
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<BlendShapePhonemeEntry>());
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(1).managedReferenceValue,
                Is.InstanceOf<AnimationClipPhonemeEntry>());
        }

        [Test]
        public void AddEntry_Expression_InsertsExpressionPhonemeEntry()
        {
            var view = CreateView();

            view.AddEntry(PhonemeEntryListView.EntryKind.Expression);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(1));
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.InstanceOf<ExpressionPhonemeEntry>());
            Assert.That(
                entry.managedReferenceFullTypename,
                Does.Contain(typeof(ExpressionPhonemeEntry).FullName));
        }

        [Test]
        public void SetEntryKind_FromBlendShapeToAnimationClip_PreservesCommonFields()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);

            _serializedObject.Update();
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "A";
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 80f;
            entry.FindPropertyRelative(nameof(BlendShapePhonemeEntry.BlendShapeName)).stringValue = "Mouth_A";
            _serializedObject.ApplyModifiedProperties();

            view.SetEntryKind(0, PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.InstanceOf<AnimationClipPhonemeEntry>());
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue,
                Is.EqualTo("A"));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue,
                Is.EqualTo(80f).Within(1e-6f));
        }

        [TestCase(
            PhonemeEntryListView.EntryKind.BlendShape,
            PhonemeEntryListView.EntryKind.AnimationClip,
            typeof(AnimationClipPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.BlendShape,
            PhonemeEntryListView.EntryKind.Expression,
            typeof(ExpressionPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.AnimationClip,
            PhonemeEntryListView.EntryKind.BlendShape,
            typeof(BlendShapePhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.AnimationClip,
            PhonemeEntryListView.EntryKind.Expression,
            typeof(ExpressionPhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.Expression,
            PhonemeEntryListView.EntryKind.BlendShape,
            typeof(BlendShapePhonemeEntry))]
        [TestCase(
            PhonemeEntryListView.EntryKind.Expression,
            PhonemeEntryListView.EntryKind.AnimationClip,
            typeof(AnimationClipPhonemeEntry))]
        public void SetEntryKind_BetweenAllFormats_PreservesCommonFields(
            PhonemeEntryListView.EntryKind sourceKind,
            PhonemeEntryListView.EntryKind targetKind,
            Type expectedEntryType)
        {
            var view = CreateView();
            view.AddEntry(sourceKind);

            _serializedObject.Update();
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "U";
            entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 72.5f;
            _serializedObject.ApplyModifiedProperties();

            view.SetEntryKind(0, targetKind);

            _serializedObject.Update();
            entry = _entriesProperty.GetArrayElementAtIndex(0);
            Assert.That(entry.managedReferenceValue, Is.TypeOf(expectedEntryType));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue,
                Is.EqualTo("U"));
            Assert.That(
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue,
                Is.EqualTo(72.5f).Within(1e-6f));
        }

        [Test]
        public void MoveEntry_ReordersSerializedArray()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            _serializedObject.Update();
            SetPhonemeId(0, "A");
            SetPhonemeId(1, "I");
            _serializedObject.ApplyModifiedProperties();

            view.MoveEntry(0, 1);

            _serializedObject.Update();
            Assert.That(GetPhonemeId(0), Is.EqualTo("I"));
            Assert.That(GetPhonemeId(1), Is.EqualTo("A"));
        }

        [Test]
        public void RemoveEntryAt_RemovesElement()
        {
            var view = CreateView();
            view.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            view.AddEntry(PhonemeEntryListView.EntryKind.AnimationClip);

            view.RemoveEntryAt(0);

            _serializedObject.Update();
            Assert.That(_entriesProperty.arraySize, Is.EqualTo(1));
            Assert.That(
                _entriesProperty.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<AnimationClipPhonemeEntry>());
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private PhonemeEntryListView CreateView()
        {
            _serializedObject.Update();
            _entriesProperty =
                _serializedObject.FindProperty(nameof(PhonemeEntryListViewTestAsset.Entries));
            return new PhonemeEntryListView(_entriesProperty);
        }

        private void SetPhonemeId(int index, string phonemeId)
        {
            _entriesProperty.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId))
                .stringValue = phonemeId;
        }

        private string GetPhonemeId(int index)
        {
            return _entriesProperty.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId))
                .stringValue;
        }

        private sealed class PhonemeEntryListViewTestAsset : ScriptableObject
        {
            [SerializeReference]
            public List<PhonemeEntryBase> Entries = new List<PhonemeEntryBase>();
        }
    }
}
