# FacialControl 作業手順書

> **バージョン**: 1.1.0
> **作成日**: 2026-02-03
> **対象リリース**: preview.1
> **実装順序**: Domain → Adapters → Editor（ボトムアップ / TDD）

---

## 概要

本手順書は FacialControl preview.1 の実装作業を大項目ごとに整理したものである。
各フェーズは独立したサブエージェントで実行可能な粒度に分割されている。

**前提条件**: 要件定義書 v3.0.0、技術仕様書 v3.0.0、QA シート完了済み。

### ID 体系

- **フェーズ ID**: `P{nn}`（例: `P00`, `P01`）
- **作業項目 ID**: `P{nn}-{nn}`（例: `P00-01`, `P01-03`）
- **テスト ID**: `P{nn}-T{nn}`（例: `P01-T01`）

---

## P00: プロジェクト基盤構築

### 目的
UPM パッケージとして正しいディレクトリ構造と Assembly Definition を整備し、TDD 開発を開始できる状態にする。

### 作業内容

#### P00-01: UPM パッケージディレクトリ作成
```
FacialControl/Packages/com.hidano.facialcontrol/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE.md
├── Runtime/
│   ├── Domain/
│   │   ├── Models/
│   │   ├── Interfaces/
│   │   └── Services/
│   ├── Application/
│   │   └── UseCases/
│   └── Adapters/
│       ├── Playable/
│       ├── OSC/
│       ├── Json/
│       ├── ScriptableObject/
│       └── Input/
├── Editor/
│   ├── Inspector/
│   ├── Windows/
│   ├── Tools/
│   └── Common/
├── Templates/
├── Documentation~/
└── Tests/
    ├── EditMode/
    │   ├── Domain/
    │   ├── Application/
    │   └── Adapters/
    ├── PlayMode/
    │   ├── Integration/
    │   └── Performance/
    └── Shared/
```

#### P00-02: package.json 作成
- パッケージ名: `com.hidano.facialcontrol`
- バージョン: `0.1.0-preview.1`
- Unity 最小バージョン: `6000.3`
- 依存: `com.unity.inputsystem`, `com.hidano.uosc`
- scopedRegistries で npmjs.com を追加（uOsc 取得用）

#### P00-03: Assembly Definition ファイル作成（6 つ）

| asmdef 名 | 配置先 | 依存先 |
|-----------|--------|--------|
| `Hidano.FacialControl.Domain` | Runtime/Domain/ | Unity.Collections |
| `Hidano.FacialControl.Application` | Runtime/Application/ | Domain |
| `Hidano.FacialControl.Adapters` | Runtime/Adapters/ | Domain, Application, Unity.Animation |
| `Hidano.FacialControl.Editor` | Editor/ | Domain, Application, Adapters |
| `Hidano.FacialControl.Tests.EditMode` | Tests/EditMode/ | Domain, Application, Adapters, UnityEngine.TestRunner, UnityEditor.TestRunner |
| `Hidano.FacialControl.Tests.PlayMode` | Tests/PlayMode/ | Domain, Application, Adapters, UnityEngine.TestRunner, UnityEditor.TestRunner |

#### P00-04: manifest.json にローカルパッケージ参照を追加
- `"com.hidano.facialcontrol": "file:com.hidano.facialcontrol"`

#### P00-05: .meta ファイルの生成確認
- Unity Editor でプロジェクトを開き、全ディレクトリの .meta が生成されることを確認

### 完了基準
- Unity Editor でエラーなくプロジェクトが開ける
- Test Runner で EditMode / PlayMode の空テストアセンブリが認識される
- asmdef の依存方向が正しい（Domain ← Application ← Adapters）

---

## P01: Domain 層 — モデル定義

### 目的
FacialProfile / Expression / BlendShapeMapping 等のドメインモデルを TDD で実装する。Unity に非依存な純粋 C# ロジック（ただし NativeArray は使用）。

### 作業内容

