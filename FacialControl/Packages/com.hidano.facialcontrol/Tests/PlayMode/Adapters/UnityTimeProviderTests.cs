using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// T6.1: UnityTimeProvider の PlayMode 契約テスト。
    /// Time.unscaledTimeAsDouble がフレーム進行に伴って単調増加することを検証する。
    /// </summary>
    [MediumTest]
    public class UnityTimeProviderTests : SizedTestFixture
    {
        [UnityTest]
        public IEnumerator UnscaledTimeSeconds_AfterOneFrame_IsGreaterThanBefore()
        {
            ITimeProvider provider = new UnityTimeProvider();

            double before = provider.UnscaledTimeSeconds;
            yield return null;
            double after = provider.UnscaledTimeSeconds;

            Assert.Greater(after, before,
                "PlayMode で 1 フレーム進行後の UnscaledTimeSeconds は直前値より大きいこと");
        }

        [UnityTest]
        public IEnumerator UnscaledTimeSeconds_OverMultipleFrames_IsMonotonicallyIncreasing()
        {
            ITimeProvider provider = new UnityTimeProvider();

            double previous = provider.UnscaledTimeSeconds;
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                double current = provider.UnscaledTimeSeconds;
                Assert.GreaterOrEqual(current, previous,
                    $"フレーム {i}: 単調増加が崩れている (previous={previous}, current={current})");
                previous = current;
            }
        }
    }
}
