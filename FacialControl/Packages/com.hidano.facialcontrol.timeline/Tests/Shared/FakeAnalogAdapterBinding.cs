using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// InputSystem の analog expression 構成（<c>InputSystemAdapterBinding.BuildAnalogExpressionSink</c>）と同形の Fake binding。
    /// </summary>
    /// <remarks>
    /// <para>OnStart で次を registry に登録する（slug 既定 <c>osc</c>）:</para>
    /// <list type="bullet">
    ///   <item><c>{slug}:lt</c> — 1 軸の <see cref="FakeAnalogInputSource"/>（Timeline の Analog チャネルの乗っ取り先）</item>
    ///   <item><c>{slug}:gaze</c> — 2 軸の <see cref="FakeAnalogInputSource"/>（Timeline の Gaze チャネルの乗っ取り先。OSC binding が登録する gaze 入力源の代役）</item>
    ///   <item><c>{slug}:analog-expression</c> — core の実 <see cref="AnalogExpressionInputSource"/>（binding <c>lt</c> → Expression <see cref="AnalogExpressionId"/>）。
    ///     <see cref="IRegistryAttachableAnalogConsumer.AttachRegistry"/> で registry に接続し、<c>{slug}:lt</c> の Replace に追従する</item>
    /// </list>
    /// <para><see cref="IAnalogExpressionBindingDeclaration"/> で同じ binding を宣言し、Edit プレビューが同じ消費者をオフラインに組めるようにする。</para>
    /// <para>Dispose で registry から切断する。テストアセンブリの型だが <c>[SerializeReference]</c> で
    /// Profile SO の AdapterBindings に保存できるよう <see cref="SerializableAttribute"/> を付ける。</para>
    /// </remarks>
    [Serializable]
    public sealed class FakeAnalogAdapterBinding : AdapterBindingBase, IAnalogExpressionBindingDeclaration
    {
        public const string DefaultSlug = "osc";
        public const string AnalogSub = "lt";
        public const string GazeSub = "gaze";

        [SerializeField] private string analogExpressionId = "squint";

        [NonSerialized] private FakeAnalogInputSource _analogSource;
        [NonSerialized] private FakeAnalogInputSource _gazeSource;
        [NonSerialized] private AnalogExpressionInputSource _analogExpressionSink;
        [NonSerialized] private IInputSourceRegistry _registry;
        [NonSerialized] private AdapterSlug _slug;
        [NonSerialized] private int _startCount;

        public FakeAnalogAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>Analog クリップ値を掛ける Expression の id。</summary>
        public string AnalogExpressionId
        {
            get => analogExpressionId;
            set => analogExpressionId = value ?? string.Empty;
        }

        /// <summary>直近の OnStart で <c>{slug}:lt</c> に登録した Fake（未開始なら null）。</summary>
        public FakeAnalogInputSource AnalogSource => _analogSource;

        /// <summary>直近の OnStart で <c>{slug}:gaze</c> に登録した Fake（未開始なら null）。</summary>
        public FakeAnalogInputSource GazeSource => _gazeSource;

        /// <summary>直近の OnStart で <c>{slug}:analog-expression</c> に登録した core の消費者（未開始なら null）。</summary>
        public AnalogExpressionInputSource AnalogExpressionSink => _analogExpressionSink;

        /// <summary>OnStart が呼ばれた回数。</summary>
        public int StartCount => _startCount;

        public string AnalogSourceId => Slug + ":" + AnalogSub;

        public string GazeSourceId => Slug + ":" + GazeSub;

        public string AnalogExpressionSourceId => Slug + ":" + AnalogExpressionInputSource.ReservedId;

        /// <inheritdoc />
        /// <remarks>OnStart で <see cref="AnalogExpressionInputSource"/> へ渡すものと同じ binding（<c>lt</c> → <see cref="AnalogExpressionId"/>）。</remarks>
        public IReadOnlyList<AnalogExpressionBinding> GetAnalogExpressionBindings()
        {
            return new[] { CreateAnalogExpressionBinding() };
        }

        public override void OnStart(in AdapterBuildContext ctx)
        {
            ReleaseRegistrations();

            string slugText = string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug;
            _slug = AdapterSlug.Parse(slugText);
            Slug = _slug.Value;
            _registry = ctx.InputSourceRegistry;
            _startCount++;

            _analogSource = new FakeAnalogInputSource(AnalogSourceId, axisCount: 1);
            _gazeSource = new FakeAnalogInputSource(GazeSourceId, axisCount: 2);
            _registry.Register(_slug, AnalogSub, _analogSource);
            _registry.Register(_slug, GazeSub, _gazeSource);

            IReadOnlyList<string> blendShapeNames = ctx.BlendShapeNames ?? Array.Empty<string>();
            _analogExpressionSink = new AnalogExpressionInputSource(
                InputSourceId.Parse(AnalogExpressionInputSource.ReservedId),
                blendShapeNames.Count,
                blendShapeNames,
                ctx.Profile,
                new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
                {
                    { AnalogSub, _analogSource },
                },
                new[] { CreateAnalogExpressionBinding() });

            // InputSystemAdapterBinding.BuildAnalogExpressionSink と同じ順序（Register → AttachRegistry）。
            _registry.Register(_slug, AnalogExpressionInputSource.ReservedId, _analogExpressionSink);
            _analogExpressionSink.AttachRegistry(_registry, _slug);
        }

        public override void Dispose()
        {
            ReleaseRegistrations();
        }

        private AnalogExpressionBinding CreateAnalogExpressionBinding()
        {
            return new AnalogExpressionBinding(AnalogSub, 0, analogExpressionId, 1f);
        }

        private void ReleaseRegistrations()
        {
            if (_registry == null)
            {
                return;
            }

            // registry は FacialController の child scope と一緒に破棄されるため、登録の取り外しは行わず切断だけする
            // （InputSystemAdapterBinding と同じ。Cleanup 中に registry 通知を発生させない）。
            _analogExpressionSink?.DetachRegistry();
            _registry = null;
        }
    }
}
