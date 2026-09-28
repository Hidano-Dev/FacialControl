# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

FacialControl は、3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリ（開発者向けアセット）。npmjs.com へのリリースを想定。主なユースケースは VTuber 配信用フェイシャルキャプチャ連動と、GUI エディタでの AnimationClip 作成支援。ターゲットユーザーは Unity エンジニア。

## 重要なドキュメント

- **QA シート**: `docs/requirements-qa.md` — プロジェクト要件の詳細な Q&A。実装判断に迷った場合はここを参照
- **要件定義**: `docs/requirements.md`
- **作業手順書**: `docs/work-procedure.md` — 実装作業のフェーズ・タスク分解。「作業手順書」と呼ばれたらこのファイルを参照
- **Backlog**: `docs/backlog.md` — 「別 PR ネタ」「preview.2 以降」「別 spec で対処」と先送りされたタスクの集約先。HANDOVER.md の優先度低項目はここへ昇格させる
- **テスト方針**: `docs/test-policy.md` — 何を守るテストを残すかの分類（A〜F）と判定基準。テストを書く・直す・消すときはここに従う
- **テストサイズ**: `docs/testing.md` — Small / Medium / Large の定義、`[SmallTest]` 等の書き方、CI での回し方、Small で書けないときの Humble Object 対処。導入時の全テスト分類は `docs/test-size-migration.md`
- **Copilot 指示**: `.github/copilot-instructions.md`

## 開発環境

- **Unity**: 6000.3.19f1 (Unity 6)。Editor 実体は `D:/UnityEditors/6000.3.19f1/Editor/Unity.exe`（他バージョンでの batchmode 実行禁止 — `ProjectVersion.txt` が書き換わり全リインポートが走る）
- **レンダリング**: URP v17.3.0（PC / モバイル別設定あり）
- **カラースペース**: Linear
- **Unity プロジェクトルート**: `FacialControl/` ディレクトリ配下

## 主要な依存パッケージ

| パッケージ | 用途 |
|-----------|------|
| `com.unity.inputsystem` (1.17.0) | 入力デバイスの動的切り替え |
| `com.unity.timeline` (1.8.9) | タイムラインアニメーション |
| `com.unity.test-framework` (1.6.0) | Edit Mode / Play Mode テスト |
| `jp.lilxyzw.liltoon` | トゥーンシェーダー |
| `com.mikunote.magica-cloth-2` | クロスシミュレーション |

外部パッケージ（lilToon、MagicaCloth2）は Git リポジトリから SSH 経由で取得される。

## 開発コマンド

### テスト実行
```bash
# テストサイズ静的チェック（Unity 不要。サイズ未宣言・Small の禁止 API・Small asmdef の参照を検査）
pwsh ./scripts/check-test-sizes.ps1

# Small テスト（EditMode、PR ごとに CI で実行）
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform EditMode -testCategory Small \
    -testResults ./test-results/small-editmode.xml

# Medium テスト（EditMode / PlayMode 両方。マージ前に CI で実行）
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform EditMode -testCategory Medium \
    -testResults ./test-results/medium-editmode.xml
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform PlayMode -testCategory Medium \
    -testResults ./test-results/medium-playmode.xml

# 全 EditMode / PlayMode（サイズを問わず）
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform EditMode \
    -testResults ./test-results/editmode.xml
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform PlayMode \
    -testResults ./test-results/playmode.xml
```

## アーキテクチャ方針

### コア設計

- **クリーンアーキテクチャ**: Domain / Application / Adapters / Presentation のレイヤー分離。Unity 依存を Adapters 層に封じ込め
- **プロファイルベースの表情管理**: 表情は「プロファイル」で抽象化。各プロファイルは基本 AnimationClip + カテゴリ属性 + リップシンク用 AnimationClip + 遷移時間を保持
- **JSON ファーストの永続化**: コア機能は JSON フォーマット。Unity 向けオプションとして ScriptableObject に変換。ビルド後も JSON で表情設定を差し替え可能にする。preview 段階では破壊的変更を許容
- **ランタイム JSON パース**: JSON → ScriptableObject 変換はランタイム機能。Asset ファイル保存のみ Editor ツール
- **マルチレイヤー構成**: デフォルト 3 レイヤー（感情ベース / リップシンク / 目）。レイヤー優先度はユーザー設定可能。カテゴリ内排他は「後勝ち」と「ブレンド」を選択可能
- **ネットワーク伝送**: UDP + uOsc（必須依存）。VRChat OSC 完全互換（`/avatar/parameters/{name}` 形式）。BlendShape 単位で個別 OSC メッセージ送受信。1 フレーム間に複数回送受信

