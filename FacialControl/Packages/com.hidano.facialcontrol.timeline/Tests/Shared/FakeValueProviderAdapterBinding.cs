using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// iFacialMocap の BlendShape 受信（<c>IFacialMocapReceiverAdapterBinding</c> の <c>_registry.Register(slug, _inputSource)</c>）と
    /// 同形の Fake binding。OnStart で slug だけの id（既定 <c>ifm</c>）にホスト BlendShape 数の値提供型を登録する。
    /// </summary>
    /// <remarks>
    /// Timeline の値提供型チャネルの乗っ取り先。<c>[SerializeReference]</c> で Profile SO の AdapterBindings に保存できるよう
    /// <see cref="SerializableAttribute"/> を付ける。
    /// </remarks>
    [Serializable]
    public sealed class FakeValueProviderAdapterBinding : AdapterBindingBase
    {
        public const string DefaultSlug = "ifm";

        [NonSerialized] private FakeLiveValueProvider _source;

        public FakeValueProviderAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>直近の OnStart で登録した live の値提供型（未開始なら null）。</summary>
        public FakeLiveValueProvider Source => _source;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            AdapterSlug slug = AdapterSlug.Parse(string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug);
            Slug = slug.Value;
            IReadOnlyList<string> names = ctx.BlendShapeNames ?? Array.Empty<string>();
            _source = new FakeLiveValueProvider(InputSourceId.Parse(slug.Value), names.Count);
            ctx.InputSourceRegistry.Register(slug, _source);
        }

        public override void Dispose()
        {
        }
    }

    /// <summary>live 入力の代役。<see cref="Set"/> した BlendShape だけ寄与し、何も Set していなければ無効。</summary>
    public sealed class FakeLiveValueProvider : ValueProviderInputSourceBase
    {
        private readonly float[] _values;
        private readonly BitArray _mask;
        private bool _isValid;

        public FakeLiveValueProvider(InputSourceId id, int blendShapeCount)
            : base(id, blendShapeCount)
        {
            _values = new float[blendShapeCount];
            _mask = new BitArray(blendShapeCount, false);
        }

        public override BitArray ContributeMask => _mask;

        public void Set(int index, float value)
        {
            _values[index] = value;
            _mask[index] = true;
            _isValid = true;
        }

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_isValid)
            {
                return false;
            }

            for (int i = 0; i < Math.Min(output.Length, BlendShapeCount); i++)
            {
                if (_mask[i])
                {
                    output[i] = _values[i];
                }
            }

            return true;
        }
    }
}
