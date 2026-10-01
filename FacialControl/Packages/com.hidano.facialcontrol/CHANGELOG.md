# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- `IAdapterBindingHeaderSummaryProvider` / `AdapterBindingHeaderSummary` — Adapter Binding の PropertyDrawer が任意で実装すると、Adapter Bindings タブの Foldout ヘッダーに要約（接続先など）とツールチップを表示する拡張ポイント。binding の値を変えるとヘッダーも即座に更新する。実装しない binding は従来どおり表示名と slug だけを出す
- `IGazeChannelOverrideProvider` / `GazeChannelOverride` — adapter binding が Gaze チャネルの目ボーン path・可動範囲を外部から上書きする契約。`FacialController` は上書きの version が変わったときだけ目ボーン provider を作り直す。上書きを使うのは、そのチャネルの入力源を実際に提供している binding（slug が一致するもの）だけ。上書き path のボーンがローカル path と異なる側は、rest 回転・yaw / pitch 軸を Humanoid fallback と同じ規則で導出する。導出に使うのは初期化時の姿勢で、上書き binding があるときだけ初期化時に配下の Transform について記録しておく。上書き path がローカル path と同じボーンを指す場合は、エディタで保存した値をそのまま使う。path が見つからなければ警告を 1 回出してローカルの規則に戻す
- `IGazeChannelSettingsConsumer` — Profile の Gaze チャネル設定（目ボーン path・可動範囲を含む）を adapter binding へ注入する契約。`IGazeChannelConsumer` と同じく rebuild ごと・OnStart 前に呼ばれる
- `FacialCharacterProfileSO` Inspector の Expression List に、参照モデルへ各 Expression を適用した顔のサムネイル（128px 表示、クリックで 512px を拡大表示）を追加した
  - 参照モデル・AnimationClip が未設定のときはプレースホルダを表示する
  - 生成は遅延で行う。キャッシュはメモリと `Library/FacialControl/ExpressionThumbnails/`（コミット対象外、最大 500 件で古い順に削除）に置く
  - Expression の中身（AnimationClip のカーブ値・参照先）や参照モデル（依存するマテリアル・テクスチャを含む）が変わったときだけ作り直す。clip を編集中は変更が落ち着いてから作り直す
  - 「サムネイルを再生成」ボタンでキャッシュを破棄して作り直せる
  - Inspector を閉じたとき・ドメインリロード前に、プレビュー用の一時オブジェクトとテクスチャを破棄する
- Editor 共通ユーティリティ `FaceTrackTargetResolver`（`com.hidano.facialcontrol.expression-creator` から移動）、`PreviewRenderCapture`（`PreviewRenderUtility` のオフスクリーン描画 → `Texture2D`）、`PreviewModelBounds`

### Changed

- Gaze チャネルの目ボーン path（`leftEyeBonePath` / `rightEyeBonePath`）を任意にした。空欄の側は実行時に Humanoid Avatar の `LeftEye` / `RightEye` を使い、初期回転は初期化時の目ボーンの姿勢、yaw / pitch 軸はキャラクター root の上方向 / 右方向から導出する。path を指定した側の挙動は従来どおり。非 Humanoid（または Eye 未マップ）で path も空の目は駆動せず、初期化ごとに警告を 1 回だけ出す。Humanoid の目ボーン・rest 回転・軸は初期化時に 1 回だけ取得し、入力源の登録変化で provider を作り直しても取り直さない。path 未指定のチャネルが複数ある場合、Humanoid の目ボーンを駆動するのは目ごとに入力源が解決できた先頭のチャネルだけで、path 指定のチャネルが同じボーンを指していればそちらを優先する
- 目線タブの目ボーン欄を「(任意)」表記にし、空欄時の案内を情報表示に変更。参照モデルが Humanoid の目ボーンを持たない場合だけ警告を出す
- `FacialCharacterProfileSO` Inspector の Adapter Bindings タブ — 各 Adapter を折り畳める `Foldout` で包んだ。ヘッダーに表示名と slug を出し、ヘッダーの `−` ボタンから確認のうえその Adapter を削除できる（型が解決できない行の削除もこのボタンに集約）。開閉状態は Adapter ごとに SessionState へ保存し、Inspector の再選択・ドメインリロード・Adapter の追加 / 削除 / 並べ替えの後も同じ Adapter に保持する（Editor 再起動でリセット）。フッターに「すべて展開」「すべて折り畳む」ボタンを追加した

