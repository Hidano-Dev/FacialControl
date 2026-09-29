using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// Expression 行に表示するサムネイル。画像か、状態を表すプレースホルダ文言のどちらかを表示する。
    /// 画像表示中にクリックすると拡大表示を開く（<see cref="EnlargeRequested"/>）。
    /// </summary>
    public sealed class ExpressionThumbnailView : VisualElement
    {
        public const string ElementName = "expression-row-thumbnail";
        public const string ImageName = "expression-row-thumbnail-image";
        public const string StatusLabelName = "expression-row-thumbnail-status";

        /// <summary>Inspector 上の表示サイズ（px）。</summary>
        public const float DisplaySize = 128f;

        public const string NoReferenceModelMessage = "参照モデル未設定";
        public const string NoClipMessage = "AnimationClip 未設定";
        public const string PendingMessage = "生成中…";
        public const string FailedMessage = "生成できません";

        private const string EnlargeTooltip = "クリックで拡大";

        private readonly Image _image;
        private readonly Label _statusLabel;

        public ExpressionThumbnailView()
        {
            name = ElementName;
            style.width = DisplaySize;
            style.height = DisplaySize;
            style.flexShrink = 0f;
            style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f));
            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            var borderColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
            style.borderTopColor = borderColor;
            style.borderBottomColor = borderColor;
            style.borderLeftColor = borderColor;
            style.borderRightColor = borderColor;
            style.justifyContent = Justify.Center;
            style.alignItems = Align.Center;

            _image = new Image
            {
                name = ImageName,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            _image.style.position = Position.Absolute;
            _image.style.left = 0f;
            _image.style.top = 0f;
            _image.style.right = 0f;
            _image.style.bottom = 0f;
            _image.style.display = DisplayStyle.None;
            Add(_image);

            _statusLabel = new Label
            {
                name = StatusLabelName,
                pickingMode = PickingMode.Ignore,
            };
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _statusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            Add(_statusLabel);

            RegisterCallback<ClickEvent>(OnClick);
        }

        /// <summary>現在表示中のサムネイルのキャッシュキー。画像を表示していなければ null。</summary>
        public string CacheKey { get; private set; }

        /// <summary>現在表示中の画像。表示していなければ null。</summary>
        public Texture2D Texture => _image.image as Texture2D;

        /// <summary>プレースホルダ表示中の文言。画像表示中は null。</summary>
        public string StatusText => _statusLabel.style.display == DisplayStyle.None ? null : _statusLabel.text;

        /// <summary>画像表示中にクリックされたとき、表示中のキャッシュキーとともに呼ばれる。</summary>
        public event Action<ExpressionThumbnailView, string> EnlargeRequested;

        public void ShowTexture(Texture2D texture, string cacheKey)
        {
            if (texture == null)
            {
                ShowStatus(FailedMessage);
                return;
            }

            CacheKey = cacheKey;
            _image.image = texture;
            _image.style.display = DisplayStyle.Flex;
            _statusLabel.style.display = DisplayStyle.None;
            tooltip = EnlargeTooltip;
        }

        public void ShowStatus(string message)
        {
            CacheKey = null;
            _image.image = null;
            _image.style.display = DisplayStyle.None;
            _statusLabel.text = message ?? string.Empty;
            _statusLabel.style.display = DisplayStyle.Flex;
            tooltip = string.Empty;
        }

        private void OnClick(ClickEvent evt)
        {
            if (Texture == null || CacheKey == null) return;
            EnlargeRequested?.Invoke(this, CacheKey);
        }
    }
}
