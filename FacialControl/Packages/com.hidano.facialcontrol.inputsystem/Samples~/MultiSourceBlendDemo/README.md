# Multi Source Blend Demo セットアップガイド

ボタン・スティック・トリガーで Unity 上のキャラモデルの表情を切り替えるデモサンプルです。所要時間 10〜15 分。

## このサンプルでできること

| 操作 | 動き |
|---|---|
| キーボード `1` / コントローラの該当ボタン | 笑顔の ON / OFF |
| キーボード `2` / 〃 | 怒りの ON / OFF |
| キーボード `3` / 〃 | 驚きの ON / OFF |
| キーボード `4` / 〃 | 「あ」の口の ON / OFF |
| キーボード `5` / 〃 | 目を閉じた笑顔（パターン違い）の ON / OFF |
| W A S D / 左スティック | 視線（両目を同時に動かす） |
| 左トリガー (LT) | 笑顔を押し込み量に応じてじわっと出す |
| 右トリガー (RT) | 今出している表情に「目を閉じる」動きを重ねる |

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `MultiSourceBlendDemo.unity` | 必要なものが結線済みのシーン |
| `MultiSourceBlendDemoCharacter.asset` | 表情・キー割り当ての定義本体（SO） |
| `MultiSourceBlendDemoActions.inputactions` | キーボード / コントローラの入力定義 |
| `Smile.anim` / `Anger.anim` / `Surprise.anim` / `Lipsync_A.anim` | サンプル用の表情アニメーション |
| `MultiSourceBlendDemoHUD.cs` | 画面左上の操作 HUD |
| `README.md` | 本ファイル |

> キャラモデル (FBX / VRM / prefab) は同梱していません。お手持ちのものを用意してください。

## 前提

- Unity Editor (6000.3 以降) でこのプロジェクトを開いている
- 自分のキャラモデルが 1 体ある

---

## 手順 1: サンプルを取り込む

1. メニュー **Window → Package Manager** を開きます。
2. 上の絞り込みを **In Project** にして、`FacialControl InputSystem` を選びます。
3. 右側の **Samples** タブで `Multi Source Blend Demo` の **Import** ボタンを押します。
4. `Assets/Samples/FacialControl InputSystem/<バージョン>/Multi Source Blend Demo/` 以下に展開されます。

## 手順 2: シーンを開く

1. Project ウィンドウで `MultiSourceBlendDemo.unity` をダブルクリックします。
2. Hierarchy に **Character / Multi Source Blend Demo HUD / Main Camera / Directional Light** が並んでいれば OK です。

> サンプルフォルダの中に `StreamingAssets/...` というフォルダが見えますが、これは出荷時の参考データで**手動で動かす必要はありません**。表情データの本体は `MultiSourceBlendDemoCharacter.asset` の中にあり、Inspector を開く／保存するたびに `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` が自動生成されます。

## 手順 3: キャラモデルを置く

1. Hierarchy の **Character** をクリックして選びます。
2. Project ウィンドウから自分のキャラモデルの prefab を **Character の上にドラッグ**します。Character の子（一段下）に入れば成功です。

> サンプル付属のアニメーションは「`笑い` / `口角上げ` / `怒り` / `まばたき`」などの日本語の標準的な BlendShape 名を持つキャラモデル前提で作られています。お手持ちのモデルがこれと合わない場合、Play しても表情が動きません。その場合は手順 6 を参照してください。

## 手順 4: 視線まわりの初期設定（モデルを差し替えるたびに必要）

両目の動きは、キャラモデルごとに「眼球ボーンがどこにあって、どの軸が上下／左右なのか」が違います。これを自動で測ってもらいます。

1. Project ウィンドウで `MultiSourceBlendDemoCharacter` を選びます。
2. Inspector 上部の **参照モデル** に自分のキャラモデルを割り当てます。
3. **目線** タブを開き、チャネル `gaze` の **「参照モデルから目ボーンを自動解決」** ボタンを押します。
4. 必要なら上級設定の可動角（上 / 下 / 外側 / 内側）で目の動く最大角度を調整します。

## 手順 5: 再生して動かしてみる

1. Unity 上部の **▶ 再生ボタン** を押します。
2. 画面左上に操作 HUD が出ます。
3. 上の「このサンプルでできること」の表どおりに、キー・スティック・トリガーを触ってみてください。

> **キー操作の動作モード** は「**Hold**（押している間だけ ON）」がデフォルトです。「**Toggle**（押すたびに ON / OFF 切替）」にしたい場合は、`MultiSourceBlendDemoCharacter` の Inspector で各バインディングの **動作モード** を変更してください。

## 手順 6: 自分のキャラに合わせて表情を作り直す（任意）

サンプル付属のアニメーション (`Smile.anim` など) がそのまま動かない場合は、自分のキャラに合わせた表情アニメーションを作ります。

1. Unity の **Animation ウィンドウ** で `Smile.anim` を開きます（または同じ要領で新規作成します）。
2. キャラモデル上の SkinnedMeshRenderer が持つ BlendShape を選び、好みの表情になるよう値を打ちます。
3. できたアニメーションを `MultiSourceBlendDemoCharacter` の Inspector の対応する **Expression** の **AnimationClip** 欄にドラッグします。
4. SO を保存すると、表情データに自動反映されます。

> **リップシンクについて**: `Lipsync_A.anim` は AnimationClip 1 個で「あ」の口を出すだけの暫定実装です。今後 uLipSync 等の外部プラグインと連携する形に置き換える予定で、ユーザーが日常的に「あ」「い」「う」…の AnimationClip を増やすワークフローではなくなります。

---

## カスタマイズ

### 「目を閉じる」動きを表情ごとに使い分ける

各表情に対して、右トリガーで重なる「目を閉じる」動きを 3 通りから選べます。`MultiSourceBlendDemoCharacter` の Inspector → 各 Expression の **`overlays.blink`** で切り替えます。

| 設定 | 動き |
|---|---|
| **Default** | 共通の「まばたき」を重ねる（標準） |
| **Suppress** | 重ねない（すでに目を閉じている表情向け） |
| **Override** | この表情専用の目閉じアニメーションを使う |

### キャラ定義を別アセットに差し替える

`MultiSourceBlendDemoCharacter` を別の `FacialCharacterProfileSO` アセットに差し替えるだけで設定一式が切り替わります。

---

## トラブルシューティング

- **HUD が出ない**: 再生中であること、Game ビューが表示されていることを確認してください。
- **表情が動かない**: お手持ちのキャラモデルの BlendShape 名が、サンプル付属アニメーションが想定する日本語名と一致していない可能性が高いです。手順 6 の方法で表情アニメーションを作り直してください。
- **視線がおかしい / 動かない**: 手順 4 の **「参照モデルから目ボーンを自動解決」** を実行したか確認してください。モデルを差し替えるたびに必要です。
- **Play 直後に何も初期化されない**: Hierarchy の `Character` を選び、Inspector で `FacialController` の **Character SO** 欄が空になっていないかを確認してください（通常はサンプル状態で結線済みです）。

## 参考

- `com.hidano.facialcontrol` の `Documentation~/quickstart.md`: コア機能の全体セットアップフロー
- `com.hidano.facialcontrol` の `Documentation~/json-schema.md`: 上級者向けの内部 JSON 仕様（通常運用では Inspector のみで完結します）
