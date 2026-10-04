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

実機症状（2026-10-05、Linear HID-137）: InputSystem の `BindingMode.Overlay` トリガー（目閉じ・笑顔の Override）が REC 再生中もライブで効き続け、記録値を上書きする。本 spec の受け入れ条件（Requirement 10.2）に「Overlay モードのトリガーを再生中に引いても表情が変わらない / 録画時のトリガー操作が再生で再現される」を含める。

## Introduction

本機能は、REC（`com.hidano.facialcontrol.rec`）の記録・基準状態捕捉・再生時遮断・注入・`.fcrec` ラウンドトリップの対象に、**レイヤー weight**（レイヤー間ブレンドの inter-layer weight）と**入力源 weight**（レイヤー内 (layer, source) スロットの weight）のランタイム変更を加える。

rec-full-input-coverage（HID-35）は「FacialControl で動く入力は例外なく全て REC 対象」の方針のもと、トリガー型・アナログ / gaze・値提供型・系1 の 4 系統を記録・遮断対象にした。一方、weight 経路は同 spec の対象外として Linear HID-80 へ分離され、次の既知制限が残っている。

- `FacialController.SetLayerWeight`（`LayerUseCase.SetLayerWeight` へ委譲）によるレイヤー weight の変更。inputsystem の overlay binding（`InputSystemAdapterBinding.ApplyOverlayLayerWeights`）は `InputActionAnalogSource` を直参照し、その値で毎 LateTick にレイヤー weight を書き込む。スクリプトからの直接呼び出しも同じ経路に乗る
- `FacialController.SetInputSourceWeight` / `LayerUseCase.SetInputSourceWeight`、および入力源 weight のバルク書込スコープ（`FacialController.BeginInputSourceWeightBatch`）による入力源 weight の変更（任意スレッドから呼出可能で、次回 Aggregate 入口で観測される）

これらは記録されず、再生中もライブ値のまま合成に効く。このため、overlay binding を使う構成や、スクリプトで weight を動かす構成では、rec-recording-playback Req 3.3（同一構成でのブレンド完全再現）が成立しない。また rec-full-input-coverage では `OverlayInputSource` を観測対象に分類したものの、overlay のレイヤー weight はライブのまま残る前提で判定していた。

本 spec は、weight 経路を第 5 の観測系統として rec-full-input-coverage と同じ強度（記録・基準状態・遮断・注入・ラウンドトリップの 5 点すべて成立。部分対応は不可）で扱い、既知制限を解消する。1.0.0 は未リリースのため、`.fcrec` の後方互換は制約としない（`formatVersion` は 1 のまま据え置く）。

既存 spec との関係: rec-recording-playback（観測面・注入面・`.fcrec` の契約オーナー）、rec-playback-input-exclusivity（ライブ遮断面・排他ライフサイクルのオーナー）、rec-full-input-coverage（全入力網羅の方針・入力源分類カタログ・4 ポートの all-or-nothing 確立・`.fcrec` ヘッダ必須 flags）が確立した語彙と設計判断を継承し、その対象を weight 経路へ拡張する。これらの spec 文書のうち「weight 経路はライブのまま残る（HID-80）」とする記述は、本 spec で上書き・修正する。

## Boundary Context

- **対象 Unity プロジェクト**: `FacialControl/`（リポジトリ直下の単一 Unity プロジェクト）。変更対象パッケージは次のとおり
  - `FacialControl/Packages/com.hidano.facialcontrol`（core）: weight 経路の観測面・遮断面・注入面の追加
  - `FacialControl/Packages/com.hidano.facialcontrol.rec`: 記録・基準状態・再生時遮断・注入・`.fcrec` 永続化・途中再生の畳み込み・入力源分類カタログと網羅性ゲートの更新・ドキュメント
  - `FacialControl/Packages/com.hidano.facialcontrol.inputsystem`: Runtime は無改修（overlay binding の weight 駆動は core 側の遮断面で遮断する）。受け入れ検証のテスト追記のみを対象に含む
  - `FacialControl/Packages/com.hidano.facialcontrol.timeline`: Editor の REC Export が新レコード kind を含む `.fcrec` を読めるようにする契約追随のみ
