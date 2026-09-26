# Technology Stack

## Architecture

**クリーンアーキテクチャ**を採用し、Unity 依存を Adapters 層に封じ込める。各レイヤーは asmdef で依存方向を強制する。

```
Domain (Unity 非依存) ← Application ← Adapters (Unity 依存)
                                       ↑
                                     Editor (UI Toolkit)
```

Domain は `Unity.Collections` のみ参照可、Adapters のみ `Unity.Animation` 等の Engine 機能に依存できる。Presentation 相当は Editor / Samples が担当（ランタイム UI は提供しない）。

## Core Technologies

- **Language**: C# (Unity 6 同梱の Roslyn 互換)
- **Runtime**: Unity **6000.3.19f1** (Unity 6)。Editor 実体: `D:/UnityEditors/6000.3.19f1/Editor/Unity.exe`（batchmode テストは必ずこのバージョンで実行する）
- **Render Pipeline**: URP **17.3.0**（PC / モバイル別設定あり、ただしパッケージ自体はパイプライン非依存契約）
- **Color Space**: Linear
- **Distribution**: UPM (Unity Package Manager)、最終的に npmjs.com `com.hidano` スコープへ公開

## Key Libraries

| パッケージ | 用途 |
|-----------|------|
| `com.unity.inputsystem` (1.17.0) | 入力デバイスの動的切り替え |
| `com.unity.timeline` (1.8.9) | Timeline 統合（将来対応） |
| `com.unity.test-framework` (1.6.0) | EditMode / PlayMode テスト |
| `uOsc` | OSC 通信（必須依存、`com.hidano.facialcontrol.osc` に同梱想定） |
| `jp.lilxyzw.liltoon` | **dev 環境のみ**（パッケージ本体はシェーダー非依存） |
| `com.mikunote.magica-cloth-2` | **dev 環境のみ**（パッケージ本体は物理演算非依存） |

JSON パースは `JsonUtility` ベース（System.Text.Json は使わない）。

## Development Standards

### Coding Style
- C#、**4 スペースインデント**、改行時に中括弧（`{` を新しい行へ）
- 明示的な `public` / `private` を推奨
- 型: `PascalCase` / インターフェース: `I` プレフィックス / プライベートフィールド: `_camelCase`
- 名前空間ルート: `Hidano.FacialControl.{Domain|Application|Adapters|Editor}`
- 日本語でコメント・ドキュメントを記述

### Architectural Contracts（破ってはならない）
- **レンダーパイプライン非依存** / **シェーダー非依存** / **物理演算非依存**（lilToon・MagicaCloth2 は dev 環境専用）
- **OSC 以外の通信プロトコル**もインターフェース経由で差し替え可能であること
- **エラーハンドリングは Unity 標準ログのみ**（`Debug.Log/Warning/Error`）。カスタム例外型は標準例外を超えて作らない
- **Editor は UI Toolkit**（IMGUI を新規 UI に使わない）
- **BlendShape 命名規則は固定しない**（2 バイト文字・特殊記号を正しく扱うこと）

### Performance Standards
- **毎フレームのヒープ確保ゼロを目標**（GC スパイク対策、Req 6.1）
- 浮動小数点は `float` 基本
- UDP 送受信はメインスレッド非依存
- プレリリースは通常 C#。インターフェース設計で **Jobs / Burst** に差し替え可能であること
- 同時 10 体以上のキャラクター制御を想定 / ユーザープリセット最大 512

### Testing
**TDD（Red-Green-Refactor）厳守**。カバレッジ数値目標は設定しない。

**配置基準は「実行時要件」で決定する**（単体 vs 統合では決めない）:

| 配置先 | 基準 |
|--------|------|
| EditMode | モック・Fake のみ、同期実行、PlayMode 機能不要（例: JSON パーステスト、プロファイル変換） |
| PlayMode | MonoBehaviour ライフサイクル / コルーチン / 実 UDP・OSC / フレーム同期が必要（例: 表情遷移補間、OSC 送受信） |

**命名**: クラス `{Target}Tests` / メソッド `{Method}_{Condition}_{Expected}`（例: `SetProfile_ValidJson_ReturnsProfileWithCorrectBlendShapes`）。

**何を守るテストを書くか**は `docs/test-policy.md` に従う。要点:
- テストファイルはテスト対象クラス単位。spec や task 単位でファイルを増やさず、既存の `{Target}Tests.cs` に追記する
- private への reflection 到達とログ文言の完全一致は「実装のテスト」なので書かない（振る舞いを変えないリファクタで赤くなる）
- Editor UI テストは生成・保存・破棄の smoke のみ
- テスト名・コメントに Legacy / V2 / Phase / task 番号などの履歴語を入れない

## Development Environment

### Required Tools
- Unity Hub + Unity 6000.3.19f1（`D:/UnityEditors/6000.3.19f1/Editor/Unity.exe`）
- Git（外部依存は SSH 経由で取得: lilToon / MagicaCloth2 等）

### Common Commands
```bash
# EditMode テスト
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform EditMode \
    -testResults ./test-results/editmode.xml

# PlayMode テスト
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform PlayMode \
    -testResults ./test-results/playmode.xml
```

Claude Code 実行時は `run_in_background` を使わず、`timeout: 600000` の同期 Bash 呼び出しでテストランナーを実行する。

### CI/CD
- **GitHub Actions + セルフホスト Windows ランナー**
- TDD ベースの自動テスト

## Key Technical Decisions

- **JSON ファースト永続化**: コアは JSON。Unity 向けオプションとして ScriptableObject に変換。ビルド後も JSON で表情設定を差し替え可能。preview 段階では破壊的変更を許容。
- **ランタイム JSON パース**: JSON → ScriptableObject 変換はランタイム機能。Asset ファイル保存のみ Editor ツール。
- **D-1 ハイブリッド入力モデル**: `ExpressionTrigger`（バイナリのスタックベース） + `ValueProvider`（直接値書き込み）を Aggregator がレイヤーごとに weighted-sum → clamp01 で合成。
- **遷移補間**: 線形補間がデフォルト（イージング・カスタムカーブで上書き可能）。遷移時間 0〜1 秒（既定 0.25 秒）。遷移中に新トリガーが来たら現在の補間値から即座に新遷移を開始。
- **OSC は VRChat 互換が必須要件**: `/avatar/parameters/{name}` 形式を維持。BlendShape 単位で個別メッセージ。
- **Timeline 統合は将来対応**: 初回は Animator ベースのリアルタイム制御。
- **対象プラットフォーム**: Windows PC のみ。モバイル / WebGL / VR は将来拡張用に設計余地のみ確保。

---
_Document standards and patterns, not every dependency_