#### P01-01: 値オブジェクト / enum の定義
- `ExclusionMode` enum: `LastWins`, `Blend`
- `TransitionCurveType` enum: `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, `Custom`
- `TransitionCurve` 構造体: `Type` + `Keys`（カスタムカーブ用キーフレーム配列）
- `BlendShapeMapping` 構造体: `Name`, `Value`, `Renderer`（nullable）

#### P01-02: LayerDefinition モデル
- `Name`: string
- `Priority`: int
- `ExclusionMode`: ExclusionMode
- バリデーション: 名前が空でないこと、Priority が 0 以上

#### P01-03: LayerSlot モデル
- `Layer`: string（ターゲットレイヤー名）
- `BlendShapeValues`: BlendShapeMapping[]

#### P01-04: Expression モデル
- `Id`: string（GUID）
- `Name`: string
- `Layer`: string
- `TransitionDuration`: float（0〜1、デフォルト 0.25）
- `TransitionCurve`: TransitionCurve
- `BlendShapeValues`: BlendShapeMapping[]
- `LayerSlots`: LayerSlot[]
- バリデーション: Id が空でない、Name が空でない、TransitionDuration が 0〜1

#### P01-05: FacialProfile モデル
- `SchemaVersion`: string
- `Layers`: LayerDefinition[]
- `Expressions`: Expression[]
- Expression のレイヤー参照が Layers に存在するか検証
- 未定義レイヤー参照時のフォールバックロジック（emotion へ）

### テスト（EditMode/Domain/）
- **P01-T01**: `BlendShapeMappingTests` — 値のクランプ（0〜1）、renderer null 許容
- **P01-T02**: `LayerDefinitionTests` — バリデーション
- **P01-T03**: `ExpressionTests` — GUID 生成、バリデーション、デフォルト値
- **P01-T04**: `FacialProfileTests` — レイヤー検証、Expression 検索、フォールバック

### 完了基準
- 全テスト Green
- 範囲外値の自動クランプが動作
- 未定義レイヤー参照時にフォールバックする

---

## P02: Domain 層 — インターフェース定義

### 目的
Adapters 層との境界インターフェースを定義する。実装はフェーズ P04 以降。

### 作業内容

#### P02-01: IJsonParser インターフェース
```csharp
public interface IJsonParser
{
    FacialProfile ParseProfile(string json);
    string SerializeProfile(FacialProfile profile);
    FacialControlConfig ParseConfig(string json);
    string SerializeConfig(FacialControlConfig config);
}
```

#### P02-02: IProfileRepository インターフェース
```csharp
public interface IProfileRepository
{
    FacialProfile LoadProfile(string path);
    void SaveProfile(string path, FacialProfile profile);
}
```

#### P02-03: ILipSyncProvider インターフェース（技術仕様書 §10 準拠）
```csharp
public interface ILipSyncProvider
{
    void GetLipSyncValues(Span<float> output);
    ReadOnlySpan<string> BlendShapeNames { get; }
}
```

#### P02-04: IBlinkTrigger インターフェース（技術仕様書 §11 準拠、preview.2 実装）
```csharp
public interface IBlinkTrigger
{
    bool ShouldBlink(float deltaTime, in FacialState currentState);
}
```

#### P02-05: FacialControlConfig モデル（config.json 対応）
- OSC 設定（ポート、プリセット、マッピング）
- キャッシュ設定（LRU サイズ）

#### P02-06: FacialState 構造体（IBlinkTrigger 引数用）
- 現在のアクティブ Expression 情報

#### P02-07: FacialOutputData 構造体（技術仕様書 §3.7 準拠）
```csharp
public struct FacialOutputData
{
    public NativeArray<float> BlendShapeWeights;
}
```

### テスト
- インターフェース定義のみのため、テスト不要（Fake 実装テストは P03 以降）

### 完了基準
- 全インターフェースがコンパイル通過
- Domain asmdef の依存が Unity.Collections のみ

---

## P03: Domain 層 — サービスロジック

### 目的
表情遷移の補間計算、排他ロジック、レイヤーブレンド計算を TDD で実装する。

### 作業内容

#### P03-01: TransitionCalculator サービス
- 遷移カーブ評価: Linear / EaseIn / EaseOut / EaseInOut / Custom
- 遷移進行度(t)からブレンドウェイトを計算
- 遷移時間 0 の場合は即座に切り替え

#### P03-02: ExclusionResolver サービス
- **LastWins ロジック**: 新 Expression アクティブ時に旧 Expression からクロスフェード
- **Blend ロジック**: 複数 Expression のウェイト加算 + クランプ（0〜1）
- **遷移割込**: 現在の補間値からスナップショットを取得し、新遷移を開始

#### P03-03: LayerBlender サービス
- レイヤー優先度に基づくウェイトブレンド
- layerSlots によるオーバーライド適用（完全置換）
- 出力: 最終的な BlendShape ウェイト配列

#### P03-04: ARKitDetector サービス
- ARKit 52 パラメータ名リスト定義
- PerfectSync 拡張パラメータ名リスト定義
- 完全一致マッチングロジック
- レイヤー単位（目/口/眉）のグルーピングロジック
- Expression 自動生成ロジック

### テスト（EditMode/Domain/）
- **P03-T01**: `TransitionCalculatorTests` — 各カーブ種類の補間値検証、境界値(t=0, t=1)、カスタムカーブ
- **P03-T02**: `ExclusionResolverTests` — LastWins クロスフェード、Blend 加算+クランプ、遷移割込
- **P03-T03**: `LayerBlenderTests` — 優先度順ブレンド、layerSlots オーバーライド
- **P03-T04**: `ARKitDetectorTests` — ARKit 52 完全一致、PerfectSync 完全一致、未対応パラメータスキップ、レイヤーグルーピング

### 完了基準
- 全テスト Green
- 遷移割込で GC が発生しないことをテストで確認（NativeArray 再利用）
- ARKit 52 全パラメータの検出テスト通過

---

## P04: Application 層 — ユースケース

### 目的
Domain 層のサービスを組み合わせたユースケースを実装する。

### 作業内容

#### P04-01: ProfileUseCase
- `LoadProfile(string path)`: JSON パース → FacialProfile 生成 → レイヤー検証
- `ReloadProfile()`: 現在のプロファイルを再パース
- `GetExpression(string id)`: ID から Expression を取得
- `GetExpressionsByLayer(string layer)`: レイヤー別 Expression リスト

#### P04-02: ExpressionUseCase
- `Activate(Expression)`: Expression をアクティブ化（排他ロジック適用）
- `Deactivate(Expression)`: Expression を非アクティブ化
- `GetActiveExpressions()`: 現在アクティブな Expression リスト

#### P04-03: LayerUseCase
- `UpdateWeights(float deltaTime)`: 全レイヤーの補間更新
- `GetBlendedOutput()`: 最終出力 BlendShape 値の計算
- `SetLayerWeight(string layer, float weight)`: レイヤーウェイト設定

#### P04-04: ARKitUseCase
- `DetectAndGenerate(string[] blendShapeNames)`: 検出 + Expression 自動生成
- `GenerateOscMapping(...)`: OSC マッピング自動生成

### テスト（EditMode/Application/）
- **P04-T01**: `ProfileUseCaseTests` — Fake IJsonParser / IProfileRepository を使用した単体テスト
- **P04-T02**: `ExpressionUseCaseTests` — アクティブ化 / 非アクティブ化の検証
- **P04-T03**: `LayerUseCaseTests` — ウェイト更新、ブレンド出力の検証
- **P04-T04**: `ARKitUseCaseTests` — 検出 + 生成フローのエンドツーエンド検証

### 完了基準
- 全テスト Green
- Domain 層の Fake 実装で完結（Unity 依存なし）

---

## P05: Adapters 層 — JSON パーサー

### 目的
IJsonParser の System.Text.Json 実装と IProfileRepository のファイルシステム実装を行う。

### 作業内容

#### P05-01: SystemTextJsonParser クラス
- `IJsonParser` の実装
- FacialProfile の JSON パース / シリアライズ
- FacialControlConfig の JSON パース / シリアライズ
- schemaVersion チェック
- 不正 JSON 時の例外スロー

#### P05-02: FileProfileRepository クラス
- `IProfileRepository` の実装
- StreamingAssets からの読み込み
- ファイルの書き込み（Editor 用）

#### P05-03: JSON スキーマ定義
- プロファイル JSON のスキーマ定義（技術仕様書 §13.7 準拠）
- config.json のスキーマ定義（技術仕様書 §13.8 準拠）

### テスト（EditMode/Adapters/）
- **P05-T01**: `SystemTextJsonParserTests` — 正常パース、シリアライズ往復、不正 JSON 例外、バージョンチェック
- **P05-T02**: `FileProfileRepositoryTests` — ファイル読み書き（テスト用一時ディレクトリ使用）
- **P05-T03**: 技術仕様書 §13.7 のサンプル JSON を使ったパーステスト

### 完了基準
- 技術仕様書のサンプル JSON が正しくパース/シリアライズできる
- 不正 JSON で適切な例外がスローされる
- 全テスト Green

---

## P06: Adapters 層 — PlayableAPI

### 目的
PlayableGraph ベースの BlendShape 制御を実装する。FacialControl の中核機能。

### 作業内容

#### P06-01: NativeArrayPool クラス
- Allocator.Persistent で事前確保
- BlendShape 総数に基づくサイズ決定
- 毎フレーム再利用（GC フリー）
- OnDisable で解放

#### P06-02: AnimationClipCache クラス（LRU）
- blendShapeValues → AnimationClip 動的生成
- デフォルト 16 エントリの LRU キャッシュ
- キャッシュミス時のみ GC 発生

#### P06-03: PropertyStreamHandleCache クラス
- BlendShape → PropertyStreamHandle のマッピング
- Expression 切替時に未取得分のみ新規取得
- 取得済みはキャッシュ再利用

#### P06-04: LayerPlayable（ScriptPlayable）
- NativeArray ベースの補間計算
- スナップショットバッファ（遷移割込用）
- LastWins / Blend 排他モード処理
- ProcessFrame 内で AnimationStream API 経由の BlendShape 書き込み

#### P06-05: FacialControlMixer（ScriptPlayable）
- レイヤーウェイトブレンド（root ノード）
- layerSlots オーバーライド処理
- 最終出力の統合

#### P06-06: PlayableGraphBuilder クラス
- FacialProfile からの PlayableGraph 構築
- レイヤー分の LayerPlayable ノード配置
- 既存 Animator への接続

### テスト
- **P06-T01**: `NativeArrayPoolTests`（EditMode）— 確保・再利用・解放
- **P06-T02**: `AnimationClipCacheTests`（EditMode）— LRU 動作、キャッシュヒット / ミス
- **P06-T03**: `PropertyStreamHandleCacheTests`（EditMode）— キャッシュ動作
- **P06-T04**: `PlayableGraphBuilderTests`（PlayMode）— Graph 構築・接続
- **P06-T05**: `LayerPlayableTests`（PlayMode）— 補間計算、排他モード
- **P06-T06**: `FacialControlMixerTests`（PlayMode）— レイヤーブレンド、オーバーライド

### 完了基準
- PlayableGraph が正しく構築される
- BlendShape 値が AnimationStream 経由で適用される
- 遷移補間が正しく動作する（割込含む）
- 毎フレーム GC ゼロ（遷移割込含む）
- NativeArray が OnDisable で正しく解放される

---

## P07: Adapters 層 — OSC 通信

### 目的
uOsc を用いた OSC 送受信と、ダブルバッファリングによるスレッド安全な通信を実装する。

### 作業内容

#### P07-01: uOsc パッケージ依存の追加
- `com.hidano.uosc` を package.json に追加
- scopedRegistries 設定

#### P07-02: OscDoubleBuffer クラス
- 受信用ダブルバッファ（ロックフリー）
- フレーム境界でスワップ
- NativeArray ベースで GC フリー

#### P07-03: OscReceiver クラス
- uOsc ラッパー
- 受信データを OscDoubleBuffer に書き込み
- VRChat / ARKit アドレスパターンの解析
- マッピングテーブルに基づくレイヤー分配

#### P07-04: OscSender クラス
- 全 BlendShape を毎フレーム送信
- 別スレッドで非同期送信
- メインスレッド負荷ゼロ

#### P07-05: OscReceiverPlayable（ScriptPlayable）
- OscDoubleBuffer からの値読み取り
- PlayableGraph への統合

#### P07-06: OscMappingTable クラス
- config.json からのマッピング読み込み
- OSC アドレス → (BlendShape 名, レイヤー) の変換

### テスト
- **P07-T01**: `OscDoubleBufferTests`（EditMode）— バッファ読み書き、スワップ
- **P07-T02**: `OscMappingTableTests`（EditMode）— マッピング変換、プリセット
- **P07-T03**: `OscSendReceiveTests`（PlayMode）— 実 UDP 送受信、ダブルバッファリング動作

### 完了基準
- OSC 送受信が正しく動作する
- ダブルバッファリングでスレッド安全
- マッピングテーブルに基づくレイヤー分配が動作する
- メインスレッドの GC ゼロ

---

## P08: Adapters 層 — ScriptableObject / Input / FacialController

### 目的
Unity コンポーネントとしての統合とユーザー向けインターフェースを実装する。

### 作業内容

#### P08-01: FacialProfileSO（ScriptableObject）
- JSON ファイルパスの参照保持
- Inspector での表示用フィールド

#### P08-02: FacialProfileMapper クラス
- FacialProfile ⟷ FacialProfileSO の変換
- SO → JSON パス取得 → JSON パース のフロー

#### P08-03: FacialController（MonoBehaviour）
- メインコンポーネント
- FacialProfileSO 参照フィールド
- SkinnedMeshRenderer リスト（自動検索 + 手動オーバーライド）
- 公開 API（技術仕様書 §15 準拠）:
  - `Activate(Expression)`
  - `Deactivate(Expression)`
  - `LoadProfile(FacialProfileSO)`
  - `ReloadProfile()`
- OnEnable で自動初期化 / Initialize() で手動初期化
- OnDisable で PlayableGraph + NativeArray 破棄

#### P08-04: InputSystemAdapter クラス
- InputAction Asset との連携
- Button / Value 両対応
- Expression トリガー

#### P08-05: デフォルト InputAction Asset
- 最小限のバインディング（Expression トリガー用）

### テスト
- **P08-T01**: `FacialProfileMapperTests`（EditMode）— SO ⟷ Profile 変換
- **P08-T02**: `FacialControllerLifecycleTests`（PlayMode）— OnEnable / OnDisable / Initialize
- **P08-T03**: `FacialControllerAPITests`（PlayMode）— Activate / Deactivate / LoadProfile
- **P08-T04**: `InputSystemAdapterTests`（PlayMode）— Button / Value トリガー

### 完了基準
- FacialController を GameObject にアタッチして動作する
- 公開 API が型安全に動作する
- OnEnable/OnDisable のライフサイクルが正しい
- 複数 SkinnedMeshRenderer の制御が動作する

---

## P09: Editor 拡張 — Inspector

### 目的
FacialController と FacialProfileSO のカスタム Inspector を UI Toolkit で実装する。

### 作業内容

#### P09-01: FacialControllerEditor（CustomEditor）
- FacialProfileSO 参照フィールド
- SkinnedMeshRenderer リスト表示
- OSC ポート設定
- プロファイルの概要表示（レイヤー数、Expression 数）

#### P09-02: FacialProfileSOEditor（CustomEditor）
- JSON ファイルパスの表示
- JSON 読み込みボタン
- 簡易プロファイル情報表示

#### P09-03: UI Toolkit スタイル共通定義
- Editor/Common/ にスタイルシート配置

### テスト
- Editor テストは Inspector の描画確認（手動検証中心）

### 完了基準
- FacialController の Inspector が正しく表示される
- FacialProfileSO の Inspector が正しく表示される
- JSON ファイルとの連携が動作する

---

## P10: Editor 拡張 — プロファイル管理ウィンドウ

### 目的
EditorWindow でプロファイル内の Expression リスト管理と CRUD 操作を実装する。

### 作業内容

#### P10-01: ProfileManagerWindow（EditorWindow）
- Expression リスト表示（プロファイル JSON から取得）
- 名前検索（部分一致）
- Expression の CRUD 操作
- Undo 連動 + JSON 自動保存
- サムネイルプレビュー（PreviewRenderUtility、手動生成）

#### P10-02: JSON インポート / エクスポート
- プロファイル JSON ファイルのインポート
- プロファイル JSON ファイルのエクスポート
- SO と JSON の手動同期

### テスト
- 手動検証中心（EditorWindow の操作確認）

### 完了基準
- Expression の一覧表示、検索、追加、編集、削除が動作する
- Undo が動作する
- JSON の読み書きが正しい

---

## P11: Editor 拡張 — Expression 作成支援ツール

### 目的
BlendShape スライダーを操作して Expression を作成する専用エディタを実装する。

### 作業内容

#### P11-01: ExpressionCreatorWindow（EditorWindow）
- PreviewRenderUtility による独立プレビューウィンドウ
- モデル指定（Scene オブジェクト / Prefab / FBX）
- BlendShape スライダー（全 BlendShape を一覧表示）
- リアルタイムプレビュー（値変更毎に即更新）
- 出力: JSON プロファイル内の Expression として保存
- レイヤー選択、遷移時間 / カーブ設定

#### P11-02: PreviewRenderUtility ラッパー
- Editor/Common/ に共通ユーティリティとして配置
- カメラ / ライティング設定
- RenderTexture 管理

### テスト
- 手動検証中心

### 完了基準
- BlendShape スライダー操作がリアルタイムでプレビューに反映される
- 作成した Expression が JSON に正しく保存される
- Scene / Prefab 両方のモデル指定が動作する

---

## P12: Editor 拡張 — ARKit 検出ツール

### 目的
ARKit 52 / PerfectSync の自動検出と Expression + OSC マッピング自動生成を実装する。

### 作業内容

#### P12-01: ARKitDetectorWindow（EditorWindow）
- 対象モデルの指定（SkinnedMeshRenderer 選択）
- 検出実行ボタン
- 検出結果表示（マッチしたパラメータ一覧）
- Expression 自動生成確認
- OSC マッピング自動生成確認

#### P12-02: Editor 向け ARKit 検出 API
- Domain 層の ARKitDetector をラップ
- SkinnedMeshRenderer から BlendShape 名リスト取得
- 検出結果の JSON 保存

### テスト
- Domain 層の ARKitDetector テスト（P03 で完了）で論理は検証済み
- Editor 統合は手動検証

### 完了基準
- モデルの BlendShape が正しくスキャンされる
- ARKit 52 / PerfectSync パラメータが完全一致で検出される
- Expression がレイヤー単位で自動生成される
- OSC マッピングが自動生成される
- 生成結果が編集可能

---

## P13: テンプレート・設定ファイル

### 目的
パッケージ同梱のテンプレートファイルを作成する。

### 作業内容

#### P13-01: デフォルトプロファイル JSON テンプレート（Templates/）
- default_profile.json: デフォルトレイヤー構成 + 基本 Expression テンプレート
- 技術仕様書 §17 準拠（default, blink, gaze_follow, gaze_camera）

#### P13-02: デフォルト config.json テンプレート
- VRChat プリセットのデフォルト設定
- 技術仕様書 §13.8 準拠

#### P13-03: デフォルト InputAction Asset
- 最小限のトリガーバインディング

### 完了基準
- テンプレートファイルが正しい JSON フォーマット
- パーサーで正しく読み込める

---

## P14: 統合テスト・パフォーマンステスト

### 目的
全レイヤーを結合した統合テストと、パフォーマンス要件の検証を行う。

### 作業内容

#### P14-01: 統合テスト（PlayMode/Integration/）
- FacialController のエンドツーエンド動作テスト
- プロファイル読み込み → Expression アクティブ → BlendShape 適用の全フロー
- 複数 Expression の遷移テスト（LastWins / Blend）
- 遷移割込テスト
- layerSlots オーバーライドテスト
- OSC 送受信統合テスト
- 複数 SkinnedMeshRenderer テスト
- プロファイル切替テスト

#### P14-02: パフォーマンステスト（PlayMode/Performance/）
- 毎フレーム GC ゼロの検証
- 遷移割込時 GC ゼロの検証
- 10 体同時制御のフレームレート検証
- NativeArray リーク検出

### テスト
- **P14-T01**: `EndToEndTests` — 全フロー統合テスト
- **P14-T02**: `TransitionIntegrationTests` — 遷移 / 割込の実動作検証
- **P14-T03**: `OscIntegrationTests` — OSC 送受信の統合検証
- **P14-T04**: `MultiRendererTests` — 複数 Renderer の統合検証
- **P14-T05**: `GCAllocationTests` — 毎フレーム / 遷移割込の GC 計測
- **P14-T06**: `MultiCharacterPerformanceTests` — 10 体同時制御

### 完了基準
- 全統合テスト Green
- 毎フレーム GC ゼロ
- 10 体同時でフレームドロップなし

---

## P15: ドキュメント

### 目的
preview.1 同梱のドキュメントを作成する。

### 作業内容

#### P15-01: XML ドキュメントコメント
- 全公開 API に XML コメントを記述（IDE IntelliSense 用途）
- 自動生成 API リファレンス（DocFX 等）は preview.1 スコープ外。Unity UPM の慣習に合わせ、README + `Documentation~/` Markdown でドキュメントを提供する

#### P15-02: クイックスタートガイド（Documentation~/quickstart.md）
- パッケージインストール手順
- 最初のプロファイル作成手順
- FacialController セットアップ手順
- 基本的な Expression 切り替え

#### P15-03: JSON スキーマドキュメント（Documentation~/json-schema.md）
- FacialProfile JSON 全フィールド定義
- config.json 全フィールド定義
- サンプル JSON

#### P15-04: パッケージ README.md / CHANGELOG.md

### 完了基準
- 全公開 API に XML コメントがある
- クイックスタートガイドの手順で実際にセットアップできる
- JSON スキーマの全フィールドが文書化されている

---

## P16: CI/CD・リリース準備

### 目的
GitHub Actions による自動テスト・リリースパイプラインを構築する。

### 作業内容

#### P16-01: GitHub Actions ワークフロー
- EditMode テスト自動実行
- PlayMode テスト自動実行
- セルフホストランナー（Windows）設定

#### P16-02: パッケージバリデーション
- package.json の妥当性チェック
- asmdef の依存方向チェック
- .meta ファイルの整合性チェック

#### P16-03: npmjs.com 公開準備
- npmjs.com 公開用設定
- uOsc フォーク（`com.hidano.uosc`）のリリース確認

#### P16-04: バージョンタグ
- `0.1.0-preview.1` タグ作成

### 完了基準
- CI で全テストが自動実行される
- パッケージバリデーションが通る
- npmjs.com から `com.hidano.facialcontrol` がインストールできる

---

## P17: Editor 改善・テンプレート拡充

### 目的
プロトタイプ段階で判明した Editor 拡張の不足機能を追加し、テンプレートファイルを拡充する。ARKit / PerfectSync 検出仕様の不整合も調査・修正する。

### 作業内容

#### P17-01: ProfileManagerWindow にプロファイル新規作成機能を追加
- **対象ファイル**: `Editor/Windows/ProfileManagerWindow.cs`
- **追加 UI 要素**:
  - ウィンドウ上部に「新規プロファイル作成」ボタンを配置
  - 押下時にダイアログを表示:
    - プロファイル名入力（`TextField`）
    - レイヤー定義リスト（デフォルト 3 レイヤー: emotion / lipsync / eye）
    - 各レイヤーの `priority`（`IntegerField`）/ `exclusionMode`（`EnumField`）設定
    - レイヤー追加・削除ボタン
- **処理フロー**:
  1. ユーザーがプロファイル名・レイヤーを設定し「作成」を押下
  2. `SystemTextJsonParser.SerializeProfile()` で空 Expression リスト付き JSON を生成
  3. `StreamingAssets/FacialControl/{profileName}.json` に保存
  4. `AssetDatabase.CreateAsset()` で `FacialProfileSO` を `Assets/` 配下に自動生成
  5. SO の `_jsonFilePath` を保存先相対パスに自動設定

#### P17-02: FacialProfileSOEditor に JSON 内詳細情報の表示を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **追加 UI 要素**:
  - 「レイヤー一覧」`Foldout`: 各レイヤーの `name` / `priority` / `exclusionMode` を `Label` で一覧表示
  - 「Expression 一覧」`Foldout`: 各 Expression の `name` / `layer` / `transitionDuration` / `BlendShapeValues.Length` を表示
  - 各 Expression 内に子 `Foldout` を設け、BlendShape 名と値の一覧を展開可能にする
- **実装方針**:
  - JSON 読み込み成功時に `FacialProfile` をフィールドにキャッシュ
  - キャッシュ済み `FacialProfile` から UI を動的構築
  - 既存の簡易表示（スキーマバージョン / レイヤー数 / Expression 数）は維持

#### P17-03: FacialProfileSOEditor にインライン編集・JSON 上書き保存機能を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P17-02 完了後
- **変更内容**:
  - P17-02 で追加した `Label` を編集可能フィールドに変更:
    - レイヤー: `TextField`(name) / `IntegerField`(priority) / `EnumField`(exclusionMode)
    - Expression: `TextField`(name) / ドロップダウン(layer) / `FloatField`(transitionDuration)
  - 「JSON に保存」ボタンを追加
- **処理フロー**:
  1. ユーザーがフィールドを編集
  2. 「JSON に保存」押下時に `Undo.RecordObject()` で Undo 登録
  3. 編集済み値で `FacialProfile` を再構築
  4. `SystemTextJsonParser.SerializeProfile()` でシリアライズ
  5. `File.WriteAllText()` で StreamingAssets の JSON を上書き
  6. SO の表示用フィールド（`_schemaVersion` / `_layerCount` / `_expressionCount`）も同期更新

#### P17-04: FacialProfileSOEditor に JSONPath の説明を明記
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **変更内容**:
  - JSON ファイルパスフィールド（`_jsonFilePath` の `PropertyField`）の直下に `HelpBox` を追加
  - メッセージ: `「パスは StreamingAssets/ からの相対パスです（例: FacialControl/default_profile.json）」`
  - `HelpBoxMessageType.Info` で表示
  - 現状の tooltip（"StreamingAssets からの相対パス"）は維持

#### P17-05: InputActions テンプレートに Xbox コントローラのバインディングを追加
- **対象ファイル**:
  - `Templates/default_inputactions.inputactions`
  - `Runtime/Adapters/Input/FacialControlDefaultActions.inputactions`
- **現状**: Gamepad は D-Pad Up/Right/Down/Left → Trigger1〜4 のみ
- **追加バインディング**:

  | ボタン | InputSystem パス | 割り当て先 |
  |--------|-----------------|-----------|
  | A (South) | `<Gamepad>/buttonSouth` | Trigger5 |
  | B (East) | `<Gamepad>/buttonEast` | Trigger6 |
  | X (West) | `<Gamepad>/buttonWest` | Trigger7 |
  | Y (North) | `<Gamepad>/buttonNorth` | Trigger8 |
  | LB | `<Gamepad>/leftShoulder` | Trigger9 |
  | RB | `<Gamepad>/rightShoulder` | Trigger10 |

- LT / RT はアナログ入力のため Button タイプでは不適切。将来の Value タイプアクション追加で対応（今回スコープ外）
- 両ファイルに同一のバインディングを追加し同期を維持

#### P17-06: ARKit / PerfectSync 検出仕様の調査と修正
- **対象ファイル**:
  - `Runtime/Domain/Services/ARKitDetector.cs`
  - `Editor/Windows/ARKitDetectorWindow.cs`
  - `Assets/Samples/ARKitModel/`、`Assets/Samples/PerfectSyncModel/`
- **調査手順**:
  1. サンプルモデル（ARKitModel / PerfectSyncModel）を Unity で開き、SkinnedMeshRenderer の BlendShape 名を全件リストアップ
  2. リストアップ結果を `ARKit52Names[]`（52 件）/ `PerfectSyncNames[]`（13 件）と突合し、一致・不一致を確認
  3. PerfectSync の正確な定義を確認: PerfectSync 対応 = ARKit 52 + 拡張 13 の全 65 パラメータを持つモデル。ARKit 52 のみのモデルは PerfectSync 非対応で正しい
  4. `ARKitDetectorWindow.UpdateSummary()` のサマリー表示が誤解を招かないか確認
- **想定される修正**:
  - サマリーラベルの文言改善（例: `「PerfectSync 拡張: 0/13」` に変更し、ARKit 52 とは別の追加パラメータであることを明示）
  - BlendShape 名の命名規則がモデル間で異なる場合、検出ロジックの調整を検討
  - 検出ロジックに修正が入る場合は既存テスト（P03-T04: `ARKitDetectorTests`）と合わせて更新

#### P17-07: FacialProfile に RendererPaths を追加
- **対象ファイル**: `Runtime/Domain/Models/FacialProfile.cs`
- コンストラクタに 4 番目のオプション引数 `string[] rendererPaths = null` を追加
- `ReadOnlyMemory<string> RendererPaths` プロパティ追加（防御的コピー、Layers/Expressions と同じパターン）
- 既存の 3 引数以下の呼び出しは全て後方互換（デフォルト値 `null` → 空配列）

#### P17-08: JSON パーサーに rendererPaths の parse/serialize を追加
- **対象ファイル**: `Runtime/Adapters/Json/SystemTextJsonParser.cs`, `Runtime/Adapters/Json/JsonSchemaDefinition.cs`
- **依存**: P17-07
- `ProfileDto` に `public List<string> rendererPaths;` フィールド追加
- `ConvertToProfile()` で `rendererPaths` を変換して `FacialProfile` コンストラクタに渡す
- `ConvertToProfileDto()` で `RendererPaths` を DTO にセット
- `JsonSchemaDefinition.Profile.RendererPaths` 定数追加
- `SampleProfileJson` に `"rendererPaths": ["Armature/Body"]` を追加
- 後方互換: `rendererPaths` フィールド欠損の既存 JSON は空配列として処理（`JsonUtility` は欠損フィールドを `null` にするため）
- スキーマバージョン "1.0" 据え置き（後方互換な追加のため）

#### P17-09: FacialProfileSO に RendererPaths と参照モデルフィールドを追加
- **対象ファイル**: `Runtime/Adapters/ScriptableObject/FacialProfileSO.cs`
- **依存**: P17-07
- `string[] _rendererPaths` SerializeField + `RendererPaths` プロパティ追加
- `GameObject _referenceModel` SerializeField（`#if UNITY_EDITOR` ガード）+ `ReferenceModel` プロパティ追加
- `_referenceModel` は Editor 専用: Inspector で BlendShape 名取得に使用。JSON には含まない