- **In scope**: レイヤー weight と入力源 weight のランタイム変更（呼び出し元・呼び出しスレッドを問わない全書込経路。inputsystem overlay binding の weight 駆動、スクリプトからの直接呼び出し、入力源 weight のバルク書込を含む）の記録、記録開始時点の weight 基準状態の捕捉、再生中のライブ weight 書込の遮断と記録 weight の注入、再生停止時のライブ引き継ぎ、`.fcrec` への weight レコード追加とラウンドトリップ（`formatVersion` 1 据え置き）、途中位置からの再生における weight の畳み込み、timeline REC Export の新 kind 継続読取、入力源分類カタログ・網羅性ゲートへの weight 経路の反映、既存 spec 文書と rec ドキュメントの既知制限の解消
- **Out of scope**: プロファイル定義そのもの（JSON / ScriptableObject）に宣言された既定 weight の編集機能、記録セッション中の `SetProfile` / `LoadCharacter` / `ReloadProfile` による再初期化を跨ぐ完全な記録保証（既存 spec の既知制限を継承）、再生開始後に新規登録された入力源のスロットに対する weight 遮断（開始時スナップショット方式の既知制限を継承）、weight の補間・イージング（weight 変更は既存どおり即時反映であり、本 spec で遷移を導入しない）、レイヤー優先度・レイヤー構成の変更、排他の on/off オプション（常時有効を継承）、ランタイム UI、ボーン経路（`SetActiveBoneSnapshots` 等。既存 spec で Out of Boundary）
- **Adjacent expectations**: rec-recording-playback Req 3.3（ブレンド完全再現）・Req 3.5（停止時のシームレスなライブ引き継ぎ）・Req 3.8（再生開始時の基準状態確立）・Req 6 系（core は rec を知らない / 未使用時の挙動・性能不変 / core 改修は観測面・注入面・遮断面の追加に限定）・Req 8 系（毎フレーム GC ゼロ）・Req 9.1（不整合な記録の安全な扱い）、rec-playback-input-exclusivity Req 1.6（排他は常時有効）・Req 3.4（開始時スナップショット方式）・Req 4.3（StopPlayback を唯一の解放点とする）・Req 4.4（部分的排他状態を定常状態として残さない）、rec-full-input-coverage Req 1（全入力の網羅分類）・Req 3.7（全入力種別の排他確立・解放の一貫順序）・Req 6.4 / 6.5（`formatVersion` 1 据え置き・旧構造非サポート）・Req 7（網羅性ゲート）・Req 8.4（拡張パッケージ無改修の原則）を、weight 経路についても同じ強度で成立させること

## Requirements

### Requirement 1: weight 書込経路の網羅分類

**Objective:** As a ライブラリ開発者, I want レイヤー weight と入力源 weight を変更し得る全てのランタイム書込経路が REC に対して「観測・遮断対象」か「理由付きの明示的除外」のどちらかに分類されていてほしい, so that weight 経路でも「半端な未対応」が残らない

#### Acceptance Criteria

1. The 本機能の設計 shall `FacialControl/Packages/com.hidano.facialcontrol*` 配下の全パッケージの Runtime および Editor アセンブリにおいて、合成に使われるレイヤー weight または入力源 weight をランタイムに変更し得る書込経路（少なくとも `FacialController.SetLayerWeight`、`LayerUseCase.SetLayerWeight`、`FacialController.SetInputSourceWeight`、`LayerUseCase.SetInputSourceWeight`、`FacialController.BeginInputSourceWeightBatch` によるバルク書込、および入力源の後付けバインド・差し替えに伴う weight の書込）を列挙し、それぞれを「観測・遮断対象」または「明示的除外」に分類した一覧を成果物として残す
2. The 本機能の設計 shall 上記経路を呼び出す拡張パッケージ側の呼び出し元（inputsystem の overlay binding による `ApplyOverlayLayerWeights` を含む）を列挙し、各呼び出し元の weight 書込が観測・遮断対象の経路を通ることを示す
3. The 本機能の設計 shall 「明示的除外」に分類した各経路について、除外しても rec-recording-playback Req 3.3 のブレンド完全再現が損なわれない根拠（例: 再生時に記録された weight から同一値が再導出される、REC 自身の注入処理の内部経路である）を文書化する
4. The 本機能 shall 「観測・遮断対象」に分類された全経路について Requirement 2〜7 の記録・基準状態・遮断・注入・ラウンドトリップを成立させ、一部の経路（例: レイヤー weight のみ）だけが成立した状態を完成とみなさない