### 表情制御方式

ブレンドシェイプ + ボーン + テクスチャ切り替え + UV アニメーションの組み合わせ。テクスチャ切り替えと UV アニメーションは AnimationClip 内で定義。表情遷移は線形補間がデフォルト（イージング/カスタムカーブで上書き可能）。遷移時間は 0〜1 秒（デフォルト 0.25 秒）。遷移中に新しい表情がトリガーされた場合は、現在の補間値から即座に新遷移を開始。

### 対応フォーマット

- FBX: プロトタイプから標準対応
- VRM: リリース後の早期マイルストーン
- ブレンドシェイプ命名規則は固定しない（2 バイト文字・特殊記号を正しく扱う）
- ARKit 52 / PerfectSync: 初回プレリリースから完全対応。自動検出+プロファイル自動生成。未対応パラメータは警告なしでスキップ

### パッケージ情報

- **パッケージ名**: `com.hidano.facialcontrol`
- **C# 名前空間**: `Hidano.FacialControl`（例: `Hidano.FacialControl.Domain`）
- **ライセンス**: MIT
- **uOsc**: 必須依存パッケージとして同梱

### ディレクトリ構成（レイヤー別）
```
Runtime/
├── Domain/             # ドメインロジック（Unity 非依存）
├── Application/        # ユースケース
└── Adapters/           # Unity 依存の実装（JSON パーサー、OSC アダプター等）
Editor/                 # Editor 拡張（UI Toolkit）
```
各レイヤーは asmdef で依存方向を強制する。

### Editor 拡張

- Inspector カスタマイズ（FacialProfileSO Inspector でプロファイル管理を一元化: Expression の追加・編集・削除・検索、JSON インポート/エクスポート、新規プロファイル作成）
- AnimationClip 作成支援ツール（専用プレビューウィンドウで BlendShape スライダー操作）
- UI Toolkit で実装。ランタイム UI は提供しない

### 入力システム

- デフォルトの InputSystem バインディング同梱（コントローラ + キーボード）
- InputAction Asset の差し替えによるカスタマイズ可能
- 入力インターフェースの抽象化により独自実装に差し替え可能

### リップシンク

- FacialControl はリップシンク用レイヤーの管理のみ提供
- 外部プラグイン（uLipSync 等）からの入力を受けるインターフェースを提供
- 音声解析はスコープ外

## 開発方針

### TDD（テスト駆動開発）
```
Red-Green-Refactorサイクル:
1. Red:    失敗するテストを書く
2. Green:  テストを通す最小限のコードを書く
3. Refactor: リファクタリング（テストは緑を維持）
```

**原則**:
- テストファースト: 実装前にテストを書く
- 小さなステップ: 1つのテストで1つの振る舞いを検証
- モックは最小限: 外部境界（I/O、ネットワーク）のみモック化
- FIRST原則: Fast, Independent, Repeatable, Self-validating, Timely

### 設計原則

- 単一責任の原則に従う
- PR ベースの開発フロー（2 名体制: Hidano がメイン開発、Junki Hiroi がレビュー・サポート）
- 対象プラットフォームは現状 Windows PC のみ。モバイル・WebGL・VR は将来拡張の余地を残す設計とする
- OSC 以外の通信プロトコルもインターフェースで抽象化し将来拡張可能にする
- レンダーパイプライン非依存の設計
- シェーダー非依存の設計（lilToon 等は開発環境のみ）
- 物理演算非依存の設計（MagicaCloth2 は開発環境のみ）
- エラーハンドリングは Unity 標準ログ（Debug.Log/Warning/Error）のみ
- Timeline 統合は将来対応。初回は Animator ベースのリアルタイム制御

