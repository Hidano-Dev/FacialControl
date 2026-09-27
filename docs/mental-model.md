# FacialControl 利用時のメンタルモデル

FacialControl を組み込む側が押さえておくべき最小の概念モデル。OSC 送受信を含むランタイム全体を 1 枚で俯瞰するためのドキュメント。

## 1. 全体構造（1 ファイルで完結）

```
FacialCharacterProfileSO (1 個)
 ├─ 入力（InputActionAsset + キーバインディング + アナログバインディング）
 ├─ レイヤー（emotion / lipsync / eye …、優先度と排他モード）
 ├─ Expression 集（BlendShape 値 + 所属レイヤー + 遷移時間/カーブ）
 ├─ Gaze セクション（既定チャネル gaze + 追加チャネル、入力源、目ボーン設定）
 └─ アダプターバインディング群（OSC Sender / OSC Receiver / LipSync など）
```

- シーンには **Animator を持つキャラ + `FacialController` + 上記 SO** を結線するだけ。BlendShape を持つ `SkinnedMeshRenderer` は子から自動探索される。
- JSON はランタイムの正規データだが、Editor が `StreamingAssets/FacialControl/{SO 名}/profile.json` に自動エクスポートする。**ユーザーは SO Inspector を触るのが基本動線**で、JSON は触らない。
- ビルド後にコンテンツ差し替えが必要な場合のみ、StreamingAssets 配下の JSON を置き換える。
- Gaze は Expression や `eye` レイヤーではなく、プロファイル直下の独立したチャネルである。`eye` レイヤーはまばたき等の BlendShape 表情を合成し、Gaze は Vector2 入力から目ボーンの回転を直接更新する。

### Gaze の設定

1. Profile Inspector の **Gaze** セクションで、先頭の既定チャネル `gaze` を使用する（必要な場合だけ追加チャネルを作る）。
2. 各チャネルの **入力ソース** ドロップダウンで、InputSystem、OSC 受信、iFacialMocap、Timeline など、利用する binding が宣言した入力源を選ぶ。空欄は自動解決である。
3. 参照モデルを割り当てると、Animator を起点に左右の目ボーンのフルパスが自動保存される。目ボーン path、初期回転、yaw/pitch 軸、上下左右の可動角は上級設定で確認・調整できる。
4. 起動時は `FacialController` がチャネル id を各 binding に注入し、入力源を解決して目ボーンへ適用する。複数チャネルや左右独立 source id は上級設定でのみ構成する。

## 2. 表情合成パイプライン

```
入力（キー / アナログ / LipSync / OSC 受信） → 各 binding が入力源として登録
  → FacialController.LateUpdate でレイヤーごとに加重和 → 優先度順に合成 → 遷移補間
  → SkinnedMeshRenderer.SetBlendShapeWeight へ直接書き込み（PlayableGraph は使わない）
  → 合成後の値を IFacialOutputBus へ配信（OSC Sender 等が購読）
```

- レイヤーは「優先度 + ウェイトでブレンド」される。Expression の `layerOverrideMask` で他レイヤーを抑制できる。
- 遷移は線形が既定（0〜1 秒、既定 1/15 秒）。遷移中に新表情がトリガーされたら、現在の補間値を起点に新遷移を開始（GC ゼロ）。
- テクスチャ切り替え / UV アニメーションは AnimationClip 内のキーフレームとして扱われる（再生するだけで対応）。

## 3. OSC 送信（Sender Binding）

- **送信単位は BlendShape 1 個 = OSC メッセージ 1 個**。毎フレーム bundle で送出。
- プリセットでアドレス決定:
  - `vrchat`: `/avatar/parameters/{name}`、Gaze は `{channelId}X` / `{channelId}Y` の 2 メッセージ
  - `arkit`: `/ARKit/{name}`、Gaze は eyeLook 系 8 BlendShape に分解
- 送信対象は **省略時に全自動**（モデルの全 BlendShape + Profile が宣言する全 Gaze チャネル）。subset 配信したいときだけ `BlendShape Names (Optional Filter)` を列挙して絞る。Gaze は FacialController が自動注入し、個別指定はしない。
- endpoint / heartbeat 周期 / loopback 抑制は `OscRuntimeSettingsSO`（Adapter Runtime Settings Collection の sub-asset）に置く。
- 起動時と `heartbeatIntervalSeconds` 周期（既定 5 秒）で `/_facialcontrol/blendshape_names` heartbeat を送出（受信側の名前整合性検査用）。bundle には送信元識別用 `/_facialcontrol/sender_id` と、gaze のチャネル id / 形式を知らせる `/_facialcontrol/gaze` 広告も同梱される。
- `suppressLoopback`（既定 ON）: 同一 child scope 内の自分の受信 endpoint と一致する送信先を抑止する。
- 別スレッド非同期送信で、メインスレッド負荷ゼロ。

