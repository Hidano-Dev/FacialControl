# JSON スキーマリファレンス

`FacialCharacterProfileSO` を編集すると `StreamingAssets/FacialControl/{SO 名}/profile.json` が自動生成され、ランタイムはこの JSON を読む。通常は Inspector だけで完結するが、ビルド後の差し替えや外部ツールからの生成ではこの形式に従う。

- `schemaVersion` は `"1.0"` 固定。それ以外は `NotSupportedException` で拒否される
- パーサは `JsonUtility` 互換。未知キーは無視され、欠落したセクションは既定値で補完される
- Adapter Bindings は JSON に含まれない（SO にのみ保存される）
- 旧形式のキー（`overlays[].expressionId`、`gazeConfigs` 等）は自動変換されない。`expressionId` は `FormatException`、`gazeConfigs` は警告のうえ読み捨て

## ルート

| フィールド | 型 | 説明 |
|---|---|---|
| `schemaVersion` | string | `"1.0"` |
| `layers` | Layer[] | レイヤー定義（必須） |
| `slots` | string[] | Overlay slot 名の宣言。Expression / defaultOverlays / binding が参照できる slot はここに列挙したものだけ |
| `expressions` | Expression[] | 表情定義 |
| `baseExpression` | Snapshot | どのレイヤーも寄与しない BlendShape に残る基準値。省略時は全 0 |
| `rendererPaths` | string[] | 各 snapshot が参照する rendererPath の和集合（エクスポート時に自動生成） |
| `gaze` | `{ "channels": GazeChannel[] }` | Gaze チャネル。省略時は既定チャネル `gaze` が補完される |
| `defaultOverlays` | OverlayBinding[] | Expression が slot を指定しなかったときのフォールバック |

## Layer

| フィールド | 型 | 説明 |
|---|---|---|
| `name` | string | レイヤー名（空不可） |
| `priority` | int | 0 以上。大きいほど後から合成される |
| `exclusionMode` | string | `"lastWins"`（クロスフェード）または `"blend"`（加算して 0〜1 にクランプ）。大文字小文字は区別しない |
| `inputSources` | InputSource[] | 必須。空配列は `FormatException` |

### InputSource

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | string | binding が登録する入力源 id。`<slug>` または `<slug>:<sub>`。正規表現 `^[a-zA-Z0-9_.\-:]{1,64}$` |
| `weight` | float | 既定 1.0 |
| `options` | object | 入力源固有のオプション（例: `{ "stalenessSeconds": 1.0 }`）。内部では文字列として保持される |

同じレイヤー内で `id` が重複した場合は最後の出現を採用して警告を出す。

## Expression

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | string | スクリプトや binding から参照する識別子 |
| `name` | string | 表示名 |
| `layer` | string | 所属レイヤー名 |
| `layerOverrideMask` | string[] | この表情がアクティブな間に抑制するレイヤー名 |
| `snapshot` | ExpressionSnapshot | BlendShape / ボーン値と遷移設定 |

### Snapshot（`baseExpression` / `overlays[].snapshot` / `defaultOverlays[].snapshot` 共通）

| フィールド | 型 | 説明 |
|---|---|---|
| `transitionDuration` | float | 遷移時間（秒）。0〜1 にクランプ。既定 0.0667（1/15 秒） |
| `transitionCurvePreset` | string | `"Linear"` / `"EaseIn"` / `"EaseOut"` / `"EaseInOut"`。未知の値は Linear |
| `blendShapes` | `{ rendererPath, name, value }[]` | value は正規化 0〜1（ランタイムで ×100 して適用） |
| `bones` | `{ bonePath, position, rotationEuler, scale }[]` | ボーンポーズ |
| `rendererPaths` | string[] | この snapshot が参照する rendererPath |

`ExpressionSnapshot` は上記に加えて `overlays: OverlayBinding[]` を持つ。

### OverlayBinding

| suppress | snapshot | 意味 |
|---|---|---|
| `false` | `null` | `defaultOverlays` にフォールバック |
| `true` | `null` | この slot を抑制 |
| `false` | Snapshot | この表情専用の snapshot で上書き |

`suppress: true` と非 null の `snapshot` を同時に指定すると `FormatException`。`slot` は `slots[]` に宣言済みであること。

## GazeChannel

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | string | チャネル id。先頭は常に `gaze` |
| `providerSlug` | string | 入力源を提供する binding の slug。空なら自動解決 |
| `useDistinctLeftRight` | bool | 左右別の入力源を使う |
| `sourceIdLeft` / `sourceIdRight` | string | 左右別入力源 id（`useDistinctLeftRight` 時） |
| `leftEyeBonePath` / `rightEyeBonePath` | string | 目ボーンの相対 path |
| `leftEyeInitialRotation` / `rightEyeInitialRotation` | Euler | 初期回転 |
| `leftEyeYawAxisLocal` / `leftEyePitchAxisLocal` など | Vector3 | ローカル yaw / pitch 軸 |
| `lookUpAngle` / `lookDownAngle` / `outerYawAngle` / `innerYawAngle` | float | 可動角（度）。既定 15 / 9 / 15 / 18 |

入力源 id の規約は `{slug}:{channelId}` または `{slug}:{channelId}.left` / `.right`。

## 例

```json
{
    "schemaVersion": "1.0",
    "slots": ["blink"],
    "rendererPaths": ["Face"],
    "layers": [
        {
            "name": "emotion",
            "priority": 0,
            "exclusionMode": "lastWins",
            "inputSources": [{ "id": "input-system", "weight": 1.0 }]
        },
        {
            "name": "overlay",
            "priority": 10,
            "exclusionMode": "blend",
            "inputSources": [{ "id": "overlay:blink", "weight": 1.0 }]
        }
    ],
    "expressions": [
        {
            "id": "smile",
            "name": "Smile",
            "layer": "emotion",
            "layerOverrideMask": [],
            "snapshot": {
                "transitionDuration": 0.0667,
                "transitionCurvePreset": "EaseInOut",
                "blendShapes": [{ "rendererPath": "Face", "name": "Fcl_ALL_Joy", "value": 1.0 }],
                "bones": [],
                "rendererPaths": ["Face"],
                "overlays": [{ "slot": "blink", "suppress": false, "snapshot": null }]
            }
        }
    ],
    "defaultOverlays": [
        {
            "slot": "blink",
            "suppress": false,
            "snapshot": {
                "transitionDuration": 0.08,
                "transitionCurvePreset": "Linear",
                "blendShapes": [
                    { "rendererPath": "Face", "name": "Fcl_EYE_Close_L", "value": 1.0 },
                    { "rendererPath": "Face", "name": "Fcl_EYE_Close_R", "value": 1.0 }
                ],
                "bones": [],
                "rendererPaths": ["Face"]
            }
        }
    ],
    "gaze": {
        "channels": [
            {
                "id": "gaze",
                "providerSlug": "",
                "useDistinctLeftRight": false,
                "leftEyeBonePath": "Armature/Hips/Spine/Chest/Neck/Head/LeftEye",
                "rightEyeBonePath": "Armature/Hips/Spine/Chest/Neck/Head/RightEye",
                "lookUpAngle": 15, "lookDownAngle": 9, "outerYawAngle": 15, "innerYawAngle": 18
            }
        ]
    }
}
```

雛形は `Templates/default_profile.json`、または **Tools → FacialControl → 新規プロファイル作成** で生成できる。
