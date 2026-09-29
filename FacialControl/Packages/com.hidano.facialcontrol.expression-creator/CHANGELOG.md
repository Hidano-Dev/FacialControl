# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Changed

- **Breaking**: `FaceTrackTargetResolver` を core の `Hidano.FacialControl.Editor.Common` へ移動した（Inspector の Expression サムネイルと共用するため）。`Hidano.FacialControl.ExpressionCreator.FaceTrackTargetResolver` を参照していたコードは名前空間を `Hidano.FacialControl.Editor.Common` に変更する
- `PreviewRenderWrapper.CapturePreviewTexture` の描画処理を core の `PreviewRenderCapture` に共通化した（挙動は変えていない）

## [1.0.0] - 2026-09-26

初回リリース。`com.hidano.facialcontrol` の Editor に含まれていた Expression 作成ツールを独立パッケージとして分離した。

### Added

- `ExpressionCreatorWindow`（**Tools → FacialControl → Expression 作成**）— モデルをプレビューしながら BlendShape スライダーで表情を作り AnimationClip にベイクする。「登録済み Expression から編集」「AnimationClip を作成・編集」の 2 タブ、Renderer 別フィルタ、存在しない BlendShape の一括削除、プレビュー PNG の単発保存と全 Expression の一括書き出し
- `ExpressionClipBakery` — BlendShape 値を `blendShape.{name}` の定数カーブとして書き込み、遷移時間とカーブプリセットを `AnimationEvent` メタとして保存する。逆ロードは core の `IExpressionAnimationClipSampler` に委譲
- `PreviewRenderWrapper` / `PreviewInputFrame` — `PreviewRenderUtility` と `com.hidano.scene-view-style-camera-controller` による Scene View 風のカメラ操作（Alt + 左ドラッグで orbit、中ドラッグで pan、スクロールで dolly）
- `FaceTrackTargetResolver` — Humanoid の Head ボーン → 名前に "head" → "neck" を含むジョイントの順で顔の注視点を自動解決
- `BoneNameProvider` — 参照モデル配下のボーン名を重複排除・ソートして列挙するユーティリティ
- EditMode テスト — ウィンドウ生成 / ベイク / 破棄の smoke、ベイクロジック、プレビュー入力、注視点解決