## 4. OSC 受信（Receiver Binding）

- `listenEndpoint` を設定して起動すると、送信側 FacialControl が heartbeat に同梱する `/_facialcontrol/gaze` 広告を受け取り、gaze の形式（`VRChat_XY` / `ARKit_8BS`）に応じた route と input source を自動生成する。受信側で gaze の mapping エントリをあらかじめ手入力したり、OnStart 時に固定したりする必要はない。
- **手動 mapping は上書き用のオプション**として残る。同じ gaze を手動で定義した場合は手動 route が優先され、広告から自動生成された route と併存できる。FacialControl 以外の外部 OSC 送信元を受ける場合は、従来どおり手動 mapping を設定する。
- 自動 route は広告のチャネル id と受信側 Profile の Gaze チャネル id を照合して生成される。広告だけで目ボーン設定まで完全自動化するものではない。
- `listenEndpoint` + **mapping エントリ（`mode` + `expressionId` + `addressPattern`）** を SO に並べる方式。BlendShape mapping と手動 gaze mapping はこの設定で定義する。
- `mode` は 3 種類:

  | mode | 意味 | `addressPattern` |
  |---|---|---|
  | `blendShape` | OSC 値を Expression（BlendShape）に流す | 完全な OSC アドレス |
  | `gazeVrchatXy` | X/Y 2 メッセージから Gaze Vector2 を組み立て | 末尾 X/Y を除いた base アドレス |
  | `gazeArkit8Bs` | eyeLook 8 BlendShape から Gaze Vector2 を逆算 | 無視（固定 8 アドレス） |

- `leftRightIndependent = true` の Gaze entry では `sourceIdLeft` / `sourceIdRight` を介して左右別 source として publish する。
- `stalenessSeconds` 超過時のフェイルセーフ:
  - `revertToBase`: ベース表情へ戻す
  - `holdLastValue`: 最後の値を保持
- staleness は binding 単位で共有される。BlendShape の受信が継続している状態で gaze だけが途絶しても gaze route は破棄されず、gaze は最後に受信した値を保持する（gaze 単位での個別タイムアウトや自動リセットではない）。
- `bundleMode` は既定 `atomicSwap`（bundle 全件を 1 フレームに一括反映）。`individualMessage` を選べば受信順で個別反映。
- 送信側 heartbeat と mapping を突き合わせ、**不一致 BlendShape のみ更新を停止**しつつ、一致分は通常通り反映。差分は Unity 警告ログへ出す（`consistencyCheckWarnLog`）。
- 受信値とローカル入力は同じバスに流れ、**後勝ち（LastWins）で統一**される。

## 5. 同一プロセスで Sender と Receiver を同居させる時

- 既定では Sender 側 `suppressLoopback = true` がループバックを止めるため、**同居運用なら Sender 側で OFF にする**。
- ループバック抑制は「同じ child scope 内」で endpoint が一致した時だけ働く（別 SO 間は素通し）。
- VRChat の自身からの受信パケットを抑止したい場合は ON のままで運用する。

## 6. メンタルモデル要約

> **「キャラ SO に表情データ・入力・OSC アダプターを全部生やす → `FacialController` が LateUpdate で合成して BlendShape へ書き込み、Gaze は独立して目ボーンへ適用する」**。
>
> OSC は表情の I/O アダプターのひとつで、送信は BlendShape と Gaze の snapshot を送出し、受信は gaze 広告を起点にチャネル route を自動生成する（必要なら手動 mapping で上書きする）。Gaze source id は `{slug}:{channelId}[.left|.right]` で統一される。JSON は永続化フォーマットだが、通常は SO の Gaze セクションを操作する。

## 参考資料

| 資料 | 場所 |
|---|---|
| 要件定義 | [requirements.md](requirements.md) |
| 技術仕様書 | [technical-spec.md](technical-spec.md) |
| Quickstart | [Packages/com.hidano.facialcontrol/Documentation~/quickstart.md](../FacialControl/Packages/com.hidano.facialcontrol/Documentation~/quickstart.md) |
| OSC Sender スキーマ | [Packages/com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md](../FacialControl/Packages/com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md) |
| OSC Receiver スキーマ | [Packages/com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md](../FacialControl/Packages/com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md) |
