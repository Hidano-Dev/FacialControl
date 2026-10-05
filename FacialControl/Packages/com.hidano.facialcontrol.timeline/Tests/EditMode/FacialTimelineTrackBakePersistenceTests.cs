using System;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Facial トラックの Bake 参照（<see cref="IFacialTimelineBakeHolder"/>）が
    /// アセットとして保存 → 再読込しても同じサブアセットを指すことを固定する。
    /// AssetDatabase を使うため Small の <see cref="FacialTimelineTrackAssetTests"/> とは分けて Medium に置く。
    /// </summary>
    [MediumTest]
    public sealed class FacialTimelineTrackBakePersistenceTests : SizedTestFixture
    {
        private string _folderPath;

        [SetUp]
        public void SetUp()
        {
            string folderName = "FacialTimelineTrackBakePersistenceTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            _folderPath = "Assets/" + folderName;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void BakeReference_SaveAndReimport_RestoresSameSubAssetOnAllFacialTracks()
        {
            string timelinePath = _folderPath + "/Timeline.playable";
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, timelinePath);

            var expressionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            var childTrack = timeline.CreateTrack<FacialExpressionTrack>(expressionTrack, "emotion Lane 1");
            var valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            AssetDatabase.AddObjectToAsset(bake, timeline);

            ((IFacialTimelineBakeHolder)expressionTrack).Bake = bake;
            ((IFacialTimelineBakeHolder)childTrack).Bake = bake;
            ((IFacialTimelineBakeHolder)valueTrack).Bake = bake;
            EditorUtility.SetDirty(expressionTrack);
            EditorUtility.SetDirty(childTrack);
            EditorUtility.SetDirty(valueTrack);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(timelinePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            UnityEngine.Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(timelinePath);
            FacialTimelineBakeAsset loadedBake = null;
            int holderCount = 0;
            foreach (UnityEngine.Object asset in loaded)
            {
                if (asset is FacialTimelineBakeAsset b)
                {
                    loadedBake = b;
                }
            }

            Assert.That(loadedBake, Is.Not.Null);
            foreach (UnityEngine.Object asset in loaded)
            {
                if (asset is IFacialTimelineBakeHolder holder)
                {
                    holderCount++;
                    Assert.That(holder.Bake, Is.SameAs(loadedBake), ((TrackAsset)asset).name);
                }
            }

            Assert.That(holderCount, Is.EqualTo(3));
        }

        [Test]
        public void BakeReference_SerializedProperty_ExistsAndIsHiddenFromInspectorIteration()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, _folderPath + "/Hidden.playable");
            var valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            AssetDatabase.AddObjectToAsset(bake, timeline);
            ((IFacialTimelineBakeHolder)valueTrack).Bake = bake;

            using (var serialized = new SerializedObject(valueTrack))
            {
                SerializedProperty property = serialized.FindProperty("bake");
                Assert.That(property, Is.Not.Null);
                Assert.That(property.objectReferenceValue, Is.SameAs(bake));

                bool visibleInInspector = false;
                SerializedProperty iterator = serialized.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.name == "bake")
                    {
                        visibleInInspector = true;
                    }
                }

                Assert.That(visibleInInspector, Is.False);
            }
        }
    }
}
