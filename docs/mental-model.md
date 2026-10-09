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
- 送信先リスト（endpoint / port / 有効 / プリセット）は binding 本体に置く。対応表の更新間隔 / loopback 抑制は任意の上級設定 `OscSenderRuntimeSettingsSO`（Adapter Runtime Settings Collection の sub-asset）に置き、未割り当てなら既定値で動く。
- 送信元識別 `/_facialcontrol/sender_id` と値フレーム `/_facialcontrol/values` を送る。対応表（BlendShape 名・gaze チャネルと目ボーン path・可動範囲）は受信側の要求に応じて返す。`layoutRefreshIntervalSeconds` 周期（既定 5 秒）で目線タブの変化を確かめ、変わっていれば対応表を別のバージョンで作り直す。
- `suppressLoopback`（既定 ON）: 同一 child scope 内の自分の受信 endpoint と一致する送信先を抑止する。
- 別スレッド非同期送信で、メインスレッド負荷ゼロ。

## 4. OSC 受信（Receiver Binding）

- 受信ポートを設定して起動すると（受信は常に全インターフェース）、送信側 FacialControl から値フレームの対応表を受け取り、対応表の gaze チャネルごとに route と input source（左右共通）を自動生成する。受信側で gaze の mapping エントリをあらかじめ手入力したり、OnStart 時に固定したりする必要はない。
- BlendShape も対応表から、名前が一致する受信側の BlendShape へ自動で割り当てる。手動 mapping は無く、値フレーム以外の名前つきアドレス（VRChat / ARKit 形式）は受けない（外部 OSC 互換は要件外。HID-169）。
- 自動 route は対応表のチャネル id と受信側 Profile の Gaze チャネル id を照合して生成される。対応表に載った目ボーン path・可動範囲は、受信側 Profile の同じ id の Gaze チャネルより優先される（`IGazeChannelOverrideProvider`）。送信側で path が未指定の目は、受信側の目線タブの path → Humanoid の目ボーンの順で解決する。そのため既定チャネル `gaze` だけを使う FacialControl 同士なら、受信側は目線タブを設定しなくてよい。
- binding に置くのは受信ポートと対象レイヤーだけ。staleness / fail-safe / bundle 解釈などは任意の上級設定 `OscReceiverRuntimeSettingsSO` に置く。
- `stalenessSeconds` 超過時のフェイルセーフ:
  - `revertToBase`: ベース表情へ戻す
  - `holdLastValue`: 最後の値を保持
- staleness は binding 単位で共有される。BlendShape の受信が継続している状態で gaze だけが途絶しても gaze route は破棄されず、gaze は最後に受信した値を保持する（gaze 単位での個別タイムアウトや自動リセットではない）。
- `bundleMode` は既定 `atomicSwap`（bundle 全件を 1 フレームに一括反映）。`individualMessage` を選べば受信順で個別反映。
- 受信値とローカル入力は同じバスに流れ、**後勝ち（LastWins）で統一**される。

## 5. 同一プロセスで Sender と Receiver を同居させる時

- 既定では Sender 側 `suppressLoopback = true` がループバックを止めるため、**同居運用なら Sender 側で OFF にする**。
- ループバック抑制は「同じ child scope 内」で endpoint が一致した時だけ働く（別 SO 間は素通し）。
- VRChat の自身からの受信パケットを抑止したい場合は ON のままで運用する。

## 6. メンタルモデル要約

> **「キャラ SO に表情データ・入力・OSC アダプターを全部生やす → `FacialController` が LateUpdate で合成して BlendShape へ書き込み、Gaze は独立して目ボーンへ適用する」**。
>
> OSC は表情の I/O アダプターのひとつで、送信は BlendShape と Gaze の snapshot を送出し、受信は値フレームの対応表を起点にチャネル route を自動生成する。Gaze source id は `{slug}:{channelId}[.left|.right]` で統一される（OSC 受信は左右共通の `{slug}:{channelId}` だけを作る）。JSON は永続化フォーマットだが、通常は SO の Gaze セクションを操作する。

## 参考資料

| 資料 | 場所 |
|---|---|
| 要件定義 | [requirements.md](requirements.md) |
| 技術仕様書 | [technical-spec.md](technical-spec.md) |
| Quickstart | [Packages/com.hidano.facialcontrol/Documentation~/quickstart.md](../FacialControl/Packages/com.hidano.facialcontrol/Documentation~/quickstart.md) |
| OSC Sender スキーマ | [Packages/com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md](../FacialControl/Packages/com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md) |
| OSC Receiver スキーマ | [Packages/com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md](../FacialControl/Packages/com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md) |
