using System;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Editor.Windows.Routing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing
{
    /// <summary>
    /// <see cref="RoutingEditorLauncher"/> の公開契約。
    /// 別パッケージが登録したハンドラへプロファイルを委譲し、未登録なら警告して null を返す。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RoutingEditorLauncherTests : SizedTestFixture
    {
        private Func<ScriptableObject, EditorWindow> _previousHandler;
        private ScriptableObject _profile;
        private EditorWindow _returnedWindow;

        [SetUp]
        public void SetUp()
        {
            // 開発プロジェクトでは routing-editor パッケージがハンドラを登録済みのため、退避して復元する。
            _previousHandler = RoutingEditorLauncher.OpenHandler;
            _profile = ScriptableObject.CreateInstance<ScriptableObject>();
        }

        [TearDown]
        public void TearDown()
        {
            RoutingEditorLauncher.OpenHandler = _previousHandler;

            if (_returnedWindow != null)
            {
                Object.DestroyImmediate(_returnedWindow);
                _returnedWindow = null;
            }

            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [Test]
        public void IsAvailable_NoHandler_ReturnsFalse()
        {
            RoutingEditorLauncher.OpenHandler = null;

            Assert.That(RoutingEditorLauncher.IsAvailable, Is.False);
        }

        [Test]
        public void IsAvailable_HandlerRegistered_ReturnsTrue()
        {
            RoutingEditorLauncher.OpenHandler = _ => null;

            Assert.That(RoutingEditorLauncher.IsAvailable, Is.True);
        }

        [Test]
        public void Open_NoHandler_LogsWarningAndReturnsNull()
        {
            RoutingEditorLauncher.OpenHandler = null;
            LogAssert.Expect(LogType.Warning, new Regex("routing-editor"));

            EditorWindow window = RoutingEditorLauncher.Open(_profile);

            Assert.That(window, Is.Null);
        }

        [Test]
        public void Open_HandlerRegistered_PassesProfileAndReturnsHandlerResult()
        {
            ScriptableObject received = null;
            var stubWindow = ScriptableObject.CreateInstance<StubWindow>();
            RoutingEditorLauncher.OpenHandler = profile =>
            {
                received = profile;
                return stubWindow;
            };

            _returnedWindow = RoutingEditorLauncher.Open(_profile);

            Assert.That(received, Is.SameAs(_profile));
            Assert.That(_returnedWindow, Is.SameAs(stubWindow));
        }

        private sealed class StubWindow : EditorWindow
        {
        }
    }
}
