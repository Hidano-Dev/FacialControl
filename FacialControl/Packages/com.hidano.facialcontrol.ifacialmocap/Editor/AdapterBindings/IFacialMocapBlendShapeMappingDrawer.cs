using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.IFacialMocap;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.IFacialMocap.Editor.AdapterBindings
{
    /// <summary>
    /// <see cref="IFacialMocapBlendShapeMapping"/> 1 件の UI Toolkit Drawer。
    /// 名前 2 欄に加え、使用する/しないのトグル・入力範囲の <see cref="MinMaxSlider"/>・Weight 欄を表示する。
    /// </summary>
    /// <remarks>
    /// 調整値の 3 欄は SerializedProperty に bind せず、<see cref="IFacialMocapBlendShapeMapping.EffectiveEnabled"/> /
    /// <see cref="IFacialMocapBlendShapeMapping.EffectiveTuning"/> 相当の実効値を表示する。旧アセット
    /// （<c>tuningVersion</c> 0）のフィールド値 0 をそのまま見せると Max 0 / Weight 0 と誤読されるため。
    /// いずれかを編集すると 4 フィールドと <c>tuningVersion</c> をまとめて書き込む。
    /// </remarks>
    [CustomPropertyDrawer(typeof(IFacialMocapBlendShapeMapping))]
    public sealed class IFacialMocapBlendShapeMappingDrawer : PropertyDrawer
    {
        public const string IFacialMocapNameFieldName = "ifacialMocapName";
        public const string BlendShapeNameFieldName = "blendShapeName";
        public const string EnabledFieldName = "enabled";
        public const string RangeMinFieldName = "rangeMin";
        public const string RangeMaxFieldName = "rangeMax";
        public const string WeightFieldName = "weight";
        public const string TuningVersionFieldName = "tuningVersion";

        public const string RootClassName = "facial-control-ifacialmocap-blendshape-mapping";
        public const string EnabledToggleName = "ifacialmocap-mapping-enabled";
        public const string RangeSliderName = "ifacialmocap-mapping-range";
        public const string WeightFieldElementName = "ifacialmocap-mapping-weight";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;

            AddBoundField(root, property, IFacialMocapNameFieldName, "iFacialMocap Name");
            AddBoundField(root, property, BlendShapeNameFieldName, "BlendShape Name");

            var enabledToggle = new Toggle("Enabled")
            {
                name = EnabledToggleName,
                tooltip = "オフのマッピングは値を出力しない（マッピング自体は残す）。",
            };
            var rangeSlider = new MinMaxSlider(
                "Range",
                IFacialMocapValueTuning.DefaultMin,
                IFacialMocapValueTuning.DefaultMax,
                0f,
                1f)
            {
                name = RangeSliderName,
                tooltip = "受信値の有効範囲（0〜1）。範囲を 0〜1 に引き伸ばし、下限以下は 0・上限以上は 1 になる。",
            };
            var weightField = new FloatField("Weight")
            {
                name = WeightFieldElementName,
                tooltip = "範囲を再マップした値に掛ける倍率。結果は 0〜1 にクランプする。",
            };

            root.Add(enabledToggle);
            root.Add(rangeSlider);
            root.Add(weightField);

            void Refresh()
            {
                SerializedProperty current = serializedObject.FindProperty(propertyPath);
                if (current == null)
                {
                    return;
                }

                ReadEffective(current, out bool enabled, out Vector2 range, out float weight);
                enabledToggle.SetValueWithoutNotify(enabled);
                rangeSlider.SetValueWithoutNotify(range);
                weightField.SetValueWithoutNotify(weight);
                rangeSlider.SetEnabled(enabled);
                weightField.SetEnabled(enabled);
            }

            void Commit()
            {
                serializedObject.Update();
                SerializedProperty current = serializedObject.FindProperty(propertyPath);
                if (current == null)
                {
                    return;
                }

                WriteTuning(current, enabledToggle.value, rangeSlider.value, weightField.value);
                serializedObject.ApplyModifiedProperties();
                Refresh();
            }

            enabledToggle.RegisterValueChangedCallback(_ => Commit());
            rangeSlider.RegisterValueChangedCallback(_ => Commit());
            weightField.RegisterValueChangedCallback(_ => Commit());

            Refresh();
            root.TrackPropertyValue(property, _ => Refresh());

            return root;
        }

        private static void AddBoundField(VisualElement root, SerializedProperty property, string relativeName, string label)
        {
            SerializedProperty child = property.FindPropertyRelative(relativeName);
            if (child == null)
            {
                root.Add(new Label($"<missing field: {relativeName}>"));
                return;
            }

            root.Add(new PropertyField(child, label));
        }

        /// <summary>
        /// mapping 要素の実効値を読む。<c>tuningVersion</c> が現行未満なら既定値（使用する / 0〜1 / 1）。
        /// </summary>
        public static void ReadEffective(SerializedProperty mapping, out bool enabled, out Vector2 range, out float weight)
        {
            var value = (IFacialMocapBlendShapeMapping)mapping.boxedValue;
            IFacialMocapValueTuning tuning = value.EffectiveTuning;
            enabled = value.EffectiveEnabled;
            range = new Vector2(tuning.Min, tuning.Max);
            weight = tuning.Weight;
        }

        /// <summary>
        /// 調整値 4 フィールドと <c>tuningVersion</c> をまとめて書き込む（ApplyModifiedProperties は呼び出し側）。
        /// Range は 0〜1・Min ≤ Max に正規化する。
        /// </summary>
        public static void WriteTuning(SerializedProperty mapping, bool enabled, Vector2 range, float weight)
        {
            var tuning = new IFacialMocapValueTuning(range.x, range.y, weight);
            mapping.FindPropertyRelative(EnabledFieldName).boolValue = enabled;
            mapping.FindPropertyRelative(RangeMinFieldName).floatValue = tuning.Min;
            mapping.FindPropertyRelative(RangeMaxFieldName).floatValue = tuning.Max;
            mapping.FindPropertyRelative(WeightFieldName).floatValue = tuning.Weight;
            mapping.FindPropertyRelative(TuningVersionFieldName).intValue =
                IFacialMocapBlendShapeMapping.CurrentTuningVersion;
        }
    }
}
