using System;
using Hidano.FacialControl.Timeline.Adapters.Session;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Edit 用の Track binding 書込口（D7）。シーン側オブジェクト（Director）の変更なので
    /// <see cref="Undo.RecordObject"/> + <see cref="EditorUtility.SetDirty"/> を伴う。
    /// </summary>
    public sealed class EditorTrackBindingWriter : ITrackBindingWriter
    {
        private const string UndoName = "Set Facial Track Binding";

        public static readonly EditorTrackBindingWriter Instance = new EditorTrackBindingWriter();

        private EditorTrackBindingWriter()
        {
        }

        public void SetGenericBinding(PlayableDirector director, TrackAsset track, UnityEngine.Object value)
        {
            if (director == null)
            {
                throw new ArgumentNullException(nameof(director));
            }

            Undo.RecordObject(director, UndoName);
            director.SetGenericBinding(track, value);
            EditorUtility.SetDirty(director);
        }
    }
}