### Requirement 2: weight 変更の観測面（core）

**Objective:** As a ライブラリ開発者, I want core がレイヤー weight と入力源 weight の変更を観測者へ通知できる観測面を持ってほしい, so that REC が呼び出し元を問わず weight の変化を記録できる

#### Acceptance Criteria

1. The core shall レイヤー weight の変化を、対象レイヤーを一意に識別できる識別子と変化後の値とともに観測者へ通知する観測面を提供する
2. The core shall 入力源 weight の変化を、対象の (layer, source) スロットを一意に識別できる識別子と変化後の値とともに観測者へ通知する観測面を提供する
3. The core の weight 観測面 shall 合成に実際に使われる weight 値（clamp 後の実効値）を通知し、呼び出し元（スクリプト / inputsystem overlay binding / バルク書込 / 任意スレッドからの書込）に関わらず同じ経路で観測される
4. The core の weight 観測面 shall weight の変化をフレームの合成で消費される粒度で観測し、1 フレーム内に同一対象へ複数回書き込まれた場合は合成が実際に使う最終値へ畳む（rec-recording-playback のアナログ消費点サンプリングと同じ意味論。ライブブレンドも同じ値しか見ないため Req 3.3 と整合する）
5. The core の weight 観測面 shall 対象の weight 値が前回通知した値から変化していないとき通知しない（同値の書込が毎フレーム繰り返されても通知が増えない）
6. The core の weight 観測面 shall 入力源スロットの識別子を、同一プロファイル・同一レイヤー設定で再生したときに同じスロットへ対応付けられる安定した形で提供し、REC 自身の注入処理による入力源の差し替え（Replace）の前後でも同一スロットの識別子が変わらないようにする
7. The core の weight 観測面 shall 観測のみを行い、ライブの weight 値・合成パイプライン・表情出力に一切書き戻さない（rec-recording-playback Req 2.5 の読取専用契約を継承）

### Requirement 3: weight 変更の記録

**Objective:** As a Unity エンジニア, I want レイヤー weight と入力源 weight の変化が他の入力と同じ時系列で記録されてほしい, so that overlay のトリガー押し量やスクリプトによる weight 操作を含むパフォーマンスを記録どおりに再現できる

#### Acceptance Criteria

1. While 記録セッションが有効な間, when レイヤー weight が変化したとき, the REC 記録サービス shall 対象レイヤーの識別子と変化後の値を、記録開始起点の相対秒とともに記録する
2. While 記録セッションが有効な間, when 入力源 weight が変化したとき, the REC 記録サービス shall 対象スロットの識別子と変化後の値を、記録開始起点の相対秒とともに記録する
3. While 記録セッションが有効な間, when weight が前フレームから変化していないとき, the REC 記録サービス shall 当該対象について新たな記録を追加しない（無変化フレームで記録量が増えない）
4. The REC 記録サービス shall weight 値を正規化・量子化せず、観測された実効値をそのまま記録する（記録値と合成に使われた値が float ビット単位で一致する）
5. While 再生中に記録セッションが有効な間, when REC の注入経路によって weight が変化したとき, the REC 記録サービス shall 当該変化を他の注入イベントと同様に記録する（rec-full-input-coverage の「再生中の記録には注入イベントが残る」規則を weight にも適用する）

### Requirement 4: weight の基準状態

**Objective:** As a Unity エンジニア, I want 記録開始時点のレイヤー weight と入力源 weight が基準状態として記録され、再生開始時に確立されてほしい, so that 再生フレーム 0 から収録時と同じ weight でブレンドが成立する

#### Acceptance Criteria

