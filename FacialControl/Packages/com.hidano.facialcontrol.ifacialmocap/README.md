# FacialControl iFacialMocap

`com.hidano.facialcontrol` の iFacialMocap (iOS) 受信アダプタ。iFacialMocap が UDP で配信する独自テキストプロトコル（OSC ではない）を解析し、ARKit 互換 52 BlendShape・視線・頭部ポーズを `FacialController` の入力源として登録する。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | Adapter Binding / Gaze チャネル / 入力源レジストリ |
| `com.hidano.facialcontrol.osc` | 1.0.0 | 受信値パイプライン（`OscDoubleBuffer` / `OscInputSource` / `GazeVector2InputSource` / `FailSafeMode`）の再利用。OSC 通信自体は使わない |

## 提供する binding

**iFacialMocap Receiver**（`IFacialMocapReceiverAdapterBinding`、既定 slug `ifacialmocap-receiver`）

| 設定 | 置き場所 | 内容 |
|---|---|---|
| Runtime Settings | `IFacialMocapRuntimeSettingsSO`（`AdapterRuntimeSettingsCollectionSO` の sub-asset） | listen ポート（既定 49983）、端末 IP とハンドシェイク、データ形式、staleness / fail-safe、視線の感度、頭部の有効化 |
| BlendShape Mappings | binding | iFacialMocap 名 → メッシュ BlendShape 名。空なら 52 BlendShape を ARKit 正準名（`eyeBlink_L` → `eyeBlinkLeft` 等）へ自動変換 |
| Gaze Invert Yaw / Pitch | binding | 視線の左右 / 上下反転（目ボーンの向きに依存するアバター固有設定） |

登録される入力源 id:

| id | 内容 |
|---|---|
| `<slug>` | BlendShape（メッシュ名と一致したものが 1 つ以上ある場合） |
| `<slug>:gaze.left` / `<slug>:gaze.right` | 視線の正規化 Vector2（X = ヨー、Y = ピッチ、各 [-1, 1]）。`enableGaze` 時 |
| `<slug>:head` | 頭部の N 軸アナログ入力。軸 0〜2 = オイラー角（度）、`includeHeadPosition` で軸 3〜5 = 位置 |

## 使い方

1. **Create → FacialControl → Adapter Runtime Settings Collection** を作成し、**Add → IFacialMocapRuntimeSettingsSO**（Add メニューは型名で表示される）で sub-asset を追加。listen ポートを iFacialMocap アプリの送信先に合わせる
2. `FacialCharacterProfileSO` の **Adapter Bindings** で **iFacialMocap Receiver** を Add し、Runtime Settings 欄に sub-asset を割り当てる。レイヤーの入力源 id に `<slug>` が自動追加される
3. モデルの BlendShape 名が ARKit 正準名なら設定不要。異なる場合は **BlendShape Mappings** に対応表を列挙する
4. 視線を目ボーンに反映する場合は Profile の目線タブでチャネル `gaze` の入力ソースにこの binding を選ぶ。左右別の入力源は自動で結線される
5. 頭部を頭ボーンに反映する場合は `AnalogBindingEntry`（TargetKind = BonePose）で `<slug>:head` の各軸を頭ボーンへ結線する
6. Play。**Import Sample** の `IFacialMocapReceiverDemo` で最小構成を確認できる

## プロトコル

- 端末が PC の `49983` へ 60 fps で UDP 送信する。端末側がストリーム未開始の場合は Settings の `sendHandshake` を ON にし、`deviceAddress` に端末 IP を入れると PC からトリガー文字列を周期送信して起動する
- パケットは `name-value|...|=head#X,Y,Z,posX,posY,posZ|rightEye#X,Y,Z|leftEye#X,Y,Z|`。BlendShape 値域は 0〜100（内部で 0〜1 に正規化）、角度は度
- v2 形式（`dataVersion = v2`）では name-value の区切りが `-` から `&` になり負値を扱える（Facemotion3d 等）
- 受信は UDP のみ。TCP 経路は未対応。listen ポートが使用中の場合はエラーを出して起動しない（OSC のような自動繰り上げはない）
- `stalenessSeconds` を超えて受信が途絶えると `RevertToBase`（BlendShape / 視線 / 頭部を 0 へ）または `HoldLastValue`

参考: <https://www.ifacialmocap.com/for-developer/>

## ドキュメント

- [使い方の詳細](Documentation~/usage.md)
- [Runtime Settings の JSON](Documentation~/ifacialmocap-options.md)

## ライセンス

[MIT License](LICENSE.md)
