# FacialControl OSC

`com.hidano.facialcontrol` の OSC 送受信アダプタ。FacialControl 同士の独自プロトコル（送信元識別 + 対応表バージョン付きの値の配列）で BlendShape と Gaze を送受信する。対応表は受信側の要求に応じて送るので、mapping の手入力なしに繋がる。受信側には外部の送信元向けの名前つきアドレス（VRChat / ARKit 形式）の手動 mapping も残っている（廃止予定）。

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

一番よく変える受信ポート・送信先は binding 本体に持つ。heartbeat 間隔や staleness など滅多に変えない項目は、任意で割り当てる上級設定アセット（Receiver: **`OscReceiverRuntimeSettingsSO`** / Sender: **`OscSenderRuntimeSettingsSO`**。どちらも `AdapterRuntimeSettingsCollectionSO` の sub-asset）に置く。未割り当てなら既定値で動く。

## 使い方

1. `FacialCharacterProfileSO` の **Adapter Bindings** で **OSC Receiver** / **OSC Sender** を Add する
2. Receiver は **受信ポート**（既定 9001。受信は常に全インターフェース）、Sender は **送信先**（既定 `127.0.0.1:9000` の 1 件。複数指定可、宛先ごとに有効 / 無効を選べる）を設定する
   - 上級設定を変えたい場合だけ、**Create → FacialControl → Adapter Runtime Settings Collection** に **Add → OscReceiverRuntimeSettingsSO** / **OscSenderRuntimeSettingsSO** で sub-asset を追加し、binding の **上級設定** に割り当てる
3. Receiver の **対象レイヤー** で、受信値を足す既存レイヤーを選ぶ（未指定ならプロファイルの先頭レイヤー）。レイヤーの入力源を手で編集する必要はない（起動時の補い方は「受信の動作」）
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
| `/_facialcontrol/values` | 値フレーム `[対応表のバージョン, offset, 値...]`。毎フレーム。slot は BlendShape → gaze チャネルごとの X / Y |
| `/_facialcontrol/layout_request` / `/_facialcontrol/layout` | 受信側が未知のバージョンを見たら送信側へ要求し、送信側が要求元へ対応表（BlendShape 名・gaze チャネルと目ボーン path・可動範囲）を返す |
旧形式の heartbeat（`/_facialcontrol/blendshape_names`）・プリセット通知（`/_facialcontrol/preset`）・Gaze 広告（`/_facialcontrol/gaze`）は送受信とも廃止した。受信側は未知のアドレスとして読み飛ばす。

対応表の gaze チャネルは、チャネル id ごとに次の属性（`key=value` 形式）を持つ。

| 属性の value | 送るとき | 受信側の扱い |
|---|---|---|
| `bone.left=<path>` / `bone.right=<path>` | 送信側の目線タブでその目の path を指定したときだけ | ローカルの目ボーン path より優先する。rest 回転・yaw / pitch 軸は、解決したボーンから実行時に導出する。path が見つからない場合は警告を 1 回出し、ローカルの規則（目線タブの path → Humanoid の目ボーン）で駆動する |
| `range=<lookUp>,<lookDown>,<outerYaw>,<innerYaw>` | 毎回の対応表で必ず | 可動範囲（度、InvariantCulture、0〜90 にクランプ）をローカル値より優先する |

- 送信側はチャネルごとに path → `range=` の順で並べる。受信側は `range=` が届いた時点でそのチャネルの属性を確定する
- 上書きを使うのは、そのチャネルを実際に駆動している OSC Receiver の分だけ。別の binding（InputSystem 等）が駆動するチャネルには適用しない
- 上書き path のボーンの rest 回転・軸は、受信側 FacialController の初期化時の姿勢から導出する
- 属性の解析は対応表を適用したときだけ行い、上書きが変わったときだけ目ボーン provider を作り直す（毎フレームのヒープ確保は増えない）。送信側は heartbeat 間隔ごとに目線タブの変化を確かめ、変わっていれば対応表を別のバージョンで作り直す
- 一度受け取った上書きは、送信元が別のアプリに替わっても受信側の再初期化まで残る（自動 route と同じ扱い）

