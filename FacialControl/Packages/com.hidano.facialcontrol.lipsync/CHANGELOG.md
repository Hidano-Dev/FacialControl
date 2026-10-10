# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- 発話ゲート（Voice Gate）— iFacialMocap 等のキャプチャと併用したとき、無言の間はキャプチャの口をそのまま出し、発話中だけ uLipSync の口に置き換える。uLipSync の正規化済み音量をヒステリシス（On / Off / Hold）で判定し、gate weight を Attack / Release で動かして、uLipSync の入力源だけを宣言したレイヤーの weight に書く。weight 0 の間は入力源を止める。コールバックが Stale Timeout 秒途絶えたら閉じる。既定 ON（UDP LipSync アダプタと同じ仕様。HID-189）
- Suppress Blend Shape Names と「フェイシャルキャプチャの口まわりを追加」ボタン — 発話中にキャプチャの口の開閉・形状系 21 個（iFacialMocap の Mappings 上書きの反映先を含む）へ 0 を書く
- Inspector にレイヤー構成の警告（uLipSync のレイヤーが無い / 他の入力源と同居 / キャプチャのレイヤーの priority が uLipSync 以上）と Live Monitor（activity / Speaking / weight / 対象レイヤー）。スクリプトから `VoiceGate` / `GateLayerNames` を参照できる

### Changed

- 発話ゲートが既定 ON のため、発話と判定されるまで（音量が Voice On Threshold 未満の間や Release 後）は phoneme overlay 入力源が出力しない。従来どおりの挙動にするには binding の Voice Gate Enabled を OFF にする（Suppress Blend Shape Names は OFF でも常に効くため、空にしておく）。フィールド追加前に保存した binding は既定値（ON）で読み込む

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `ULipSyncAdapterBinding`（"uLipSync"、既定 slug `ulipsync`）— 再生時に `AudioSource` / `uLipSync` / マイクまたは ASIO 入力を動的に構築し、音素比率 × 音量を `lipsync-overlay:{a|i|u|e|o}` 入力源として登録する
- 音素エントリ 3 形式（Expression / AnimationClip / BlendShape）と、新規追加時の A〜O Expression プリセット・自動リンク
- `LipSyncPhonemeOverlayInputSource` — Expression の Override / Suppress → Default Overlays → uLipSync 既定出力の優先順位で口形状を解決
- マイク / ASIO デバイスの解決（同名デバイスの序数指定、空指定時の既定マイクフォールバック）と実行中の hot-swap
- デバイス設定の PlayerPrefs 保存（`LipSyncDeviceStore`）
- 同梱の既定 uLipSync Profile と、UI Toolkit の Drawer（デバイス選択 / Analyzer Profile / 音素エントリ一覧）
- サンプル `MicLipSyncDemo` / `AnimationClipLipSyncDemo`
- ホットパスの GC アロケーション 0 検証と、10 体同時のデバイス分離テスト
