# Requirements Document

## Project Description (Input)
HID-80: REC がレイヤー weight / 入力源 weight のランタイム変更（`FacialController.SetLayerWeight`、`LayerUseCase.SetInputSourceWeight`）を記録・遮断できるようにする。

背景: HID-35（spec `rec-full-input-coverage`）の Gap 分析（`.kiro/specs/rec-full-input-coverage/research.md` §2.3 (b)、§2.5-1、§3-5）で、次の経路が REC の観測面・遮断面のどちらにも乗っていないことが判明し、同 spec の Gate A（2026-10-04）で本 Issue に切り出された。

| 経路 | 実装 | 状態 |
| -- | -- | -- |
| レイヤー weight のランタイム変更 | `FacialController.SetLayerWeight`。inputsystem の overlay binding（`InputSystemAdapterBinding.ApplyOverlayLayerWeights`）が `InputActionAnalogSource` を直参照して毎 LateTick で駆動 | 未記録・未遮断 |
| 入力源 weight のランタイム変更 | `LayerUseCase.SetInputSourceWeight` | 未記録・未遮断 |

どちらもブレンド結果に直接効くため、weight が live のまま残ると rec-recording-playback Req 3.3（同一構成でのブレンド完全再現）が崩れる。

やること:
- core の観測面に weight 変更イベント（レイヤー weight / 入力源 weight）を追加する
- REC の記録・基準状態・再生時遮断・注入を weight 経路に拡張する（inputsystem の overlay weight 駆動の遮断面を含む）
- `.fcrec` に weight 用レコードを追加する（formatVersion は未リリースのため据え置き可）
- `rec-full-input-coverage` の網羅性ゲートの分類一覧に weight 経路の扱いを反映する
- `rec-full-input-coverage/design.md` と rec README の既知制限「レイヤー weight / 入力源 weight のランタイム変更は記録・遮断されない」を解消する

対象 Unity プロジェクト: `FacialControl/`（パッケージ `com.hidano.facialcontrol` / `com.hidano.facialcontrol.rec` / `com.hidano.facialcontrol.inputsystem`）

## Requirements
<!-- Will be generated in /kiro-spec-requirements phase -->
