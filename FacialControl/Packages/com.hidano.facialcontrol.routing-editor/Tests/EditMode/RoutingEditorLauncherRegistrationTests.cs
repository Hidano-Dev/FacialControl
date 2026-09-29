using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Windows.Routing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.RoutingEditor.Tests.EditMode
{
    /// <summary>
    /// 本パッケージを導入するだけで core Inspector の「ルーティングを編集」経路が
    /// <see cref="RoutingEditorWindow"/> に繋がることを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RoutingEditorLauncherRegistrationTests : SizedTestFixture
    {
        private FacialCharacterProfileSO _profile;
        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                Object.DestroyImmediate(_window);
                _window = null;
            }

            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [Test]
        public void OpenHandler_AfterDomainLoad_IsRegistered()
        {
            Assert.That(RoutingEditorLauncher.IsAvailable, Is.True);
        }

        [Test]
        public void Open_ThroughLauncher_ReturnsRoutingEditorWindow()
        {
            _window = RoutingEditorLauncher.Open(_profile);

            Assert.That(_window, Is.InstanceOf<RoutingEditorWindow>());
        }
    }
}
