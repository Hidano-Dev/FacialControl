# Adapter Runtime Settings

`AdapterRuntimeSettingsCollectionSO` は、キャラクター Profile（`FacialCharacterProfileSO`）から **環境依存 / マシン依存の設定値** を切り離すための sub-asset コンテナ。iFacialMocap の listen ポートや OSC の上級設定などをここに置くと、同じ Profile を配信環境ごとに使い回せる。OSC の受信ポート・送信先は binding 本体に持つ。

```
AdapterRuntimeSettingsCollection.asset
  ├─ OscReceiverRuntimeSettingsSO    (com.hidano.facialcontrol.osc、割り当て任意)
  │    └─ stalenessSeconds / failSafeMode / consistencyCheckWarnLog / bundleMode / bundleAccumulationTimeoutMs
  ├─ OscSenderRuntimeSettingsSO      (com.hidano.facialcontrol.osc、割り当て任意)
  │    └─ heartbeatIntervalSeconds / suppressLoopback
  └─ IFacialMocapRuntimeSettingsSO   (com.hidano.facialcontrol.ifacialmocap)
```

## 作成と編集

1. Project ウィンドウで **Create → FacialControl → Adapter Runtime Settings Collection** を作成
2. Inspector の **Add** で sub-asset の型を選ぶ（`AdapterRuntimeSettingsBase` の派生型が型名で自動列挙される。例: `OscReceiverRuntimeSettingsSO`。`[HideInAdapterRuntimeSettingsMenu]` を付けた移行専用の旧型は出さない）
3. `_label` に識別名（例 `local-debug`）を付ける。同じ型を複数追加でき、同じ label を重複させると警告が出る
4. Profile の Adapter Bindings で該当 binding の設定欄（OSC は Foldout「上級設定」）に sub-asset を割り当てる
5. 不要になった sub-asset は **Remove**（確認ダイアログあり）。参照していた binding は Inspector に「未設定」警告を出し、`OnStart` で起動をスキップする

## OSC の上級設定

`OscReceiverRuntimeSettingsSO` / `OscSenderRuntimeSettingsSO` はそれぞれ OSC Receiver / OSC Sender binding の Foldout「上級設定」に割り当てる。割り当ては任意で、未割り当ての binding は既定値で動く。受信・送信を一時的に止めたい場合は、Adapter Bindings の Foldout ヘッダーのトグルで binding を無効にする（設定値は残る）。値の変更は次の Play から反映される。

旧 `OscRuntimeSettingsSO`（Receiver / Sender を 1 つにまとめていた型）は既存アセットの移行専用に残している。binding に割り当てたままだとその値で起動して警告を出すので、Inspector の **旧設定から移行** で binding 側へ移す（手順は `com.hidano.facialcontrol.osc` の CHANGELOG を参照）。

## LipSync のマイクデバイス

`com.hidano.facialcontrol.lipsync` のマイク / ASIO デバイス名はアセットではなく PlayerPrefs に保存される（`LipSyncDeviceStore`）。開発者ごとに Inspector で一度選び直せばよく、git の差分にならない。

| キー | 型 | 説明 |
|---|---|---|
| `Hidano.FacialControl.LipSync.MicDevice.Name` | string | デバイス名。空なら既定マイクにフォールバック |
| `Hidano.FacialControl.LipSync.MicDevice.Disambiguator` | int | 同名デバイスの 0 始まり序数 |

## JSON との相互変換

各 sub-asset は `ToJson()` / `FromJson(string)` を持ち、camelCase のキーで全フィールドを書き出す。enum は文字列（例 `"revertToBase"`）。`_schemaVersion`（int、既定 1）は将来の移行判定用に予約されている。