## 受信の動作

- **自動マッピング**: 値フレームの対応表を受け取ると、BlendShape 名が一致する受信側の BlendShape へ slot を割り当てる。Gaze も対応表の gaze チャネルから自動で route を作る。対応表に載った目ボーン path・可動範囲は受信側の目線タブの値より優先するので、FacialControl 同士なら受信側は目線タブを設定しなくてよい
- **手動 mapping**: FacialControl 以外の送信元には `Mappings` に mode 別 entry を並べる。mode は `Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`
- **bundle 解釈**: 既定 `AtomicSwap`（同一 bundle を 1 フレームで一括反映）。`IndividualMessage` で受信順に個別反映
- **staleness fail-safe**: `stalenessSeconds` を超えて受信が途絶えると `RevertToBase`（ベース表情へ戻す）または `HoldLastValue`（最後の値を保持）
- **ポート自動繰り上げ**: listen ポートが使用中なら空きポートへ最大 10 回繰り上げ、警告で実際のポートを通知する
- 受信スレッドは Unity API を呼ばず、メインスレッドの `Update` でパースと反映を行う

登録する入力源 id: BlendShape は `<slug>`、Gaze は `<slug>:<channelId>`（左右別は `.left` / `.right`）。

- **レイヤーへの自動宣言**: 起動時に、対象レイヤーの入力源宣言へ `<slug>`（weight 1.0）を補う。補うのはランタイムの解決結果だけで、Profile アセットは書き換えない
  - `<slug>` がいずれかのレイヤーに手で宣言済みなら何もしない（従来の手動宣言はそのまま動き、同じ入力源を 2 回合成しない）
  - 対象レイヤーが未指定ならプロファイルの先頭レイヤーに補う。指定したレイヤーがプロファイルに無い場合は補わず、初期化のたびに警告を出す。受信が起動しなかった（ポート不正等）場合は補わない
  - ルーティングエディタでは、自動で補う配線を半透明の読み取り専用エッジで表示する（ポート不正等で起動しない設定でも表示される）。そのレイヤーへ手で配線すると手動の宣言になり、weight を編集できる

## 送信の動作

- `FacialOutputBus` を購読し、`OnLateTick` で 1 フレーム 1 bundle（`/_facialcontrol/sender_id` + 値フレーム `/_facialcontrol/values`）を送る。MTU（1472 byte）を超える場合は同一タイムスタンプの複数 bundle に分割。対応表は受信側の要求に応じて返す。送信先ごとのアドレス形式（プリセット）は無い
- 送信対象の BlendShape は既定でモデルの全 BlendShape。**BlendShape Names (Optional Filter)** に列挙すると絞り込める
- Gaze は Profile の目線タブに宣言されたチャネルが `FacialController` から自動注入される。Inspector で個別指定する項目はない
- **Suppress Loopback**（既定 ON）: 同じ Profile 内の OSC Receiver と同じ endpoint への送信を抑止する。同一プロセスで送受信デモを同居させるときは OFF にする
- 送信は別スレッドの `UdpClient` で行い、メインスレッドをブロックしない

## サンプル

| Sample | 内容 |
|---|---|
| `OscOutputDemo` | sin 波のデモ信号を BlendShape / Gaze として合成し、9000 と 9001 の 2 endpoint へ値フレームで送信 |
| `OscReceiverDemo` | 9000 で受信し、値フレームの対応表でモデルへ反映。Gaze も対応表から自動 route |

## JSON リファレンス

- [OSC Sender の設定](Documentation~/osc-sender-options.md)
- [OSC Receiver の設定](Documentation~/osc-receiver-options.md)

## ライセンス

[MIT License](LICENSE.md)
