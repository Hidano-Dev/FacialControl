using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// アセット化した Profile SO と、それと食い違う StreamingAssets の profile.json を用意する Medium テスト用 fixture。
    /// 生成物（<c>Assets/{prefix}_{guid}/</c> と profile.json のフォルダ）は <see cref="Dispose"/> で削除する。
    /// </summary>
    internal sealed class ProfileJsonTestFixture : IDisposable
    {
        public const string LayerName = "emotion";

        private ProfileJsonTestFixture(string folderPath, string assetName, FacialCharacterProfileSO profileAsset)
        {
            FolderPath = folderPath;
            AssetName = assetName;
            ProfileAsset = profileAsset;
        }

        public string FolderPath { get; }

        public string AssetName { get; }

        public FacialCharacterProfileSO ProfileAsset { get; }

        public string ProfileJsonPath => FacialCharacterProfileSO.GetStreamingAssetsProfilePath(AssetName);

        /// <summary>SO 側の Expression（<paramref name="expressionIds"/>）だけを持つ Profile SO を保存して返す。</summary>
        public static ProfileJsonTestFixture Create(string prefix, params string[] expressionIds)
        {
            string guid = Guid.NewGuid().ToString("N");
            string folderName = prefix + "_" + guid;
            string folderPath = "Assets/" + folderName;
            string assetName = prefix + "Profile_" + guid;
            AssetDatabase.CreateFolder("Assets", folderName);

            var profileAsset = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            AddContent(profileAsset, expressionIds);
            AssetDatabase.CreateAsset(profileAsset, folderPath + "/" + assetName + ".asset");
            AssetDatabase.SaveAssets();
            TimelineProfileSource.InvalidateAll();
            return new ProfileJsonTestFixture(folderPath, assetName, profileAsset);
        }

        /// <summary>
        /// <paramref name="expressionIds"/> を持つ Profile を profile.json として書き、最終更新時刻を指定値にする
        /// （同一秒内の連続書き込みでもキャッシュキーが変わるようにするため）。
        /// </summary>
        public void WriteProfileJson(DateTime lastWriteTimeUtc, params string[] expressionIds)
        {
            var source = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            try
            {
                AddContent(source, expressionIds);
                WriteJson(source, lastWriteTimeUtc);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private void WriteJson(FacialCharacterProfileSO source, DateTime lastWriteTimeUtc)
        {
            string path = ProfileJsonPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new SystemTextJsonParser().SerializeProfile(source.BuildFallbackProfile()));
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        /// <summary>
        /// SO と同じ内容（<see cref="LayerName"/> レイヤーと <paramref name="baseExpressionIds"/>）に、SO に無いレイヤー
        /// <paramref name="extraLayerName"/> と Expression <paramref name="extraExpressionId"/> を足した profile.json を書く。
        /// </summary>
        public void WriteProfileJsonWithExtraLayer(
            DateTime lastWriteTimeUtc,
            string extraLayerName,
            string extraExpressionId,
            params string[] baseExpressionIds)
        {
            var source = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            try
            {
                AddContent(source, baseExpressionIds);
                source.Layers.Add(new LayerDefinitionSerializable
                {
                    name = extraLayerName,
                    priority = 1,
                    exclusionMode = ExclusionMode.LastWins,
                });
                source.Expressions.Add(CreateExpression(extraExpressionId, extraLayerName));
                WriteJson(source, lastWriteTimeUtc);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        public string ContentHash(FacialProfile profile)
        {
            return FacialTimelineHashCalculator.ComputeProfileContentHashHex(
                profile,
                FacialTimelineHashCalculator.ToGazeChannelArray(ProfileAsset.GazeChannels));
        }

        public void Dispose()
        {
            TimelineProfileSource.InvalidateAll();
            if (AssetDatabase.IsValidFolder(FolderPath))
            {
                AssetDatabase.DeleteAsset(FolderPath);
            }

            string exportDir = Path.GetDirectoryName(ProfileJsonPath);
            if (string.IsNullOrEmpty(exportDir))
            {
                return;
            }

            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }

            if (File.Exists(exportDir + ".meta"))
            {
                File.Delete(exportDir + ".meta");
            }
        }

        private static void AddContent(FacialCharacterProfileSO profileAsset, string[] expressionIds)
        {
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = LayerName,
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            });

            for (int i = 0; i < expressionIds.Length; i++)
            {
                profileAsset.Expressions.Add(CreateExpression(expressionIds[i], LayerName));
            }
        }

        private static ExpressionSerializable CreateExpression(string id, string layerName)
        {
            return new ExpressionSerializable
            {
                id = id,
                name = id,
                layer = layerName,
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable
                    {
                        name = "Smile",
                        value = 1f,
                    },
                },
            };
        }
    }
}