#### P17-10: FacialProfileMapper に RendererPaths 同期を追加
- **対象ファイル**: `Runtime/Adapters/ScriptableObject/FacialProfileMapper.cs`
- **依存**: P17-07, P17-09
- `UpdateSO()` に `profile.RendererPaths` → `so.RendererPaths` の同期処理を追加

#### P17-11: FacialProfileSOEditor に参照モデル指定と RendererPaths 自動検出を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`, `Editor/Tools/ExpressionCreatorWindow.cs`
- **依存**: P17-08, P17-09, P17-10
- **参照モデルセクション**:
  - `ObjectField`（`typeof(GameObject)`, `allowSceneObjects = false`）で Prefab/FBX を指定
  - 「RendererPaths 自動検出」ボタン: 参照モデルの全 SkinnedMeshRenderer のヒエラルキーパス（モデルルートからの相対パス）を算出
  - RendererPaths 一覧表示（パス + BlendShape 数）
- **RendererPaths 保持**:
  - `RebuildProfileFromEdits()` で `originalProfile.RendererPaths.ToArray()` を 4 番目引数に渡す
  - `ExpressionCreatorWindow.OnSaveExpressionClicked()` でも同様に RendererPaths を保持

#### P17-12: FacialProfileSOEditor の BlendShape 値表示を DropdownField に変更
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P17-11
- `BuildExpressionDetailUI()` 内の BlendShape 名表示を条件分岐:
  - 参照モデル設定済み → BlendShape 名を `DropdownField` で選択可能に
  - 参照モデル未設定 → 従来通り `Label` で読み取り専用表示
- ドロップダウンの選択肢は参照モデルの全 SkinnedMeshRenderer から収集した BlendShape 名（重複排除、ソート済み）

### テスト（EditMode/）
- **P17-T01**: `ProfileCreationTests` — 新規プロファイル JSON 生成の検証（デフォルトレイヤー構成、空 Expression リスト、スキーマバージョン "1.0"）
- **P17-T02**: `ProfileEditSaveTests` — レイヤー編集・Expression 編集後の JSON 上書き保存ラウンドトリップ検証（パース → 編集 → シリアライズ → 再パースで値が一致）
- **P17-T03**: `FacialProfileRendererPathsTests` — 既存 `FacialProfileTests.cs` に追加。RendererPaths の構築、防御的コピー、null → 空配列のデフォルト値
- **P17-T04**: `SystemTextJsonParserRendererPathsTests` — 既存 `SystemTextJsonParserTests.cs` に追加。rendererPaths の parse / serialize ラウンドトリップ、`rendererPaths` 欠損 JSON の後方互換
- **P17-T05**: `FacialProfileMapperRendererPathsTests` — 既存 `FacialProfileMapperTests.cs` に追加。RendererPaths の SO ↔ Profile 同期

