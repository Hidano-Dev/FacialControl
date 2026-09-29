using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class EyeGazeConverterTests : SizedTestFixture
    {
        [Test]
        public void Convert_NoValue_ReturnsZero()
        {
            EyeGazeConverter converter = EyeGazeConverter.Default;
            var sample = new IFacialMocapTransformSample { HasValue = false, EulerX = 50f, EulerY = 50f };

            Assert.That(converter.Convert(sample), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Convert_NormalizesYawFromEulerYAndPitchFromEulerX()
        {
            var converter = new EyeGazeConverter { maxYawDegrees = 30f, maxPitchDegrees = 20f };
            var sample = new IFacialMocapTransformSample { HasValue = true, EulerX = 10f, EulerY = 15f };

            Vector2 v = converter.Convert(sample);

            Assert.That(v.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(v.y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Convert_ClampsToUnitRange()
        {
            var converter = new EyeGazeConverter { maxYawDegrees = 10f, maxPitchDegrees = 10f };
            var sample = new IFacialMocapTransformSample { HasValue = true, EulerX = 100f, EulerY = -100f };

            Vector2 v = converter.Convert(sample);

            Assert.That(v.x, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(v.y, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Convert_ZeroMax_DisablesThatAxis()
        {
            var converter = new EyeGazeConverter { maxYawDegrees = 0f, maxPitchDegrees = 20f };
            var sample = new IFacialMocapTransformSample { HasValue = true, EulerX = 10f, EulerY = 90f };

            Vector2 v = converter.Convert(sample);

            Assert.That(v.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(v.y, Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}
