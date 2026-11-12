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

        // _frameRateField.choices と同じ並びのフレームレート（0 は Timeline に従う）。表示文字列から読み直すと丸めで値が変わるため値を持つ。
        private readonly List<double> _frameRateValues = new List<double>();

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
            _frameRateValues.Add(0d);
            for (int i = 0; i < PresetFrameRates.Length; i++)
            {
                frameRateChoices.Add(FormatFrameRate(PresetFrameRates[i]));
                _frameRateValues.Add(PresetFrameRates[i]);
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
                int index = _frameRateField.index;
                double frameRate = index >= 0 && index < _frameRateValues.Count ? _frameRateValues[index] : 0d;
                return new FacialAnimationClipExportOptions(splitMode, frameRate);
            }
        }

        /// <summary>欄の値を変える（EditorPrefs には覚えない）。一覧に無いフレームレートは選択肢に足す。</summary>
        public void SetOptions(FacialAnimationClipExportOptions options)
        {
            _splitField.SetValueWithoutNotify(
                options.SplitMode == FacialAnimationClipSplitMode.BlendShape ? SplitByBlendShapeChoice : SplitByLayerChoice);

            double frameRate = options.HasFrameRate ? options.FrameRate : 0d;
            int index = _frameRateValues.IndexOf(frameRate);
            if (index < 0)
            {
                // 一覧に無い値は選択肢に足す。表示が既存の選択肢と同じになる値（丸めで重なる）でも区別できるよう値を付ける。
                string label = FormatFrameRate(frameRate);
                List<string> choices = _frameRateField.choices;
                while (choices.Contains(label))
                {
                    label += "*";
                }

                choices.Add(label);
                _frameRateField.choices = choices;
                _frameRateValues.Add(frameRate);
                index = _frameRateValues.Count - 1;
            }

            _frameRateField.SetValueWithoutNotify(_frameRateField.choices[index]);
        }

        private void Save()
        {
            FacialAnimationClipExportOptions options = Options;
            EditorPrefs.SetInt(SplitModePrefsKey, (int)options.SplitMode);
            // EditorPrefs は double を持てないので文字列（往復で値が変わらない "R"）で保存する。
            EditorPrefs.SetString(FrameRatePrefsKey, (options.HasFrameRate ? options.FrameRate : 0d).ToString("R", CultureInfo.InvariantCulture));
        }

        private static FacialAnimationClipExportOptions LoadSaved()
        {
            var splitMode = (FacialAnimationClipSplitMode)EditorPrefs.GetInt(SplitModePrefsKey, (int)FacialAnimationClipSplitMode.Layer);
            if (splitMode != FacialAnimationClipSplitMode.BlendShape)
            {
                splitMode = FacialAnimationClipSplitMode.Layer;
            }

            double frameRate = double.TryParse(
                EditorPrefs.GetString(FrameRatePrefsKey, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 0d;
            return new FacialAnimationClipExportOptions(splitMode, frameRate);
        }

        private static string FormatFrameRate(double frameRate)
        {
            return frameRate.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
