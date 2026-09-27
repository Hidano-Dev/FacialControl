# FacialControl OSC

`com.hidano.facialcontrol` の OSC 送受信アダプタ。VRChat / ARKit（PerfectSync）互換のアドレスで BlendShape と Gaze を送受信し、FacialControl 同士なら heartbeat による自動マッピングで mapping の手入力なしに繋がる。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | Adapter Binding / 出力バス / Gaze チャネル |
| `com.hidano.uosc` | 1.0.0 | OSC メッセージ表現と単発送信。受信と bundle 送信は本パッケージ独自の UDP 実装 |

## 提供する binding

| displayName | 既定 slug | 役割 |
|---|---|---|
| **OSC Receiver** (`OscReceiverAdapterBinding`) | `osc-receiver` | UDP で受信した BlendShape / Gaze を入力源として登録する |
| **OSC Sender** (`OscSenderAdapterBinding`) | `osc-sender` | 合成後の BlendShape と Gaze を購読し、OSC bundle として複数 endpoint へ送信する |
| **ARKit / PerfectSync** (`ArKitOscAdapterBinding`) | `arkit-perfectsync` | `/ARKit/{name}` を購読するアナログ入力源（実験的。入力源の登録経路は未接続） |

endpoint やポートなど環境依存の設定は binding ではなく **`OscRuntimeSettingsSO`**（`AdapterRuntimeSettingsCollectionSO` の sub-asset）に置き、binding の **OSC Runtime Settings** 欄から参照する。同じ sub-asset を Receiver と Sender で共有できる。

## 使い方

1. **Create → FacialControl → Adapter Runtime Settings Collection** を作成し、**Add → OscRuntimeSettingsSO**（Add メニューは型名で表示される）で sub-asset を追加。Receiver の listen ポート（既定 9001）、Sender の endpoint 一覧（既定送信先 9000）とプリセット（VRChat / ARKit）を設定
2. `FacialCharacterProfileSO` の **Adapter Bindings** で **OSC Receiver** / **OSC Sender** を Add し、Runtime Settings 欄に sub-asset を割り当てる
3. 受信をレイヤーに繋ぐ場合はレイヤーの入力源 id に `<slug>`（例 `osc-receiver`）を追加する。binding を Add した時点で既定レイヤーが自動追加される
4. Gaze を受信する場合は Profile の目線タブでチャネル `gaze` の入力ソースに Receiver を選ぶ。送信側が FacialControl なら手動 mapping は不要
5. Play。**Import Sample** から `OscOutputDemo` / `OscReceiverDemo` を取り込むと、送信側・受信側それぞれの最小 Scene を確認できる

## アドレス形式

| 種別 | VRChat プリセット | ARKit プリセット |
|---|---|---|
| BlendShape | `/avatar/parameters/{name}` (float 0〜1) | `/ARKit/{name}` |
| Gaze | `/avatar/parameters/{channelId}X` と `...Y` | `/ARKit/eyeLook{In,Out,Up,Down}{Left,Right}` の固定 8 アドレスに分解 |

制御アドレス（FacialControl 同士の連携用）:

| アドレス | 内容 |
|---|---|
| `/_facialcontrol/sender_id` | 送信元識別（UUID + 起動時刻）。毎 bundle に同梱。受信側は最新起動の sender だけを採用しゾンビ送信元を排除 |
| `/_facialcontrol/blendshape_names` | heartbeat。送信側が持つ BlendShape 名一覧。起動時と `heartbeatIntervalSeconds`（既定 5 秒）周期 |
| `/_facialcontrol/preset` | `"vrchat"` / `"arkit"` のプリセット通知（Sender の Send Preset Address が ON のとき） |
| `/_facialcontrol/gaze` | Gaze 広告。チャネル id と形式（`VRChat_XY` / `ARKit_8BS`）の組 |

## 受信の動作

- **自動マッピング**: heartbeat を受け取ると、送信側 BlendShape 名とモデルの BlendShape 名の積集合から mapping を生成する。手入力 mapping（`Mappings` リスト）があればそれを優先し、不足分だけ自動生成する。Gaze も `/_facialcontrol/gaze` 広告から自動で route を作る
- **手動 mapping**: FacialControl 以外の送信元には `Mappings` に mode 別 entry を並べる。mode は `Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`
- **bundle 解釈**: 既定 `AtomicSwap`（同一 bundle を 1 フレームで一括反映）。`IndividualMessage` で受信順に個別反映
- **staleness fail-safe**: `stalenessSeconds` を超えて受信が途絶えると `RevertToBase`（ベース表情へ戻す）または `HoldLastValue`（最後の値を保持）
- **整合性検査**: heartbeat と mapping の差分を警告ログに出す（`consistencyCheckWarnLog`）
- **ポート自動繰り上げ**: listen ポートが使用中なら空きポートへ最大 10 回繰り上げ、警告で実際のポートを通知する
- 受信スレッドは Unity API を呼ばず、メインスレッドの `Update` でパースと反映を行う

登録する入力源 id: BlendShape は `<slug>`、Gaze は `<slug>:<channelId>`（左右別は `.left` / `.right`）。

## 送信の動作

- `FacialOutputBus` を購読し、`OnLateTick` で 1 フレーム 1 bundle を送る。MTU（1472 byte）を超える場合は同一タイムスタンプの複数 bundle に分割
- 送信対象の BlendShape は既定でモデルの全 BlendShape。**BlendShape Names (Optional Filter)** に列挙すると絞り込める
- Gaze は Profile の目線タブに宣言されたチャネルが `FacialController` から自動注入される。Inspector で個別指定する項目はない
- **Suppress Loopback**（既定 ON）: 同じ Profile 内の OSC Receiver と同じ endpoint への送信を抑止する。同一プロセスで送受信デモを同居させるときは OFF にする
- 送信は別スレッドの `UdpClient` で行い、メインスレッドをブロックしない

## サンプル

| Sample | 内容 |
|---|---|
| `OscOutputDemo` | sin 波のデモ信号を BlendShape / Gaze として合成し、VRChat（9000）と ARKit（9001）の 2 endpoint へ送信 |
| `OscReceiverDemo` | 9000 で受信し、heartbeat 自動マッピングでモデルへ反映。Gaze は広告から自動 route |

## JSON リファレンス

- [OSC Sender の設定](Documentation~/osc-sender-options.md)
- [OSC Receiver の設定](Documentation~/osc-receiver-options.md)

## ライセンス

[MIT License](LICENSE.md)
