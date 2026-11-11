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
        private PlayableDirector _director;
        private List<TrackAsset> _tracks = new List<TrackAsset>();
        private bool _built;

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
            _tracks = tracks != null ? new List<TrackAsset>(tracks) : new List<TrackAsset>();
            EnsureBuilt();
        }

        /// <summary>選んだオプションで変換する（結果が空なら変換しなかった。理由は Console）。</summary>
        internal List<AnimationTrackConversionResult> Convert()
        {
            EnsureBuilt();
            if (_director == null)
            {
                Debug.LogWarning("[ConvertToAnimationTrackDialog] 変換対象の PlayableDirector が無くなったため変換しません。");
                return new List<AnimationTrackConversionResult>();
            }

            return FacialAnimationTrackConverter.Convert(_director, _tracks, OptionsFields.Options);
        }

        private void CreateGUI()
        {
            EnsureBuilt();
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
