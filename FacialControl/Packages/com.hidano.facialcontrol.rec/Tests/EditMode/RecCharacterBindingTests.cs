using System;
using System.Reflection;
using Hidano.FacialControl.Rec.Adapters.Playable;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    /// <summary>
    /// uGUI Button の OnClick など Inspector の UnityEvent に列挙される条件（public インスタンス・void 戻り・
    /// 引数 0 個または string 1 個）を <see cref="RecCharacterBinding"/> の操作 API が満たし続けることを守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class RecCharacterBindingTests : SizedTestFixture
    {
        private static readonly object[] UnityEventMethods =
        {
            new object[] { "Record", Type.EmptyTypes },
            new object[] { "Record", new[] { typeof(string) } },
            new object[] { "Load", Type.EmptyTypes },
            new object[] { "Load", new[] { typeof(string) } },
            new object[] { "Play", Type.EmptyTypes },
            new object[] { "StopRecording", Type.EmptyTypes },
            new object[] { "StopPlayback", Type.EmptyTypes },
            new object[] { "set_RecordingName", new[] { typeof(string) } },
        };

        [TestCaseSource(nameof(UnityEventMethods))]
        public void OperationApi_ForUnityEvent_IsPublicInstanceVoidWithZeroOrOneStringArgument(string methodName, Type[] parameterTypes)
        {
            MethodInfo method = typeof(RecCharacterBinding).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                parameterTypes,
                null);

            Assert.That(method, Is.Not.Null, $"{methodName}({parameterTypes.Length} args) が public インスタンスメソッドとして見つからない");
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
        }

        [Test]
        public void ScriptApi_StartRecordingLoadRecordingStartPlayback_KeepsBoolReturn()
        {
            Type type = typeof(RecCharacterBinding);

            Assert.That(type.GetMethod("StartRecording", new[] { typeof(string) })?.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(type.GetMethod("LoadRecording", new[] { typeof(string) })?.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(type.GetMethod("StartPlayback", Type.EmptyTypes)?.ReturnType, Is.EqualTo(typeof(bool)));
        }
    }
}
