# OSC Receiver の設定

`OscReceiverAdapterBinding` の設定は 2 か所に分かれる。

| 置き場所 | 項目 | 理由 |
|---|---|---|
| binding（Profile 内） | `port` / `targetLayer` | 受信ポートは一番よく変える項目。対象レイヤーはキャラクター固有 |
| `OscReceiverRuntimeSettingsSO`（上級設定 sub-asset、割り当て任意） | `stalenessSeconds` / `failSafeMode` / `bundleMode` / `bundleAccumulationTimeoutMs` | 滅多に変えない。未割り当てなら既定値で動く |

受信は常に全インターフェース（`0.0.0.0` 相当、IPv6 dual-mode）で行うため、受信 IP の設定は無い。受信を止めたいときは binding を外す。

BlendShape と Gaze の割り当ては、送信側から受け取る値フレームの対応表から自動で作る（手で入力する mapping は無い）。値フレーム以外の名前つきアドレス（VRChat / ARKit 形式）は受けない。

## binding の受信ポート

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `port` | int | `9001` | 使用中なら空きポートへ最大 10 回繰り上げ、警告で実ポートを通知 |

## OscReceiverRuntimeSettingsSO（上級設定）

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `stalenessSeconds` | float | `0` | 受信途絶とみなす秒数。0 で無効 |
| `failSafeMode` | `RevertToBase` / `HoldLastValue` | `RevertToBase` | 途絶時にベース表情へ戻すか、最後の値を保持するか |
| `bundleMode` | `AtomicSwap` / `IndividualMessage` | `AtomicSwap` | bundle を 1 フレームで一括反映するか、受信順に個別反映するか |
| `bundleAccumulationTimeoutMs` | float | `5` | 同一 bundle として蓄積する待ち時間（ミリ秒） |

## 登録される入力源 id

| 入力 | id |
|---|---|
| BlendShape | `<slug>`（起動時に登録。対応表を適用するまでは何も書き込まない） |
| 対応表の gaze チャネル | `<slug>:<channelId>`（左右共通。対応表を適用したときに登録） |

## OscReceiverOptionsDto（参考用 JSON）

設定内容を JSON で記述・共有するための DTO（`Samples~/OscReceiverDemo/OscReceiverOptions.json`）。ランタイムの設定経路は上記の binding と SO であり、この DTO は直接読み込まれない。旧形式の `listenEndpoint`・`mappings` キーは廃止した（残っていても無視される）。

| フィールド | 既定値 |
|---|---|
| `listenPort` | `9001` |
| `stalenessSeconds` | `0.0` |
| `failSafeMode` | `"revertToBase"` / `"holdLastValue"` |
| `bundleMode` | `"atomicSwap"` / `"individualMessage"` |
| `bundleAccumulationTimeoutMs` | `5.0` |

```json
{
  "listenPort": 9001,
  "stalenessSeconds": 0.25,
  "failSafeMode": "revertToBase",
  "bundleMode": "atomicSwap",
  "bundleAccumulationTimeoutMs": 5.0
}
```
