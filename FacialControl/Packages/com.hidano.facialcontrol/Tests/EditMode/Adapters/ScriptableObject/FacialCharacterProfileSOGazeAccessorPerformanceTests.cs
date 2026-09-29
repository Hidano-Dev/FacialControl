using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Profile
{
    [TestFixture]
    [SmallTest]
    public sealed class FacialCharacterProfileSOGazeAccessorPerformanceTests : SizedTestFixture
    {
        [Test]
        public void GazeChannels_AfterSelfRepair_RepeatedAccessDoesNotReallocateOrReenter()
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            try
            {
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_gazeChannels").arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var repaired = profile.GazeChannels;
                Assert.That(repaired, Has.Count.EqualTo(1));
                Assert.That(repaired[0].id, Is.EqualTo("gaze"));
                for (int i = 0; i < 100; i++)
                {
                    Assert.That(profile.GazeChannels, Is.SameAs(repaired));
                    Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"));
                }
            }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
