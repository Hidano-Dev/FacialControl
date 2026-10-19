using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// InputSystem の trigger sink（<c>InputSystemAdapterBinding</c> が登録する <c>ExpressionTriggerInputSource</c>）と同じ構成の
    /// trigger 入力源を <c>{slug}:trigger</c> に登録する Fake binding（slug 既定 <c>kbd</c>）。
    /// </summary>
    /// <remarks>
    /// REC 再生の trigger 注入（<c>RecTriggerInjector</c>）は、記録の source id と同じ id の trigger 入力源を registry から引いて直接駆動する。
    /// InputSystem と同じく最大スタック深度 16・LastWins で作る。テストアセンブリの型だが <c>[SerializeReference]</c> で
    /// Profile SO の AdapterBindings に保存できるよう <see cref="SerializableAttribute"/> を付ける。
    /// </remarks>
    [Serializable]
    public sealed class FakeTriggerAdapterBinding : AdapterBindingBase
    {
        public const string DefaultSlug = "kbd";
        public const string TriggerSub = "trigger";

        /// <summary>InputSystem の trigger sink と同じ最大スタック深度。</summary>
        public const int MaxStackDepth = 16;

        [NonSerialized] private FakeTriggerInputSource _source;

        public FakeTriggerAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>直近の OnStart で登録した trigger 入力源（未開始なら null）。</summary>
        public FakeTriggerInputSource Source => _source;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            AdapterSlug slug = AdapterSlug.Parse(string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug);
            Slug = slug.Value;
            IReadOnlyList<string> names = ctx.BlendShapeNames ?? Array.Empty<string>();
            _source = new FakeTriggerInputSource(
                InputSourceId.Parse(slug.Value + ":" + TriggerSub),
                names,
                ctx.Profile);
            ctx.InputSourceRegistry.Register(slug, TriggerSub, _source);
        }

        public override void Dispose()
        {
        }
    }

    /// <summary><see cref="FakeTriggerAdapterBinding"/> が登録する trigger 入力源。</summary>
    public sealed class FakeTriggerInputSource : ExpressionTriggerInputSourceBase
    {
        public FakeTriggerInputSource(InputSourceId id, IReadOnlyList<string> blendShapeNames, FacialProfile profile)
            : base(
                id,
                blendShapeCount: blendShapeNames.Count,
                maxStackDepth: FakeTriggerAdapterBinding.MaxStackDepth,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: blendShapeNames,
                profile: profile)
        {
        }
    }
}
