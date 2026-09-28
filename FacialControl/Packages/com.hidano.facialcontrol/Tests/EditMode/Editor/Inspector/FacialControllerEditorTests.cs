using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Editor.Inspector;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialControllerEditor"/> の smoke テスト。
    /// Inspector が例外なく生成できること、および実機で発生した
    /// 「FacialController 二重配置（親子階層に 2 つ）で同じ renderer を奪い合い表情が動かない」不具合を
    /// Inspector 警告として検出できること（<see cref="FacialControllerEditor.FindHierarchyDuplicate"/>）を守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerEditorTests : SizedTestFixture
    {
        private GameObject _host;
        private FacialController _controller;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("FacialControllerEditorTestHost");
            _host.AddComponent<Animator>();
            _controller = _host.AddComponent<FacialController>();
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

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_controller, typeof(FacialControllerEditor));
            Assert.IsNotNull(_editor, "FacialControllerEditor が生成できませんでした。");

            var root = _editor.CreateInspectorGUI();
            Assert.IsNotNull(root, "CreateInspectorGUI() は VisualElement を返すべきです。");
            return root;
        }

        // ================================================================
        // smoke: 生成できる（重複なし → 警告なし）
        // ================================================================

        [Test]
        public void CreateInspectorGUI_NoDuplicateController_DoesNotShowWarning()
        {
            var root = BuildInspectorRoot();

            var helpBox = root.Q<HelpBox>(name: FacialControllerEditor.DuplicateWarningHelpBoxName);

            Assert.IsNull(helpBox, "重複が無いのに警告 HelpBox が表示されています。");
        }

        // ================================================================
        // 重複 FacialController の警告（実機不具合: 二重配置で表情が動かない）
        // ================================================================

        [Test]
        public void CreateInspectorGUI_DuplicateControllerInChild_ShowsWarning()
        {
            var child = new GameObject("Model");
            child.transform.SetParent(_host.transform, false);
            child.AddComponent<Animator>();
            child.AddComponent<FacialController>();

            var root = BuildInspectorRoot();

            var helpBox = root.Q<HelpBox>(name: FacialControllerEditor.DuplicateWarningHelpBoxName);

            Assert.IsNotNull(helpBox, "子孫に FacialController があるのに警告 HelpBox がありません。");
            StringAssert.Contains("Model", helpBox.text, "警告に重複相手のパスが含まれていません。");
        }

        [Test]
        public void FindHierarchyDuplicate_ControllerInParent_ReturnsParentController()
        {
            var parent = new GameObject("Root");
            parent.AddComponent<Animator>();
            var parentController = parent.AddComponent<FacialController>();
            _host.transform.SetParent(parent.transform, false);

            try
            {
                var duplicate = FacialControllerEditor.FindHierarchyDuplicate(_controller);

                Assert.That(duplicate, Is.SameAs(parentController));
            }
            finally
            {
                // TearDown が _host を破棄できるよう、親から切り離してから親を破棄する。
                _host.transform.SetParent(null, false);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FindHierarchyDuplicate_SiblingController_ReturnsNull()
        {
            var parent = new GameObject("Root");
            _host.transform.SetParent(parent.transform, false);

            var sibling = new GameObject("OtherCharacter");
            sibling.transform.SetParent(parent.transform, false);
            sibling.AddComponent<Animator>();
            sibling.AddComponent<FacialController>();

            try
            {
                var duplicate = FacialControllerEditor.FindHierarchyDuplicate(_controller);

                Assert.IsNull(duplicate, "兄弟関係の FacialController は重複として扱わない。");
            }
            finally
            {
                _host.transform.SetParent(null, false);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FindHierarchyDuplicate_Null_ReturnsNull()
        {
            Assert.IsNull(FacialControllerEditor.FindHierarchyDuplicate(null));
        }
    }
}