### パフォーマンス設計指針

- 毎フレームのヒープ確保を避ける（GC スパイク対策）
- 浮動小数点は `float` 基本
- UDP 送受信はメインスレッド非依存
- JSON パース負荷を抑えるデータ構造

## 開発規約

### コーディングスタイル
- C#、4スペースインデント、改行時に中括弧
- 日本語で応答・コメント・ドキュメントを記述
- 明示的な `public` / `private` を推奨
- クラス / 構造体 / enum: PascalCase
- インターフェース: `I` プレフィックス
- プライベートフィールド: `_camelCase`

### テスト命名規則
- クラス名: `{対象クラス}Tests`
- メソッド名: `{メソッド}_{条件}_{期待結果}`
- 例: `SetProfile_ValidJson_ReturnsProfileWithCorrectBlendShapes`

### テストサイズ（Small / Medium / Large）
- すべての fixture クラスに `[SmallTest]` / `[MediumTest]` / `[LargeTest]`（`Hidano.FacialControl.Testing`）を 1 つ付け、基底クラスがなければ `SizedTestFixture` を継承する
- Small はシーン・MonoBehaviour ライフサイクル・UnityWebRequest・PlayerPrefs・ファイル I/O・Resources/AssetDatabase・WaitForSeconds・Time.* 直接参照を禁止。守れないなら Medium にする
- 詳細と Small で書けないときの対処は `docs/testing.md`

### テストフォルダ構造
```
Tests/
├── Small/              # Domain / Application のみに依存する Small（最小参照の asmdef）
├── Testing/            # サイズ属性・SizedTestFixture（engine 非依存、core パッケージのみ）
├── EditMode/           # PlayMode不要なテスト（単体・Fake統合）。Small と EditMode の Medium が混在
│   ├── Domain/         # プロファイル、ブレンドシェイプ等のドメインロジック
│   ├── Application/    # ユースケーステスト
│   └── Adapters/       # リポジトリ、JSONパーサー等
├── PlayMode/           # PlayMode必須なテスト（実通信・性能）
│   ├── Integration/
│   └── Performance/
└── Shared/             # EditMode/PlayMode共用（Fakes等）
```

### テスト配置基準（EditMode vs PlayMode）

**配置はテストカテゴリ（単体/統合）ではなく、実行時要件で決定する。**

| 配置先 | 基準 | 例 |
|--------|------|-----|
| EditMode | モック・Fakeのみ、同期実行、PlayMode機能不要 | JSONパーステスト、プロファイル変換テスト |
| PlayMode | MonoBehaviourライフサイクル、コルーチン、実UDP/OSC通信、フレーム同期が必要 | 表情遷移の補間テスト、OSC送受信テスト |

## 品質基準

### 性能要件
| 指標 | 基準 |
|------|------|
| GCアロケーション | 毎フレーム処理でゼロ目標 |
| 表情遷移 | 線形補間 0〜1秒対応 |
| ネットワーク | 1フレーム間に複数回UDP送受信可能 |
| プリセット上限 | ユーザープリセット最大512（ペイロード可変） |
| 同時キャラクター | 10体以上の同時制御を想定 |
| 最適化 | プレリリースは通常C#。インターフェース設計でJobs/Burst差し替え可能 |

### CI/CD
- GitHub Actions + セルフホストランナー（Windows マシン）
- TDD 厳守（Red-Green-Refactor）。カバレッジ数値目標は設定しない

### リリース計画
- 機能単位リリース: preview.1 → preview.2 → ... → 1.0.0
- 2026 年 2 月末までにプレリリース目標
- プレリリーススコープ: コア + Editor 拡張 + OSC 通信 + ARKit/PerfectSync 完全対応
- プレリリース同梱物: ドキュメント + `com.hidano.facialcontrol.inputsystem` の `Multi Source Blend Demo` サンプル（Scene / FacialProfileSO / InputBindingProfileSO / JSON / HUD 一式。モデルはユーザー持ち込み）