### Removed

破壊的変更（公開 API の削除）を含む。次のリリースでバージョンを上げる際はメジャー更新が必要。

- ARKit 検出ツール（Tools → FacialControl → ARKit 検出ツール）と、それが使っていた `ARKitEditorService` / `ARKitUseCase` を削除。ARKit 命名の BlendShape を「グループ内を全部 1.0」にした Expression を自動生成する機能は、Clip ベース + キャプチャ入力の運用では使われていなかった。OSC マッピング生成は `com.hidano.facialcontrol.osc` の heartbeat 自動マッピングと ARKit プリセットで代替できる
- `ARKitDetector.GroupByLayer` / `ARKitDetector.GenerateExpressions` を削除（上記ツール専用だった）。名前表（`ARKit52Names` / `PerfectSyncNames`）・`GetLayerGroup`・完全一致検出（`DetectARKit` / `DetectPerfectSync` / `DetectAll`）は残す

### Fixed

- 同じ目ボーンを複数の Gaze チャネルが駆動していた場合に、`FacialController` の終了時に目ボーンが駆動前の回転へ戻らないことがあった

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `FacialCharacterProfileSO` — 表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug の 6 タブ UI Toolkit Inspector。編集時・Play 突入時・ビルド時に `StreamingAssets/FacialControl/{SO 名}/profile.json` を自動書き出し
- `FacialController` — レイヤー合成と遷移補間を `LateUpdate` で行い `SkinnedMeshRenderer` へ直接書き込む MonoBehaviour。`Activate` / `Deactivate` / `LoadCharacter` / `ReloadProfile` / `SetLayerWeight` / `SetInputSourceWeight` API。同一 Renderer を複数の Controller が掴んだ場合の自動解決
- レイヤー定義（優先度 / `lastWins` / `blend`）、入力源の重み付き合成、Expression ごとの `layerOverrideMask`、ベース表情
- 表情遷移（0〜1 秒、既定 1/15 秒、Linear / EaseIn / EaseOut / EaseInOut）と遷移中の割り込み
- Overlay slot（Default / Suppress / Override の 3 状態）と音素予約 slot `a / i / u / e / o`
- Gaze チャネル（Vector2 入力 → 目ボーン yaw / pitch、可動角制限、参照モデルからの目ボーン自動解決）
- Adapter Binding 拡張点 — `AdapterBindingBase` + `[FacialAdapterBinding]` + slug 規約 + `IInputSourceRegistry`。VContainer のキャラクターごとの子スコープでライフサイクルを駆動
- `IFacialOutputBus`（合成後の BlendShape / Gaze を出力系 binding へ配信）と `IFacialInputObservationBus`（入力イベントの観測）
- `AdapterRuntimeSettingsCollectionSO` — 環境依存設定を Profile から分離する sub-asset コンテナ
- JSON プロファイル（`schemaVersion "1.0"`）のパーサ / エクスポータと `Templates/default_profile.json`
- Editor ツール — ARKit 52 / PerfectSync 検出ツール、新規プロファイル作成ダイアログ、ルーティングの配線ロジック（`Editor/Windows/Routing/Logic`）と起動点 `RoutingEditorLauncher`。Expression 作成ツールとルーティングエディタ本体は `com.hidano.facialcontrol.expression-creator` / `com.hidano.facialcontrol.routing-editor` として別パッケージ
- サンプル `MultiSourceBlendBasicSample`
- PlayMode gate テスト — 定常状態の GC アロケーション 0、10 体同時制御、NativeArray リーク検出
