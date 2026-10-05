using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// <c>PlayableDirector.SetGenericBinding</c> の書込口（D7）。Runtime は直接設定し（<see cref="RuntimeTrackBindingWriter"/>）、
    /// Editor 実装は Undo 記録と SetDirty を伴う（後続タスクで Editor asmdef に置く）。
    /// </summary>
    public interface ITrackBindingWriter
    {
        void SetGenericBinding(PlayableDirector director, TrackAsset track, UnityEngine.Object value);
    }

    /// <summary>
    /// Runtime（Play モード）用の書込口。Undo を持たず Director へ直接設定する。
    /// </summary>
    public sealed class RuntimeTrackBindingWriter : ITrackBindingWriter
    {
        public static readonly RuntimeTrackBindingWriter Instance = new RuntimeTrackBindingWriter();

        private RuntimeTrackBindingWriter()
        {
        }

        public void SetGenericBinding(PlayableDirector director, TrackAsset track, UnityEngine.Object value)
        {
            director.SetGenericBinding(track, value);
        }
    }
}