### 完了基準
- 新規プロファイルが UI から作成でき、有効な JSON として StreamingAssets に保存される
- SO Inspector でレイヤー・Expression の詳細が閲覧できる
- インライン編集の結果が JSON に正しく書き戻される
- JSONPath の説明が Inspector 上で `HelpBox` として視覚的に確認できる
- Xbox コントローラの A/B/X/Y/LB/RB が Trigger5〜10 に割り当てられる
- ARKit / PerfectSync の検出結果がサンプルモデルの実態と一致する
- FacialProfile に RendererPaths が保持され、JSON ラウンドトリップで値が保存・復元される
- SO Inspector で参照モデルを設定し、RendererPaths を自動検出できる
- BlendShape 名がドロップダウンから選択できる（参照モデル設定時）

## P18: プロファイル管理を Inspector に一本化

### 目的

ProfileManagerWindow（独立 EditorWindow）の機能を FacialProfileSO の Inspector に統合し、プロファイル編集の入口を一本化する。ProfileManagerWindow は削除する。

### 作業内容

#### P18-01: Inspector に Expression 追加機能を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- Expression 一覧セクション上部に「Expression 追加」ボタンを設置
- デフォルト値（名前="New Expression"、先頭レイヤー）で新規 Expression を作成
- 作成後に JSON 自動保存 → UI 再構築
- Undo 対応

#### P18-02: Inspector に Expression 削除機能を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- 各 Expression の Foldout 内に「削除」ボタンを設置
- 確認ダイアログ表示後に削除
- 削除後に JSON 自動保存 → UI 再構築
- Undo 対応

#### P18-03: Inspector に Expression 検索フィルタを追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- Expression 一覧セクション上部に検索 TextField を設置
- 名前の部分一致（大文字小文字区別なし）でフィルタリング
- 検索テキスト変更時に即座に表示更新

#### P18-04: Inspector にインポート・エクスポート機能を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- JSON ファイルセクションに「インポート」「エクスポート」ボタンを追加
- インポート: `EditorUtility.OpenFilePanel` → パース → SO の JSON パスへ保存 → UI 更新
- エクスポート: `EditorUtility.SaveFilePanel` → 現在のプロファイルをシリアライズして書き出し
- Undo 対応

#### P18-05: Inspector に新規プロファイル作成ボタンを追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- Inspector 上部に「新規プロファイル作成」ボタンを設置
- 既存の `ProfileCreationDialog` を呼び出す
- 作成完了後に Inspector を自動更新

#### P18-06: ProfileManagerWindow・ExpressionEditDialog を削除
- **対象ファイル**: `Editor/Windows/ProfileManagerWindow.cs`（削除）
- 対応する `.meta` ファイルを削除
- `MenuItem "FacialControl/プロファイル管理"` が消滅することを確認

#### P18-07: 要件ドキュメント・CLAUDE.md を更新
- **対象ファイル**: `docs/requirements.md`, `CLAUDE.md`
- `requirements.md` FR-008 の「プロファイル管理ウィンドウ」行を Inspector 統合に修正
- `CLAUDE.md` の Editor 拡張セクションから「表情プロファイル管理ウィンドウ（EditorWindow）」を削除し、Inspector カスタマイズの説明を更新

### テスト
- 手動検証中心（Inspector の操作確認）
- 既存の `ProfileCreationTests`・`ProfileEditSaveTests` が引き続き Green であること

### 完了基準
- FacialProfileSO の Inspector から Expression の追加・編集・削除・検索ができる
- Inspector から JSON のインポート・エクスポートができる
- Inspector から新規プロファイルを作成できる
- `ProfileManagerWindow` が完全に削除されている
- 既存テストが全て Green
- 要件ドキュメントが実態と一致している

---

## P19: Inspector UI 改善（フィードバック対応）

### 目的

プロトタイプ段階のユーザーフィードバックに基づき、FacialProfileSO Inspector の操作性向上、入力デバイス対応拡充、バグ修正を行う。

### 作業内容

#### P19-01: JSON ファイルパスを読み取り専用表示に変更
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: インポート機能が実装済みのため、ファイルパスの手動テキスト入力は不要。読み込んだファイルのパスを表示するだけで十分
- **変更内容**:
  - `_jsonFilePath` の `PropertyField`（テキスト入力可能）を読み取り専用の `Label` に変更
  - インポート / JSON 読み込み成功時にパスを Label に反映
  - 既存の `HelpBox`（"パスは StreamingAssets/ からの相対パスです"）は維持

#### P19-02: Expression の追加/削除を Unity 標準 List UI に置き換え
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: 現在のカスタムボタン方式は直感的でない。Unity エンジニアが慣れている標準の配列/List 操作 UI を使用する
- **変更内容**:
  - 現在の「Expression 追加」ボタン + 各 Expression 内「削除」ボタンを撤去
  - Unity 標準の List 操作 UI パターン（`+` / `-` ボタン、または `ListView` のリオーダー対応）に置き換え
  - Expression 検索機能（`_searchField`）はオミット（List UI との干渉を回避）
  - 各 Expression の Foldout 内の編集フィールド（名前、レイヤー、遷移時間）は維持
- **実装方針**:
  - UI Toolkit の `ListView` もしくは手動の `+` / `-` ボタン付きリスト構造を採用
  - Expression の追加/削除後は JSON 自動保存 → UI 再構築の既存フローを維持

#### P19-03: Expression 内 BlendShape の Weight 値編集を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P19-02
- **背景**: 現在 BlendShape の Weight 値は `Label`（読み取り専用）で表示されており、Inspector だけで Expression を完結して編集できない
- **変更内容**:
  - BlendShape の Weight 値表示を `Label` → `Slider`（0〜1 範囲）+ `FloatField` に変更
  - `ExpressionEditData` に `float[] BlendShapeValueEdits` フィールドを追加
  - `RebuildProfileFromEdits()` で Weight 編集値を反映（BlendShapeNameEdits と同様のパターン）
  - 値変更時に即座に `ExpressionEditData` を更新

#### P19-04: Expression 内 BlendShape の追加・削除機能を追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P19-03
- **背景**: Expression に BlendShape を Inspector から追加・削除できないと、JSON 手動編集が必須になる
- **変更内容**:
  - BlendShape 一覧セクション内に「BlendShape 追加」ボタンを設置
  - 追加時: 参照モデル設定済みならドロップダウンで BlendShape 名選択、未設定なら TextField で名前入力。Weight 初期値は 0
  - 各 BlendShape 行に「×」（削除）ボタンを設置
  - 追加/削除後は `_cachedProfile` を即時更新し、JSON 自動保存 → UI 再構築

#### P19-05: Expression の追加/削除の Undo を修正
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P19-02
- **背景**: `Undo.RecordObject()` で SO の表示用フィールド変更を記録しているが、実データは JSON ファイルに書き込まれるため Undo 操作で JSON が元に戻らない
- **変更内容**:
  - `Undo.undoRedoPerformed` コールバックを登録し、Undo/Redo 発火時に SO の JSON パスから JSON を再読み込み → `_cachedProfile` を再構築 → UI を更新するフローを実装
  - Undo 対象データとして、JSON 文字列自体を SO の SerializeField（`[HideInInspector]` 付き）に保持することを検討
  - 代替案: Undo 前の JSON 文字列をスタックに保持し、Undo 発火時に復元書き込み
- **実装方針**:
  - シンプルな方式として、操作前に JSON 文字列全体を `Undo.RecordObject()` の対象フィールドに保持
  - Undo 発火時にそのフィールドの JSON 文字列を JSON ファイルに書き戻し、`_cachedProfile` と UI を同期

#### P19-06: 新規プロファイル作成ボタンをクローン機能に置き換え
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: Inspector 上の「新規プロファイル作成」ボタンは文脈にそぐわない。既存プロファイルのクローン作成のほうが実用的
- **変更内容**:
  - 「新規プロファイル作成」ボタン → 「プロファイルのクローンを作成」ボタンに変更
  - 処理フロー:
    1. `EditorUtility.SaveFilePanel` で保存先を選択（デフォルトファイル名: `{元のプロファイル名}_copy.json`）
    2. 現在の `_cachedProfile` をシリアライズして新しい JSON ファイルに保存
    3. `AssetDatabase.CreateAsset()` で新しい `FacialProfileSO` を生成
    4. 新しい SO の `_jsonFilePath` を保存先相対パスに設定
    5. 作成した SO を Selection に設定して Inspector で開く
  - `_cachedProfile` が null の場合はボタンを無効化（プロファイル未読み込み時）

#### P19-07: Xbox コントローラに LT/RT バインディングを追加（Value タイプ）
- **対象ファイル**:
  - `Runtime/Adapters/Input/FacialControlDefaultActions.inputactions`
  - `Templates/default_inputactions.inputactions`
- **背景**: P17-05 で LT/RT はスコープ外としていたが、対応が必要
- **変更内容**:
  - Trigger11, Trigger12 のアクション定義を追加
  - **重要**: `"type": "Value"`, `"expectedControlType": "Axis"` とする（Button ではなく 0〜1 の連続値）

  | ボタン | InputSystem パス | 割り当て先 | タイプ |
  |--------|-----------------|-----------|--------|
  | LT | `<Gamepad>/leftTrigger` | Trigger11 | Value (Axis) |
  | RT | `<Gamepad>/rightTrigger` | Trigger12 | Value (Axis) |

  - 両ファイルに同一のアクション定義・バインディングを追加
  - `InputSystemAdapter` は既に Value タイプ対応済み（`action.type == InputActionType.Button` で分岐）のため変更不要

#### P19-08: ARKit 検出ツールのボタン表示崩れを修正
- **対象ファイル**: `Editor/Windows/ARKitDetectorWindow.cs`
- **背景**: 検出実行後に各ボタンの見た目が潰れる（UI レイアウト崩れ）
- **調査ポイント**:
  - 検出結果の `ScrollView`（`flexGrow = 1`）が生成ボタンセクションの領域を圧迫している可能性
  - `actionSection` / `generateSection` の `flexShrink` が未設定のためレイアウト計算で縮小される可能性
- **想定される修正**:
  - ボタンセクション（`actionSection`, `generateSection`）に `flexShrink = 0` を設定
  - ボタンに `minHeight` を設定して潰れを防止
  - `ScrollView` のサイズ制約を適切に設定

#### P19-09: 参照モデルの ObjectField で Scene オブジェクトと Packages フォルダを選択可能にする
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: 現在 `allowSceneObjects = false` のため、Packages フォルダ内の FBX から Hierarchy に配置した GameObject を選択できない
- **変更内容**:
  - `ObjectField` の `allowSceneObjects` を `true` に変更
  - これにより Hierarchy 上の GameObject（Packages フォルダの FBX を配置したもの含む）を選択可能になる
  - Scene オブジェクト選択時の注意: Scene が閉じられると参照が切れる点は許容（Editor 専用フィールドのため）

#### P19-10: RendererPaths 検出結果の JSON 保存を修正
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: `OnDetectRendererPathsClicked()` で検出した RendererPaths は SO のフィールドに保存されるが、`_cachedProfile` が更新されないため「JSON に保存」を押しても検出結果が JSON に含まれない
- **変更内容**:
  - `OnDetectRendererPathsClicked()` の末尾で `_cachedProfile` の RendererPaths を更新
  - 方法: 現在の `_cachedProfile` から新しい `FacialProfile` を RendererPaths 付きで再構築してキャッシュを差し替える
  ```csharp
  if (_cachedProfile != null)
  {
      var p = _cachedProfile.Value;
      _cachedProfile = new FacialProfile(
          p.SchemaVersion, p.Layers.ToArray(), p.Expressions.ToArray(), paths);
  }
  ```
  - 「JSON に保存」押下時に RendererPaths が正しく JSON に含まれることを確認

### テスト（EditMode/）
- **P19-T01**: `ProfileRebuildBlendShapeWeightTests` — `RebuildProfileFromEdits` 相当ロジックで BlendShape Weight 編集値が正しく反映されるか検証
  - Weight 編集前後のラウンドトリップ（元の値を変更 → 再構築 → 値が一致）
  - 複数 BlendShape の同時編集
  - 範囲外値（負数、1超）のクランプ確認
- **P19-T02**: `ProfileBlendShapeAddRemoveTests` — Expression 内 BlendShape の追加・削除後にプロファイルが正しく再構築されるか検証
  - BlendShape 追加後の Expression.BlendShapeValues.Length 増加
  - BlendShape 削除後の Expression.BlendShapeValues.Length 減少
  - 追加した BlendShape の名前と Weight が保持される
  - 削除後に残りの BlendShape の順序と値が維持される
  - JSON シリアライズ → パースのラウンドトリップ
- **P19-T03**: `RendererPathsCacheUpdateTests` — RendererPaths 検出後のキャッシュ更新 → JSON ラウンドトリップ検証
  - FacialProfile に RendererPaths を差し替えて再構築 → シリアライズ → パース → RendererPaths が一致
  - 既存の Layers / Expressions が RendererPaths 差し替え後も維持される
  - 空 RendererPaths → 非空への更新、非空 → 別の非空への更新

