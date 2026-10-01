# Project Structure

## Organization Philosophy

**マルチパッケージ + レイヤード（クリーンアーキテクチャ）**。

リポジトリ直下は Unity プロジェクト 1 つ（`FacialControl/`）+ ドキュメント類。配布物は `FacialControl/Packages/` 配下のローカル UPM パッケージ群として開発する。各パッケージ内は Domain / Application / Adapters の 3 層 + Editor + Tests + Samples~ で構成し、依存方向を asmdef で強制する。

## Top-Level Layout

```
/                          # リポジトリルート
├── FacialControl/         # Unity プロジェクトルート（このディレクトリで開く）
│   ├── Assets/            # dev 用シーン・モデル参照・確認用 Samples ミラー
│   ├── Packages/          # ローカル UPM パッケージ群（配布物）
│   ├── ProjectSettings/
│   └── (Library/Temp/obj/UserSettings — 触らない)
├── docs/                  # 要件定義・QA・作業手順書
├── .kiro/                 # spec / steering（spec-driven 開発の成果物）
├── .github/               # CI / Copilot 指示
├── CLAUDE.md              # プロジェクト指示（Claude 用、真実の源）
└── README.md
```

## Package Layout (`FacialControl/Packages/`)

9 つのローカル UPM パッケージで分割配布する:

| パッケージ | 役割 |
|-----------|------|
| `com.hidano.facialcontrol` | コア（Domain / Application / Adapters / Editor。Editor は Profile Inspector・ルーティング配線ロジック） |
| `com.hidano.facialcontrol.expression-creator` | Expression 作成ツール（Editor のみ。プレビュー / ベイク / PNG 書き出し。依存: core + `com.hidano.scene-view-style-camera-controller`） |
| `com.hidano.facialcontrol.routing-editor` | ルーティングエディタ（Editor のみ。GraphView 薄層。配線ロジックは core の `Editor/Windows/Routing/Logic` を参照し、core の `RoutingEditorLauncher` へ `[InitializeOnLoad]` で起動経路を登録） |
| `com.hidano.facialcontrol.osc` | OSC 通信拡張（VRChat 互換、uOsc 同梱想定） |
| `com.hidano.facialcontrol.inputsystem` | InputSystem 連携 + `Multi Source Blend Demo` サンプル提供 |
| `com.hidano.facialcontrol.lipsync` | uLipSync 連携アダプター（音素 overlay 入力） |
| `com.hidano.facialcontrol.ifacialmocap` | iFacialMocap 受信アダプター（ARKit 52 / gaze） |
| `com.hidano.facialcontrol.rec` | 操作イベント記録・再生（.fcrec、基準状態 + 注入再生） |
| `com.hidano.facialcontrol.timeline` | Timeline 統合（表情/連続値 Track、ベイク、REC 書き出し。依存: core + rec(Editor のみ) + com.unity.timeline） |

各パッケージ内の標準構成:

```
{package}/
├── Runtime/
│   ├── Domain/            # Unity 非依存。{Models, Interfaces, Services}/
│   ├── Application/       # ユースケース。UseCases/
│   └── Adapters/          # Unity 依存実装。{Playable, OSC, Json, ScriptableObject, Input, InputSources, FileSystem}/
├── Editor/                # UI Toolkit。{Inspector, Windows, Tools, Common}/
├── Tests/
│   ├── EditMode/          # 同期実行・モック/Fake のみ
│   ├── PlayMode/          # MonoBehaviour・コルーチン・実 I/O が必要
│   └── Shared/            # 両モード共用（Fake、テストユーティリティ）
├── Samples~/              # UPM 配布の canonical サンプル（~ で Unity が無視）
├── Documentation~/        # Markdown ドキュメント
├── package.json
├── README.md / CHANGELOG.md / LICENSE.md
```

### Asmdef Dependency Direction（破ってはならない）

```
Hidano.FacialControl.Domain      ← (Unity.Collections のみ)
Hidano.FacialControl.Application ← Domain
Hidano.FacialControl.Adapters    ← Domain, Application, Unity.Animation, Unity.Collections
Hidano.FacialControl.Editor      ← Editor 専用 asmdef
```

Domain は Engine 参照を持たない（`noEngineReferences` ではないが Unity 型を使わない契約）。Adapters のみ Engine 機能と統合する。

## Samples の配置ルール

`Samples~/` を唯一の正本とする（二重管理は廃止）:

- **`Packages/{package}/Samples~/`** — UPM 配布の canonical かつ唯一の編集対象。`package.json` の `samples` 配列に登録されたものだけが Package Manager から Import 可能。`~` suffix で Unity のコンパイル対象外。
- **`FacialControl/Assets/Samples/`** — **リポジトリ管理下に置かない**。dev での動作確認は Package Manager の Import Sample で `Assets/Samples/{displayName}/{version}/{sampleName}/` へ展開して行い、展開結果はコミットしない。

かつては dev ミラーを二重管理していたが、3 コピー（dev `StreamingAssets` / `Samples~` / import 結果）間の drift が慢性化したため 2026-08-25 に mirror を削除した。サンプルの修正は `Samples~/` 側だけを編集し、必要なら Import し直して確認する。`package.json` の `samples[].path` に dev 側の path を登録してはいけない。

## Naming Conventions

- **クラス / 構造体 / enum**: `PascalCase`
- **インターフェース**: `I` プレフィックス（例: `IExpressionTrigger`）
- **プライベートフィールド**: `_camelCase`
- **名前空間**: `Hidano.FacialControl.{Domain|Application|Adapters|Editor}.{SubArea}`
- **テストクラス**: `{Target}Tests`
- **テストメソッド**: `{Method}_{Condition}_{Expected}`（例: `SetProfile_ValidJson_ReturnsProfileWithCorrectBlendShapes`）

## Code Organization Principles

- **依存は内向き**: Adapters → Application → Domain。逆方向の参照を asmdef で物理的に禁止する。
- **境界モック**: 外部境界（I/O、ネットワーク、Renderer、Time）のみ Fake 化。Domain 内部はモックしない。
- **配布単位の独立性**: コア / OSC / InputSystem は独立してインストール可能。コアは OSC・InputSystem を知らない。
- **3 レイヤー既定の表情合成**: 感情 / リップシンク / 目（優先度はユーザー設定可、カテゴリ内排他は LastWins / Blend を選択可）。
- **Editor 専用機能はランタイムに混入させない**: `Editor/` 配下のコードは asmdef の `includePlatforms: ["Editor"]` で隔離する想定。

## File Management Rules

- **`.meta` は常にアセットと一緒に管理**（コミット対象）
- **生成バイナリ・ログはコミット禁止**
- **`Library/Temp/obj/UserSettings` は触らない**
- **`tasks.txt` はタスク ID のみを列挙**するファイル（for ループ実行用）。タスクの説明・詳細は `docs/work-procedure.md` に書く。`tasks.txt` に説明を直接追記してはならない。
- **パッケージ更新は `FacialControl/Packages/manifest.json`** で行い、`packages-lock.json` を同期維持する。

---
_Document patterns, not file trees. New files following patterns shouldn't require updates_
