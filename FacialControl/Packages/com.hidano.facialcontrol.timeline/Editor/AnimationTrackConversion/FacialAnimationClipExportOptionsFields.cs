using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// AnimationClip 書き出しのオプション（Clip の分け方・フレームレート。HID-422）を選ぶ欄。
    /// REC → AnimationClip ウィンドウと、AnimationTrack 変換のダイアログが共有する。
    /// </summary>
    /// <remarks>
    /// UI で選び直した値は EditorPrefs に覚え、次に開いたときの初期値にする（このマシンの利用者だけの設定）。
    /// <see cref="SetOptions"/> で入れた値は覚えない。
    /// </remarks>
    internal sealed class FacialAnimationClipExportOptionsFields : VisualElement
    {
        internal const string SplitModePrefsKey = "Hidano.FacialControl.Timeline.ClipExport.SplitMode";
        internal const string FrameRatePrefsKey = "Hidano.FacialControl.Timeline.ClipExport.FrameRate";

        internal const string SplitByLayerChoice = "Layer";
        internal const string SplitByBlendShapeChoice = "BlendShape";
        internal const string TimelineFrameRateChoice = "Timeline";

        private static readonly double[] PresetFrameRates = { 60d, 30d, 24d, 15d };

        private readonly DropdownField _splitField;
        private readonly DropdownField _frameRateField;

        public FacialAnimationClipExportOptionsFields()
        {
            _splitField = new DropdownField(
                "Split Clips By",
                new List<string> { SplitByLayerChoice, SplitByBlendShapeChoice },
                0)
            {
                tooltip = "Layer: BlendShape の担当レイヤーごとに 1 本（+ Base / Gaze）。\n" +
                    "BlendShape: BlendShape 1 つにつき 1 本（+ Gaze）。1 本あたりの .anim が小さくなる（トラック数は増える）",
            };
            _splitField.RegisterValueChangedCallback(_ => Save());
            Add(_splitField);

            var frameRateChoices = new List<string> { TimelineFrameRateChoice };
            for (int i = 0; i < PresetFrameRates.Length; i++)
            {
                frameRateChoices.Add(FormatFrameRate(PresetFrameRates[i]));
            }

            _frameRateField = new DropdownField("Frame Rate", frameRateChoices, 0)
            {
                tooltip = "評価して Clip に書き出すフレームレート。Timeline: Timeline のフレームレート（無ければ 60fps）。\n" +
                    "下げるとキー数（.anim のサイズ）が減る。Clip 境界・状態の切り替わりなど値が跳ぶ時刻は常に評価する",
            };
            _frameRateField.RegisterValueChangedCallback(_ => Save());
            Add(_frameRateField);

            SetOptions(LoadSaved());
        }

        /// <summary>選んでいるオプション。</summary>
        public FacialAnimationClipExportOptions Options
        {
            get
            {
                FacialAnimationClipSplitMode splitMode = _splitField.value == SplitByBlendShapeChoice
                    ? FacialAnimationClipSplitMode.BlendShape
                    : FacialAnimationClipSplitMode.Layer;
                double frameRate = double.TryParse(_frameRateField.value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : 0d;
                return new FacialAnimationClipExportOptions(splitMode, frameRate);
            }
        }

        /// <summary>欄の値を変える（EditorPrefs には覚えない）。一覧に無いフレームレートは選択肢に足す。</summary>
        public void SetOptions(FacialAnimationClipExportOptions options)
        {
            _splitField.SetValueWithoutNotify(
                options.SplitMode == FacialAnimationClipSplitMode.BlendShape ? SplitByBlendShapeChoice : SplitByLayerChoice);

            string frameRateChoice = options.HasFrameRate ? FormatFrameRate(options.FrameRate) : TimelineFrameRateChoice;
            if (!_frameRateField.choices.Contains(frameRateChoice))
            {
                List<string> choices = _frameRateField.choices;
                choices.Add(frameRateChoice);
                _frameRateField.choices = choices;
            }

            _frameRateField.SetValueWithoutNotify(frameRateChoice);
        }

        private void Save()
        {
            FacialAnimationClipExportOptions options = Options;
            EditorPrefs.SetInt(SplitModePrefsKey, (int)options.SplitMode);
            EditorPrefs.SetFloat(FrameRatePrefsKey, options.HasFrameRate ? (float)options.FrameRate : 0f);
        }

        private static FacialAnimationClipExportOptions LoadSaved()
        {
            var splitMode = (FacialAnimationClipSplitMode)EditorPrefs.GetInt(SplitModePrefsKey, (int)FacialAnimationClipSplitMode.Layer);
            if (splitMode != FacialAnimationClipSplitMode.BlendShape)
            {
                splitMode = FacialAnimationClipSplitMode.Layer;
            }

            return new FacialAnimationClipExportOptions(splitMode, EditorPrefs.GetFloat(FrameRatePrefsKey, 0f));
        }

        private static string FormatFrameRate(double frameRate)
        {
            return frameRate.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
