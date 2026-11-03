namespace Hidano.FacialControl.Adapters.Json
{
    /// <summary>
    /// FacialControl JSON スキーマの定数定義。
    /// プロファイル JSON（技術仕様書 §13.7）および
    /// 設定 JSON（技術仕様書 §13.8）のスキーマ情報を提供する。
    /// </summary>
    public static class JsonSchemaDefinition
    {
        /// <summary>
        /// 現在サポートする Profile JSON スキーマバージョン（"1.0"）。
        /// </summary>
        public const string CurrentSchemaVersion = "1.0";

        /// <summary>
        /// 現在サポートする設定 JSON スキーマバージョン（Profile とは別系統）。
        /// </summary>
        public const string CurrentConfigSchemaVersion = "1.0";

        /// <summary>
        /// プロファイル JSON スキーマ定義（技術仕様書 §13.7）
        /// </summary>
        public static class Profile
        {
            // --- ルートフィールド ---

            /// <summary>schemaVersion フィールド名</summary>
            public const string SchemaVersion = "schemaVersion";

            /// <summary>layers フィールド名</summary>
            public const string Layers = "layers";

            /// <summary>expressions フィールド名</summary>
            public const string Expressions = "expressions";

            /// <summary>slots フィールド名（profile root、overlay slot 識別子の宣言）</summary>
            public const string Slots = "slots";

            /// <summary>rendererPaths フィールド名</summary>
            public const string RendererPaths = "rendererPaths";

            /// <summary>defaultOverlays フィールド名（profile root、Overlay 機能の slot 別 fallback 宣言）</summary>
            public const string DefaultOverlays = "defaultOverlays";

            /// <summary>
            /// bonePoses フィールド名。
            /// 後方互換のため optional フィールド：欠落 / null / 空配列はすべて空 BonePoses 扱い。
            /// </summary>
            public const string BonePoses = "bonePoses";

            /// <summary>
            /// BonePose のフィールド名。
            /// </summary>
            public static class BonePose
            {
                /// <summary>プロファイル内識別子（参照キー未使用、空文字許容）</summary>
                public const string Id = "id";

                /// <summary>姿勢オーバーライドエントリの配列</summary>
                public const string Entries = "entries";
            }

            /// <summary>
            /// BonePoseEntry のフィールド名。
            /// </summary>
            public static class BonePoseEntry
            {
                /// <summary>対象ボーン名（多バイト文字を含む任意の文字列）</summary>
                public const string BoneName = "boneName";

                /// <summary>X/Y/Z 軸オイラー角（度、Z-X-Y Tait-Bryan 順-4.4）</summary>
                public const string EulerXYZ = "eulerXYZ";
            }

            /// <summary>
            /// レイヤー定義のフィールド名
            /// </summary>
            public static class Layer
            {
                /// <summary>レイヤー名</summary>
                public const string Name = "name";

                /// <summary>優先度（0 以上の整数）</summary>
                public const string Priority = "priority";

                /// <summary>排他モード（"lastWins" | "blend"）</summary>
                public const string ExclusionMode = "exclusionMode";

                /// <summary>
                /// 入力源ウェイトエントリの配列（必須フィールド）。
                /// </summary>
                public const string InputSources = "inputSources";
            }

            /// <summary>
            /// <c>layers[].inputSources[]</c> の 1 エントリのフィールド名。
            /// </summary>
            public static class InputSource
            {
                /// <summary>入力源 ID（予約 ID または <c>x-</c> プレフィックス）</summary>
                public const string Id = "id";

                /// <summary>ソースウェイト（0〜1、省略時 1.0）</summary>
                public const string Weight = "weight";

                /// <summary>アダプタ固有 options（任意）</summary>
                public const string Options = "options";
            }

            /// <summary>
            /// Expression のフィールド名
            /// </summary>
            public static class Expression
            {
                /// <summary>ID（GUID 文字列）</summary>
                public const string Id = "id";

                /// <summary>表情名</summary>
                public const string Name = "name";

                /// <summary>所属レイヤー名</summary>
                public const string Layer = "layer";

                /// <summary>遷移時間（0〜1 秒、デフォルト 1/15 ≒ 0.0667）</summary>
                public const string TransitionDuration = "transitionDuration";

                /// <summary>遷移カーブ設定</summary>
                public const string TransitionCurve = "transitionCurve";

                /// <summary>BlendShape 値の配列</summary>
                public const string BlendShapeValues = "blendShapeValues";

                /// <summary>
                /// LayerOverrideMask の永続化形式（layer 名配列）。
                /// 本フィールドで他レイヤーへのオーバーライド対象を表現する。
                /// </summary>
                public const string LayerOverrideMask = "layerOverrideMask";

                /// <summary>
                /// snapshot 内の overlays フィールド名（slot 別 overlay binding）。
                /// </summary>
                public const string Overlays = "overlays";
            }

            /// <summary>
            /// OverlaySlotBinding (overlays / defaultOverlays の各エントリ) のフィールド名。
            /// </summary>
            public static class OverlaySlot
            {
                /// <summary>slot 識別子</summary>
                public const string Slot = "slot";

                /// <summary>当該 slot の overlay を明示的に抑制するかどうか</summary>
                public const string Suppress = "suppress";

                /// <summary>個別 override として適用する ExpressionSnapshot</summary>
                public const string Snapshot = "snapshot";
            }

            /// <summary>
            /// 遷移カーブのフィールド名
            /// </summary>
            public static class TransitionCurve
            {
                /// <summary>カーブ種別（"linear" | "easeIn" | "easeOut" | "easeInOut" | "custom"）</summary>
                public const string Type = "type";

                /// <summary>カスタムカーブ用キーフレーム配列</summary>
                public const string Keys = "keys";
            }

            /// <summary>
            /// カーブキーフレームのフィールド名
            /// </summary>
            public static class CurveKeyFrame
            {
                /// <summary>時間（0〜1）</summary>
                public const string Time = "time";

                /// <summary>値</summary>
                public const string Value = "value";

                /// <summary>入力タンジェント</summary>
                public const string InTangent = "inTangent";

                /// <summary>出力タンジェント</summary>
                public const string OutTangent = "outTangent";

                /// <summary>入力ウェイト</summary>
                public const string InWeight = "inWeight";

                /// <summary>出力ウェイト</summary>
                public const string OutWeight = "outWeight";

                /// <summary>ウェイトモード</summary>
                public const string WeightedMode = "weightedMode";
            }

            /// <summary>
            /// BlendShapeMapping のフィールド名
            /// </summary>
            public static class BlendShapeMapping
            {
                /// <summary>BlendShape 名</summary>
                public const string Name = "name";

                /// <summary>値（0〜1）</summary>
                public const string Value = "value";

                /// <summary>対象 Renderer 名（省略可）</summary>
                public const string Renderer = "renderer";
            }

            /// <summary>
            /// ExclusionMode の JSON 値
            /// </summary>
            public static class ExclusionModeValues
            {
                public const string LastWins = "lastWins";
                public const string Blend = "blend";
            }

            /// <summary>
            /// TransitionCurveType の JSON 値
            /// </summary>
            public static class TransitionCurveTypeValues
            {
                public const string Linear = "linear";
                public const string EaseIn = "easeIn";
                public const string EaseOut = "easeOut";
                public const string EaseInOut = "easeInOut";
                public const string Custom = "custom";
            }
        }

        /// <summary>
        /// 設定 JSON スキーマ定義（技術仕様書 §13.8）
        /// </summary>
        public static class Config
        {
            // --- ルートフィールド ---

            /// <summary>schemaVersion フィールド名</summary>
            public const string SchemaVersion = "schemaVersion";

            /// <summary>osc フィールド名</summary>
            public const string Osc = "osc";

            /// <summary>cache フィールド名</summary>
            public const string Cache = "cache";

            /// <summary>
            /// OSC 設定のフィールド名
            /// </summary>
            public static class OscConfig
            {
                /// <summary>送信ポート（デフォルト 9000）</summary>
                public const string SendPort = "sendPort";

                /// <summary>受信ポート（デフォルト 9001）</summary>
                public const string ReceivePort = "receivePort";

                /// <summary>プリセット名（デフォルト "vrchat"）</summary>
                public const string Preset = "preset";

                /// <summary>マッピング配列</summary>
                public const string Mapping = "mapping";
            }

            /// <summary>
            /// OSC マッピングのフィールド名
            /// </summary>
            public static class OscMapping
            {
                /// <summary>OSC アドレス</summary>
                public const string OscAddress = "oscAddress";

                /// <summary>BlendShape 名</summary>
                public const string BlendShapeName = "blendShapeName";

                /// <summary>レイヤー名</summary>
                public const string Layer = "layer";
            }

            /// <summary>
            /// キャッシュ設定のフィールド名
            /// </summary>
            public static class CacheConfig
            {
                /// <summary>AnimationClip LRU キャッシュサイズ（デフォルト 16）</summary>
                public const string AnimationClipLruSize = "animationClipLruSize";
            }
        }

        /// <summary>
        /// 技術仕様書 §13.7 のサンプルプロファイル JSON（schema v1.0 / snapshot 形式）
        /// </summary>
        public const string SampleProfileJson = @"{
    ""schemaVersion"": ""1.0"",
    ""slots"": [""blink""],
    ""rendererPaths"": [""Armature/Body"", ""Face""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 0.5},
            {""id"": ""osc"", ""weight"": 0.5, ""options"": {""stalenessSeconds"": 1.0}}
        ]},
        {""name"": ""lipsync"", ""priority"": 1, ""exclusionMode"": ""blend"", ""inputSources"": [
            {""id"": ""lipsync"", ""weight"": 1.0}
        ]},
        {""name"": ""eye"", ""priority"": 2, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 1.0}
        ]}
    ],
    ""expressions"": [
        {
            ""id"": ""smile"",
            ""name"": ""笑顔"",
            ""layer"": ""emotion"",
            ""layerOverrideMask"": [""lipsync""],
            ""snapshot"": {
                ""transitionDuration"": 0.0667,
                ""transitionCurvePreset"": ""EaseInOut"",
                ""blendShapes"": [
                    {""rendererPath"": """", ""name"": ""Fcl_ALL_Joy"", ""value"": 1.0},
                    {""rendererPath"": """", ""name"": ""Fcl_EYE_Joy"", ""value"": 0.8},
                    {""rendererPath"": ""Face"", ""name"": ""Fcl_EYE_Joy_R"", ""value"": 0.6}
                ],
                ""bones"": [],
                ""rendererPaths"": [""Armature/Body"", ""Face""],
                ""overlays"": [
                    {""slot"": ""blink"", ""suppress"": false, ""snapshot"": {
                        ""transitionDuration"": 0.08,
                        ""transitionCurvePreset"": ""Linear"",
                        ""blendShapes"": [
                            {""rendererPath"": ""Face"", ""name"": ""Fcl_EYE_Close_L"", ""value"": 1.0}
                        ],
                        ""bones"": [],
                        ""rendererPaths"": [""Face""],
                        ""overlays"": []
                    }}
                ]
            }
        },
        {
            ""id"": ""smile_closed_eye"",
            ""name"": ""笑顔（閉眼）"",
            ""layer"": ""emotion"",
            ""layerOverrideMask"": [],
            ""snapshot"": {
                ""transitionDuration"": 0.15,
                ""transitionCurvePreset"": ""Linear"",
                ""blendShapes"": [
                    {""rendererPath"": """", ""name"": ""Fcl_ALL_Joy"", ""value"": 1.0},
                    {""rendererPath"": """", ""name"": ""Fcl_EYE_Close"", ""value"": 1.0}
                ],
                ""bones"": [],
                ""rendererPaths"": [""Armature/Body""],
                ""overlays"": [
                    {""slot"": ""blink"", ""suppress"": true, ""snapshot"": null}
                ]
            }
        }
    ],
    ""defaultOverlays"": [
        {""slot"": ""blink"", ""suppress"": false, ""snapshot"": null}
    ]
}";

        /// <summary>
        /// 技術仕様書 §13.8 のサンプル設定 JSON
        /// </summary>
        public const string SampleConfigJson = @"{
    ""schemaVersion"": ""1.0"",
    ""osc"": {
        ""sendPort"": 9000,
        ""receivePort"": 9001,
        ""preset"": ""vrchat"",
        ""mapping"": [
            {""oscAddress"": ""/avatar/parameters/Fcl_ALL_Joy"", ""blendShapeName"": ""Fcl_ALL_Joy"", ""layer"": ""emotion""},
            {""oscAddress"": ""/avatar/parameters/Fcl_MTH_A"", ""blendShapeName"": ""Fcl_MTH_A"", ""layer"": ""lipsync""}
        ]
    },
    ""cache"": {
        ""animationClipLruSize"": 16
    }
}";
    }
}
