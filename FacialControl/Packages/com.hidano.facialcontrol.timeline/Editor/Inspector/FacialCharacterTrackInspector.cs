using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.Inspector
{
    /// <summary>
    /// <see cref="FacialCharacterTrack"/> の Inspector（UI Toolkit。HID-408）。Timeline ウィンドウで開いている Director の binding を表示し、
    /// Bind された FacialController の再生診断（<see cref="FacialTimelineReceiverInspector"/>）を埋め込む。
    /// </summary>
    /// <remarks>binding の変更は 500 ms ごとに確認し、対象が変わったときだけ作り直す。</remarks>
    [CustomEditor(typeof(FacialCharacterTrack))]
    public sealed class FacialCharacterTrackInspector : UnityEditor.Editor
    {
        internal const string StatusName = "facial-character-track-status";

        private const long PollIntervalMs = 500;

        private VisualElement _root;
        private VisualElement _body;
        private PlayableDirector _shownDirector;
        private FacialController _shownController;
        private bool _hasShown;

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            var note = new Label(
                "このトラックに FacialController を Bind すると、子の表情 / 値 / レイヤー weight トラックはすべてこの対象で再生されます。"
                + "Bind されていないトラックは再生されません。");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            _root.Add(note);

            _body = new VisualElement();
            _root.Add(_body);

            Refresh();
            _root.schedule.Execute(Refresh).Every(PollIntervalMs);
            return _root;
        }

        private void Refresh()
        {
            if (_root == null || !(target is FacialCharacterTrack track) || track == null)
            {
                return;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            FacialController controller = director != null ? FacialCharacterTrackBinding.ToController(director.GetGenericBinding(track)) : null;
            if (_hasShown && director == _shownDirector && controller == _shownController)
            {
                return;
            }

            _hasShown = true;
            _shownDirector = director;
            _shownController = controller;
            _body.Clear();

            if (director == null)
            {
                _body.Add(new HelpBox("Timeline ウィンドウで PlayableDirector を選択すると binding と診断を表示します。", HelpBoxMessageType.Info) { name = StatusName });
                return;
            }

            if (controller == null)
            {
                _body.Add(new HelpBox(
                    $"'{director.name}' のこのトラックに FacialController が Bind されていません。トラックの binding 欄に FacialController をセットしてください。",
                    HelpBoxMessageType.Warning) { name = StatusName });
                return;
            }

            _body.Add(new HelpBox($"対象: {controller.name}（{director.name}）", HelpBoxMessageType.None) { name = StatusName });
            FacialTimelineReceiver receiver = FacialTimelineReceiver.GetOrCreate(controller);
            if (receiver == null)
            {
                return;
            }

            // Receiver は他の Inspector・Mixer と共有するので書き換えない（Director は binding からシーン走査で解決される）。
            _body.Add(new InspectorElement(receiver));
        }
    }
}
