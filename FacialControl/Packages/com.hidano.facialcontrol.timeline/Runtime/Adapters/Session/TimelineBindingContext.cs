using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// Timeline binding（<c>TimelineAdapterBinding.OnStart</c>）が Receiver に渡す接続コンテキスト。
    /// </summary>
    /// <remarks>
    /// <para>binding の OnStart は FacialController の初期化中（child scope build）に呼ばれるため、受け取った時点では
    /// controller は未初期化。Receiver はコンテキストを保持するだけで、接続は再生セッションの開始まで遅らせる。</para>
    /// </remarks>
    public readonly struct TimelineBindingContext
    {
        public TimelineBindingContext(
            AdapterSlug slug,
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            IInputSourceRegistry registry,
            FacialController controller,
            bool enabled)
        {
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            Slug = slug;
            Profile = profile;
            BlendShapeNames = blendShapeNames ?? throw new ArgumentNullException(nameof(blendShapeNames));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Controller = controller;
            Enabled = enabled;
        }

        /// <summary>Timeline binding の slug（値 sink の id の prefix）。</summary>
        public AdapterSlug Slug { get; }

        /// <summary>binding 構築時点の Profile（controller が保持する値と同じ）。</summary>
        public FacialProfile Profile { get; }

        /// <summary>ホストの BlendShape 名列。</summary>
        public IReadOnlyList<string> BlendShapeNames { get; }

        /// <summary>FacialController の child scope の入力源 registry。</summary>
        public IInputSourceRegistry Registry { get; }

        /// <summary>binding の宿主 GameObject の FacialController（無ければ null）。</summary>
        public FacialController Controller { get; }

        /// <summary>Timeline からの受信を許可するか。</summary>
        public bool Enabled { get; }
    }
}