### 手動検証
- Inspector の UI 操作確認（Expression List UI、BlendShape 編集、Undo/Redo、クローン作成）
- ARKit 検出ツールのボタン表示確認
- 参照モデルの Scene オブジェクト / Packages フォルダ選択確認
- 既存テスト（`ProfileCreationTests`、`ProfileEditSaveTests`、`FacialProfileRendererPathsTests` 等）が引き続き Green であること

### 完了基準
- JSON ファイルパスが読み取り専用表示になっている
- Expression の追加/削除が Unity 標準 UI で操作できる
- Inspector だけで Expression の BlendShape（名前 + Weight）を完結して編集できる
- Expression の追加/削除の Undo/Redo が正しく動作する
- 「プロファイルのクローンを作成」で現在のプロファイルを複製できる
- LT/RT が Value タイプで Trigger11/12 に割り当てられている
- ARKit 検出ツールのボタンが検出後も正常に表示される
- 参照モデルに Scene オブジェクトと Packages フォルダ内アセットを指定できる
- RendererPaths 検出結果が「JSON に保存」で正しく JSON に含まれる

## P20: Inspector UI 改善 第2弾（フィードバック対応）

### 目的

ユーザーフィードバックに基づき、FacialProfileSO Inspector のレイアウト改善、表記統一、操作性向上、バグ修正を行う。

### 作業内容

#### P20-01: 「参照モデル」の表記を「使用モデル」に変更
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: 「参照モデル」という名称がわかりにくい。実際に使用するモデルであるため「使用モデル」に統一する
- **変更内容**:
  - `ReferenceModelSectionLabel` 定数を `"参照モデル"` → `"使用モデル"` に変更
  - `ObjectField` のラベルを `"参照モデル"` → `"使用モデル"` に変更
  - `OnDetectRendererPathsClicked()` のエラーメッセージ `"参照モデルが設定されていません。"` → `"使用モデルが設定されていません。"` に変更
  - コメント・XMLDoc 内の「参照モデル」も「使用モデル」に置換

#### P20-02: 使用モデルセクションを Inspector 最上部に移動
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P20-01
- **背景**: モデル指定は最初に行う操作であるため、Inspector の最上部に配置すべき
- **変更内容**:
  - `CreateInspectorGUI()` 内で使用モデルセクション（`refModelFoldout`）の `root.Add()` を、クローンボタンの直後（現在の位置から移動）に配置
  - 現在の配置順: クローンボタン → JSON → プロファイル情報 → レイヤー → Expression → +/- ボタン → 参照モデル → JSON に保存
  - 変更後の配置順: クローンボタン → **使用モデル** → レイヤー → Expression → +/- ボタン → JSON に保存 → JSON ファイル（P20-03 で最下部へ）

#### P20-03: JSON ファイルセクションを Inspector 最下部に移動
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: JSON ファイルパスやインポート/エクスポートは頻繁に操作しないため、最下部に配置して主要な操作を上部に集約する
- **変更内容**:
  - `CreateInspectorGUI()` 内で JSON ファイルセクション（`jsonFoldout`）の `root.Add()` を最後に配置
  - `_statusLabel` は JSON セクション内に維持
  - 変更後の最終配置順: クローンボタン → 使用モデル → レイヤー → Expression → +/- ボタン → JSON に保存 → JSON ファイル

#### P20-04: プロファイル情報からレイヤー数・Expression 数の表示を削除
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: レイヤー数と Expression 数はそれぞれの一覧を見れば明らかであり、冗長な情報表示を削減する
- **変更内容**:
  - `_layerCountLabel` / `_expressionCountLabel` フィールドを削除
  - `CreateInspectorGUI()` 内の `_layerCountLabel` / `_expressionCountLabel` の生成・追加を削除
  - `UpdateProfileInfo()` 内の `_layerCountLabel` / `_expressionCountLabel` の更新処理を削除
  - プロファイル情報セクション（`infoFoldout`）自体はスキーマバージョンが P20-05 で移動するため削除可能

#### P20-05: スキーマバージョンを JSON ファイルセクション近くに移動
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **依存**: P20-03, P20-04
- **背景**: スキーマバージョンは日常的に確認する情報ではないため、JSON ファイルセクション（最下部）付近に目立たない形で配置する
- **変更内容**:
  - プロファイル情報セクション（`infoFoldout`）を完全に削除（レイヤー数・Expression 数は P20-04 で削除済み）
  - `_schemaVersionLabel` を JSON ファイルセクション（`jsonFoldout`）内の先頭（`_jsonPathLabel` の上）に移動
  - スタイルを小さめのフォントや薄いグレーにして目立たない表示にする

#### P20-06: BlendShape 削除時に Expression の Foldout が折りたたまれる不具合を修正
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: BlendShape の「×」ボタンを押すと `SaveProfileAndRebuild()` → `RebuildDetailUI()` が呼ばれ、全 Expression Foldout が `value = false`（折りたたみ）で再構築されるため、操作中の Foldout が閉じてしまう
- **変更内容**:
  - `RebuildDetailUI()` 呼び出し前に各 Expression Foldout および子 BlendShape Foldout の開閉状態を保存する仕組みを追加
  - 方法: `Dictionary<int, bool>` で Expression インデックス → Foldout 開閉状態をキャッシュ。BlendShape Foldout も同様にキャッシュ
  - `BuildExpressionDetailUI()` で Foldout 生成時にキャッシュされた開閉状態を復元（`exprFoldout.value = cachedState`）
  - `OnRemoveBlendShapeClicked()` / `OnAddBlendShapeClicked()` / `OnAddExpressionClicked()` / `OnDeleteExpressionClicked()` の全てで開閉状態を保存してから `RebuildDetailUI()` を呼ぶ
  - `_expressionFoldoutStates`（`Dictionary<int, bool>`）と `_blendShapeFoldoutStates`（`Dictionary<int, bool>`）をクラスフィールドとして追加

#### P20-07: BlendShape 選択ドロップダウンに検索入力ボックスを追加
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: BlendShape 名が多数（100 以上）ある場合、ドロップダウンからの選択が困難。検索で絞り込めるようにする
- **変更内容**:
  - `BuildExpressionDetailUI()` 内の BlendShape 名 `DropdownField` を検索機能付きの UI に置き換え
  - 実装方法: `DropdownField` の代わりに `TextField`（検索入力）+ フィルタ済みリストの `ListView` または `PopupField` を組み合わせたカスタム UI を構築
  - 具体的には:
    - `TextField`（検索ボックス）を配置。プレースホルダーテキスト: `"BlendShape を検索..."`
    - 入力値で `allBlendShapeNames` を部分一致フィルタし、結果リストを `DropdownField` の `choices` に動的反映
    - 検索テキストが空の場合は全候補を表示
  - BlendShape 追加セクション（`addBsDropdown`）にも同様の検索入力を追加

#### P20-08: レイヤー優先度をドラッグ順序で設定し、数値はラベル表示のみに変更
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: 優先度を数字で手入力するのは直感的でない。リストの上下順（表示順）で優先度を決定し、結果の数値だけ表示する方が操作しやすい
- **変更内容**:
  - `BuildLayerDetailUI()` の各レイヤーに対する `IntegerField`（優先度入力）を `Label`（優先度表示のみ）に変更
  - 優先度値はリスト内の位置（上から 0, 1, 2, ...）で自動設定
  - 各レイヤー行に上下移動ボタン（`▲` / `▼`）を追加
  - 上下移動ボタン押下時に `_layerEdits` リスト内の要素を入れ替え、UI を再構築
  - `LayerEditData.Priority` は上下移動後にリスト位置から自動算出
  - `RebuildProfileFromEdits()` 内で `_layerEdits` のインデックスをそのまま `Priority` として使用

#### P20-09: JSON 読み込みボタンを削除
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: Inspector 表示時に `TryLoadCachedProfile()` で自動読み込みしており、インポート機能も別途あるため、手動の「JSON 読み込み」ボタンは不要
- **変更内容**:
  - `CreateInspectorGUI()` 内の `loadButton`（`"JSON 読み込み"` ボタン）の生成・追加を削除
  - `OnLoadJsonClicked()` メソッドは削除せず内部メソッドとして維持（`TryLoadCachedProfile()` やインポート処理で間接的に使用される可能性があるため）
  - `OnLoadJsonClicked()` が他から呼ばれていない場合は合わせて削除

### テスト
- 手動検証中心（Inspector のレイアウト・操作確認）
- 既存テスト（`ProfileEditSaveTests`、`ProfileBlendShapeAddRemoveTests` 等）が引き続き Green であること

### 完了基準
- 「参照モデル」の表記が全て「使用モデル」に統一されている
- Inspector の最上部に使用モデルセクションが表示される
- JSON ファイルセクションが Inspector の最下部に表示される
- レイヤー数・Expression 数の冗長な表示が削除されている
- スキーマバージョンが JSON ファイルセクション付近に目立たない形で表示されている
- BlendShape 削除後も操作中の Foldout が展開されたまま維持される
- BlendShape 選択ドロップダウンで検索による絞り込みができる
- レイヤーの優先度がリストの上下順で決定され、数値はラベルで表示される
- JSON 読み込みボタンが削除されている
- 既存テストが全て Green

---

## P21: Inspector・Editor 改善 第3弾（フィードバック対応）

### 目的

ARKit 検出ツールの JSON 保存後処理の改善、Inspector のインポート・クローン周りの UX 改善、使用モデルの Hierarchy 対応を行う。

### 作業内容

#### P21-01: ARKit 検出ツールで JSON 保存後に AssetDatabase.Refresh を実行
- **対象ファイル**: `Editor/Windows/ARKitDetectorWindow.cs`
- **背景**: ARKit 検出ツールで Expression JSON や OSC Config JSON を StreamingAssets 等に保存した後、`AssetDatabase.Refresh()` が呼ばれないため Unity Editor がファイルの追加・変更を認識しない。保存先が Assets フォルダ配下の場合、Project ウィンドウに反映されず手動 Refresh が必要になる
- **変更内容**:
  - `OnGenerateExpressionsClicked()` の JSON 保存成功後（`ShowStatus` の前）に `AssetDatabase.Refresh()` を追加
  - `OnGenerateOscClicked()` の JSON 保存成功後（`ShowStatus` の前）に `AssetDatabase.Refresh()` を追加
  - 保存先がプロジェクト外の場合も副作用はないため、無条件で呼ぶ

#### P21-02: JSON インポートで JsonFilePath 未設定時もファイル選択ダイアログを表示
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: 現在の `OnImportClicked()` は `so.JsonFilePath` が未設定の場合にエラーメッセージを表示してファイル選択ダイアログを開かずに return する（305〜315 行目）。新規作成した SO に対して外部 JSON をインポートしたいケースでダイアログが開かない
- **変更内容**:
  - `OnImportClicked()` 先頭の `JsonFilePath` 空チェック（311〜315 行目）を削除し、常にファイル選択ダイアログを開く
  - ファイル選択後のパース成功時:
    - `so.JsonFilePath` が設定済みの場合: 従来通り StreamingAssets 配下の該当パスへ JSON を書き出し、SO を更新
    - `so.JsonFilePath` が未設定の場合: JSON ファイルの書き出しをスキップし、パース結果で SO のフィールドのみを更新（`mapper.UpdateSO()` と `_cachedProfile` の更新は実行する）
  - ステータス表示を分岐: 書き出しを行った場合は `"インポートしました: {filename}"`、SO 更新のみの場合は `"インポートしました（JSON ファイル書き出しはスキップ）: {filename}"`

