# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

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
