# Product Overview

FacialControl は、3D キャラクターの表情をリアルタイムに制御する **Unity 向け開発者アセット**（ライブラリ）。Unity Package Manager（UPM）経由で配布し、最終的には npmjs.com の `com.hidano` スコープに公開する。エンドユーザー向けツールではなく、Unity エンジニアが組み込むためのライブラリである。

- **パッケージ ID**: `com.hidano.facialcontrol`（コア） / `com.hidano.facialcontrol.osc`（OSC 拡張） / `com.hidano.facialcontrol.inputsystem`（InputSystem 連携） / ほか `lipsync` / `ifacialmocap` / `rec` / `timeline` と、Editor ツールのみの `expression-creator` / `routing-editor`
- **ライセンス**: MIT
- **開発体制**: 2 名（Hidano: メイン開発、Junki Hiroi: レビュー・サポート）

## Core Capabilities

1. **プロファイルベースの表情管理** — 表情を「プロファイル」で抽象化（基本 AnimationClip + カテゴリ + リップシンク用 Clip + 遷移時間）。表情制御は BlendShape + ボーン + テクスチャ切り替え + UV アニメの組み合わせ。
2. **マルチレイヤー合成** — デフォルト 3 レイヤー（感情 / リップシンク / 目）。レイヤー優先度はユーザー設定可能。カテゴリ内排他は **LastWins** と **Blend** から選択。
3. **ハイブリッド入力モデル（D-1）** — `ExpressionTrigger`（バイナリのスタックベース）と `ValueProvider`（直接値書き込み）を Aggregator が weighted-sum + clamp01 で合成。
4. **OSC ネットワーク伝送** — UDP + uOsc を必須依存として同梱。VRChat OSC と完全互換（`/avatar/parameters/{name}` 形式）。BlendShape 単位で個別メッセージを送受信、1 フレームに複数回可能。
5. **JSON ファースト永続化 + Editor 拡張** — コア表情データは JSON。Unity 向けに ScriptableObject へ変換可能。FacialProfileSO Inspector / AnimationClip 作成支援ウィンドウ等を UI Toolkit で提供（ランタイム UI は提供しない）。

## Target Use Cases

- **VTuber 配信向けフェイシャルキャプチャ連動** — ARKit 52 / PerfectSync を初回プレリリースから完全対応。自動検出 + プロファイル自動生成、未対応パラメータは警告なしでスキップ。
- **GUI エディタでの AnimationClip 作成支援** — 専用プレビューウィンドウで BlendShape スライダー操作、JSON インポート/エクスポート、Expression の追加・編集・削除・検索を Inspector で一元管理。
- **ゲーム / アプリ内のリアルタイム表情制御** — 同時 10 体以上のキャラクター制御を想定。Animator ベースのリアルタイム制御（Timeline 統合は将来対応）。

## Value Proposition

- **配布性**: UPM パッケージとして配布。コア / OSC / InputSystem を分離し、利用者が必要な機能だけ取り込める。
- **拡張性**: クリーンアーキテクチャ（Domain/Application/Adapters）と asmdef による依存方向の強制で、レンダーパイプライン・シェーダー・物理演算に依存しない**契約**を維持。OSC 以外の通信プロトコル / 独自入力ソース / Jobs+Burst 最適化への差し替えがインターフェース経由で可能。
- **互換性**: VRChat OSC 完全互換、ARKit/PerfectSync 完全対応、ブレンドシェイプ命名規則を固定しない（2 バイト文字・特殊記号 OK）。
- **品質**: TDD（Red-Green-Refactor）厳守、毎フレームのヒープ確保ゼロを目標とする性能設計。

## Release Plan

機能単位でのプレリリース連番（`preview.1` → `preview.2` → … → `1.0.0`）。

- **プレリリーススコープ**: コア + Editor 拡張 + OSC 通信 + ARKit/PerfectSync 完全対応
- **同梱物**: ドキュメント + `com.hidano.facialcontrol.inputsystem` の `Multi Source Blend Demo` サンプル一式（モデルはユーザー持ち込み）
- **当初目標**: 2026 年 2 月末プレリリース → **現状（2026-04-28 時点）マイルストーン超過**。再見積りが必要。
- **対象プラットフォーム**: 現状 Windows PC のみ。モバイル / WebGL / VR は将来拡張のため設計上の余地のみ確保。

---
_Focus on patterns and purpose, not exhaustive feature lists_
