using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    [SmallTest]
    public sealed class GazeInputReaderTests : SizedTestFixture
    {
        [Test]
        public void TryReadXY_ScalarInput_UsesXAndZeroY()
        {
            var source = new StubInputSource(axisCount: 1, scalar: 2.5f);

            Assert.IsTrue(GazeInputReader.TryReadXY(source, out float x, out float y));
            Assert.AreEqual(1f, x);
            Assert.AreEqual(0f, y);
        }

        [Test]
        public void TryReadXY_Vector2Input_ReturnsBothClampedAxes()
        {
            var source = new StubInputSource(axisCount: 2, vectorX: -2f, vectorY: 0.5f);

            Assert.IsTrue(GazeInputReader.TryReadXY(source, out float x, out float y));
            Assert.AreEqual(-1f, x);
            Assert.AreEqual(0.5f, y);
        }

        [Test]
        public void TryReadXY_NullOrInvalidInput_ReturnsFalseAndZeroes()
        {
            Assert.IsFalse(GazeInputReader.TryReadXY(null, out float nullX, out float nullY));
            Assert.AreEqual(0f, nullX);
            Assert.AreEqual(0f, nullY);

            var invalid = new StubInputSource(axisCount: 1, isValid: false, scalar: 0.5f);
            Assert.IsFalse(GazeInputReader.TryReadXY(invalid, out float invalidX, out float invalidY));
            Assert.AreEqual(0f, invalidX);
            Assert.AreEqual(0f, invalidY);
        }

        private sealed class StubInputSource : IAnalogInputSource
        {
            private readonly float _scalar;
            private readonly float _vectorX;
            private readonly float _vectorY;
            private readonly bool _isValid;

            public StubInputSource(
                int axisCount,
                float scalar = 0f,
                float vectorX = 0f,
                float vectorY = 0f,
                bool isValid = true)
            {
                AxisCount = axisCount;
                _scalar = scalar;
                _vectorX = vectorX;
                _vectorY = vectorY;
                _isValid = isValid;
            }

            public string Id => "test-gaze";
            public bool IsValid => _isValid;
            public int AxisCount { get; }
            public void Tick(float deltaTime) { }
            public bool TryReadScalar(out float value) { value = _scalar; return true; }
            public bool TryReadVector2(out float x, out float y) { x = _vectorX; y = _vectorY; return true; }
            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0) output[0] = _scalar;
                return output.Length > 0;
            }
        }
    }
}
