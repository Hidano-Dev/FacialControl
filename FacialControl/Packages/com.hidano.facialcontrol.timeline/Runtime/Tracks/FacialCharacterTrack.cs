using System;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    /// <summary>
    /// キャラ 1 体ぶんの親トラック。FacialController を Bind し、子の表情 / 値 / レイヤー weight トラックはすべてこの対象を使う（HID-408）。
    /// </summary>
    /// <remarks>
    /// <para>子トラックは Bind を持たない。Mixer は自分の祖先にあるこのトラックの binding（<see cref="FacialCharacterTrackBinding"/>）
    /// から対象を引く。AnimationTrack の Override Track と同じく、Bind はこのトラック 1 つで足りる。</para>
    /// <para>Bind されていない親トラックの子は再生しない（自動 Bind はしない）。</para>
    /// <para>子トラックは <see cref="CreateChildTrack{T}"/> で作る。Timeline の <c>CreateTrack(parent, …)</c> は親の型に
    /// <c>SupportsChildTracks</c> 属性（Timeline の internal）を要求するため、本トラックの子は作れない。Timeline ウィンドウの
    /// メニューから子トラックを足すこともできない（REC Export が作る）。</para>
    /// <para>子トラックは型が違うので Timeline の出力トラックになり、Timeline は子の Mixer をこのトラックの Playable の入力へ
    /// 繋いで、その入力の番号を出力ポートとして出力を作る。各出力が自分の子の Mixer だけを辿るよう、本トラックの Playable は
    /// Passthrough（出力 i → 入力 i）にし、子の数だけ出力ポートを用意する。本トラックは子孫の Clip もまとめて
    /// 自分の Mixer へ繋ぐ（Timeline の仕様）が、本トラックの Mixer は何もしない。</para>
    /// </remarks>
    [TrackBindingType(typeof(FacialController))]
    [TrackColor(0.55f, 0.42f, 0.78f)]
    public sealed class FacialCharacterTrack : TrackAsset
    {
        /// <summary>
        /// Clip が無くても常に Mixer を作る。作らないと、子孫の Clip が 1 トラック分だけのときに Timeline がその子の Mixer を
        /// 本トラックの位置に作り、子の Mixer が 2 つ（本トラックの出力と子自身の出力）できてしまう。
        /// </summary>
        public override bool CanCreateTrackMixer()
        {
            return true;
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<FacialCharacterMixerBehaviour> playable =
                ScriptPlayable<FacialCharacterMixerBehaviour>.Create(graph, inputCount);
            playable.SetTraversalMode(PlayableTraversalMode.Passthrough);

            // Director の時刻（シーク・Edit のスクラブ）を子トラックの Mixer へ伝える（Timeline 直下の Mixer と同じ時刻になる）。
            playable.SetPropagateSetTime(true);

            // Timeline は子トラックの Mixer を、まとめて繋いだ Clip の後ろへ 1 つずつ足し、その入力番号を出力ポートに使う。
            playable.SetOutputCount(inputCount + CountChildTracks() + 1);
            return playable;
        }

        /// <summary>
        /// 本トラックの子として Facial トラック（表情 / 値 / レイヤー weight）を作る。Timeline の <c>CreateTrack</c> と同じ生成処理
        /// （サブアセット化・Undo 記録・親への追加）を使い、親の型の検査だけを省く。
        /// </summary>
        public T CreateChildTrack<T>(string trackName) where T : TrackAsset
        {
            if (!FacialCharacterTrackBinding.IsFacialChildTrack(typeof(T)))
            {
                throw new InvalidOperationException(
                    $"{nameof(FacialCharacterTrack)} の子にできるのは表情 / 値 / レイヤー weight トラックだけです（{typeof(T).Name}）。");
            }

            TimelineAsset timeline = timelineAsset;
            if (timeline == null)
            {
                throw new InvalidOperationException($"{nameof(FacialCharacterTrack)} '{name}' が TimelineAsset に属していません。");
            }

            AllocateTrackMethod ??= typeof(TimelineAsset).GetMethod(
                "AllocateTrack",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TrackAsset), typeof(string), typeof(Type) },
                null);
            if (AllocateTrackMethod == null)
            {
                throw new InvalidOperationException("TimelineAsset.AllocateTrack が見つかりません（Timeline パッケージの非互換）。");
            }

            return (T)AllocateTrackMethod.Invoke(timeline, new object[] { this, trackName ?? typeof(T).Name, typeof(T) });
        }

        private static MethodInfo AllocateTrackMethod;

        private int CountChildTracks()
        {
            int count = 0;
            foreach (TrackAsset _ in GetChildTracks())
            {
                count++;
            }

            return count;
        }
    }
}
