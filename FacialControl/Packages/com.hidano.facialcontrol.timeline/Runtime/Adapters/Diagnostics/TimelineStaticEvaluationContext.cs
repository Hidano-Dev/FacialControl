using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Models;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Diagnostics
{
    /// <summary>
    /// <see cref="TimelineDiagnosticsEvaluator.EvaluateStatic"/> の入力。Edit / Play 開始前に呼び出し側が解決した値をまとめて渡す。
    /// </summary>
    /// <remarks>
    /// <para>Profile は Edit では Editor が TimelineProfileSource 経由で、Play では <c>controller.CurrentProfile</c> を渡す（Evaluator は読み込まない）。</para>
    /// <para>Unity 型（Director / Timeline / Controller / Bake）を含むため Adapters に置く（D14）。</para>
    /// </remarks>
    public readonly struct TimelineStaticEvaluationContext
    {
        private static readonly GazeChannel[] EmptyGazeChannels = Array.Empty<GazeChannel>();

        /// <param name="director">解決した Director（未解決なら null）。</param>
        /// <param name="directorStatus">Director の解決経路。</param>
        /// <param name="timeline">Director にバインドされた TimelineAsset（未バインドなら null）。</param>
        /// <param name="controller">Receiver が接続する FacialController（無ければ null）。</param>
        /// <param name="profileSource">Profile SO（無ければ null）。</param>
        /// <param name="profile">評価に使う Profile。</param>
        /// <param name="hasProfile">Profile を解決できたか。</param>
        /// <param name="gazeChannels">Profile 内容ハッシュに含める GazeChannels。null なら Profile SO の値を使う。</param>
        /// <param name="bake">Bake の解決結果。</param>
        /// <param name="derivation">トラック記述子と Profile からの導出結果（null 可）。</param>
        /// <param name="trackBindings">Track binding 自動設定の結果（未実行なら null）。</param>
        /// <param name="expectedProfileContentHashHex">Bake に記録された Profile 内容ハッシュ。null なら採用 Bake の値を使う。</param>
        public TimelineStaticEvaluationContext(
            PlayableDirector director,
            DirectorResolveStatus directorStatus,
            TimelineAsset timeline,
            FacialController controller,
            FacialCharacterProfileSO profileSource,
            FacialProfile profile,
            bool hasProfile,
            IReadOnlyList<GazeChannel> gazeChannels,
            BakeLocateResult bake,
            TimelineDerivation derivation,
            TrackBindingReport? trackBindings = null,
            string expectedProfileContentHashHex = null)
        {
            Director = director;
            DirectorStatus = directorStatus;
            Timeline = timeline;
            Controller = controller;
            ProfileSource = profileSource;
            Profile = profile;
            HasProfile = hasProfile;
            GazeChannels = gazeChannels
                ?? (profileSource != null ? profileSource.GazeChannels : null)
                ?? EmptyGazeChannels;
            Bake = bake;
            Derivation = derivation;
            HasTrackBindings = trackBindings.HasValue;
            TrackBindings = trackBindings ?? default;
            ExpectedProfileContentHashHex = expectedProfileContentHashHex
                ?? (bake.Bake != null ? bake.Bake.ProfileContentHashHex : string.Empty);
        }

        public PlayableDirector Director { get; }

        public DirectorResolveStatus DirectorStatus { get; }

        public TimelineAsset Timeline { get; }

        public FacialController Controller { get; }

        public FacialCharacterProfileSO ProfileSource { get; }

        public FacialProfile Profile { get; }

        public bool HasProfile { get; }

        public IReadOnlyList<GazeChannel> GazeChannels { get; }

        public BakeLocateResult Bake { get; }

        public TimelineDerivation Derivation { get; }

        /// <summary><see cref="TrackBindings"/> が与えられているか。</summary>
        public bool HasTrackBindings { get; }

        public TrackBindingReport TrackBindings { get; }

        /// <summary>Bake に記録された Profile 内容ハッシュ（空文字は不一致扱い）。</summary>
        public string ExpectedProfileContentHashHex { get; }
    }
}