## Claude Code 実行ルール

- Unity テストランナーは `run_in_background` を使わず、`timeout: 600000` の同期 Bash 呼び出しで実行する
- `tasks.txt` は作業手順書（`docs/work-procedure.md`）に記載のタスク ID のみを列挙するファイルである。ターミナルから for 文で連続実行するために使用する。タスクの説明や詳細を `tasks.txt` に直接追記してはならない。タスクの追加・変更は必ず `docs/work-procedure.md` に記載し、`tasks.txt` には ID のみを転記する
- 同じ原因仮説を 2 回外したら、推測を続けず実機ログ/データ取得に切り替えて一度ユーザーに確認する

## 重要な注意事項

### ファイル管理
- `.meta` ファイルは常にアセットと共に管理
- 生成されたバイナリやログはコミット禁止
- `Library/Temp/obj/UserSettings` は触らない

### パッケージ管理
- `FacialControl/Packages/manifest.json` でパッケージ更新
- `packages-lock.json` を同期維持

### Samples 配置ルール
- `Packages/com.hidano.facialcontrol*/Samples~/` が UPM 配布用の canonical なサンプル置き場。`package.json` の `samples` 配列に登録されたもののみが Package Manager から Import 可能
- `Assets/Samples/` は使用しない（過去の dev 動作確認用 mirror を削除済み）。動作確認は `Samples~` を直接編集し、必要に応じて Package Manager の Import Sample 経由で `Assets/Samples/` に展開する
- サンプル Scene にはユーザーモデルを同梱しない。README で「モデル持ち込み」方式を案内する（`Multi Source Blend Demo` / `OscOutputDemo` / `OscReceiverDemo` 共通）

### バージョン管理
- 短縮系命令形コミットメッセージ（日本語可）
- 例: "表情プロファイルのJSON読み込み機能を追加"


# Agentic SDLC and Spec-Driven Development

Kiro-style Spec-Driven Development on an agentic SDLC

## Project Context

### Paths
- Steering: `.kiro/steering/`
- Specs: `.kiro/specs/`

### Steering vs Specification

**Steering** (`.kiro/steering/`) - Guide AI with project-wide rules and context
**Specs** (`.kiro/specs/`) - Formalize development process for individual features

### Active Specifications
- Check `.kiro/specs/` for active specifications
- Use `/kiro:spec-status [feature-name]` to check progress

## Development Guidelines
- Think in English, generate responses in Japanese. All Markdown content written to project files (e.g., requirements.md, design.md, tasks.md, research.md, validation reports) MUST be written in the target language configured for this specification (see spec.json.language).

## Minimal Workflow
- Phase 0 (optional): `/kiro:steering`, `/kiro:steering-custom`
- Phase 1 (Specification):
  - `/kiro:spec-init "description"`
  - `/kiro:spec-requirements {feature}`
  - `/kiro:validate-gap {feature}` (optional: for existing codebase)
  - `/kiro:spec-design {feature} [-y]`
  - `/kiro:validate-design {feature}` (optional: design review)
  - `/kiro:spec-tasks {feature} [-y]`
- Phase 2 (Implementation): `/kiro:spec-impl {feature} [tasks]`
  - `/kiro:validate-impl {feature}` (optional: after implementation)
- Progress check: `/kiro:spec-status {feature}` (use anytime)

## Development Rules
- 3-phase approval workflow: Requirements → Design → Tasks → Implementation
- Human review required each phase; use `-y` only for intentional fast-track
- Keep steering current and verify alignment with `/kiro:spec-status`
- Follow the user's instructions precisely, and within that scope act autonomously: gather the necessary context and complete the requested work end-to-end in this run, asking questions only when essential information is missing or the instructions are critically ambiguous.

## Steering Configuration
- Load entire `.kiro/steering/` as project memory
- Default files: `product.md`, `tech.md`, `structure.md`
- Custom files are supported (managed via `/kiro:steering-custom`)

@.claude/rules/sdd-workflow.md
