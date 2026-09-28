using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生型を生成し、
    /// <c>_schemaVersion == 1</c>、<c>Label</c> getter、ToJson 既定実装が空文字を返すことを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AdapterRuntimeSettingsBaseTests : SizedTestFixture
    {
        public sealed class FakeAdapterRuntimeSettings : AdapterRuntimeSettingsBase
        {
        }

        private FakeAdapterRuntimeSettings _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<FakeAdapterRuntimeSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
                _instance = null;
            }
        }

        [Test]
        public void SchemaVersion_OnFreshInstance_ReturnsOne()
        {
            Assert.AreEqual(1, _instance.SchemaVersion);
        }

        [Test]
        public void Label_OnFreshInstance_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _instance.Label);
        }

        [Test]
        public void Label_AfterSerializedFieldAssignment_ReturnsAssignedValue()
        {
            var so = new UnityEditor.SerializedObject(_instance);
            so.FindProperty("_label").stringValue = "primary";
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual("primary", _instance.Label);
        }

        [Test]
        public void ToJson_WithoutOverride_ReturnsEmptyString()
        {
            // 未 override の既定実装は warning を出すが、照合は安定したメソッド名のみに留める。
            LogAssert.Expect(LogType.Warning, new Regex("ToJson"));

            var json = _instance.ToJson();

            Assert.AreEqual(string.Empty, json);
        }
    }
}
