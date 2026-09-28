using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    [SmallTest]
    public class ToggleStateReconcilerTests : SizedTestFixture
    {
        private sealed class TestToggleStateEntry : IToggleStateEntry
        {
            public TestToggleStateEntry(string expressionId, bool isActive = false)
            {
                ExpressionId = expressionId;
                IsActive = isActive;
            }

            public string ExpressionId { get; }

            public bool IsActive { get; set; }
        }

        [Test]
        public void TryFlip_WhenSuspended_ReturnsFalseWithoutChangingIsActive()
        {
            var entry = new TestToggleStateEntry("smile", isActive: false);

            bool flipped = ToggleStateReconciler.TryFlip(true, entry);

            Assert.That(flipped, Is.False);
            Assert.That(entry.IsActive, Is.False);
        }

        [Test]
        public void TryFlip_WhenNotSuspended_TogglesIsActiveAndReturnsTrue()
        {
            var entry = new TestToggleStateEntry("smile", isActive: false);

            bool firstFlip = ToggleStateReconciler.TryFlip(false, entry);
            bool secondFlip = ToggleStateReconciler.TryFlip(false, entry);

            Assert.That(firstFlip, Is.True);
            Assert.That(secondFlip, Is.True);
            Assert.That(entry.IsActive, Is.False);
        }

        [Test]
        public void SyncWithStack_WhenExpressionIsPresent_SetsIsActiveTrue()
        {
            var entry = new TestToggleStateEntry("smile", isActive: false);
            IReadOnlyList<string> activeExpressionIds = new[] { "angry", "smile" };

            ToggleStateReconciler.SyncWithStack(activeExpressionIds, entry);

            Assert.That(entry.IsActive, Is.True);
        }

        [Test]
        public void SyncWithStack_WhenExpressionIsMissing_SetsIsActiveFalse()
        {
            var entry = new TestToggleStateEntry("smile", isActive: true);
            IReadOnlyList<string> activeExpressionIds = new[] { "angry", "sad" };

            ToggleStateReconciler.SyncWithStack(activeExpressionIds, entry);

            Assert.That(entry.IsActive, Is.False);
        }
    }
}
