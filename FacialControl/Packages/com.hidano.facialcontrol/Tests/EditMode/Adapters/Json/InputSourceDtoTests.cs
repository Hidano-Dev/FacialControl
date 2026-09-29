using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.Json.Dto;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// <see cref="InputSourceDto"/> / <see cref="InputSourceOptionsDto"/> 階層の
    /// EditMode 契約テスト 。
    /// <para>
    /// 観測完了条件: <c>{"stalenessSeconds":2.5}</c> を <see cref="OscOptionsDto"/> に
    /// 逆シリアライズすると <c>stalenessSeconds == 2.5f</c> となる (Critical 2, 3.7)。
    /// </para>
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class InputSourceDtoTests : SizedTestFixture
    {
        // ================================================================
        // InputSourceDto — JSON 往復
        // ================================================================

        [Test]
        public void InputSourceDto_DefaultWeight_IsOne()
        {
            var dto = new InputSourceDto();

            Assert.AreEqual(1.0f, dto.weight);
        }

        [Test]
        public void InputSourceDto_FromJson_PopulatesIdAndWeight()
        {
            var json = "{\"id\":\"osc\",\"weight\":0.5}";

            var dto = JsonUtility.FromJson<InputSourceDto>(json);

            Assert.IsNotNull(dto);
            Assert.AreEqual("osc", dto.id);
            Assert.AreEqual(0.5f, dto.weight);
        }

        [Test]
        public void InputSourceDto_RoundTrip_PreservesValues()
        {
            var src = new InputSourceDto
            {
                id = "input",
                weight = 0.75f,
                optionsJson = "{\"maxStackDepth\":4}"
            };

            var json = JsonUtility.ToJson(src);
            var dst = JsonUtility.FromJson<InputSourceDto>(json);

            Assert.AreEqual(src.id, dst.id);
            Assert.AreEqual(src.weight, dst.weight);
            Assert.AreEqual(src.optionsJson, dst.optionsJson);
        }

#if FACIALCONTROL_HAS_OSC_MODULE
        // ================================================================
        // OscOptionsDto — Critical 2 観測完了条件
        // ================================================================

        [Test]
        public void OscOptionsDto_FromJsonWithStalenessSeconds_ReturnsCorrectValue()
        {
            // Critical 2: {"stalenessSeconds":2.5} → stalenessSeconds == 2.5f
            var json = "{\"stalenessSeconds\":2.5}";

            var options = JsonUtility.FromJson<OscOptionsDto>(json);

            Assert.IsNotNull(options);
            Assert.AreEqual(2.5f, options.stalenessSeconds);
        }

        [Test]
        public void OscOptionsDto_DefaultStalenessSeconds_IsZero()
        {
            var options = new OscOptionsDto();

            Assert.AreEqual(0f, options.stalenessSeconds);
        }

        [Test]
        public void OscOptionsDto_RoundTrip_PreservesStalenessSeconds()
        {
            var src = new OscOptionsDto { stalenessSeconds = 1.25f };

            var json = JsonUtility.ToJson(src);
            var dst = JsonUtility.FromJson<OscOptionsDto>(json);

            Assert.AreEqual(src.stalenessSeconds, dst.stalenessSeconds);
        }

        [Test]
        public void OscOptionsDto_IsInputSourceOptionsDto()
        {
            var options = new OscOptionsDto();

            Assert.IsInstanceOf<InputSourceOptionsDto>(options);
        }
#endif

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
        // ================================================================
        // ExpressionTriggerOptionsDto
        // ================================================================

        [Test]
        public void ExpressionTriggerOptionsDto_FromJson_ReturnsCorrectMaxStackDepth()
        {
            var json = "{\"maxStackDepth\":4}";

            var options = JsonUtility.FromJson<ExpressionTriggerOptionsDto>(json);

            Assert.IsNotNull(options);
            Assert.AreEqual(4, options.maxStackDepth);
        }

        [Test]
        public void ExpressionTriggerOptionsDto_DefaultMaxStackDepth_IsZero()
        {
            var options = new ExpressionTriggerOptionsDto();

            Assert.AreEqual(0, options.maxStackDepth);
        }

        [Test]
        public void ExpressionTriggerOptionsDto_RoundTrip_PreservesMaxStackDepth()
        {
            var src = new ExpressionTriggerOptionsDto { maxStackDepth = 8 };

            var json = JsonUtility.ToJson(src);
            var dst = JsonUtility.FromJson<ExpressionTriggerOptionsDto>(json);

            Assert.AreEqual(src.maxStackDepth, dst.maxStackDepth);
        }

        [Test]
        public void ExpressionTriggerOptionsDto_IsInputSourceOptionsDto()
        {
            var options = new ExpressionTriggerOptionsDto();

            Assert.IsInstanceOf<InputSourceOptionsDto>(options);
        }
#endif

        // ================================================================
        // LipSyncOptionsDto
        // ================================================================

        [Test]
        public void LipSyncOptionsDto_FromEmptyJson_ReturnsInstance()
        {
            var options = JsonUtility.FromJson<LipSyncOptionsDto>("{}");

            Assert.IsNotNull(options);
        }

        [Test]
        public void LipSyncOptionsDto_IsInputSourceOptionsDto()
        {
            var options = new LipSyncOptionsDto();

            Assert.IsInstanceOf<InputSourceOptionsDto>(options);
        }
    }
}