1. When 記録が開始されたとき, the REC 記録サービス shall 現在のプロファイルの全レイヤーについて開始時点のレイヤー weight を基準状態として記録する（ランタイムに一度も変更されていないレイヤーも、その時点の実効値を記録する）
2. When 記録が開始されたとき, the REC 記録サービス shall 開始時点に存在する全ての (layer, source) スロットについて開始時点の入力源 weight を基準状態として記録する
3. The weight の基準状態レコード shall 最初の時刻付きイベントより前に出現する（rec-recording-playback の `.fcrec` 不変条件を weight の基準種別にも適用する）
4. When 再生が開始されたとき, the REC 再生サービス shall 記録されたレイヤー weight と入力源 weight の基準状態を、時系列イベントの発火を開始する前に確立する
5. When 再生が開始されたとき and 記録の基準状態に含まれないレイヤーまたは入力源スロットがライブに存在するとき, the REC 再生サービス shall 当該対象の weight を確定的な値に確立する（ライブの残存 weight を引き継がない。確定値の方式は既存の初期状態規則との整合を含めて設計フェーズで決定し、本要件では固定しない）
6. The REC 再生サービス shall weight 基準状態の確立を観測者へ通知しない（基準確立は操作イベントではない。rec-recording-playback の `ResetToExpressionStack` の規則を継承）

### Requirement 5: 再生中のライブ weight 書込の遮断

**Objective:** As a Unity エンジニア, I want REC 再生中はコントローラの overlay 押し量やスクリプトによるライブの weight 書込が合成に反映されないでほしい, so that 再生中にライブの weight が混入せず、収録時と同一のブレンドが得られる

#### Acceptance Criteria

1. The core shall レイヤー weight と入力源 weight のライブ書込を遮断する面（遮断の開始・解除。それぞれ冪等）を提供する
2. While weight の遮断が有効な間, when いずれかの呼び出し元（inputsystem の overlay binding を含む）からライブのレイヤー weight 書込が行われたとき, the core shall 当該書込を合成に反映させず、観測者へ通知しない
3. While weight の遮断が有効な間, when ライブの入力源 weight 書込（単発書込・バルク書込のいずれも、メインスレッド以外からの書込を含む）が行われたとき, the core shall 当該書込を合成に反映させず、観測者へ通知しない
4. The core の weight 遮断面 shall inputsystem パッケージを含む拡張パッケージの改修を伴わずに、拡張パッケージからの weight 書込を遮断できる（inputsystem の overlay binding が `InputActionAnalogSource` を直参照して行う weight 駆動を、core 側で遮断する）
5. When 再生が開始されたとき, the REC 再生サービス shall weight の遮断を確立してから weight の基準状態を確立する（遮断前のライブ書込が基準状態を上書きしない）
6. When 再生停止（StopPlayback）が指示されたとき, the REC 再生サービス shall weight の遮断を解除し、レイヤー weight と入力源 weight を解除時点の値のまま維持する（自動復元なし。以後のライブ書込が通常どおり反映される。rec-recording-playback Req 3.5 を weight へ拡張）
7. When 再生が記録の最終イベントに到達して自然完了（Completed）したとき, the REC 再生サービス shall weight の遮断を解除せず維持する（StopPlayback を唯一の解放点とする。rec-playback-input-exclusivity Req 4.3 を継承）
8. The 入力排他 shall weight の遮断を再生中は常時有効とし、weight の遮断だけを無効化する設定・オプションを提供しない（rec-playback-input-exclusivity Req 1.6 を継承）

### Requirement 6: 記録 weight の注入と再生ライフサイクルへの統合

**Objective:** As a Unity エンジニア, I want 記録された weight の変化がライブと同一の合成経路へ時系列どおりに注入されてほしい, so that 収録時の weight の動きを含めてブレンドが完全再現される

#### Acceptance Criteria