#### P21-03: クローン作成ボタンを削除
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`
- **背景**: プロファイルのクローンは Unity 標準のアセット複製（Ctrl+D）で代替できるため、専用ボタンは不要
- **変更内容**:
  - `_cloneProfileButton` フィールド宣言（53 行目付近）を削除
  - `CreateInspectorGUI()` 内のクローンボタン生成・`root.Add()` 処理（101〜107 行目付近）を削除
  - `OnCloneProfileClicked()` メソッド全体（396〜479 行目付近）を削除
  - `UpdateProfileInfo()` 等で `_cloneProfileButton.SetEnabled()` を呼んでいる箇所（1005〜1006、1034〜1035 行目付近）を削除
  - Inspector の配置順が変更: 使用モデル → レイヤー → Expression → +/- ボタン → JSON に保存 → JSON ファイル

#### P21-04: 使用モデルに Hierarchy 上の GameObject をアタッチ可能にする
- **対象ファイル**: `Editor/Inspector/FacialProfileSOEditor.cs`, `Runtime/Adapters/ScriptableObject/FacialProfileSO.cs`
- **背景**: `FacialProfileSO._referenceModel` は ScriptableObject の `[SerializeField]` フィールドであるため、Hierarchy 上の Scene オブジェクトへの参照はドメインリロードやエディタ再起動で失われる。`allowSceneObjects = true` は設定済みだが、`BindProperty` による SerializedProperty バインディングでは Scene オブジェクトが正しく保持されないケースがある
- **変更内容**:
  - `FacialProfileSOEditor` に非シリアライズのエディタ専用フィールド `private GameObject _sceneReferenceModel` を追加（Scene オブジェクト参照の一時保持用）
  - 使用モデルの `ObjectField` を `BindProperty` から `RegisterValueChangedCallback` に切り替え:
    - 値変更時にアセット参照（Prefab 等）なら `so.ReferenceModel` に保存、Scene オブジェクトなら `_sceneReferenceModel` に保存
    - ObjectField の初期値は `so.ReferenceModel ?? _sceneReferenceModel` で設定
  - `OnDetectRendererPathsClicked()` 等の使用モデル参照箇所で `so.ReferenceModel ?? _sceneReferenceModel` を使用するヘルパープロパティ `ActiveReferenceModel` を追加
  - `allowSceneObjects = true` は維持
  - Scene オブジェクト参照はセッション中のみ有効であることを Inspector のツールチップで明示（例: `"Hierarchy 上のオブジェクトはセッション中のみ有効です"`）

### テスト
- 手動検証中心（Editor 操作確認）
- 既存テスト（`ProfileEditSaveTests`、`ProfileBlendShapeAddRemoveTests`、`RendererPathsCacheUpdateTests` 等）が引き続き Green であること

### 完了基準
- ARKit 検出ツールで JSON 保存後に Project ウィンドウへファイルが即時反映される
- JsonFilePath 未設定の SO でもインポートボタンでファイル選択ダイアログが表示される
- クローン作成ボタンが Inspector から削除されている
- Hierarchy 上の GameObject を使用モデルにドラッグ＆ドロップでき、RendererPaths 自動検出が動作する
- 既存テストが全て Green

---

## P22: リリース前クリーンアップ

### 目的

P18-06 の実施漏れ修正、ファイル重複の解消、ドキュメント整合性の確保、全テスト最終通過確認、および npmjs.com 公開を行い preview.1 をリリースする。

### 作業内容

#### P22-01: ProfileManagerWindow.cs と .meta の削除（P18-06 実施漏れ）
- **対象ファイル**: `Editor/Windows/ProfileManagerWindow.cs`、対応する `.meta`
- **背景**: P18-06 で削除予定だったが実施されなかった。P18 で Inspector に統合済みのため不要
- **変更内容**:
  - `Editor/Windows/ProfileManagerWindow.cs` と `.meta` を削除
  - `MenuItem "FacialControl/プロファイル管理"` が消滅することを確認
  - コンパイルエラーが発生しないことを確認

#### P22-02: OscReceiverPlayable.cs の重複解消
- **対象ファイル**:
  - `Runtime/Adapters/Playable/OscReceiverPlayable.cs`
  - `Runtime/Adapters/OSC/OscReceiverPlayable.cs`
- **背景**: 同一ファイルが 2 箇所に存在しており、コンパイルエラーまたは意図しない動作の原因になり得る
- **変更内容**:
  - P07-05 の設計意図に従い、PlayableGraph 統合用のため `Runtime/Adapters/Playable/` に正本を残す
  - `Runtime/Adapters/OSC/OscReceiverPlayable.cs` と `.meta` を削除
  - テスト（`OscReceiverPlayableTests`）の参照が正しいことを確認

#### P22-03: README.md の ProfileManagerWindow 記述を更新
- **対象ファイル**: `README.md`（パッケージ内）
- **背景**: P18 で Inspector に統合済みだが、README に「プロファイル管理ウィンドウ」の記述が残っている
- **変更内容**:
  - 「Inspector カスタマイズ、プロファイル管理ウィンドウ、BlendShape スライダー付き Expression 作成ツール」→「Inspector カスタマイズ（プロファイル管理を統合）、BlendShape スライダー付き Expression 作成ツール」に修正

#### P22-04: CHANGELOG.md の更新
- **対象ファイル**: `CHANGELOG.md`（パッケージ内）
- **背景**: P18 で Inspector に統合済みだが、CHANGELOG に `ProfileManagerWindow` の記述が残っている。また P17〜P21 の変更が未反映
- **変更内容**:
  - Editor 拡張セクションから `ProfileManagerWindow` 行を削除し Inspector 統合の記述に置換
  - P17〜P21 の主要変更を CHANGELOG に反映（Inspector 一本化、RendererPaths、UI 改善等）

#### P22-05: P17〜P21 追加分の XML コメント確認・補完
- **対象ファイル**: P17〜P21 で追加・変更された全公開 API
- **背景**: P15-01 で全公開 API に XML コメントを記述したが、P17〜P21 で追加されたフィールド・メソッド・プロパティに XML コメントが付与されていない可能性がある
- **変更内容**:
  - P17〜P21 で追加された公開メンバーを洗い出し、XML コメント未記述のものを補完
  - 特に `FacialProfile.RendererPaths`、`FacialProfileSO.RendererPaths`、`FacialProfileSO.ReferenceModel` 等

#### P22-06: 全テスト最終通過確認（EditMode + PlayMode）
- **背景**: P17〜P21 で Inspector コードを大幅に変更しており、全テストのリグレッション確認が必要
- **変更内容**:
  - EditMode 全テスト実行 → Green 確認
  - PlayMode 全テスト実行 → Green 確認
  - 失敗テストがあれば原因を特定し修正

#### P22-07: CHANGELOG.md の日付更新
- **対象ファイル**: `CHANGELOG.md`（パッケージ内）
- **依存**: P22-04
- **変更内容**:
  - `## [0.1.0-preview.1] - Unreleased` → `## [0.1.0-preview.1] - {リリース日}` に更新

#### P22-08: uOsc フォーク（com.hidano.uosc）の npmjs.com 公開確認
- **背景**: `package.json` で `com.hidano.uosc` を必須依存として宣言しているため、npmjs.com から取得可能であることを確認する
- **変更内容**:
  - `npm view com.hidano.uosc` で公開状況を確認
  - 未公開の場合は uOsc フォークリポジトリから公開手順を実施

#### P22-09: npmjs.com へのパッケージ公開
- **依存**: P22-01〜P22-08 すべて完了
- **変更内容**:
  - `npm publish` で `com.hidano.facialcontrol@0.1.0-preview.1` を公開
  - 公開後に `npm view com.hidano.facialcontrol` で確認
  - 別プロジェクトからインストールテスト

#### P22-10: バージョンタグ作成
- **依存**: P22-09
- **変更内容**:
  - `git tag v0.1.0-preview.1` でタグ作成
  - `git push origin v0.1.0-preview.1` でリモートに push

### テスト
- **P22-T01**: P22-06 で全 EditMode / PlayMode テストの Green を確認

### 完了基準
- `ProfileManagerWindow.cs` が削除されている
- `OscReceiverPlayable.cs` が 1 箇所（`Runtime/Adapters/Playable/`）のみに存在する
- README.md / CHANGELOG.md が P18〜P21 の変更を反映している
- 全公開 API に XML コメントが付与されている
- 全 EditMode / PlayMode テストが Green
- CHANGELOG.md にリリース日が記載されている
- npmjs.com から `com.hidano.facialcontrol` をインストールできる
- バージョンタグ `v0.1.0-preview.1` が作成されている

---

## P23: キーコンフィグマッピングの永続化

### 目的

InputAction と Expression の紐付けを ScriptableObject で永続化し、コード記述なしで Unity Editor から設定できるようにする。現状の `TestExpressionToggle.cs` はデバッグ用コード直書きで運用に耐えないため、`InputBindingProfileSO` + `FacialInputBinder` 方式に置き換え、preview.1 リリースの実用性を確保する。

要件: `docs/requirements-qa.md` Q8-3 参照。

### 作業内容

#### P23-01: InputBinding ドメインモデルの定義
- **対象ファイル**: `Runtime/Domain/Models/InputBinding.cs`（新規）
- **内容**:
  - `InputBinding` struct（`ActionName: string`, `ExpressionId: string`）
  - Unity 非依存のイミュータブル値型（`readonly` フィールド）
  - 等価性判定（`IEquatable<InputBinding>`）
  - バリデーション: `ActionName`・`ExpressionId` が空文字 / null の場合はコンストラクタで `ArgumentException`

#### P23-02: InputBindingProfileSO の ScriptableObject 新設
- **対象ファイル**: `Runtime/Adapters/ScriptableObject/InputBindingProfileSO.cs`（新規）
- **内容**:
  - `[SerializeField] InputActionAsset _actionAsset`
  - `[SerializeField] string _actionMapName = "Expression"`（デフォルトは同梱 InputActions の ActionMap 名）
  - `[SerializeField] List<InputBindingEntry> _bindings`（`InputBindingEntry` は `[Serializable]` な内部クラス。Unity のシリアライズ制約のため struct ではなく class）
  - `[CreateAssetMenu(menuName = "FacialControl/Input Binding Profile")]` を付与
  - Getter プロパティで `IReadOnlyList<InputBinding>` を返す（Domain モデルに変換）

#### P23-03: FacialInputBinder MonoBehaviour 新設
- **対象ファイル**: `Runtime/Adapters/Input/FacialInputBinder.cs`（新規）
- **責務**:
  - `[SerializeField] FacialController _facialController`
  - `[SerializeField] InputBindingProfileSO _bindingProfile`
  - `[AddComponentMenu("FacialControl/Facial Input Binder")]`
  - `OnEnable` で `InputActionAsset` をインスタンス化し、対象 ActionMap を有効化
  - 内部で `InputSystemAdapter` を動的生成、Profile の Bindings を走査して各 Action と Expression を `BindExpression` で紐付け
  - Profile が null の場合は警告ログのみ（エラーを投げない）
  - Profile 内の `ExpressionId` が FacialController の現在のプロファイルに見つからない場合は警告ログ（該当バインディングのみスキップ）
  - `OnDisable` で UnbindAll + ActionMap の Disable

#### P23-04: InputBindingProfileSOEditor（Inspector カスタマイズ）
- **対象ファイル**: `Editor/Inspector/InputBindingProfileSOEditor.cs`（新規）
- **実装**: UI Toolkit
- **機能**:
  - InputActionAsset ObjectField（`_actionAsset` にバインド）
  - 参照用 FacialProfileSO ObjectField（Editor 専用・SO 本体には保存しない）
  - ActionMap ドロップダウン（選択 Asset から ActionMap 名を動的列挙）
  - バインディング一覧: 各行に Action ドロップダウン + Expression ドロップダウン + 削除ボタン
    - Action ドロップダウンは選択 ActionMap の Action 名を列挙
    - Expression ドロップダウンは参照 FacialProfileSO の Expression 名を列挙（保存時は ExpressionId で記録）
  - バインディング追加ボタン
  - Asset または ActionMap 変更時に一覧を自動再構築
  - 共通スタイル `FacialControlStyles` を利用

#### P23-05: サンプルシーンの更新
- **対象ファイル**（最終実装）:
  - `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/` 一式（UPM 配布用 canonical）
    - `MultiSourceBlendDemo.unity`（Scene）
    - `MultiSourceBlendDemoProfile.asset`（FacialProfileSO）
    - `MultiSourceBlendDemoInputBinding.asset`（InputBindingProfileSO）
    - `MultiSourceBlendDemoHUD.cs`（HUD + TextAsset ブートストラップ）
    - `multi_source_blend_demo.json`（プロファイル JSON）
    - `README.md`
  - ~~`FacialControl/Assets/Samples/FacialControl InputSystem/<version>/Multi Source Blend Demo/` 一式（dev ミラー）~~ **2026-08-25 に廃止**。dev 確認は Package Manager の Import Sample 経由で行い、展開結果はコミットしない（本手順書「Samples 編集手順」を参照）
- **内容**:
  - Scene は `Character` / `Facial Input Binder` / `Multi Source Blend Demo HUD` の 3 GameObject に役割分割
    - `Character`: Animator + FacialController + InputFacialControllerExtension（Extension は同 GO 必須）
    - `Facial Input Binder`: FacialInputBinder（Character の FacialController を参照）
    - `Multi Source Blend Demo HUD`: HUD（JSON TextAsset + Character の FacialController を参照）
  - HUD が Awake で JSON TextAsset をパースし `InitializeWithProfile` を呼び出すため、StreamingAssets 配置不要
  - `InputBindingProfileSO`: `FacialControlDefaultActions.inputactions` を参照し、`Trigger1`〜`Trigger4` = `smile` / `angry` / `surprise` / `troubled` にバインド（category=Keyboard）
  - 操作: HUD の On/Off ボタンまたはキーボード `1`〜`4` キーで表情発動。`Controller weight` / `Keyboard weight` スライダーで加重和ブレンドを目視確認
  - モデルはライセンスの都合で同梱せず、ユーザーが `Character` GameObject の子として配置する

