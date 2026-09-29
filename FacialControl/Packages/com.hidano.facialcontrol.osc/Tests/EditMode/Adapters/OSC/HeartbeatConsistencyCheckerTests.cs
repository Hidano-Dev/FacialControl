using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class HeartbeatConsistencyCheckerTests : SizedTestFixture
    {
        [Test]
        public void Constructor_WithReceiverMappings_InitializesReceiverNameCount()
        {
            var checker = new HeartbeatConsistencyChecker(new[]
            {
                new OscMapping("/avatar/parameters/Happy", "Happy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
            });

            Assert.AreEqual(2, checker.BlendShapeCount);
            Assert.IsFalse(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.IsEmpty(checker.ReceiverOnlyNames);
        }

        [Test]
        public void Constructor_WithMeshBlendShapeNames_UsesReceiverMappingNamesForMismatch()
        {
            var checker = new HeartbeatConsistencyChecker(
                new[] { "Blink", "Happy", "Extra", "Angry" },
                new[]
                {
                    new OscMapping("/avatar/parameters/Happy", "Happy", "emotion"),
                    new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
                },
                false);

            checker.UpdateFromHeartbeat(new[] { "Happy" });

            Assert.AreEqual(2, checker.BlendShapeCount);
            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { "Angry" }, checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_MappingPresentInSender_HasNoMismatch()
        {
            var checker = new HeartbeatConsistencyChecker(
                new[] { "Blink", "Happy", "Angry" },
                new[]
                {
                    new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
                },
                false);

            checker.UpdateFromHeartbeat(new[] { "Angry" });

            Assert.IsFalse(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.IsEmpty(checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_MappingMissingInSender_RecordsReceiverOnlyName()
        {
            var checker = new HeartbeatConsistencyChecker(
                new[] { "Blink", "Happy", "Angry" },
                new[]
                {
                    new OscMapping("/avatar/parameters/Happy", "Happy", "emotion"),
                    new OscMapping("/avatar/parameters/Angry", "Angry", "emotion")
                },
                false);

            checker.UpdateFromHeartbeat(new[] { "Happy" });

            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { "Angry" }, checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_PartialMismatch_RecordsSenderOnlyAndReceiverOnlyNames()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy", "Angry", "Sad" });

            LogAssert.Expect(LogType.Warning, MismatchLog("Extra", "Angry", "Sad"));
            checker.UpdateFromHeartbeat(new[] { "Happy", "Extra" });

            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.AreEqual(new[] { "Extra" }, checker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { "Angry", "Sad" }, checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_SenderOnlyMismatch_RecordsSenderOnlyName()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy" });

            LogAssert.Expect(LogType.Warning, MismatchLog("Extra", string.Empty));
            checker.UpdateFromHeartbeat(new[] { "Happy", "Extra" });

            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.AreEqual(new[] { "Extra" }, checker.SenderOnlyNames);
            CollectionAssert.IsEmpty(checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_AllNamesMatch_ClearsPreviousMismatch()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy", "Angry" });

            LogAssert.Expect(LogType.Warning, MismatchLog(string.Empty, "Angry"));
            checker.UpdateFromHeartbeat(new[] { "Happy" });

            checker.UpdateFromHeartbeat(new[] { "Angry", "Happy" });

            Assert.IsFalse(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.IsEmpty(checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_SameMismatchSet_LogsOnlyOnce()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy", "Angry" });

            LogAssert.Expect(LogType.Warning, MismatchLog("Extra", "Angry"));
            checker.UpdateFromHeartbeat(new[] { "Happy", "Extra" });
            checker.UpdateFromHeartbeat(new[] { "Extra", "Happy" });

            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.AreEqual(new[] { "Extra" }, checker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { "Angry" }, checker.ReceiverOnlyNames);
        }

        [Test]
        public void UpdateFromHeartbeat_DifferentMismatchSet_LogsAgain()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy", "Angry", "Sad" });

            LogAssert.Expect(LogType.Warning, MismatchLog(string.Empty, "Angry"));
            checker.UpdateFromHeartbeat(new[] { "Happy", "Sad" });

            LogAssert.Expect(LogType.Warning, MismatchLog(string.Empty, "Sad"));
            checker.UpdateFromHeartbeat(new[] { "Happy", "Angry" });

            Assert.IsTrue(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.AreEqual(new[] { "Sad" }, checker.ReceiverOnlyNames);
        }

        [Test]
        public void Clear_AfterMismatch_ResetsMismatchState()
        {
            var checker = new HeartbeatConsistencyChecker(new[] { "Happy", "Angry" }, warnLogEnabled: false);

            checker.UpdateFromHeartbeat(new[] { "Happy" });
            checker.Clear();

            Assert.IsFalse(checker.HasMismatch);
            CollectionAssert.IsEmpty(checker.SenderOnlyNames);
            CollectionAssert.IsEmpty(checker.ReceiverOnlyNames);
        }

        private static Regex MismatchLog(string senderOnlyName, params string[] receiverOnlyNames)
        {
            string pattern = "HeartbeatConsistencyChecker mismatch:";
            if (!string.IsNullOrEmpty(senderOnlyName))
            {
                pattern += ".*senderOnly=.*" + Regex.Escape(senderOnlyName);
            }

            bool addedReceiverOnlyPrefix = false;
            for (int i = 0; i < receiverOnlyNames.Length; i++)
            {
                string name = receiverOnlyNames[i];
                if (!string.IsNullOrEmpty(name))
                {
                    if (!addedReceiverOnlyPrefix)
                    {
                        pattern += ".*receiverOnly=";
                        addedReceiverOnlyPrefix = true;
                    }

                    pattern += ".*" + Regex.Escape(name);
                }
            }

            return new Regex(pattern);
        }
    }
}
