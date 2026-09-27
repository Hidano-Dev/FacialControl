# FacialControl InputSystem

`com.hidano.facialcontrol` の Unity InputSystem 連携アダプタ。InputActionAsset の Action を、表情の on/off・アナログ駆動・Overlay の重み・目線（Gaze）に結線する。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | Adapter Binding / 入力源レジストリ / Gaze チャネル |
| `com.unity.inputsystem` | 1.17.0 以上（1.19.0 で検証） | InputActionAsset の購読 |

## 提供する binding

**Input System**（`InputSystemAdapterBinding`、既定 slug `input-system`）。設定項目は 3 つだけで、行ごとの **動作モード** で結線の種類を切り替える。

| 設定 | 内容 |
|---|---|
| **InputActionAsset** | 購読する `.inputactions` |
| **ActionMap 名** | 既定 `Expression` |
| **キーバインディング** | Action と Expression の対応行のリスト |

| 動作モード | Action の型 | 動き |
|---|---|---|
| `Normal` | Button | 表情の on/off。トリガモード `Hold`（押している間だけ ON、既定）/ `Toggle`（押すたび切替） |
| `Analog` | Axis (float) | 押し込み量で Expression の重みを 0〜1 で駆動 |
| `Overlay` | Axis (float) | 指定 Overlay slot のレイヤー重みを 0〜1 で駆動（例: RT で「目を閉じる」を重ねる） |
| `Gaze` | Vector2 | 目線チャネルへ Vector2 を流す。左右別 Action も指定可能 |

Value 型の Action を `Normal` に割り当てた場合は、値が 0 より大きい間 ON になる。

## 登録される入力源 id

| id | 条件 |
|---|---|
| `<slug>` | 常に登録（表情トリガー） |
| `<slug>:analog-expression` | `Analog` 行がある場合 |
| `<slug>:overlay:<slot>` | `Overlay` 行ごと。slot は Profile の Slots に宣言済みであること |
| `<slug>:<channelId>` / `.left` / `.right` | `Gaze` 行ごと（既定チャネル id は `gaze`） |
| `<slug>:<actionName>` | `Analog` / `Overlay` 行が参照する Action ごと（他 binding から参照可能） |

binding を Add した時点で、これらの id を持つ既定レイヤーが Profile に自動追加される。

## 使い方

1. `.inputactions` を用意する（同梱サンプルの `MultiSourceBlendDemoActions.inputactions` を複製してもよい。ActionMap `Expression` に `Trigger1`〜`Trigger10`（Button）、`Look`（Vector2）、`LeftTrigger` / `RightTrigger`（Axis）を持つ）
2. `FacialCharacterProfileSO` の **Adapter Bindings** で **Input System** を Add し、InputActionAsset と ActionMap 名を設定
3. **キーバインディング** に行を追加し、表情 ID・動作モード・Action 名を設定
4. `Gaze` 行を使う場合は Profile の目線タブでチャネル `gaze` の入力ソースにこの binding を選ぶ
5. キャラクターに `FacialController` を付けて Play。binding は `OnStart` で InputActionAsset を複製して有効化し、`Dispose` で破棄する（Scene に追加の MonoBehaviour は不要）

## カスタム InputProcessor

`.inputactions` の processors 欄で使える float 用 processor を 6 種登録する（Editor では `[InitializeOnLoad]`、Runtime では `BeforeSceneLoad`）。

| 名前 | パラメータ | 処理 |
|---|---|---|
| `analogDeadZone` | `min` = 0, `max` = 1 | 絶対値でデッドゾーン再センタリング |
| `analogScale` | `factor` = 1 | 倍率 |
| `analogOffset` | `offset` = 0 | 加算 |
| `analogClamp` | `min` = 0, `max` = 1 | クランプ |
| `analogCurve` | `preset` (0 Linear / 1 EaseIn / 2 EaseOut / 3 SmoothStep) | カーブ |
| `analogInvert` | なし | 符号反転 |

## サンプル

**Multi Source Blend Demo** — キーボード `1`〜`5` / ゲームパッドで 5 表情、WASD / 左スティックで目線、LT で笑顔をアナログ駆動、RT で「目を閉じる」Overlay を重ねる統合デモ。Scene / Profile / InputActionAsset / AnimationClip / HUD を同梱し、モデルを `Character` の子に置くだけで動く。詳細は `Samples~/MultiSourceBlendDemo/README.md`。

## ライセンス

[MIT License](LICENSE.md)
