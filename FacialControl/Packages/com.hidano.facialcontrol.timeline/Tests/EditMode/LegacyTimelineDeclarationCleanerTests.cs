using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Profile SO の旧 <c>timeline:*:state</c> 宣言を走査し、Undo 可能に削除する
    /// <see cref="LegacyTimelineDeclarationCleaner"/> を固定する。AssetDatabase で SO を作るため Medium。
    /// </summary>
    [MediumTest]
    public sealed class LegacyTimelineDeclarationCleanerTests : SizedTestFixture
    {
        private static readonly AdapterSlug Slug = AdapterSlug.Parse("timeline");

        private string _folderPath;
        private readonly List<UnityEngine.Object> _transient = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            string folderName = "LegacyTimelineDeclarationCleanerTests_" + Guid.NewGuid().ToString("N");
            _folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            for (int i = 0; i < _transient.Count; i++)
            {
                if (_transient[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_transient[i]);
                }
            }

            _transient.Clear();
            if (AssetDatabase.IsValidFolder(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
            }
        }

        [Test]
        public void Scan_TwoStateAndOneValueDeclaration_ListsThreeWithTwoStates()
        {
            FacialCharacterProfileSO so = CreateProfile();

            IReadOnlyList<LegacyTimelineDeclaration> found = LegacyTimelineDeclarationCleaner.Scan(so, Slug);

            Assert.That(found.Count, Is.EqualTo(3));
            Assert.That(CountStates(found), Is.EqualTo(2));
            Assert.That(Ids(found), Is.EquivalentTo(new[] { "timeline:emotion:state", "timeline:emotion", "timeline:layer1:state" }));
            Assert.That(Find(found, "timeline:layer1:state").LayerName, Is.EqualTo("表情"));
            Assert.That(Find(found, "timeline:emotion").IsStateDeclaration, Is.False);
        }

        [Test]
        public void Scan_UsesSameJudgementAsConnector()
        {
            FacialCharacterProfileSO so = CreateProfile();

            IReadOnlyList<LegacyTimelineDeclaration> found = LegacyTimelineDeclarationCleaner.Scan(so, Slug);

            Assert.That(found, Is.Not.Empty);
            for (int i = 0; i < found.Count; i++)
            {
                Assert.That(
                    found[i].IsStateDeclaration,
                    Is.EqualTo(TimelineSinkIdConvention.IsLegacyStateDeclaration(found[i].DeclaredId, Slug)),
                    found[i].DeclaredId);
            }
        }

        [Test]
        public void Scan_DoesNotModifyProfile()
        {
            FacialCharacterProfileSO so = CreateProfile();
            EditorUtility.ClearDirty(so);

            LegacyTimelineDeclarationCleaner.Scan(so, Slug);

            Assert.That(so.Layers[0].inputSources.Count, Is.EqualTo(4));
            Assert.That(EditorUtility.IsDirty(so), Is.False);
        }

        [Test]
        public void RemoveStateDeclarations_RemovesOnlyStateAndKeepsValueSinkWeight()
        {
            FacialCharacterProfileSO so = CreateProfile();

            int removed = LegacyTimelineDeclarationCleaner.RemoveStateDeclarations(so, Slug);

            Assert.That(removed, Is.EqualTo(2));
            IReadOnlyList<LegacyTimelineDeclaration> after = LegacyTimelineDeclarationCleaner.Scan(so, Slug);
            Assert.That(Ids(after), Is.EquivalentTo(new[] { "timeline:emotion" }));
            InputSourceDeclarationSerializable value = so.Layers[0].inputSources.Find(d => d.id == "timeline:emotion");
            Assert.That(value.weight, Is.EqualTo(0.5f));
            Assert.That(so.Layers[0].inputSources.Exists(d => d.id == "osc:emotion"), Is.True, "他の宣言は残る");
            Assert.That(so.Layers[0].inputSources.Exists(d => d.id == "other:emotion:state"), Is.True, "他 slug の state は対象外");
            Assert.That(EditorUtility.IsDirty(so), Is.True);
        }

        [Test]
        public void RemoveStateDeclarations_Undo_RestoresThreeDeclarations()
        {
            FacialCharacterProfileSO so = CreateProfile();
            Undo.IncrementCurrentGroup();

            LegacyTimelineDeclarationCleaner.RemoveStateDeclarations(so, Slug);
            Undo.PerformUndo();

            IReadOnlyList<LegacyTimelineDeclaration> restored = LegacyTimelineDeclarationCleaner.Scan(so, Slug);
            Assert.That(restored.Count, Is.EqualTo(3));
            Assert.That(CountStates(restored), Is.EqualTo(2));
        }

        [Test]
        public void Scan_OtherSlugStateDeclaration_IsNotTarget()
        {
            FacialCharacterProfileSO so = CreateProfile();

            IReadOnlyList<LegacyTimelineDeclaration> found = LegacyTimelineDeclarationCleaner.Scan(so, AdapterSlug.Parse("other"));

            Assert.That(Ids(found), Is.EquivalentTo(new[] { "other:emotion:state" }));
            Assert.That(CountStates(found), Is.EqualTo(1));
        }

        [Test]
        public void RemoveStateDeclarations_NothingToRemove_ReturnsZeroWithoutDirty()
        {
            FacialCharacterProfileSO so = CreateProfile();
            LegacyTimelineDeclarationCleaner.RemoveStateDeclarations(so, Slug);
            EditorUtility.ClearDirty(so);

            int removed = LegacyTimelineDeclarationCleaner.RemoveStateDeclarations(so, Slug);

            Assert.That(removed, Is.EqualTo(0));
            Assert.That(EditorUtility.IsDirty(so), Is.False);
        }

        [Test]
        public void ResolveSlug_UsesTimelineBindingSlugOrDefault()
        {
            var withBinding = ScriptableObject.CreateInstance<TimelineTestProfileSO>();
            _transient.Add(withBinding);
            withBinding.WritableAdapterBindings.Add(new TimelineAdapterBinding { Slug = "tl" });
            var withoutBinding = ScriptableObject.CreateInstance<TimelineTestProfileSO>();
            _transient.Add(withoutBinding);

            Assert.That(LegacyTimelineDeclarationCleaner.ResolveSlug(withBinding).Value, Is.EqualTo("tl"));
            Assert.That(LegacyTimelineDeclarationCleaner.ResolveSlug(withoutBinding).Value, Is.EqualTo("timeline"));
        }

        private FacialCharacterProfileSO CreateProfile()
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                priority = 0,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "timeline:emotion:state", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "timeline:emotion", weight = 0.5f },
                    new InputSourceDeclarationSerializable { id = "osc:emotion", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "other:emotion:state", weight = 1f },
                },
            });
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = "表情",
                priority = 1,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "timeline:layer1:state", weight = 1f },
                },
            });
            AssetDatabase.CreateAsset(so, _folderPath + "/Profile.asset");
            AssetDatabase.SaveAssets();
            return so;
        }

        private static int CountStates(IReadOnlyList<LegacyTimelineDeclaration> found)
        {
            int count = 0;
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].IsStateDeclaration)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<string> Ids(IReadOnlyList<LegacyTimelineDeclaration> found)
        {
            var ids = new List<string>();
            for (int i = 0; i < found.Count; i++)
            {
                ids.Add(found[i].DeclaredId);
            }

            return ids;
        }

        private static LegacyTimelineDeclaration Find(IReadOnlyList<LegacyTimelineDeclaration> found, string id)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].DeclaredId == id)
                {
                    return found[i];
                }
            }

            throw new InvalidOperationException(id + " not found");
        }
    }
}
