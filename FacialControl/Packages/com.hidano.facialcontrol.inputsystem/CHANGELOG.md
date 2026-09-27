# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `InputSystemAdapterBinding`（"Input System"、既定 slug `input-system`）— InputActionAsset / ActionMap / キーバインディング行だけで、表情トリガー（Hold / Toggle）、アナログ駆動、Overlay 重み、Gaze（左右別対応）を結線する
- 入力源 id `<slug>` / `<slug>:analog-expression` / `<slug>:overlay:<slot>` / `<slug>:<channelId>` / `<slug>:<actionName>` の登録と、Add 時の既定レイヤー自動追加
- `ExpressionInputSourceAdapter`（キーボード / コントローラの Action を自動分類して購読する MonoBehaviour。binding が動的に付与）
- 6 種の float InputProcessor（`analogDeadZone` / `analogScale` / `analogOffset` / `analogClamp` / `analogCurve` / `analogInvert`）
- UI Toolkit の `InputSystemAdapterBindingDrawer`（Overlay slot は Profile の Slots からドロップダウン）
- サンプル `Multi Source Blend Demo`（Scene / Profile / InputActionAsset / AnimationClip / HUD）
