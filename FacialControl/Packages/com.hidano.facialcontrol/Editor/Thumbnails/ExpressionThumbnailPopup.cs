using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// サムネイルの拡大表示ポップアップ。
    /// </summary>
    public sealed class ExpressionThumbnailPopup : PopupWindowContent
    {
        private const float MaxDisplaySize = 384f;
        private const float Padding = 4f;

        private readonly Texture2D _texture;
        private readonly bool _ownsTexture;
        private readonly string _title;

        /// <param name="texture">表示するテクスチャ</param>
        /// <param name="ownsTexture">true ならポップアップを閉じたときに <paramref name="texture"/> を破棄する</param>
        /// <param name="title">上部に表示する見出し（Expression 名）。null / 空なら表示しない</param>
        public ExpressionThumbnailPopup(Texture2D texture, bool ownsTexture, string title)
        {
            _texture = texture;
            _ownsTexture = ownsTexture;
            _title = title;
        }

        public override Vector2 GetWindowSize()
        {
            float titleHeight = string.IsNullOrEmpty(_title) ? 0f : EditorGUIUtility.singleLineHeight;
            return new Vector2(MaxDisplaySize + Padding * 2f, MaxDisplaySize + titleHeight + Padding * 2f);
        }

        public override void OnGUI(Rect rect)
        {
            var area = new Rect(rect.x + Padding, rect.y + Padding, rect.width - Padding * 2f, rect.height - Padding * 2f);
            if (!string.IsNullOrEmpty(_title))
            {
                var titleRect = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(titleRect, _title, EditorStyles.boldLabel);
                area.yMin += EditorGUIUtility.singleLineHeight;
            }

            if (_texture != null)
            {
                GUI.DrawTexture(area, _texture, ScaleMode.ScaleToFit, false);
            }
        }

        public override void OnClose()
        {
            if (_ownsTexture && _texture != null)
            {
                Object.DestroyImmediate(_texture);
            }
        }
    }
}
