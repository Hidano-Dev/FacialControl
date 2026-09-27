# Adapter Runtime Settings

`AdapterRuntimeSettingsCollectionSO` は、キャラクター Profile（`FacialCharacterProfileSO`）から **環境依存 / マシン依存の設定値** を切り離すための sub-asset コンテナ。OSC の endpoint やポート、iFacialMocap の listen ポートなどをここに置くと、同じ Profile を配信環境ごとに使い回せる。

```
AdapterRuntimeSettingsCollection.asset
  ├─ OscRuntimeSettingsSO            (com.hidano.facialcontrol.osc)
  │    ├─ Receiver: receiverEnabled / listenEndpoint / listenPort / stalenessSeconds / failSafeMode / consistencyCheckWarnLog / bundleMode / bundleAccumulationTimeoutMs
  │    └─ Sender:   senderEnabled / endpoints[] / heartbeatIntervalSeconds / suppressLoopback
  └─ IFacialMocapRuntimeSettingsSO   (com.hidano.facialcontrol.ifacialmocap)
```

## 作成と編集

1. Project ウィンドウで **Create → FacialControl → Adapter Runtime Settings Collection** を作成
2. Inspector の **Add** で sub-asset の型を選ぶ（`AdapterRuntimeSettingsBase` の派生型が型名で自動列挙される。例: `OscRuntimeSettingsSO`）
3. `_label` に識別名（例 `local-debug`）を付ける。同じ型を複数追加でき、同じ label を重複させると警告が出る
4. Profile の Adapter Bindings で該当 binding の **Runtime Settings** 欄に sub-asset を割り当てる
5. 不要になった sub-asset は **Remove**（確認ダイアログあり）。参照していた binding は Inspector に「未設定」警告を出し、`OnStart` で起動をスキップする

## receiverEnabled / senderEnabled

`OscRuntimeSettingsSO` は Receiver と Sender の両セクションを 1 つの sub-asset に持つ。`OscReceiverAdapterBinding` と `OscSenderAdapterBinding` に同じ sub-asset を割り当て、片方だけ止めたい場合は該当セクションの enabled を false にする。false のセクションを参照する binding は警告を出して起動しない。値の変更は次の Play から反映される。

## LipSync のマイクデバイス

`com.hidano.facialcontrol.lipsync` のマイク / ASIO デバイス名はアセットではなく PlayerPrefs に保存される（`LipSyncDeviceStore`）。開発者ごとに Inspector で一度選び直せばよく、git の差分にならない。

| キー | 型 | 説明 |
|---|---|---|
| `Hidano.FacialControl.LipSync.MicDevice.Name` | string | デバイス名。空なら既定マイクにフォールバック |
| `Hidano.FacialControl.LipSync.MicDevice.Disambiguator` | int | 同名デバイスの 0 始まり序数 |

## JSON との相互変換

各 sub-asset は `ToJson()` / `FromJson(string)` を持ち、camelCase のキーで全フィールドを書き出す。enum は文字列（例 `"revertToBase"`）。`_schemaVersion`（int、既定 1）は将来の移行判定用に予約されている。
