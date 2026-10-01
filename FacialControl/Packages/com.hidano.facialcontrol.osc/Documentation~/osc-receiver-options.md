# OSC Receiver の設定

`OscReceiverAdapterBinding` の設定は 2 か所に分かれる。

| 置き場所 | 項目 | 理由 |
|---|---|---|
| binding（Profile 内） | `port` / `mappings[]` | 受信ポートは一番よく変える項目。どの OSC アドレスをどの BlendShape / Gaze に流すかはキャラクター固有 |
| `OscReceiverRuntimeSettingsSO`（上級設定 sub-asset、割り当て任意） | `stalenessSeconds` / `failSafeMode` / `consistencyCheckWarnLog` / `bundleMode` / `bundleAccumulationTimeoutMs` | 滅多に変えない。未割り当てなら既定値で動く |

受信は常に全インターフェース（`0.0.0.0` 相当、IPv6 dual-mode）で行うため、受信 IP の設定は無い。受信を止めたいときは binding を外す。

FacialControl 同士の接続では heartbeat と Gaze 広告から mapping が自動生成されるため、`mappings` は空でよい。

## binding の受信ポート

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `port` | int | `9001` | 使用中なら空きポートへ最大 10 回繰り上げ、警告で実ポートを通知 |

## OscReceiverRuntimeSettingsSO（上級設定）

| フィールド | 型 | 既定値 | 説明 |
|---|---|---|---|
| `stalenessSeconds` | float | `0` | 受信途絶とみなす秒数。0 で無効 |
| `failSafeMode` | `RevertToBase` / `HoldLastValue` | `RevertToBase` | 途絶時にベース表情へ戻すか、最後の値を保持するか |
| `consistencyCheckWarnLog` | bool | `true` | heartbeat と mapping の差分を警告ログに出す |
| `bundleMode` | `AtomicSwap` / `IndividualMessage` | `AtomicSwap` | bundle を 1 フレームで一括反映するか、受信順に個別反映するか |
| `bundleAccumulationTimeoutMs` | float | `5` | 同一 bundle として蓄積する待ち時間（ミリ秒） |

## mappings[]（`OscMappingEntry`）

| フィールド | 型 | 説明 |
|---|---|---|
| `mode` | `Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` | entry の種類 |
| `expressionId` | string | BlendShape 名（Normal）または Gaze チャネル id（Gaze） |
| `addressPattern` | string | Normal: 完全な OSC アドレス。VRChat_XY: 末尾 X / Y を除いた base アドレス。ARKit_8BS: 無視（固定 8 アドレス） |
| `leftRightIndependent` | bool | Gaze を左右別の入力源として登録する |
| `sourceIdLeft` / `sourceIdRight` | string | `leftRightIndependent` のとき両方非空であること（値は id の生成には使われない） |

登録される入力源 id:

| 条件 | id |
|---|---|
| BlendShape（有効な Normal mapping が 1 件以上） | `<slug>` |
| `Gaze_VRChat_XY` かつ左右共通 | `<slug>:<channelId>` |
| `Gaze_ARKit_8BS`、または `leftRightIndependent` | `<slug>:<channelId>.left` / `<slug>:<channelId>.right` |

heartbeat / 広告で自動生成された mapping も同じ規約で登録される。手入力と同じ id は自動生成の対象外。

## OscReceiverOptionsDto（参考用 JSON）

設定内容を JSON で記述・共有するための DTO（`Samples~/OscReceiverDemo/OscReceiverOptions.json`）。ランタイムの設定経路は上記の binding と SO であり、この DTO は直接読み込まれない。旧形式の `listenEndpoint` キーは廃止した（残っていても無視される）。

| フィールド | 既定値 |
|---|---|
| `listenPort` | `9001` |
| `mappings[]` | `[]`。各 entry は `mode`（`"blendShape"` / `"gazeVrchatXy"` / `"gazeArkit8Bs"`）、`expressionId`、`addressPattern`、`sourceIdLeft`、`sourceIdRight`、`leftRightIndependent` |
| `stalenessSeconds` | `0.0` |
| `failSafeMode` | `"revertToBase"` / `"holdLastValue"` |
| `consistencyCheckWarnLog` | `true` |
| `bundleMode` | `"atomicSwap"` / `"individualMessage"` |
| `bundleAccumulationTimeoutMs` | `5.0` |

```json
{
  "listenPort": 9001,
  "mappings": [
    { "mode": "blendShape", "expressionId": "Smile", "addressPattern": "/avatar/parameters/Smile" },
    { "mode": "gazeVrchatXy", "expressionId": "gaze", "addressPattern": "/avatar/parameters/gaze" }
  ],
  "stalenessSeconds": 0.25,
  "failSafeMode": "revertToBase",
  "consistencyCheckWarnLog": true,
  "bundleMode": "atomicSwap",
  "bundleAccumulationTimeoutMs": 5.0
}
```