1. The core shall weight の遮断を迂回する注入経路を提供し、注入経路経由のレイヤー weight / 入力源 weight の書込をライブ書込と同一の合成処理（clamp・反映タイミングを含む）で受理し、観測者へ通知する
2. While 再生中, when 記録された weight 変化イベントのタイムスタンプに到達したとき, the REC 再生サービス shall 注入経路経由で当該 weight を書き込む（レイヤー合成はライブと同一コードパスで実行される）
3. The REC 再生サービス shall 再生中に REC 自身が行う入力源の差し替え（値提供型・アナログ注入の Replace）とその停止時の原本復元によって、基準状態または注入で確立した入力源 weight を変化させない（差し替えに伴う weight の再書込が記録値を上書きしない）
4. The REC 再生サービス shall weight の遮断・注入の確立と解放を、トリガー・系1・アナログ / gaze・値提供型の 4 種と一貫した順序で行い、all-or-nothing の再生開始（確立途中で失敗した場合は確立済みの排他をすべて解放する）に weight を含める（rec-full-input-coverage Req 3.7 を 5 種へ拡張。部分的な排他状態を定常状態として残さない）
5. If 再生開始時点で weight の遮断・注入に必要な依存が解決できないとき（FacialController 未初期化等）, the REC 再生サービス shall 再生を開始せず、Unity 標準ログでエラーを出し、どの入力種別の排他も確立しない
6. If 記録された weight イベントまたは基準状態が、現在のプロファイルに存在しないレイヤーまたは入力源スロットを参照しているとき, the REC 再生サービス shall 再生全体を停止させずに当該イベントをスキップし、識別子ごとに 1 回だけ警告を出す（rec-recording-playback Req 9.1 を weight へ拡張）
7. While 再生中, when 遮断対象外の経路（再生開始後に新規登録された入力源のスロット）で weight が書き込まれたとき, the REC 再生サービス shall 開始時スナップショット方式の既知制限として当該書込を遮断しない（rec-playback-input-exclusivity Req 3.4 を継承）

### Requirement 7: `.fcrec` の weight レコードとラウンドトリップ

**Objective:** As a ライブラリ開発者, I want weight の時刻付きイベントと基準状態が `.fcrec` に書き出され、読み戻したときに記録時と同一に復元されてほしい, so that 記録の正本性が weight 経路でも維持される

#### Acceptance Criteria

1. The `.fcrec` 永続化 shall レイヤー weight と入力源 weight の時刻付き変化イベント、および両者の基準状態を書き出し・読み戻しできる
2. When 記録を書き出して読み戻したとき, the REC 永続化 shall weight イベントの種別・順序・タイムスタンプ・対象識別子・値（float ビット単位）と weight の基準状態を記録時と同一に復元する
3. The `.fcrec` 永続化 shall `formatVersion` を 1 のまま据え置き、weight のためのレコード追加を版分岐なしで行う（v1 は未リリースであり、本 spec 実装後の構造が `formatVersion` 1 の唯一の定義となる。rec-full-input-coverage Req 6.4 を継承）
4. If 本 spec 以前の記録構造で書かれた `.fcrec`（weight の基準状態を持たない構造）を読み込もうとしたとき, the REC 永続化 shall 読込互換・移行を提供せず、読込エラーとして Unity 標準ログで通知し再生を開始しない（「weight の基準が無いまま成功したように見える再生」を起こさない。判別方式は設計フェーズで決定する）
5. The REC 永続化 shall weight を含まない記録（記録中に weight が一度も変化しなかった記録）でも weight の基準状態を書き出し、読み戻した基準状態から再生開始時の weight を確立できる
6. When 途中位置からの再生（`StartPlayback` のオフセット指定、`RecTimelineSeek.BuildBaselineAt`）が指示されたとき, the REC 再生サービス shall weight イベントを基準状態へ畳み込み、オフセット時点の各レイヤー weight と各入力源 weight の最終値を基準として確立する
7. The timeline パッケージの REC Export（Editor）shall weight のレコード kind を含む `.fcrec` ファイルの読込に失敗しない（Export 対象としない kind は無視してよい）
8. The core のリーダー（`RecBinaryFormat` 等）shall 未知のレコード kind を黙ってスキップしない（rec-full-input-coverage の既存規則を維持し、無視を許すのは timeline REC Export のみとする）

### Requirement 8: 入力源分類カタログと網羅性ゲートへの反映

**Objective:** As a ライブラリ開発者, I want rec-full-input-coverage の入力源分類一覧と網羅性ゲートが weight 経路の扱いを正しく反映していてほしい, so that 分類の正本と実装が一致し、weight 経路が再び未対応になったときに機械的に気付ける

