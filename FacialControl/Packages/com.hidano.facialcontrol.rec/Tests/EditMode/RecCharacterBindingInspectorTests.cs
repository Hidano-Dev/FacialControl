using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Editor.Inspector;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    /// <summary>
    /// <see cref="RecCharacterBindingInspector"/> の smoke テスト。
    /// 「CreateInspectorGUI が例外なく VisualElement を返す」「破棄で例外を出さない」だけを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RecCharacterBindingInspectorTests : SizedTestFixture
    {
        private GameObject _host;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("RecCharacterBindingInspectorTestsHost");
            _host.AddComponent<Animator>();
            _host.AddComponent<FacialController>();
            _host.AddComponent<RecCharacterBinding>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void CreateInspectorGUI_ThenDestroy_BuildsRootAndDoesNotThrow()
        {
            var binding = _host.GetComponent<RecCharacterBinding>();
            _editor = UnityEditor.Editor.CreateEditor(binding, typeof(RecCharacterBindingInspector));
            Assert.That(_editor, Is.Not.Null);

            VisualElement root = null;
            Assert.DoesNotThrow(() => root = _editor.CreateInspectorGUI());
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.StartRecordingButtonName), Is.Not.Null);
            Assert.That(root.Q<DropdownField>(RecCharacterBindingInspector.RecordingDropdownName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.RefreshRecordingsButtonName), Is.Not.Null);

            Assert.DoesNotThrow(() =>
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            });
        }
    }
}