#### P23-06: クイックスタート・ドキュメント更新
- **対象ファイル**:
  - `Packages/com.hidano.facialcontrol/Documentation~/quickstart.md`
  - `Packages/com.hidano.facialcontrol/README.md`
  - `Packages/com.hidano.facialcontrol/CHANGELOG.md`
- **内容**:
  - quickstart.md に「6. キーコンフィグの設定」セクションを追加
    - `InputBindingProfileSO` の作成手順
    - `FacialInputBinder` の配置と割り当て
    - `FacialControlDefaultActions.inputactions` の `Trigger1〜Trigger12` スロット説明
    - カスタマイズ方法（InputActions Asset を複製してキーバインドを変更する手順）
  - README.md の主要機能に「キーコンフィグの永続化」項目を追加
  - CHANGELOG.md の preview.1 エントリに P23 の変更を記載

#### P23-07: テスト
- **対象ファイル**:
  - `Tests/EditMode/Domain/InputBindingTests.cs`（新規）
  - `Tests/EditMode/Adapters/InputBindingProfileSOTests.cs`（新規）
  - `Tests/PlayMode/Integration/FacialInputBinderTests.cs`（新規）
- **内容**:
  - Domain モデルの等価性・バリデーションを単体テスト
  - ScriptableObject のバインディング追加・削除・シリアライズを単体テスト
  - PlayMode で実 InputAction を `Press()` してトリガーし、FacialController の Expression がアクティブ化されることを統合テスト

### テスト
- **P23-T01**: `InputBindingTests`（EditMode）
- **P23-T02**: `InputBindingProfileSOTests`（EditMode）
- **P23-T03**: `FacialInputBinderTests`（PlayMode）
- **P23-T04**: 既存 `InputSystemAdapterTests`（PlayMode）が引き続き Green

### 完了基準
- `InputBinding`, `InputBindingProfileSO`, `FacialInputBinder` が実装されている
- Inspector でバインディングを GUI 編集できる（InputActionAsset と FacialProfileSO を指定すると Action / Expression が自動列挙される）
- `com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo` に Scene / FacialProfileSO / InputBindingProfileSO / JSON / HUD 一式が揃い、モデル配置のみで Play 可能
- サンプル Scene が 3 GameObject（Character / Facial Input Binder / Multi Source Blend Demo HUD）に役割分割されている
- HUD の TextAsset bootstrap により StreamingAssets 配置なしで初期化できる
- `TestExpressionToggle.cs` が削除されている
- quickstart.md にキーコンフィグ設定手順が記載されている
- 全 EditMode / PlayMode テストが Green（P23-T01〜T04 + 既存）

### preview.1 スコープへの影響
P23 は preview.1 リリースに含める。これにより以下の順序調整が発生する:
- P22-04（CHANGELOG 更新）は P23 完了後に再実行（P23 変更を反映）
- P22-06（全テスト最終通過確認）は P23 完了後に再実行
- その後 P22-07（日付更新）→ P22-08 → P22-09 → P22-10 の順で公開・タグ付け

---

## spec: osc-output-binding 作業手順（preview.2）

### 目的
spec `osc-output-binding` に基づき、OSC 送信 binding、受信 binding の mode 別拡張、Gaze Vector2 送受信、JSON DTO / Drawer / Samples を preview.2 の配布単位として整備する。

### 作業順序

#### OOB-01: post-blend 出力観察基盤
- `IFacialOutputBus` / `IFacialOutputObserver` / `GazeSnapshot` を追加し、`FacialController.LateUpdate` から post-blend BlendShape と Gaze を publish する。
- `AdapterBuildContext` に output bus を渡し、送信 binding が core に逆依存しない形で購読できるようにする。

#### OOB-02: GazeBindingConfig と cross-binding slug 規約
- `GazeBindingConfig` は `expressionId` 必須、`useDistinctLeftRight`、条件付き `sourceIdLeft` / `sourceIdRight` の構造に統一する。
- OSC / InputSystem の Gaze source は `<slug>:<expressionId>` または `<slug>:<expressionId>.left/right` で解決する。

#### OOB-03: 受信側 `OscReceiverAdapterBinding` 拡張
- `OscMappingEntry` に `Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` の mode を持たせ、旧 BlendShape 専用 mapping から破壊的に移行する。
- bundle atomic swap、staleness fail-safe、sender identity によるゾンビ排除、heartbeat 整合性検査を同一 binding 内に集約する。

#### OOB-04: 送信側 `OscSenderAdapterBinding`
- `FacialOutputBus` を購読し、BlendShape と Gaze Vector2 を VRChat / ARKit preset の OSC bundle として送信する。
- 複数 endpoint、heartbeat、sender identity、loopback 抑制を binding 設定として保持する。

#### OOB-05: JSON / Drawer / Samples / 配布メタデータ
- `Documentation~/osc-sender-options.md` / `osc-receiver-options.md` に DTO スキーマとサンプル JSON を記載する。
- `OscSenderAdapterBindingDrawer` / `OscReceiverAdapterBindingDrawer` は UI Toolkit で実装し、mode 別 foldout と Gaze source 検証 HelpBox を持つ。
- `com.hidano.facialcontrol.osc/package.json` の `samples` 配列に `OscOutputDemo` / `OscReceiverDemo` を登録する。
- `CHANGELOG.md` と `README.md` は OSC 送信、受信拡張、Gaze Vector2 受信、`OscReceiverAdapterBinding` の破壊的変更を必ず記載する。

### Samples 編集手順

`Samples~/` を唯一の正本として扱う（`Assets/Samples/` への dev ミラーは 2026-08-25 に廃止・削除済み）。

1. サンプルの編集は必ず `FacialControl/Packages/{package}/Samples~/` 側で行う。
2. dev プロジェクトで動作確認する場合は Package Manager の Import Sample で `Assets/Samples/` へ展開する。展開結果はコミットしない（`Assets/Samples/` はリポジトリ管理下に置かない）。
3. Import した Scene / Asset を Unity Editor 側で編集した場合は、差分を確認して必要な変更だけ手で `Samples~/` へ戻す。
4. `OscSenderOptions.json` / `OscReceiverOptions.json` は `ConvertFrom-Json` 等で JSON として parse できることを確認する。
5. package.json の `samples[].path` は `Samples~/OscOutputDemo` / `Samples~/OscReceiverDemo` のまま維持する。dev 側 path を package.json に登録してはいけない。

### 完了基準
- Package Manager の Import Sample に `OscOutputDemo` と `OscReceiverDemo` が表示される。
- `Samples~/` の README / JSON / Scene / Profile / Bootstrap が最新の実装と整合している。
- `CHANGELOG.md` / `README.md` / `docs/backlog.md` が preview.2 の OSC 送受信スコープを反映している。
- `tasks.txt` には作業説明を追記しない。spec の詳細は本手順書に集約する（`.kiro/specs/osc-output-binding/` は SDD 廃止で削除済み。残作業は HID-151）。

---

## フェーズ間の依存関係

```
P00 (基盤)
  └→ P01 (Domain モデル)
      └→ P02 (Domain インターフェース)
          ├→ P03 (Domain サービス)
          │   └→ P04 (Application ユースケース)
          │       ├→ P05 (JSON パーサー)
          │       ├→ P06 (PlayableAPI) ←── 最重要・最大工数
          │       ├→ P07 (OSC 通信)
          │       └→ P08 (SO / Input / Controller)
          │           ├→ P09 (Inspector) ─────┐
          │           ├→ P10 (プロファイル管理) ┼→ P17 (Editor 改善)
          │           ├→ P11 (Expression 作成) │       │
          │           └→ P12 (ARKit 検出) ────┘       │
          │                                            └→ P18 (Inspector 一本化)
          │                                                    │
          │                                                    └→ P19 (Inspector UI 改善)
          │                                                            │
          │                                                            └→ P20 (Inspector UI 改善 第2弾)
          │                                                                    │
          │                                                                    └→ P21 (Inspector・Editor 改善 第3弾)
          │                                                                            │
          │                                                                            └→ P22 (リリース前クリーンアップ)
          │                                                                                    │
          │                                                                                    └→ P23 (キーコンフィグ永続化) ←── preview.1 に含める
          └→ P13 (テンプレート) ←── P05 の後でも可
              └→ P14 (統合・性能テスト) ←── P06〜P08 完了後
                  └→ P15 (ドキュメント)
                      └→ P16 (CI/CD・リリース)
```

**並行実行可能な組み合わせ**:
- P05 / P06 / P07 / P08 は P04 完了後に並行着手可能
- P09 / P10 / P11 / P12 は P08 完了後に並行着手可能
- P13 は P05 完了後ならいつでも着手可能
- P17 は P09 / P10 / P12 / P13 完了後に着手可能（P17-01〜P17-06 は並行着手可能、ただし P17-03 は P17-02 に依存。P17-07〜P17-12 は P17-07 → P17-08/P17-09 → P17-10 → P17-11 → P17-12 の順で依存）
- P18 は P17 完了後に着手可能（P18-01〜P18-05 は並行着手可能、P18-06 は P18-01〜P18-05 完了後、P18-07 は P18-06 完了後）
- P19 は P18 完了後に着手可能（P19-01/P19-06〜P19-10 は並行着手可能、P19-02 → P19-03 → P19-04 の順で依存、P19-02 → P19-05 の依存あり）
- P20 は P19 完了後に着手可能（P20-01 → P20-02 の依存あり、P20-03/P20-04 → P20-05 の依存あり、それ以外の P20-06〜P20-09 は並行着手可能）
- P21 は P20 完了後に着手可能（P21-01〜P21-04 は全て並行着手可能）
- P22 は P21 完了後に着手可能（P22-01〜P22-06 は並行着手可能、P22-04 → P22-07 の依存あり、P22-01〜P22-08 → P22-09 → P22-10 の順で依存）
- P23 は P08（InputSystemAdapter / InputActions 完成後）以降いつでも着手可能。preview.1 スコープに含めるため、P22-09 より前に完了させる。P23 完了後は P22-04（CHANGELOG 更新）と P22-06（全テスト通過確認）を再実行する必要がある。P23-01 → P23-02 → P23-03 の順で依存、P23-04 は P23-02 に依存、P23-05 は P23-03 に依存、P23-06 は P23-01〜P23-05 に依存、P23-07 は P23-01〜P23-03 に依存

---

## ID 一覧

### フェーズ一覧

| ID | フェーズ名 | 依存先 |
|----|-----------|--------|
| P00 | プロジェクト基盤構築 | — |
| P01 | Domain 層 — モデル定義 | P00 |
| P02 | Domain 層 — インターフェース定義 | P01 |
| P03 | Domain 層 — サービスロジック | P02 |
| P04 | Application 層 — ユースケース | P03 |
| P05 | Adapters 層 — JSON パーサー | P04 |
| P06 | Adapters 層 — PlayableAPI | P04 |
| P07 | Adapters 層 — OSC 通信 | P04 |
| P08 | Adapters 層 — SO / Input / Controller | P04 |
| P09 | Editor 拡張 — Inspector | P08 |
| P10 | Editor 拡張 — プロファイル管理ウィンドウ | P08 |
| P11 | Editor 拡張 — Expression 作成支援ツール | P08 |
| P12 | Editor 拡張 — ARKit 検出ツール | P08 |
| P13 | テンプレート・設定ファイル | P05 |
| P14 | 統合テスト・パフォーマンステスト | P06, P07, P08 |
| P15 | ドキュメント | P14 |
| P16 | CI/CD・リリース準備 | P15 |
| P17 | Editor 改善・テンプレート拡充 | P09, P10, P12, P13 |
| P18 | プロファイル管理を Inspector に一本化 | P17 |
| P19 | Inspector UI 改善（フィードバック対応） | P18 |
| P20 | Inspector UI 改善 第2弾（フィードバック対応） | P19 |
| P21 | Inspector・Editor 改善 第3弾（フィードバック対応） | P20 |
| P22 | リリース前クリーンアップ | P21 |
| P23 | キーコンフィグマッピングの永続化 | P08（preview.1 スコープ、P22-09 より前に完了） |

### 作業項目一覧（全 128 項目）