#### Acceptance Criteria

1. The 入力源分類の正本（`RecInputSourceCoverageCatalog` と `.kiro/specs/rec-full-input-coverage/design.md` の入力源分類表）shall weight 経路を観測対象の系統として扱う旨を反映し、「weight 経路はライブのまま残る（HID-80）」を前提・根拠とする記述（分類表の前提文、`OverlayInputSource` の根拠、`InputActionAnalogSource` の許容 referrer 理由等）を本 spec 実装後の実態に合わせて更新する
2. The 入力源分類の正本 shall Requirement 1 で列挙した weight 書込経路の分類（観測・遮断対象 / 明示的除外と理由）を、利用者と開発者が参照できる形で保持する（保持形式は設計フェーズで決定する）
3. The 網羅性ゲートテスト shall 分類一覧に記載された weight 書込経路が実在しない（削除・改名済みの陳腐化した記載がある）とき失敗する
4. The 網羅性ゲートテスト shall 既存の検査（`IInputSource` / `IAnalogInputSource` 単独実装の分類漏れ・二重分類・陳腐化・空理由・アセンブリの双方向包含・除外区分ごとの契約）を緑のまま維持する
5. The 網羅性ゲートテスト shall Unity のシーン・MonoBehaviour ライフサイクル・ファイル I/O を使わず EditMode で実行でき、`docs/testing.md` のテストサイズ規約に従ったサイズ属性を持ち、push / PR ごとの CI で自動実行される

### Requirement 9: core 制約・性能・パッケージ境界の維持

**Objective:** As a ライブラリ開発者, I want weight 経路への対応を既存 spec の制約群の範囲内で実現したい, so that core の独立性・既存挙動・GC ゼロ目標・パッケージ分離を守ったまま REC の対象を拡張できる

#### Acceptance Criteria

1. The core 改修 shall weight 経路の観測面・遮断面・注入面の追加に限定し、既存コードパスの挙動を変更しない（rec-recording-playback Req 6.1 の範囲内）
2. If weight の観測者が未登録で、weight の遮断・注入のいずれも使用されていないとき, the core shall weight 書込経路の既存の挙動・性能を変更しない（rec-recording-playback Req 6.5 を維持。任意スレッドから呼出可能という入力源 weight 書込の既存契約も維持する）
3. The core shall rec パッケージへの依存を持たない（rec-recording-playback Req 6.6 を維持）
4. The 本機能 shall inputsystem / osc / lipsync / ifacialmocap / timeline の Runtime を改修せずに weight 経路の記録・遮断・注入を成立させる（timeline Editor の REC Export の契約追随と、各パッケージのテスト追記を除く。rec-full-input-coverage Req 8.4 の原則を継承）
5. While 記録中または再生中, the core および REC shall weight の観測・記録・遮断・注入の毎フレーム定常処理でヒープ確保を発生させない
6. The core および REC shall 同時 10 体のキャラクターがそれぞれ weight を毎フレーム変化させながら記録・再生する構成でも、キャラクターごとに独立した観測・遮断・注入でスケールする（rec-recording-playback の per-FC スコープを維持し、キャラクター間で weight の状態を共有しない）
7. The 本機能 shall エラーハンドリングを Unity 標準ログ（`Debug.Log/Warning/Error`）のみで行い、カスタム例外型を追加しない

### Requirement 10: 受け入れ検証

**Objective:** As a ライブラリ開発者, I want weight 経路の記録→再生の完全再現と再生中の遮断が自動テストで固定されてほしい, so that weight 対応が「宣言」ではなく「検証済み」の状態で完成する

#### Acceptance Criteria

