# FacialControl Expression Creator

`com.hidano.facialcontrol` の Expression 作成ツール。モデルをプレビューしながら BlendShape スライダーで表情を作り、`FacialCharacterProfileSO` の Expression に割り当てる AnimationClip をベイクする。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | Profile / Expression の定義、AnimationClip の読み取り（`AnimationClipExpressionSampler`）、BlendShape 名の候補列挙 |
| `com.hidano.scene-view-style-camera-controller` | 1.0.0 | プレビューカメラの Scene View 風操作（orbit / pan / dolly） |

## 開き方

**Tools → FacialControl → Expression 作成**

## 使い方

1. **モデル** に BlendShape を持つキャラクターの GameObject（Scene 上のオブジェクトか prefab）を割り当てる
2. Clip を選ぶ
   - **登録済み Expression から編集**: `FacialCharacterProfileSO` を選び、ドロップダウンから Expression を選ぶと割り当て済みの Clip を読み込む
   - **AnimationClip を作成・編集**: 既存 Clip を割り当てるか、**新規 Clip を作成** で保存先を選んで作る
3. BlendShape スライダーを動かす。検索とレンダラー別フィルタで絞り込める。プレビューは即時反映され、Alt + 左ドラッグで orbit、中ドラッグで pan、スクロールで dolly
4. **ベイク** で Clip に書き込む。未ベイクの編集があるままウィンドウを閉じようとすると確認ダイアログが出る

## 機能

- **ベイク形式** — 各 BlendShape を `SkinnedMeshRenderer.blendShape.{name}` の定数カーブ（0〜100）として書き込み、遷移時間とカーブプリセットを `AnimationEvent` のメタとして保存する。core の `FacialCharacterProfileSO` はこの Clip をそのまま Expression に割り当てられる
- **顔のトラッキング** — Humanoid の Head ボーン、なければ名前に "head" / "neck" を含むジョイントを自動で注視点にし、FoV を下げて顔のアップを表示する。**トラッキング対象** で手動指定もできる
- **存在しない BlendShape の一括削除** — Clip に含まれるがモデルに存在しない BlendShape カーブを警告し、まとめて削除できる
- **PNG 書き出し** — 表示中のプレビューを PNG 保存。Profile を選んでいる場合は全 Expression のプレビューを 1 フォルダへ一括書き出しできる（512 px）

## core との境界

Clip の読み取り（`IExpressionAnimationClipSampler`）と BlendShape 名の列挙は core に残し、本パッケージは UI・プレビュー描画・ベイク書き込みだけを担う。core 側から本パッケージへの参照はなく、未導入でも core の Inspector は動作する（Clip は別の手段で用意する）。

## ライセンス

[MIT License](LICENSE.md)
