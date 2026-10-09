# OSC Sender の設定

`OscSenderAdapterBinding` の設定は 2 か所に分かれる。

| 置き場所 | 項目 | 理由 |
|---|---|---|
| binding（Profile 内） | `endpoints[]` / `blendShapeNames`（任意フィルタ） | 送信先は一番よく変える項目。フィルタはキャラクター固有 |
| `OscSenderRuntimeSettingsSO`（上級設定 sub-asset、割り当て任意） | `layoutRefreshIntervalSeconds` / `suppressLoopback` | 滅多に変えない。未割り当てなら既定値で動く |

送信を止めたいときは binding を外すか、送信先ごとの `enabled` を false にする。

Gaze の送信対象は Profile の目線タブに宣言されたチャネルが自動注入されるため、設定項目はない。

## binding の送信先

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `endpoints[]` | `{ endpoint, port, enabled }` | `[{ 127.0.0.1, 9000, true }]` | 送信先。複数指定すると全宛先へ同じ内容を送る。有効な endpoint が 0 件なら起動しない。重複 endpoint は 1 つにまとめる |

送信する BlendShape と gaze チャネルが 1 つも無い場合は、警告を出して起動しない。

## OscSenderRuntimeSettingsSO（上級設定）

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `layoutRefreshIntervalSeconds` | float | `5.0` | 対応表の更新間隔。目線タブの目ボーン path・可動範囲の変更を確かめる周期で、変わっていれば対応表を別のバージョンで作り直す。実行時に 0.5〜60 秒にクランプ |
| `suppressLoopback` | bool | `true` | 同じ Profile 内の OSC Receiver と同じポートへの送信のうち、宛先が loopback か自機のインターフェースアドレス（LAN IP 等）のものを抑止 |

`ToJson()` / `FromJson()` は `schemaVersion` / `label` / `layoutRefreshIntervalSeconds` / `suppressLoopback` を読み書きする。旧キー `heartbeatIntervalSeconds` も読める（新キーがあれば新キーを優先。書き出しは新キーのみ）。旧フィールド名で保存されたアセットもそのまま読める。

## OscSenderOptionsDto（参考用 JSON）

`Samples~/OscOutputDemo/OscSenderOptions.json` のように、設定内容を JSON で記述・共有するための DTO。ランタイムの設定経路は上記の binding と SO であり、この DTO は直接読み込まれない。

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `endpoints` | `{ ip, port, enabled }[]` | `[{ "ip": "127.0.0.1", "port": 9000, "enabled": true }]` | 送信先 |
| `blendShapeMapping` | string[] | `[]` | 送信する BlendShape 名。空なら全 BlendShape |
| `suppressLoopback` | bool | `true` | loopback 抑制 |
| `layoutRefreshIntervalSeconds` | float | `5.0` | 0 以下 / NaN は 5.0 に補完。旧キー `heartbeatIntervalSeconds` も読める |

旧形式の `preset`（送信先ごと）・`sendPreset` キーが残っていても無視される。

## 送信される内容

毎フレーム、同じ timestamp の bundle で次の 2 つだけを送る（MTU を超える場合は同じ timestamp の複数 bundle に分割）。

| アドレス | 内容 |
|---|---|
| `/_facialcontrol/sender_id` | 送信元識別（UUID + 起動時刻） |
| `/_facialcontrol/values` | `[対応表のバージョン, offset, 値...]`。slot は BlendShape（対応表の順）→ gaze チャネルごとの X / Y |

対応表（BlendShape 名・gaze チャネルと目ボーン path・可動範囲の属性）は、受信側の要求（`/_facialcontrol/layout_request`）に応じて要求元へ `/_facialcontrol/layout` で返す。BlendShape 名に関係なく（2 バイト文字・独自名を含む）送れる。送信先によってアドレスや送る BlendShape が変わることはない。

## サンプル JSON

```json
{
  "endpoints": [
    { "ip": "127.0.0.1", "port": 9000, "enabled": true },
    { "ip": "192.168.0.42", "port": 9012, "enabled": true }
  ],
  "blendShapeMapping": ["Joy", "Blink_L", "Blink_R"],
  "suppressLoopback": true,
  "layoutRefreshIntervalSeconds": 5.0
}
```
