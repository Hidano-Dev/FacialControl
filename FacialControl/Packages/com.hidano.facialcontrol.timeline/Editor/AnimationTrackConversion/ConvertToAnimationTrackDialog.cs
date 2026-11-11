using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// 「AnimationTrack へ変換」の実行前に、Clip の分け方とフレームレートを選ぶダイアログ（HID-422）。
    /// 開いたときの PlayableDirector と選択トラックを覚え、Convert で <see cref="FacialAnimationTrackConverter"/> を呼ぶ。
    /// </summary>
    internal sealed class ConvertToAnimationTrackDialog : EditorWindow
    {
        private const string LogPrefix = "[ConvertToAnimationTrackDialog] ";

        private PlayableDirector _director;
        private PlayableAsset _timeline;
        private List<TrackAsset> _tracks = new List<TrackAsset>();
        private bool _built;

        [SerializeField]
        private bool _initialized;

        /// <summary>オプションの欄（テスト用にも公開）。</summary>
        internal FacialAnimationClipExportOptionsFields OptionsFields { get; private set; }

        public static void Open(PlayableDirector director, IEnumerable<TrackAsset> tracks)
        {
            var dialog = CreateInstance<ConvertToAnimationTrackDialog>();
            dialog.titleContent = new GUIContent("Convert To AnimationTrack");
            dialog.Initialize(director, tracks);
            dialog.minSize = new Vector2(360f, 120f);
            dialog.maxSize = new Vector2(600f, 120f);
            dialog.ShowUtility();
        }

        /// <summary>変換対象を覚えて UI を組む（ウィンドウを表示せずにテストから呼べる）。</summary>
        internal void Initialize(PlayableDirector director, IEnumerable<TrackAsset> tracks)
        {
            _director = director;
            _timeline = director != null ? director.playableAsset : null;
            _tracks = tracks != null ? new List<TrackAsset>(tracks) : new List<TrackAsset>();
            _initialized = true;
            EnsureBuilt();
        }

        /// <summary>
        /// 選んだオプションで変換する（結果が空なら変換しなかった。理由は Console）。ダイアログは非モーダルなので、開いた後に
        /// PlayableDirector・その TimelineAsset・選んだトラックが変わっていれば、選んでいないものを変換しないよう何もしない。
        /// </summary>
        internal List<AnimationTrackConversionResult> Convert()
        {
            EnsureBuilt();
            var empty = new List<AnimationTrackConversionResult>();
            if (_director == null || _timeline == null)
            {
                Debug.LogWarning(LogPrefix + "変換対象の PlayableDirector が無くなったため変換しません。トラックを選び直して実行してください。");
                return empty;
            }

            if (_director.playableAsset != _timeline)
            {
                Debug.LogWarning(LogPrefix + $"'{_director.name}' の Timeline がダイアログを開いた後に変わったため変換しません。トラックを選び直して実行してください。", _director);
                return empty;
            }

            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i] == null)
                {
                    Debug.LogWarning(LogPrefix + "選んだトラックがダイアログを開いた後に削除されたため変換しません。トラックを選び直して実行してください。", _director);
                    return empty;
                }
            }

            return FacialAnimationTrackConverter.Convert(_director, _tracks, OptionsFields.Options);
        }

        private void CreateGUI()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            // ドメインリロードで変換対象（シリアライズしない）を失ったダイアログは使えないので閉じる
            // （_initialized だけはシリアライズされて残る。開いた直後の OnEnable は Initialize より前なので false）。
            if (_initialized && _director == null)
            {
                EditorApplication.delayCall += Close;
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            OptionsFields = new FacialAnimationClipExportOptionsFields();
            root.Add(OptionsFields);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 8f;
            root.Add(buttons);
            buttons.Add(new Button(Close) { text = "Cancel" });
            var convertButton = new Button(OnConvertClicked) { text = "Convert" };
            convertButton.style.marginLeft = 6f;
            buttons.Add(convertButton);
        }

        private void OnConvertClicked()
        {
            List<AnimationTrackConversionResult> results = Convert();
            if (results.Count > 0)
            {
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            }

            Close();
        }
    }
}
