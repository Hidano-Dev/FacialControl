using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P07-T02: OscMappingTable の単体テスト。
    /// マッピング変換、プリセットを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscMappingTableTests : SizedTestFixture
    {
        // ================================================================
        // コンストラクタ — OscMapping 配列からの初期化
        // ================================================================

        [Test]
        public void Constructor_WithMappings_CreatesMappingTable()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync")
            };

            var table = new OscMappingTable(mappings);

            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void Constructor_EmptyMappings_CreatesEmptyTable()
        {
            var table = new OscMappingTable(Array.Empty<OscMapping>());

            Assert.AreEqual(0, table.Count);
        }

        [Test]
        public void Constructor_NullMappings_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new OscMappingTable(null));
        }

        // ================================================================
        // コンストラクタ — OscConfiguration からの初期化
        // ================================================================

        [Test]
        public void Constructor_WithOscConfiguration_CreatesMappingTable()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var config = new OscConfiguration(mapping: mappings);

            var table = new OscMappingTable(config);

            Assert.AreEqual(1, table.Count);
        }

        // ================================================================
        // コンストラクタ — FacialControlConfig からの初期化
        // ================================================================

        [Test]
        public void Constructor_WithFacialControlConfig_CreatesMappingTable()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync")
            };
            var oscConfig = new OscConfiguration(mapping: mappings);
            var config = new FacialControlConfig("1.0", oscConfig);

            var table = new OscMappingTable(config);

            Assert.AreEqual(2, table.Count);
        }

        // ================================================================
        // TryGetByAddress — OSC アドレスからの変換
        // ================================================================

        [Test]
        public void TryGetByAddress_ExistingAddress_ReturnsTrueAndMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByAddress("/avatar/parameters/Fcl_ALL_Joy", out var result);

            Assert.IsTrue(found);
            Assert.AreEqual("Fcl_ALL_Joy", result.BlendShapeName);
            Assert.AreEqual("emotion", result.Layer);
        }

        [Test]
        public void TryGetByAddress_NonExistentAddress_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByAddress("/avatar/parameters/NonExistent", out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetByAddress_NullAddress_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByAddress(null, out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetByAddress_EmptyAddress_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByAddress("", out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetByAddress_CaseSensitive_ExactMatchOnly()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByAddress("/avatar/parameters/fcl_all_joy", out _);

            Assert.IsFalse(found);
        }

        // ================================================================
        // TryGetByBlendShapeName — BlendShape 名からの変換
        // ================================================================

        [Test]
        public void TryGetByBlendShapeName_ExistingName_ReturnsTrueAndMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByBlendShapeName("Fcl_ALL_Joy", out var result);

            Assert.IsTrue(found);
            Assert.AreEqual("/avatar/parameters/Fcl_ALL_Joy", result.OscAddress);
            Assert.AreEqual("emotion", result.Layer);
        }

        [Test]
        public void TryGetByBlendShapeName_NonExistentName_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByBlendShapeName("NonExistent", out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TryGetByBlendShapeName_NullName_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetByBlendShapeName(null, out _);

            Assert.IsFalse(found);
        }

        // ================================================================
        // TryGetIndex — インデックス取得
        // ================================================================

        [Test]
        public void TryGetIndex_ExistingAddress_ReturnsTrueAndIndex()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetIndex("/avatar/parameters/Fcl_MTH_A", out int index);

            Assert.IsTrue(found);
            Assert.AreEqual(1, index);
        }

        [Test]
        public void TryGetIndex_NonExistentAddress_ReturnsFalse()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            bool found = table.TryGetIndex("/nonexistent", out _);

            Assert.IsFalse(found);
        }

        // ================================================================
        // GetMappings — 全マッピング取得
        // ================================================================

        [Test]
        public void GetMappings_ReturnsAllMappings()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync"),
                new OscMapping("/avatar/parameters/Fcl_EYE_Close", "Fcl_EYE_Close", "eye")
            };
            var table = new OscMappingTable(mappings);

            var result = table.GetMappings();

            Assert.AreEqual(3, result.Length);
        }

        [Test]
        public void GetMappings_ReturnedArrayIsDefensiveCopy()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            var result1 = table.GetMappings();
            var result2 = table.GetMappings();

            Assert.AreNotSame(result1, result2);
        }

        // ================================================================
        // GetMappingsByLayer — レイヤー別マッピング取得
        // ================================================================

        [Test]
        public void GetMappingsByLayer_ExistingLayer_ReturnsFilteredMappings()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync"),
                new OscMapping("/avatar/parameters/Fcl_ALL_Sad", "Fcl_ALL_Sad", "emotion")
            };
            var table = new OscMappingTable(mappings);

            var result = table.GetMappingsByLayer("emotion");

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("Fcl_ALL_Joy", result[0].BlendShapeName);
            Assert.AreEqual("Fcl_ALL_Sad", result[1].BlendShapeName);
        }

        [Test]
        public void GetMappingsByLayer_NonExistentLayer_ReturnsEmptyArray()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            var result = table.GetMappingsByLayer("nonexistent");

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetMappingsByLayer_NullLayer_ReturnsEmptyArray()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion")
            };
            var table = new OscMappingTable(mappings);

            var result = table.GetMappingsByLayer(null);

            Assert.AreEqual(0, result.Length);
        }

        // ================================================================
        // 重複アドレスの処理
        // ================================================================

        [Test]
        public void Constructor_DuplicateAddress_LastWins()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy_Old", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy_New", "emotion")
            };

            var table = new OscMappingTable(mappings);

            table.TryGetByAddress("/avatar/parameters/Fcl_ALL_Joy", out var result);
            Assert.AreEqual("Fcl_ALL_Joy_New", result.BlendShapeName);
        }

        [Test]
        public void Constructor_DuplicateBlendShapeName_LastWins()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/addr1", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/addr2", "Fcl_ALL_Joy", "lipsync")
            };

            var table = new OscMappingTable(mappings);

            table.TryGetByBlendShapeName("Fcl_ALL_Joy", out var result);
            Assert.AreEqual("/avatar/parameters/addr2", result.OscAddress);
            Assert.AreEqual("lipsync", result.Layer);
        }

        // ================================================================
        // VRChat プリセットパターン
        // ================================================================

        [Test]
        public void TryGetByAddress_VRChatPattern_CorrectMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL_Joy", "Fcl_ALL_Joy", "emotion"),
                new OscMapping("/avatar/parameters/Fcl_MTH_A", "Fcl_MTH_A", "lipsync"),
                new OscMapping("/avatar/parameters/Fcl_EYE_Close", "Fcl_EYE_Close", "eye")
            };
            var table = new OscMappingTable(mappings);

            Assert.IsTrue(table.TryGetByAddress("/avatar/parameters/Fcl_ALL_Joy", out var r1));
            Assert.AreEqual("emotion", r1.Layer);

            Assert.IsTrue(table.TryGetByAddress("/avatar/parameters/Fcl_MTH_A", out var r2));
            Assert.AreEqual("lipsync", r2.Layer);

            Assert.IsTrue(table.TryGetByAddress("/avatar/parameters/Fcl_EYE_Close", out var r3));
            Assert.AreEqual("eye", r3.Layer);
        }

        // ================================================================
        // ARKit プリセットパターン
        // ================================================================

        [Test]
        public void TryGetByAddress_ARKitPattern_CorrectMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/ARKit/eyeBlinkLeft", "eyeBlinkLeft", "eye"),
                new OscMapping("/ARKit/jawOpen", "jawOpen", "lipsync")
            };
            var table = new OscMappingTable(mappings);

            Assert.IsTrue(table.TryGetByAddress("/ARKit/eyeBlinkLeft", out var r1));
            Assert.AreEqual("eye", r1.Layer);
            Assert.AreEqual("eyeBlinkLeft", r1.BlendShapeName);

            Assert.IsTrue(table.TryGetByAddress("/ARKit/jawOpen", out var r2));
            Assert.AreEqual("lipsync", r2.Layer);
            Assert.AreEqual("jawOpen", r2.BlendShapeName);
        }

        // ================================================================
        // 2 バイト文字・特殊記号の正しい処理
        // ================================================================

        [Test]
        public void TryGetByAddress_JapaneseCharacters_CorrectMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/笑顔", "笑顔", "emotion")
            };
            var table = new OscMappingTable(mappings);

            Assert.IsTrue(table.TryGetByAddress("/avatar/parameters/笑顔", out var result));
            Assert.AreEqual("笑顔", result.BlendShapeName);
        }

        [Test]
        public void TryGetByBlendShapeName_SpecialCharacters_CorrectMapping()
        {
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Fcl_ALL+Joy(1)", "Fcl_ALL+Joy(1)", "emotion")
            };
            var table = new OscMappingTable(mappings);

            Assert.IsTrue(table.TryGetByBlendShapeName("Fcl_ALL+Joy(1)", out var result));
            Assert.AreEqual("/avatar/parameters/Fcl_ALL+Joy(1)", result.OscAddress);
        }

        // ================================================================
        // 大量マッピングの処理
        // ================================================================

        [Test]
        public void Constructor_LargeMappingSet_AllLookupSuccessful()
        {
            var mappings = new OscMapping[512];
            for (int i = 0; i < 512; i++)
            {
                mappings[i] = new OscMapping(
                    $"/avatar/parameters/bs_{i}",
                    $"bs_{i}",
                    i % 3 == 0 ? "emotion" : i % 3 == 1 ? "lipsync" : "eye");
            }
            var table = new OscMappingTable(mappings);

            Assert.AreEqual(512, table.Count);

            for (int i = 0; i < 512; i++)
            {
                Assert.IsTrue(table.TryGetByAddress($"/avatar/parameters/bs_{i}", out _),
                    $"インデックス {i} のアドレスが見つかりません");
                Assert.IsTrue(table.TryGetByBlendShapeName($"bs_{i}", out _),
                    $"インデックス {i} の BlendShape 名が見つかりません");
            }
        }
    }
}