| ID | 作業項目 |
|----|---------|
| P00-01 | UPM パッケージディレクトリ作成 |
| P00-02 | package.json 作成 |
| P00-03 | Assembly Definition ファイル作成 |
| P00-04 | manifest.json にローカルパッケージ参照を追加 |
| P00-05 | .meta ファイルの生成確認 |
| P01-01 | 値オブジェクト / enum の定義 |
| P01-02 | LayerDefinition モデル |
| P01-03 | LayerSlot モデル |
| P01-04 | Expression モデル |
| P01-05 | FacialProfile モデル |
| P02-01 | IJsonParser インターフェース |
| P02-02 | IProfileRepository インターフェース |
| P02-03 | ILipSyncProvider インターフェース |
| P02-04 | IBlinkTrigger インターフェース |
| P02-05 | FacialControlConfig モデル |
| P02-06 | FacialState 構造体 |
| P02-07 | FacialOutputData 構造体 |
| P03-01 | TransitionCalculator サービス |
| P03-02 | ExclusionResolver サービス |
| P03-03 | LayerBlender サービス |
| P03-04 | ARKitDetector サービス |
| P04-01 | ProfileUseCase |
| P04-02 | ExpressionUseCase |
| P04-03 | LayerUseCase |
| P04-04 | ARKitUseCase |
| P05-01 | SystemTextJsonParser クラス |
| P05-02 | FileProfileRepository クラス |
| P05-03 | JSON スキーマ定義 |
| P06-01 | NativeArrayPool クラス |
| P06-02 | AnimationClipCache クラス（LRU） |
| P06-03 | PropertyStreamHandleCache クラス |
| P06-04 | LayerPlayable（ScriptPlayable） |
| P06-05 | FacialControlMixer（ScriptPlayable） |
| P06-06 | PlayableGraphBuilder クラス |
| P07-01 | uOsc パッケージ依存の追加 |
| P07-02 | OscDoubleBuffer クラス |
| P07-03 | OscReceiver クラス |
| P07-04 | OscSender クラス |
| P07-05 | OscReceiverPlayable（ScriptPlayable） |
| P07-06 | OscMappingTable クラス |
| P08-01 | FacialProfileSO（ScriptableObject） |
| P08-02 | FacialProfileMapper クラス |
| P08-03 | FacialController（MonoBehaviour） |
| P08-04 | InputSystemAdapter クラス |
| P08-05 | デフォルト InputAction Asset |
| P09-01 | FacialControllerEditor（CustomEditor） |
| P09-02 | FacialProfileSOEditor（CustomEditor） |
| P09-03 | UI Toolkit スタイル共通定義 |
| P10-01 | ProfileManagerWindow（EditorWindow） |
| P10-02 | JSON インポート / エクスポート |
| P11-01 | ExpressionCreatorWindow（EditorWindow） |
| P11-02 | PreviewRenderUtility ラッパー |
| P12-01 | ARKitDetectorWindow（EditorWindow） |
| P12-02 | Editor 向け ARKit 検出 API |
| P13-01 | デフォルトプロファイル JSON テンプレート |
| P13-02 | デフォルト config.json テンプレート |
| P13-03 | デフォルト InputAction Asset |
| P14-01 | 統合テスト |
| P14-02 | パフォーマンステスト |
| P15-01 | API リファレンス |
| P15-02 | クイックスタートガイド |
| P15-03 | JSON スキーマドキュメント |
| P15-04 | パッケージ README.md / CHANGELOG.md |
| P16-01 | GitHub Actions ワークフロー |
| P16-02 | パッケージバリデーション |
| P16-03 | npmjs.com 公開準備 |
| P16-04 | バージョンタグ |
| P17-01 | ProfileManagerWindow にプロファイル新規作成機能を追加 |
| P17-02 | FacialProfileSOEditor に JSON 内詳細情報の表示を追加 |
| P17-03 | FacialProfileSOEditor にインライン編集・JSON 上書き保存機能を追加 |
| P17-04 | FacialProfileSOEditor に JSONPath の説明を明記 |
| P17-05 | InputActions テンプレートに Xbox コントローラのバインディングを追加 |
| P17-06 | ARKit / PerfectSync 検出仕様の調査と修正 |
| P17-07 | FacialProfile に RendererPaths を追加 |
| P17-08 | JSON パーサーに rendererPaths の parse/serialize を追加 |
| P17-09 | FacialProfileSO に RendererPaths と参照モデルフィールドを追加 |
| P17-10 | FacialProfileMapper に RendererPaths 同期を追加 |
| P17-11 | FacialProfileSOEditor に参照モデル指定と RendererPaths 自動検出を追加 |
| P17-12 | FacialProfileSOEditor の BlendShape 値表示を DropdownField に変更 |
| P18-01 | Inspector に Expression 追加機能を追加 |
| P18-02 | Inspector に Expression 削除機能を追加 |
| P18-03 | Inspector に Expression 検索フィルタを追加 |
| P18-04 | Inspector にインポート・エクスポート機能を追加 |
| P18-05 | Inspector に新規プロファイル作成ボタンを追加 |
| P18-06 | ProfileManagerWindow・ExpressionEditDialog を削除 |
| P18-07 | 要件ドキュメント・CLAUDE.md を更新 |
| P19-01 | JSON ファイルパスを読み取り専用表示に変更 |
| P19-02 | Expression の追加/削除を Unity 標準 List UI に置き換え |
| P19-03 | Expression 内 BlendShape の Weight 値編集を追加 |
| P19-04 | Expression 内 BlendShape の追加・削除機能を追加 |
| P19-05 | Expression の追加/削除の Undo を修正 |
| P19-06 | 新規プロファイル作成ボタンをクローン機能に置き換え |
| P19-07 | Xbox コントローラに LT/RT バインディングを追加（Value タイプ） |
| P19-08 | ARKit 検出ツールのボタン表示崩れを修正 |
| P19-09 | 参照モデルの ObjectField で Scene オブジェクトを選択可能にする |
| P19-10 | RendererPaths 検出結果の JSON 保存を修正 |
| P20-01 | 「参照モデル」の表記を「使用モデル」に変更 |
| P20-02 | 使用モデルセクションを Inspector 最上部に移動 |
| P20-03 | JSON ファイルセクションを Inspector 最下部に移動 |
| P20-04 | プロファイル情報からレイヤー数・Expression 数の表示を削除 |
| P20-05 | スキーマバージョンを JSON ファイルセクション近くに移動 |
| P20-06 | BlendShape 削除時に Expression の Foldout が折りたたまれる不具合を修正 |
| P20-07 | BlendShape 選択ドロップダウンに検索入力ボックスを追加 |
| P20-08 | レイヤー優先度をドラッグ順序で設定し数値はラベル表示のみに変更 |
| P20-09 | JSON 読み込みボタンを削除 |
| P21-01 | ARKit 検出ツールで JSON 保存後に AssetDatabase.Refresh を実行 |
| P21-02 | JSON インポートで JsonFilePath 未設定時もファイル選択ダイアログを表示 |
| P21-03 | クローン作成ボタンを削除 |
| P21-04 | 使用モデルに Hierarchy 上の GameObject をアタッチ可能にする |
| P22-01 | ProfileManagerWindow.cs と .meta の削除（P18-06 実施漏れ） |
| P22-02 | OscReceiverPlayable.cs の重複解消 |
| P22-03 | README.md の ProfileManagerWindow 記述を更新 |
| P22-04 | CHANGELOG.md の更新 |
| P22-05 | P17〜P21 追加分の XML コメント確認・補完 |
| P22-06 | 全テスト最終通過確認（EditMode + PlayMode） |
| P22-07 | CHANGELOG.md の日付更新 |
| P22-08 | uOsc フォーク（com.hidano.uosc）の npmjs.com 公開確認 |
| P22-09 | npmjs.com へのパッケージ公開 |
| P22-10 | バージョンタグ作成 |
| P23-01 | InputBinding ドメインモデルの定義 |
| P23-02 | InputBindingProfileSO（ScriptableObject）新設 |
| P23-03 | FacialInputBinder（MonoBehaviour）新設 |
| P23-04 | InputBindingProfileSOEditor（Inspector）新設 |
| P23-05 | サンプルシーンの FacialInputBinder 対応 |
| P23-06 | quickstart.md / README.md / CHANGELOG.md 更新 |
| P23-07 | InputBinding 関連テスト追加 |

### テスト一覧（全 47 項目）

| ID | テスト名 |
|----|---------|
| P01-T01 | BlendShapeMappingTests |
| P01-T02 | LayerDefinitionTests |
| P01-T03 | ExpressionTests |
| P01-T04 | FacialProfileTests |
| P03-T01 | TransitionCalculatorTests |
| P03-T02 | ExclusionResolverTests |
| P03-T03 | LayerBlenderTests |
| P03-T04 | ARKitDetectorTests |
| P04-T01 | ProfileUseCaseTests |
| P04-T02 | ExpressionUseCaseTests |
| P04-T03 | LayerUseCaseTests |
| P04-T04 | ARKitUseCaseTests |
| P05-T01 | SystemTextJsonParserTests |
| P05-T02 | FileProfileRepositoryTests |
| P05-T03 | サンプル JSON パーステスト |
| P06-T01 | NativeArrayPoolTests |
| P06-T02 | AnimationClipCacheTests |
| P06-T03 | PropertyStreamHandleCacheTests |
| P06-T04 | PlayableGraphBuilderTests |
| P06-T05 | LayerPlayableTests |
| P06-T06 | FacialControlMixerTests |
| P07-T01 | OscDoubleBufferTests |
| P07-T02 | OscMappingTableTests |
| P07-T03 | OscSendReceiveTests |
| P08-T01 | FacialProfileMapperTests |
| P08-T02 | FacialControllerLifecycleTests |
| P08-T03 | FacialControllerAPITests |
| P08-T04 | InputSystemAdapterTests |
| P14-T01 | EndToEndTests |
| P14-T02 | TransitionIntegrationTests |
| P14-T03 | OscIntegrationTests |
| P14-T04 | MultiRendererTests |
| P14-T05 | GCAllocationTests |
| P14-T06 | MultiCharacterPerformanceTests |
| P17-T01 | ProfileCreationTests |
| P17-T02 | ProfileEditSaveTests |
| P17-T03 | FacialProfileRendererPathsTests |
| P17-T04 | SystemTextJsonParserRendererPathsTests |
| P17-T05 | FacialProfileMapperRendererPathsTests |
| P19-T01 | ProfileRebuildBlendShapeWeightTests |
| P19-T02 | ProfileBlendShapeAddRemoveTests |
| P19-T03 | RendererPathsCacheUpdateTests |
| P22-T01 | 全テスト最終通過確認（EditMode + PlayMode リグレッション） |
| P23-T01 | InputBindingTests |
| P23-T02 | InputBindingProfileSOTests |
| P23-T03 | FacialInputBinderTests |
| P23-T04 | InputSystemAdapterTests リグレッション確認 |


---

## チェックリスト（preview.1 リリース前）

- [ ] 全 EditMode テスト Green
- [ ] 全 PlayMode テスト Green
- [ ] 毎フレーム GC ゼロ確認
- [ ] 遷移割込 GC ゼロ確認
- [ ] 10 体同時制御テスト通過
- [ ] NativeArray リークなし
- [ ] ARKit 52 / PerfectSync 全パラメータ検出確認
- [ ] OSC 送受信動作確認（VRChat プリセット）
- [ ] Editor 拡張全機能動作確認
- [ ] JSON テンプレートのパース確認
- [ ] 全公開 API に XML コメント
- [ ] クイックスタートガイドの手順で実際にセットアップ可能
- [ ] package.json バリデーション通過
- [ ] CI 全テスト通過
- [ ] CHANGELOG.md 記載
- [ ] ProfileManagerWindow.cs が削除されている
- [ ] OscReceiverPlayable.cs が 1 箇所のみ
- [ ] README.md / CHANGELOG.md が P18〜P21 の変更を反映
- [ ] uOsc フォーク（com.hidano.uosc）が npmjs.com から取得可能
- [ ] npmjs.com から com.hidano.facialcontrol をインストール可能
- [ ] バージョンタグ v0.1.0-preview.1 が作成されている
- [ ] InputBindingProfileSO / FacialInputBinder が実装され Inspector で編集可能
- [ ] `com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo` に Scene + FacialProfileSO + InputBindingProfileSO + JSON + HUD が同梱されている（TestExpressionToggle 削除済み）
- [ ] quickstart.md にキーコンフィグ設定手順が記載されている

---

## 未整理メモ

- FacialControlDefaultActions.inputactions は Hierarchy 上でどのように適用すればよいですか？
