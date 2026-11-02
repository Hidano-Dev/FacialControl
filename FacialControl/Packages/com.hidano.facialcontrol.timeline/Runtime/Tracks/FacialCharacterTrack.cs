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
    /// Passthrough（出力 i → 入力 i）にし、子の数だけ出力ポートを用意する。Timeline は本トラックの Mixer にも子孫の Clip を
    /// まとめて渡すが、Clip の Playable は作らない（<see cref="CreatePlayable(PlayableGraph, GameObject, TimelineClip)"/>）。</para>
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

        /// <summary>
        /// 子孫の Clip の Playable を作らない。Timeline は本トラックの Mixer 生成時に子孫の Clip をまとめて本トラックへ渡す
        /// （<c>GatherCompilableTracks</c> は子孫を常に辿る）が、子孫の Clip は各子トラックの Mixer が自分で再生するため、
        /// ここで作ると Clip の Playable が 2 倍になる（Interval Tree にも二重に載る）。Null を返すと Timeline は接続・登録を飛ばす。
        /// </summary>
        protected override Playable CreatePlayable(PlayableGraph graph, GameObject gameObject, TimelineClip clip)
        {
            return Playable.Null;
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
        /// 本トラックの子として Facial トラック（表情 / 値 / レイヤー weight）を作る。名前は兄弟トラックと重ならないよう
        /// <c>名前 (1)</c> 形式で一意にする（Timeline の <c>CreateTrack</c> と同じ）。
        /// </summary>
        /// <remarks>
        /// <para>Timeline の公開 API で子トラックを作れるのは <c>CreateTrack(type, parent, name)</c> だけで、親の型に Timeline の internal
        /// 属性 <c>SupportsChildTracks</c> を要求するため本トラックには使えない。そこで <c>CreateTrack</c> が内部で呼ぶ
        /// <c>TimelineAsset.AllocateTrack</c>（サブアセット化・Undo 記録・親への追加）を reflection で呼ぶ。</para>
        /// <para>Timeline パッケージの更新で <c>AllocateTrack</c> が無くなった / 失敗したときは、トラックを root に作って Error を出す
        /// （親トラックの外なので再生されず、診断で <c>LegacyTrackStructure</c> が出る）。</para>
        /// </remarks>
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

            string uniqueName = MakeUniqueChildName(string.IsNullOrEmpty(trackName) ? typeof(T).Name : trackName);
            MethodInfo allocate = ResolveAllocateTrackMethod();
            if (allocate != null)
            {
                try
                {
                    if (allocate.Invoke(timeline, new object[] { this, uniqueName, typeof(T) }) is T created)
                    {
                        return created;
                    }
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }

            Debug.LogError(
                $"[{nameof(FacialCharacterTrack)}] Timeline パッケージの内部 API（TimelineAsset.AllocateTrack）で子トラック '{uniqueName}' を"
                + $" '{name}' の下に作れなかったため、root に作りました。このトラックは親トラックの外にあるので再生されません。"
                + " FacialControl の対応する Timeline パッケージのバージョンを確認してください。",
                timeline);
            return (T)timeline.CreateTrack(typeof(T), null, uniqueName);
        }

        /// <summary>子トラックの名前を兄弟と重ならないようにする（<c>名前</c> → <c>名前 (1)</c> → <c>名前 (2)</c> …）。</summary>
        internal string MakeUniqueChildName(string baseName)
        {
            var used = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (TrackAsset child in GetChildTracks())
            {
                if (child != null)
                {
                    used.Add(child.name);
                }
            }

            if (!used.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 1; ; i++)
            {
                string candidate = baseName + " (" + i + ")";
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>テスト用: reflection の解決先を差し替える（null を入れるとフォールバック経路を通る）。</summary>
        internal static Func<MethodInfo> AllocateTrackResolverOverride { get; set; }

        private static MethodInfo ResolveAllocateTrackMethod()
        {
            if (AllocateTrackResolverOverride != null)
            {
                return AllocateTrackResolverOverride();
            }

            return AllocateTrackMethod ??= typeof(TimelineAsset).GetMethod(
                "AllocateTrack",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TrackAsset), typeof(string), typeof(Type) },
                null);
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
