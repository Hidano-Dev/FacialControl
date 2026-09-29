# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Removed

- ARKit 検出ツール（Tools → FacialControl → ARKit 検出ツール）と、それが使っていた `ARKitEditorService` / `ARKitUseCase` を削除。ARKit 命名の BlendShape を「グループ内を全部 1.0」にした Expression を自動生成する機能は、Clip ベース + キャプチャ入力の運用では使われていなかった。OSC マッピング生成は `com.hidano.facialcontrol.osc` の heartbeat 自動マッピングと ARKit プリセットで代替できる
- `ARKitDetector.GroupByLayer` / `ARKitDetector.GenerateExpressions` を削除（上記ツール専用だった）。名前表（`ARKit52Names` / `PerfectSyncNames`）・`GetLayerGroup`・完全一致検出（`DetectARKit` / `DetectPerfectSync` / `DetectAll`）は残す

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
