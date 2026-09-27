using System;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class ZombieEvictionPolicyTests
    {
        [Test]
        public void Observe_MultipleSenderIdentities_AdoptsNewestStartedAtUnixMs()
        {
            var policy = new ZombieEvictionPolicy();
            var oldSender = new SenderIdentity(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                1000L);
            var newSender = new SenderIdentity(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                2000L);

            Assert.IsTrue(policy.Observe(oldSender));
            LogAssert.Expect(LogType.Log, SwitchLogPattern(oldSender, newSender));
            Assert.IsTrue(policy.Observe(newSender));

            Assert.AreEqual(newSender, policy.CurrentSender);
            Assert.IsFalse(policy.IsAccepted(oldSender));
            Assert.IsTrue(policy.IsAccepted(newSender));
        }

        [Test]
        public void Observe_OlderSenderAfterNewerSender_RejectsOlderSenderValues()
        {
            var policy = new ZombieEvictionPolicy();
            var newSender = new SenderIdentity(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                3000L);
            var oldSender = new SenderIdentity(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                2000L);

            Assert.IsTrue(policy.Observe(newSender));
            Assert.IsFalse(policy.Observe(oldSender));

            Assert.AreEqual(newSender, policy.CurrentSender);
            Assert.IsTrue(policy.IsAccepted(newSender));
            Assert.IsFalse(policy.IsAccepted(oldSender));
        }

        [Test]
        public void Observe_MoreThanCapacity_EvictsOldestObservedSenderByFifo()
        {
            var policy = new ZombieEvictionPolicy(maxObservedSenders: 3);
            var first = new SenderIdentity(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                1000L);
            var second = new SenderIdentity(
                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                2000L);
            var third = new SenderIdentity(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                3000L);
            var fourth = new SenderIdentity(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                4000L);

            Assert.IsTrue(policy.Observe(first));
            LogAssert.Expect(LogType.Log, SwitchLogPattern(first, second));
            Assert.IsTrue(policy.Observe(second));
            LogAssert.Expect(LogType.Log, SwitchLogPattern(second, third));
            Assert.IsTrue(policy.Observe(third));
            LogAssert.Expect(LogType.Log, SwitchLogPattern(third, fourth));
            Assert.IsTrue(policy.Observe(fourth));

            Assert.AreEqual(3, policy.ObservedSenderCount);
            Assert.IsFalse(policy.ContainsObservedSender(first.SenderId));
            Assert.IsTrue(policy.ContainsObservedSender(second.SenderId));
            Assert.IsTrue(policy.ContainsObservedSender(third.SenderId));
            Assert.IsTrue(policy.ContainsObservedSender(fourth.SenderId));
            Assert.AreEqual(fourth, policy.CurrentSender);
        }

        [Test]
        public void Observe_SameUuidWithNewerStartedAtUnixMs_UpdatesObservedSenderWithoutDuplicate()
        {
            var policy = new ZombieEvictionPolicy();
            Guid senderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var firstStart = new SenderIdentity(senderId, 1000L);
            var secondStart = new SenderIdentity(senderId, 2000L);

            Assert.IsTrue(policy.Observe(firstStart));
            Assert.IsTrue(policy.Observe(secondStart));

            Assert.AreEqual(1, policy.ObservedSenderCount);
            Assert.AreEqual(secondStart, policy.CurrentSender);
        }

        [Test]
        public void Clear_AfterObservedSender_RemovesCurrentAndObservedSenders()
        {
            var policy = new ZombieEvictionPolicy();
            var sender = new SenderIdentity(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                1000L);

            policy.Observe(sender);
            policy.Clear();

            Assert.AreEqual(0, policy.ObservedSenderCount);
            Assert.IsFalse(policy.HasCurrentSender);
            Assert.IsFalse(policy.ContainsObservedSender(sender.SenderId));
        }

        [Test]
        public void CurrentSender_NoObservedSender_ThrowsInvalidOperationException()
        {
            var policy = new ZombieEvictionPolicy();

            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = policy.CurrentSender;
            });
        }

        [Test]
        public void Constructor_InvalidCapacity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ZombieEvictionPolicy(maxObservedSenders: 0));
        }

        private static Regex SwitchLogPattern(SenderIdentity previous, SenderIdentity current)
        {
            string pattern = "ZombieEvictionPolicy adopted sender changed:.*"
                + Regex.Escape(previous.SenderId.ToString("D"))
                + ".*"
                + previous.StartedAtUnixMs
                + ".*"
                + Regex.Escape(current.SenderId.ToString("D"))
                + ".*"
                + current.StartedAtUnixMs;

            return new Regex(pattern);
        }
    }
}