1. The 本機能の受け入れテスト shall レイヤー weight と入力源 weight の変化を含む操作列を記録→停止→読込→再生したとき、ブレンド出力（`BlendedOutputSpan`）が再生フレーム 0 から収録時と一致することを検証する（同一プロファイル・同一レイヤー設定）
2. The 本機能の受け入れテスト shall inputsystem の overlay binding がレイヤー weight を駆動する構成について、記録→再生でブレンド出力が一致し、再生中のライブの overlay 入力がレイヤー weight とブレンド出力に反映されないことを検証する
3. The 本機能の受け入れテスト shall 再生中のスクリプトからの `SetLayerWeight` / `SetInputSourceWeight` / バルク書込がブレンド出力に反映されないことを検証する
4. The 本機能の受け入れテスト shall 再生停止後に weight が停止時点の値で維持され、その後のライブ書込が反映されることを検証する
5. The 本機能の受け入れテスト shall 記録開始時点で既定値以外の weight が設定されている構成で、再生開始時に基準状態の weight が確立され、ライブの残存 weight が引き継がれないことを検証する
6. The 本機能の受け入れテスト shall 再生中の REC 自身による入力源の差し替えと停止時の原本復元の前後で、入力源 weight が記録値・停止時点の値から変化しないことを検証する（Requirement 6.3）
7. The 本機能の受け入れテスト shall weight レコードを含む `.fcrec` のラウンドトリップ、途中位置からの再生における weight の基準確立、および本 spec 以前の構造のファイルの読込拒否を検証する
8. The 本機能の受け入れテスト shall weight 経路を含む 5 種の排他確立において、確立途中の失敗で部分的な排他状態が残らないことを検証する（Requirement 6.4）
9. The 本機能の受け入れテスト shall core の weight 観測・遮断・注入ロジックと REC の記録・再生・永続化ロジックを Fake のみで EditMode 検証できる構造とし、MonoBehaviour ライフサイクル・フレーム進行・InputSystem・実 I/O を要する統合検証のみ PlayMode（または該当サイズ）に配置する
10. The 本機能 shall 既存の EditMode / PlayMode テストスイート（rec-recording-playback・rec-playback-input-exclusivity・rec-full-input-coverage の受け入れテストと `FacialControllerGcZeroGateTests` / `RecGcZeroGateTests` 等の GC ゲートを含む）を緑のまま維持する

### Requirement 11: 既知制限の解消と文書整合

**Objective:** As a ライブラリ利用者・開発者, I want 「weight は記録・遮断されない」という既知制限の記述が解消され、REC の対象範囲に関する文書が実装と一致していてほしい, so that weight 経路について誤った前提で運用・設計しない

#### Acceptance Criteria

1. The 本機能 shall `.kiro/specs/rec-full-input-coverage/design.md` の Non-Goals・Out of Boundary・直参照経路の記述・入力源分類表の前提文・既知制限 1 にある「レイヤー weight / 入力源 weight のランタイム変更は記録も遮断もされない（HID-80）」旨の記述を、本 spec（rec-weight-coverage）により上書きされた旨が分かる形で修正する
2. The 本機能 shall `.kiro/specs/rec-full-input-coverage/requirements.md` のうち weight 経路を対象外・既知制限とする記述（Boundary Context の Out of scope、Req 1.3 の前提、Req 10.7）に、本 spec で上書きされた旨を付記する
3. The 本機能 shall `.kiro/specs/rec-recording-playback/design.md` のうち「残る未到達は inputsystem overlay binding の layer weight 駆動のみ（HID-80）」等、weight 経路が未到達であるとする記述を、本 spec で上書きされた旨が分かる形で修正する
4. The rec パッケージのドキュメント（`FacialControl/Packages/com.hidano.facialcontrol.rec/README.md` / `Documentation~/README.md`）shall 既知制限「レイヤー weight / 入力源 weight のランタイム変更は記録も遮断もされない（Linear HID-80）」を削除する
5. The rec パッケージのドキュメント shall 記録される内容の一覧に weight 系統（レイヤー weight / 入力源 weight）を追加し、`.fcrec` のレコード kind 表に weight 用 kind を追加し、再生中の入力遮断の節に weight の遮断・基準確立・注入・停止時の維持を記載する
6. The rec パッケージのドキュメント shall 開始時スナップショット方式（再生開始後に新規登録された入力源のスロットの weight は遮断対象外）が weight にも適用される既知制限として記載する
7. The timeline パッケージのドキュメント（REC Export に関する節）shall weight のレコード kind を Export 対象としない場合、それを無視する旨を記載する
