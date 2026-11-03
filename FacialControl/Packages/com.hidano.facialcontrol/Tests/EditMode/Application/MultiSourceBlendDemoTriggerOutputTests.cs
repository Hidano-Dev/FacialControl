using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Application
{
    /// <summary>
    /// 同梱サンプル MultiSourceBlendDemo の Profile で、トリガー型入力源をレイヤー別スタックにしても（HID-418）
    /// 出力が改修前と変わらないことを確かめる。改修前の接続（入力源そのものを宣言レイヤーのスロットへ登録）を
    /// 素通しラッパーで再現し、同じ操作列を流したときの最終出力をフレームごとに比べる。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class MultiSourceBlendDemoTriggerOutputTests : SizedTestFixture
    {
        private const string SampleProfilePath =
            "Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json";

        // InputSystemAdapterBinding が登録するトリガー型入力源の既定 slug。
        private const string TriggerSourceId = "input-system";

        private sealed class TriggerSource : ExpressionTriggerInputSourceBase
        {
            // InputSystemAdapterBinding と同じく深さ 16・LastWins で作る。
            public TriggerSource(FacialProfile profile, string[] blendShapeNames)
                : base(InputSourceId.Parse(TriggerSourceId), blendShapeNames.Length, 16, ExclusionMode.LastWins, blendShapeNames, profile)
            {
            }
        }

        private sealed class LegacySharedSlot : IInputSource
        {
            private readonly IInputSource _inner;

            public LegacySharedSlot(IInputSource inner) => _inner = inner;

            public string Id => _inner.Id;
            public InputSourceType Type => _inner.Type;
            public int BlendShapeCount => _inner.BlendShapeCount;
            public BitArray ContributeMask => _inner.ContributeMask;
            public void Tick(float deltaTime) => _inner.Tick(deltaTime);
            public bool TryWriteValues(Span<float> output) => _inner.TryWriteValues(output);
        }

        [Test]
        public void UpdateWeights_SampleProfile_TriggerOutputMatchesLegacySharedSlot()
        {
            FacialProfile profile = LoadSampleProfile();
            string[] blendShapeNames = CollectBlendShapeNames(profile);
            List<int> declaringLayers = FindDeclaringLayers(profile, TriggerSourceId);
            Assert.That(declaringLayers, Is.Not.Empty, "サンプルの Profile が input-system を宣言していない");

            var source = new TriggerSource(profile, blendShapeNames);
            var legacySource = new TriggerSource(profile, blendShapeNames);
            var legacySlot = new LegacySharedSlot(legacySource);
            var current = new List<(int layerIdx, IInputSource source, float weight)>();
            var legacy = new List<(int layerIdx, IInputSource source, float weight)>();
            for (int i = 0; i < declaringLayers.Count; i++)
            {
                current.Add((declaringLayers[i], source, 1f));
                legacy.Add((declaringLayers[i], legacySlot, 1f));
            }

            using var currentUseCase = new LayerUseCase(profile, new ExpressionUseCase(profile), blendShapeNames, current);
            using var legacyUseCase = new LayerUseCase(profile, new ExpressionUseCase(profile), blendShapeNames, legacy);

            var expressions = profile.Expressions.Span;
            Assert.That(expressions.Length, Is.GreaterThan(0));
            for (int round = 0; round < 2; round++)
            {
                for (int e = 0; e < expressions.Length; e++)
                {
                    string id = expressions[e].Id;
                    source.TriggerOn(id);
                    legacySource.TriggerOn(id);
                    StepAndCompare(currentUseCase, legacyUseCase, $"on {id} (round {round})");
                }

                for (int e = 0; e < expressions.Length; e += 2)
                {
                    string id = expressions[e].Id;
                    source.TriggerOff(id);
                    legacySource.TriggerOff(id);
                    StepAndCompare(currentUseCase, legacyUseCase, $"off {id} (round {round})");
                }
            }
        }

        private static void StepAndCompare(LayerUseCase current, LayerUseCase legacy, string label)
        {
            for (int frame = 0; frame < 12; frame++)
            {
                current.UpdateWeights(1f / 30f);
                legacy.UpdateWeights(1f / 30f);
                float[] expected = legacy.GetBlendedOutput();
                float[] actual = current.GetBlendedOutput();
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.AreEqual(expected[i], actual[i], 1e-6f, $"{label} frame {frame} blendShape {i}");
                }
            }
        }

        private static FacialProfile LoadSampleProfile()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string path = Path.GetFullPath(Path.Combine(projectRoot, SampleProfilePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(File.Exists(path), Is.True, $"サンプルの Profile が見つからない: {SampleProfilePath}");
            return new SystemTextJsonParser().ParseProfile(File.ReadAllText(path));
        }

        private static string[] CollectBlendShapeNames(FacialProfile profile)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var expressions = profile.Expressions.Span;
            for (int e = 0; e < expressions.Length; e++)
            {
                var values = expressions[e].BlendShapeValues.Span;
                for (int v = 0; v < values.Length; v++)
                {
                    if (seen.Add(values[v].Name))
                    {
                        names.Add(values[v].Name);
                    }
                }
            }

            return names.ToArray();
        }

        private static List<int> FindDeclaringLayers(FacialProfile profile, string sourceId)
        {
            var result = new List<int>();
            var declarations = profile.LayerInputSources.Span;
            for (int l = 0; l < declarations.Length && l < profile.Layers.Length; l++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[l];
                if (layerDeclarations == null)
                {
                    continue;
                }

                for (int d = 0; d < layerDeclarations.Length; d++)
                {
                    if (string.Equals(layerDeclarations[d].Id, sourceId, StringComparison.Ordinal))
                    {
                        result.Add(l);
                        break;
                    }
                }
            }

            return result;
        }
    }
}
