# OSC Sender の設定

`OscSenderAdapterBinding` の設定は 2 か所に分かれる。

| 置き場所 | 項目 | 理由 |
|---|---|---|
| `OscRuntimeSettingsSO`（Runtime Settings sub-asset） | `senderEnabled` / `endpoints[]` / `heartbeatIntervalSeconds` / `suppressLoopback` | 送信先は配信環境ごとに変わる |
| binding（Profile 内） | `blendShapeNames`（任意フィルタ） / `sendPreset` | キャラクター固有 |

Gaze の送信対象は Profile の目線タブに宣言されたチャネルが自動注入されるため、設定項目はない。

## OscRuntimeSettingsSO の Sender セクション

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `senderEnabled` | bool | `true` | false なら Sender binding は警告を出して起動しない |
| `endpoints[]` | `{ endpoint, port, enabled, preset }` | `[]` | 送信先。有効な endpoint が 0 件なら起動しない。重複 endpoint は 1 つにまとめる |
| `endpoints[].preset` | `VRChat` / `ARKit` / `Custom` | `VRChat` | アドレスプリセット。`Custom` は BlendShape アドレスを生成できないため送信されない |
| `heartbeatIntervalSeconds` | float | `5.0` | heartbeat 周期。実行時に 0.5〜60 秒にクランプ |
| `suppressLoopback` | bool | `true` | 同じ Profile 内の OSC Receiver と同じ endpoint への送信を抑止 |

`ToJson()` / `FromJson()` は Receiver セクションと合わせて 1 つの JSON（`schemaVersion`, `label`, `receiverEnabled`, `listenEndpoint`, …, `senderEnabled`, `endpoints`, `heartbeatIntervalSeconds`, `suppressLoopback`）として読み書きする。

## OscSenderOptionsDto（参考用 JSON）

`Samples~/OscOutputDemo/OscSenderOptions.json` のように、設定内容を JSON で記述・共有するための DTO。ランタイムの設定経路は上記 SO であり、この DTO は直接読み込まれない。

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `endpoints` | `{ ip, port, preset, enabled }[]` | `[{ "ip": "127.0.0.1", "port": 9000, "preset": "vrchat", "enabled": true }]` | `preset` は `"vrchat"` / `"arkit"` |
| `blendShapeMapping` | string[] | `[]` | 送信する BlendShape 名。空なら全 BlendShape |
| `sendPreset` | bool | `true` | `/_facialcontrol/preset` を heartbeat に同梱する |
| `suppressLoopback` | bool | `true` | loopback 抑制 |
| `heartbeatIntervalSeconds` | float | `5.0` | 0 以下 / NaN は 5.0 に補完 |

## 送信されるアドレス

| プリセット | BlendShape | Gaze |
|---|---|---|
| `vrchat` | `/avatar/parameters/{name}` | `/avatar/parameters/{channelId}X` / `...Y` |
| `arkit` | `/ARKit/{name}` | `/ARKit/eyeLookInLeft` `eyeLookOutLeft` `eyeLookUpLeft` `eyeLookDownLeft` `eyeLookInRight` `eyeLookOutRight` `eyeLookUpRight` `eyeLookDownRight` |

毎フレームの bundle 先頭に `/_facialcontrol/sender_id`、起動時と周期ごとに `/_facialcontrol/blendshape_names`（MTU を超える場合は複数メッセージに分割）、`sendPreset` が ON なら `/_facialcontrol/preset`、Gaze を送る endpoint には `/_facialcontrol/gaze` 広告を同梱する。

## サンプル JSON

```json
{
  "endpoints": [
    { "ip": "127.0.0.1", "port": 9000, "preset": "vrchat", "enabled": true },
    { "ip": "192.168.0.42", "port": 9012, "preset": "arkit", "enabled": true }
  ],
  "blendShapeMapping": ["Joy", "Blink_L", "Blink_R"],
  "sendPreset": true,
  "suppressLoopback": true,
  "heartbeatIntervalSeconds": 5.0
}
```
