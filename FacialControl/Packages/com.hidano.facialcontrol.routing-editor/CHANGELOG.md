# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [1.0.0] - 2026-09-26

初回リリース。`com.hidano.facialcontrol` の Editor に含まれていたルーティングエディタを独立パッケージとして分離した。

### Added

- `RoutingEditorWindow` — `FacialCharacterProfileSO` の入力源とレイヤーの配線を GraphView で編集する EditorWindow。同じ Profile に対しては 1 ウィンドウを再利用し、Profile が破棄されると自動で閉じる
- ノード表示 — Adapter Binding（出力ポート = 入力源 id）/ レイヤー（名前・優先度・排他モード・override mask）/ Composite Output（レイヤー合成順）と、解決できない入力源 id を示す孤立ノード
- エッジ操作 — エッジ作成でレイヤーの入力源宣言を追加、削除で宣言を削除、ポート上で重みを編集。編集は core の `IWiringSerializedMapper` 経由で SerializedObject に書き込み、Undo に対応
- `RoutingEditorLauncherRegistration` — ドメインリロード時に core の `RoutingEditorLauncher` へ登録し、Profile Inspector の「ルーティングを編集」ボタンを有効化
- EditMode テスト — ウィンドウ生成 / 保存経路 / 破棄の smoke と、Inspector 起動経路の登録確認
