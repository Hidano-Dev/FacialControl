# FacialControl 技術仕様書

> **バージョン**: 3.1.0
> **最終更新**: 2026-08-25
> **ステータス**: レビュー待ち
> **対象リリース**: preview.1

---

## 1. preview.1 スコープ

> **注記**: 本節は実装実態に追従して更新する。以降の「決定事項」テーブル（v1.0 / v2.0.0 dig セッション）は策定当時の記録であり、スコープの最新状態は本節を正とする。

### 含まれる機能

| 機能 | 説明 |
|------|------|
| コア（プロファイル + レイヤー + 遷移） | 表情プロファイル管理、マルチレイヤー制御、表情遷移・補間 |
| OSC 送受信 | uOsc ベースの UDP 通信。VRChat + ARKit アドレスプリセット |
| OSC 自動マッピング | heartbeat 広告による送受信 mapping の自動構築（BlendShape / gaze）。手動 mapping との共存 |
| ARKit 52 / PerfectSync | 手動トリガーによる BlendShape スキャン + Expression 自動生成 |
| 視線制御（Gaze セクション） | 独立チャネルの Vector2 入力（OSC / InputSystem / Timeline / iFacialMocap）→ 目ボーン yaw/pitch。入力源はドロップダウンで選択し、可動角はチャネル単位で設定 |
| AdapterBinding アーキテクチャ | 入力源・出力先を `IAdapterBinding` として `FacialCharacterProfileSO` に結線。ランタイム設定は `AdapterRuntimeSettingsCollectionSO` |
| 入力源ルーティング・グラフエディタ | ノードグラフ UI で入力源とレイヤーを配線（slug 直書きの廃止） |
| Editor 拡張 | Inspector カスタマイズ、プロファイル管理ウィンドウ、Expression 作成支援（プレビューカメラ / PNG 書き出し）、JSON インポート / エクスポート |
| 複数 Renderer 対応 | 1 つの FacialController が複数の SkinnedMeshRenderer を制御 |
| Timeline 統合（`com.hidano.facialcontrol.timeline`） | Timeline トラックからの表情 / gaze 駆動と、AnimationClip へのベイク |
| 記録・再生（`com.hidano.facialcontrol.rec`） | 入力イベントの記録・再生、Timeline への書き出し、再生中の入力排他 |
| uLipSync アダプタ（`com.hidano.facialcontrol.lipsync`） | 外部リップシンクプラグインからの音素入力を overlay slot へ接続 |
| iFacialMocap 受信（`com.hidano.facialcontrol.ifacialmocap`） | iOS の UDP テキストプロトコルを受信し、ARKit 互換 BlendShape / 視線 / 頭部ポーズへ変換 |
| ドキュメント | パッケージ README、クイックスタートガイド、JSON スキーマドキュメント（`Documentation~/` 配下の Markdown） |

### preview.2 以降に延期

| 機能 | 理由 / 現状 |
|------|------|
| 自動まばたき | `IBlinkTrigger` インターフェースは定義済み。実装は延期（§11） |
| 視線追従の Vector3 ターゲット指定 / カメラ目線 | Gaze は preview.1 のボーン経路を提供する。Vector3 ターゲット解決とカメラ目線 procedural ソースは延期（backlog M-5）。入力ソースの宣言・選択による拡張点は用意する |
| BlendShape ベース視線の runtime 配線 | 本 spec では扱わない。look* 系スキーマと runtime 配線は削除し、将来 spec で新データモデルごと再設計する（backlog M-29） |
| 瞳の微細動（マイクロサッカード） | 入力が静止すると瞳も完全静止する。プロシージャル生成は未計画（backlog M-31） |
| VRM 対応 | リリース後の早期マイルストーン |
| テクスチャ切替 / UV アニメーションの JSON 対応 | preview.1 では BlendShape のみ JSON 対応 |
| ホットリロード自動検知 | preview.1 では明示的 API（`ProfileUseCase.ReloadProfile`）のみ |
| Addressables 対応 | プロファイル JSON は `StreamingAssets/FacialControl/` から直接読み込む（`IProfileJsonLoader` 抽象化は preview.2、backlog M-2） |

### 実装順序

**Domain → Adapters → Editor** のボトムアップ順で実装。TDD（Red-Green-Refactor）に最適な順序。

---

## 2. 用語定義

| 用語 | 定義 |
|------|------|
| **FacialProfile（プロファイル）** | キャラクター単位の表情設定一式。レイヤー定義と複数の Expression を包含する。1 プロファイル = 1 JSON ファイル |
| **Expression（表情）** | 個々の表情ポーズ（笑顔、怒り等）。BlendShape 値の組み合わせで 1 つの表情を定義する |
| **Layer（レイヤー）** | 表情制御の優先度階層。emotion / lipsync / eye 等 |

---

## 3. プロファイル構造

### 3.1 FacialProfile（キャラクター単位の設定）

```
FacialProfile
├── schemaVersion: string              // JSON スキーマバージョン（例: "1.0"）
├── layers: LayerDefinition[]          // レイヤー定義（優先度・排他モード）
└── expressions: Expression[]          // 全 Expression の配列
```

**重要な設計決定**:

- **プロファイル = キャラクター単位**: 1 つの FacialProfile が 1 キャラクターの全表情設定を保持
- **1 プロファイル = 1 JSON ファイル**: レイヤー定義と全 Expression を 1 つの JSON ファイルに統合
- **複数プロファイル切替可能**: FacialController はランタイムでプロファイルを切り替え可能（衣装差分等）
- **JSON が正規データ**: SO は Editor での操作性用ビュー。ランタイムでは JSON をパース

### 3.2 Expression（個々の表情ポーズ）

```
Expression
├── id: string                         // GUID 自動生成（System.Guid.NewGuid()）
├── name: string                       // 表示名
├── layer: string                      // 所属レイヤー名
├── transitionDuration: float          // 遷移時間（0〜1秒、デフォルト0.25秒）
├── transitionCurve: TransitionCurve   // 遷移カーブ設定
├── blendShapeValues: BlendShapeMapping[]  // BlendShape 名と値のマッピング（正規データ）
└── layerSlots: LayerSlot[]            // 他レイヤーへのオーバーライド値（配列形式）
```

**重要な設計決定**:

- **blendShapeValues が正規データ**: JSON 内の blendShapeValues が Expression の正規データ源。AnimationClip は Adapters 層で blendShapeValues から動的生成されるデータソース（ポーズの入れ物）
- **ID は GUID 自動生成**: `System.Guid.NewGuid().ToString()` で生成。衝突リスクゼロ
- **排他モードはレイヤー単位**: 同一レイヤー内では LastWins か Blend のいずれかに統一（混在禁止）。Expression ではなくレイヤーが排他モードを保持

### 3.3 BlendShape マッピング

```
BlendShapeMapping
├── name: string                       // BlendShape 名
├── value: float                       // 値（0〜1 正規化）
└── renderer: string?                  // 対象 Renderer 名（省略時は全 Renderer に適用）
```

- **renderer フィールド**: Renderer ごとに個別の値を指定可能。省略時は全 SkinnedMeshRenderer に同一値を適用
- **範囲外値の処理**: 0〜1 の範囲外の値は Adapters 層の入口で無言でクランプ

### 3.4 レイヤー別スロット（オーバーライド方式）

- **オーバーライド方式**: `layerSlots` は Expression がアクティブになったときに他のレイヤーへ影響を与えるオーバーライド値
- **完全置換**: layerSlots の値はターゲットレイヤーの排他モードに関わらず、対象 BlendShape の値を完全に置換する
- 例: 笑顔(emotion レイヤー)がアクティブな時に、口の形(lipsync レイヤー)も変える

```json
"layerSlots": [
  {
    "layer": "lipsync",
    "blendShapeValues": [
      {"name": "Fcl_MTH_A", "value": 0.5}
    ]
  }
]
```

### 3.5 カテゴリ属性

- **レイヤー指定のみ**: Expression は属するレイヤー名を 1 つだけ持つ
- タグやサブカテゴリは将来拡張

### 3.6 BlendShape 値の範囲

- **内部表現は 0〜1（正規化値）**: ドメインモデルでは 0.0〜1.0 の float で表現
- **Unity 適用時に 0〜100 変換**: Adapters 層で SkinnedMeshRenderer の 0〜100 範囲に変換
- OSC / ARKit の 0〜1 範囲と自然に整合

### 3.7 ランタイム出力データ

BlendShape 配列として出力。テクスチャ / UV アニメーション情報はインターフェースのみ定義（preview.2 以降で実装）:

```csharp
public struct FacialOutputData
{
    public NativeArray<float> BlendShapeWeights;
    // preview.2 以降で実装
    // public ITextureSwapInfo[] TextureSwaps;
    // public IUVAnimationInfo[] UVAnimations;
}
```

---

## 4. レイヤーシステム

### 4.1 デフォルトレイヤー構成

| レイヤー | 優先度 | 排他モード（デフォルト） | 用途 |
|----------|--------|------------------------|------|
| emotion | 最低 | LastWins | 感情ベースの表情 |
| lipsync | 中 | Blend | リップシンク |
| eye | 最高 | LastWins | 目の表情 |

### 4.2 排他モード（レイヤー単位で設定）

**排他モードはレイヤー単位で固定**。同一レイヤー内での LastWins と Blend の混在は禁止。

#### 後勝ち（LastWins）
- 同じレイヤーに後勝ち Expression A と B がある場合
- B をアクティブにすると **A から B へクロスフェード**
- 設定された遷移時間で A のウェイトを 1→0、B のウェイトを 0→1 に同時遷移
- 遷移中に新しい表情がトリガーされた場合は、**現在の補間値から即座に新遷移を開始**（GC フリー）

#### ブレンド（Blend）
- 同じレイヤーに複数のブレンド Expression がアクティブな場合
- **加算ブレンド（クランプ）**: 各 Expression のウェイトを加算し、0〜1 にクランプ
- 例: A=0.6, B=0.5 → 合計 1.1 → クランプ 1.0

### 4.3 レイヤー優先度

- レイヤーウェイトによるブレンドで制御（AnimationLayerMixerPlayable 標準動作と同等）
- 高優先度レイヤーの値が低優先度レイヤーをブレンドウェイトに基づいて上書き

### 4.4 レイヤー検証

- プロファイル読み込み時に、Expression が参照するレイヤーの存在を検証
- **未定義レイヤー参照時**: `Debug.LogWarning` で警告を出力し、emotion（最低優先度）レイヤーにフォールバック
- 動作を止めずに問題を通知するバランス方針

### 4.5 OSC vs ローカル入力の優先度

- OSC 受信値とローカルの Expression は**区別しない**
- 同一レイヤー内では**後勝ち（LastWins）**：最後に値を更新したソースが優先
- 入力ソースに関わらず、レイヤーの排他モード設定に従う

---

## 5. 表情遷移

### 5.1 サポートする遷移カーブ（preview.1）

| 種類 | 説明 |
|------|------|
| 線形補間（Lerp） | デフォルト |
| EaseIn | 開始が緩やか |
| EaseOut | 終了が緩やか |
| EaseInOut | 開始と終了が緩やか |
| AnimationCurve | ユーザー定義のカスタムカーブ |

### 5.2 遷移パラメータ

- **遷移時間**: 0〜1 秒（デフォルト 0.25 秒）
- **遷移カーブ**: プリセット名 or カスタム AnimationCurve
- **遷移中の新トリガー**: 現在の補間値から即座に新遷移を開始

### 5.3 補間実装方式

**全て ScriptPlayable で統一**。通常遷移も遷移割込みも同一コードパスで処理。

- ScriptPlayable 内で NativeArray ベースの補間計算を実行
- **スナップショットバッファ**: 遷移割込時に現在の BlendShape 値を NativeArray にスナップショット保存
- スナップショットから新ターゲットへ補間を開始
- **GC フリー**: 遷移割込時も GC は発生しない（NativeArray の再利用）

### 5.4 AnimationCurve の JSON シリアライズ

キーフレーム配列をそのまま JSON 化:

```json
{
  "transitionCurve": {
    "type": "custom",
    "keys": [
      {"time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 2.0, "inWeight": 0.0, "outWeight": 0.0, "weightedMode": 0},
      {"time": 1.0, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0, "inWeight": 0.0, "outWeight": 0.0, "weightedMode": 0}
    ]
  }
}
```

プリセットイージングの場合:

```json
{
  "transitionCurve": {
    "type": "easeInOut"
  }
}
```

---

## 6. PlayableAPI アーキテクチャ

### 6.1 設計方針

- **ScriptPlayable ベース**: BlendShape の補間は全て ScriptPlayable で制御。AnimationMixerPlayable のクロスフェードは不使用
- **AnimationStream API**: BlendShape の適用は AnimationStream API 経由で PlayableGraph 内で完結
- **既存 Animator と共有**: ユーザーの既存 Animator コンポーネントに PlayableGraph を接続。ボディアニメーションと共存可能
- **1 Graph / キャラクター**: キャラクター 1 体につき 1 つの PlayableGraph
- **レイヤー分ノード**: Graph 内にプロファイルのレイヤー定義に基づく ScriptPlayable ノードを配置
- **AnimationClip はデータソース**: AnimationClip は「ポーズの入れ物」として NativeArray への値展開に使用

### 6.2 PlayableGraph 構成

```
PlayableGraph (per character)
├── AnimationPlayableOutput → Animator（既存 Animator と共有）
├── ScriptPlayable<FacialControlMixer> (root: レイヤーウェイトブレンド)
│   ├── ScriptPlayable<LayerPlayable> (emotion)
│   │   ├── NativeArray バッファ (補間計算用)
│   │   ├── スナップショットバッファ (遷移割込用)
│   │   └── ScriptPlayable<OscReceiverPlayable> (OSC input)
│   ├── ScriptPlayable<LayerPlayable> (lipsync)
│   │   ├── NativeArray バッファ
│   │   └── ScriptPlayable<LipSyncPlayable> (external lipsync)
│   └── ScriptPlayable<LayerPlayable> (eye)
│       ├── NativeArray バッファ
│       └── ScriptPlayable<BlinkPlayable> (auto-blink, preview.2)
└── AnimationClipPlayable (テクスチャ/UV用, preview.2)
```

### 6.3 BlendShape 適用方式（AnimationStream API）

- **PropertyStreamHandle**: BlendShape へのアクセスは PropertyStreamHandle で行う
- **マッピング構築タイミング**: Expression 切替時に、アクティブな Expression が使用する BlendShape の PropertyStreamHandle を取得
  - 未取得の BlendShape のみ新規取得（切替時の GC は許容: 決定事項 #30 に合致）
  - 取得済みの Handle はキャッシュして再利用
- **PlayableGraph 内で完結**: ProcessFrame 内で AnimationStream 経由で BlendShape を書き込み。Animator 必須

### 6.4 データフロー

```
JSON (FacialProfile)
  → ドメインモデル (FacialProfile + Expression[])
  → AnimationClip (Adapters層で動的生成, LRUキャッシュ)
  → NativeArray に BlendShape 値を展開
  → ScriptPlayable 内で NativeArray 間の補間計算
  → AnimationStream API で BlendShape に適用
```

### 6.5 OSC 受信の Playable 統合

- **マッピングテーブルで全レイヤーに分配**: OSC アドレスとレイヤーのマッピングテーブルで受信値を各レイヤーに分配
- ARKit データ（目/口/感情が混在）が自動的に適切なレイヤーに分離される
- **GC フリー**: NativeArray ベースのバッファで毎フレームのヒープ確保を回避

### 6.6 Expression 切り替え

- **LRU キャッシュ**: AnimationClip をデフォルト 16 個までキャッシュ（Inspector / JSON で変更可能）
- **GC 許容**: Expression 切り替え時の GC は許容（毎フレームの GC はゼロ）
- ドメインモデル（FacialProfile 内の全 Expression）は全件メモリに保持
- AnimationClip のみ LRU でキャッシュ管理

### 6.7 プロファイル切り替え

- **Graph 再構築**: プロファイル切替時は PlayableGraph を破棄して新プロファイルで再構築
- **デフォルト状態から開始**: 切替後は常にデフォルト表情から開始（前のプロファイルの状態は復元しない）
- **GC 許容**: プロファイル切替は初期化相当の処理のため GC を許容

### 6.8 NativeArray 管理

- **プール方式**: 初期化時に必要なサイズ分を `Allocator.Persistent` で確保し、毎フレーム再利用
- **サイズ決定**: モデルの SkinnedMeshRenderer から BlendShape 総数を取得し、そのサイズで確保
- サイズ変更時（Renderer 変更時）のみ再確保
- `OnDisable` で解放
- Domain 層でも NativeArray を使用（GC フリー優先。asmdef で Unity.Collections を参照）

### 6.9 PlayableGraph ライフサイクル

- **ハイブリッド方式**: `OnEnable` で自動初期化、`Initialize()` で手動初期化も可能
- `OnDisable` で PlayableGraph と NativeArray を破棄
- **状態復元なし**: OnEnable 時はデフォルト表情から開始（前回のアクティブ Expression は復元しない）
- ホットリロードは明示的 API（`ReloadProfile()`）で実行

---

## 7. OSC 通信

### 7.1 基本設計

- **送受信両方**: preview.1 で送信・受信を両方実装
- **uOsc**: 自前フォーク `com.hidano.uosc` を `https://registry.npmjs.com` から取得（package.json の dependencies で定義）
- **フォーク変更範囲**: ポート再利用設定（SO_REUSEADDR 相当）のみ変更。同じポートを連続して使えるようにする最小限の変更
- **UDP ポート**: デフォルト送信 9000、受信 9001（VRChat 標準）。Inspector / JSON で変更可能
- **キャラクターごとに個別ポート**: 複数キャラクターはポート番号で識別。VRChat OSC アドレス形式を変更しない

### 7.2 アドレスパターン

| プリセット | 送信アドレス | 受信アドレス |
|-----------|-------------|-------------|
| VRChat | `/avatar/parameters/{name}` | `/avatar/parameters/{name}` |
| ARKit | `/ARKit/{blendShapeName}` | `/ARKit/{blendShapeName}` |

- preview.1 では VRChat と ARKit のプリセットを提供
- 完全カスタムアドレスは将来対応

### 7.3 通信仕様

- BlendShape 単位で個別 OSC メッセージ送受信
- **全 BlendShape を毎フレーム送信**: 変更の有無に関わらず全 BlendShape 値を送信（VRChat 標準動作）
- 1 フレーム間に複数回送受信可能
- UDP 送受信はメインスレッド非依存

### 7.4 スレッド安全性

- **受信**: ダブルバッファリング方式。受信スレッドがバッファ A に書き込み、メインスレッドがバッファ B を読む。フレーム境界でスワップ。ロックフリー・低遅延
- **送信**: 別スレッドで非同期送信。ダブルバッファの読み取り側から値を取得し、非同期で UDP 送信。メインスレッド負荷ゼロ

### 7.5 OSC マッピングテーブル

- OSC アドレスとレイヤーのマッピングを config.json で管理
- StreamingAssets に配置（ビルド後も差し替え可能）
- ARKit プリセットのマッピングテーブルを自動生成
- **preview.1 では JSON 直接編集のみ**: Editor UI でのマッピング編集は将来対応

---

## 8. ARKit 52 / PerfectSync

### 8.1 自動検出

- **手動トリガー**: ユーザーが API 呼び出しまたは Editor ボタンで実行
- 自動的な検出タイミングは設けない（意図しないタイミングでの検出を避ける）

### 8.2 マッチングアルゴリズム

- **完全一致のみ**: ARKit 52 標準名 / PerfectSync 標準名と BlendShape 名が完全一致する場合のみ自動検出
- 独自命名規則のモデルはユーザーが手動でマッピングを設定
- 誤検出リスクをゼロにするシンプルなアプローチ

### 8.3 検出フロー

1. ユーザーが API / Editor ボタンで検出を実行
2. 対象モデルの SkinnedMeshRenderer から BlendShape 名をスキャン
3. ARKit 52 パラメータ名 / PerfectSync パラメータ名と完全一致マッチング
4. マッチしたパラメータから Expression を**レイヤー単位で自動生成**（目/口/眉等のグループ別）
5. 未対応パラメータは警告なしでスキップ
6. 同時に OSC マッピングテーブルも自動生成

### 8.4 自動生成後のカスタマイズ

- 生成された Expression とマッピングテーブルはユーザーが**完全に編集可能**
- Editor UI で BlendShape の追加・削除・値の変更が可能

---

## 9. 入力システム

### 9.1 デフォルトバインディング

- **最小限のトリガーバインディングのみ同梱**: Expression 切り替え用のボタンバインドのみ
- **ボタン（トグル）**: ボタンで Expression をアクティブ/非アクティブ切り替え
- **アナログ（強度）**: ゲームパッドトリガー等で表情強度（ブレンドウェイト）を 0〜1 で制御
- InputAction の Button 型と Value 型の**両方対応（バインドで選択）**
- 強度制御のバインディングはユーザーがカスタマイズして追加

### 9.2 カスタマイズ

- InputAction Asset の差し替えによるカスタマイズ
- 入力インターフェースの抽象化により独自実装に差し替え可能

---

## 10. リップシンク

### 10.1 インターフェース

```csharp
public interface ILipSyncProvider
{
    /// <summary>
    /// リップシンクの BlendShape 値を取得する。
    /// 内部実装は固定長バッファで GC フリー。
    /// </summary>
    void GetLipSyncValues(Span<float> output);

    /// <summary>
    /// 対応する BlendShape 名の一覧を取得する。
    /// </summary>
    ReadOnlySpan<string> BlendShapeNames { get; }
}
```

- 外部プラグイン（uLipSync 等）がインターフェースを実装
- 内部実装は固定長バッファで GC フリー
- FacialControl はリップシンク用レイヤーに値を適用するのみ

---

## 11. 自動まばたき（preview.2 延期）

### 11.1 インターフェース定義（preview.1 で定義）

```csharp
public interface IBlinkTrigger
{
    /// <summary>
    /// まばたきをトリガーすべきかを判定する。
    /// </summary>
    bool ShouldBlink(float deltaTime, in FacialState currentState);
}
```

### 11.2 実装方針（preview.2）

- 単純なランダムではなく、人間のしぐさをベースにしたアルゴリズム
- 基本トリガー: ランダム間隔（3〜7 秒）+ 視線変更時
- 拡張可能: ユーザーが独自の IBlinkTrigger を実装可能
- OSC / ARKit からのまばたき入力があればそちらを優先（フォールバック機構）

---

## 12. 視線制御（Gaze セクション、preview.1）

### 12.1 設計方針

- Gaze は `FacialCharacterProfileSO` 直下の `gaze.channels[]` で構成し、先頭チャネルの id は `gaze` に固定する。チャネルは Expression やレイヤー合成に参加しない。
- 各チャネルは入力 source の選択、左右の目ボーン path、初期回転、ローカル yaw/pitch 軸、上下左右の可動角を保持する。参照モデル割当時に Animator 起点のフル path を自動解決する。
- 入力 source は `IGazeSourceProvider` の宣言を Inspector が列挙し、ユーザーは Gaze セクションのドロップダウンで切り替える。`providerSlug` が空の場合は binding の宣言と `{slug}:{channelId}[.left|.right]` の規約から自動解決する。
- `GazeBonePoseProvider` は source の Vector2 を読み、Quaternion 合成で左右の目の `Transform.localRotation` を毎フレーム更新する。未入力時は設定した初期回転へ戻し、破棄時は書込み開始前の回転へ復元する。
- 「視線追従」「カメラ目線」は Expression テンプレートではなく、将来追加できる procedural gaze 入力 source の候補である。Vector3 ターゲットとカメラ目線 source 自体は本リリースに含めない。

### 12.2 JSON と source id

- JSON はルート `gaze` オブジェクト内の `channels[]` にチャネルを保存する。旧 `gaze_configs` は検出して警告し、gaze 部分を読み捨てる。
- source id は `GazeSourceIdConvention` が `{slug}:{channelId}`（共有）、`{slug}:{channelId}.left`、`{slug}:{channelId}.right`（左右独立）へ統一する。`GazeSnapshot` は `ChannelId` と正規化された `X` / `Y` を保持する。

---

## 13. JSON 設計

### 13.1 バージョニング

- JSON スキーマに `schemaVersion` フィールドを含める
- パーサーがバージョンを見てマイグレーション可能
- preview 間でもバージョン管理

### 13.2 JSON パーサー

- **第一候補**: System.Text.Json（.NET 標準。高速・低 GC）
- **フォールバック**: JsonUtility + ラッパー（Unity 標準のみ。外部依存ゼロ）
- **IJsonParser インターフェース**: Domain 層に `IJsonParser` インターフェースを定義し、Adapters 層で実装。パーサー実装の差し替えが容易
- クリーンアーキテクチャに忠実な設計

### 13.3 ファイル配置構造

```
StreamingAssets/FacialControl/
├── config.json                      # グローバル設定（OSCマッピング、キャッシュ設定等）
├── character01_profile.json          # プロファイル（キャラクター単位）
├── avatar01_profile.json
└── ...
```

- **推奨ファイル名**: `{character_name}_profile.json`（ユーザーが自由に命名可能だが推奨規則を提供）
- **1 プロファイル = 1 ファイル**: レイヤー定義 + 全 Expression を 1 つの JSON に統合
- config.json にはグローバル設定（OSC マッピング + キャッシュサイズ等）を配置
- Android 将来対応時は StreamingAssets の読み取り専用制約に注意

### 13.4 ランタイム JSON 変換

- **初期化時にプロファイル JSON をパース**: アプリ起動時に JSON をパースし、ドメインモデル（FacialProfile + Expression[]）を全件メモリに保持
- AnimationClip への変換は LRU キャッシュ（デフォルト 16）で遅延実行
- **ホットリロード**: 明示的 API（`ReloadProfile()`）で JSON を再パース

### 13.5 不正 JSON の処理

- **例外スロー**: パース失敗時は例外をスローし、呼び出し元に即座にエラーを報告
- 開発者向けライブラリのため、エラーを隠さない方針

### 13.6 ScriptableObject との関係

- **SO は参照ポインター**: Inspector で FacialController にプロファイルを指定するための SO。内部に JSON ファイルパス情報を保持
- **JSON が正規データ**: SO はあくまで Editor での操作性のためのビュー。ランタイムでは SO から JSON パスを取得し、JSON をパース
- ドメインモデルと SO の間にマッパーを配置（Adapters 層）
- **SO と JSON の同期は手動エクスポートのみ**: Editor メニューから明示的にエクスポート操作で JSON 生成（自動同期なし）

### 13.7 プロファイル JSON 例

```json
{
  "schemaVersion": "1.0",
  "layers": [
    {"name": "emotion", "priority": 0, "exclusionMode": "lastWins"},
    {"name": "lipsync", "priority": 1, "exclusionMode": "blend"},
    {"name": "eye", "priority": 2, "exclusionMode": "lastWins"}
  ],
  "expressions": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "name": "笑顔",
      "layer": "emotion",
      "transitionDuration": 0.25,
      "transitionCurve": {
        "type": "easeInOut"
      },
      "blendShapeValues": [
        {"name": "Fcl_ALL_Joy", "value": 1.0},
        {"name": "Fcl_EYE_Joy", "value": 0.8},
        {"name": "Fcl_EYE_Joy_R", "value": 0.6, "renderer": "Face"}
      ],
      "layerSlots": [
        {
          "layer": "lipsync",
          "blendShapeValues": [
            {"name": "Fcl_MTH_A", "value": 0.5}
          ]
        }
      ]
    },
    {
      "id": "661f9511-f30c-52e5-b827-557766551111",
      "name": "怒り",
      "layer": "emotion",
      "transitionDuration": 0.15,
      "transitionCurve": {
        "type": "linear"
      },
      "blendShapeValues": [
        {"name": "Fcl_ALL_Angry", "value": 1.0},
        {"name": "Fcl_BRW_Angry", "value": 0.9}
      ],
      "layerSlots": []
    },
    {
      "id": "772a0622-a41d-63f6-c938-668877662222",
      "name": "まばたき",
      "layer": "eye",
      "transitionDuration": 0.08,
      "transitionCurve": {
        "type": "linear"
      },
      "blendShapeValues": [
        {"name": "Fcl_EYE_Close", "value": 1.0}
      ],
      "layerSlots": []
    }
  ]
}
```

**preview.1 の JSON スキーマからの除外項目**:
- `baseClip`（AnimationClipRef）: blendShapeValues が正規データのため不要
- テクスチャ/UV 関連フィールド: preview.2 以降で対応

### 13.8 設定ファイル（config.json）例

```json
{
  "schemaVersion": "1.0",
  "osc": {
    "sendPort": 9000,
    "receivePort": 9001,
    "preset": "vrchat",
    "mapping": [
      {"oscAddress": "/avatar/parameters/Fcl_ALL_Joy", "blendShapeName": "Fcl_ALL_Joy", "layer": "emotion"},
      {"oscAddress": "/avatar/parameters/Fcl_MTH_A", "blendShapeName": "Fcl_MTH_A", "layer": "lipsync"}
    ]
  },
  "cache": {
    "animationClipLruSize": 16
  }
}
```

---

## 14. 複数キャラクター制御

### 14.1 コンポーネントモデル

- **キャラクターごとにコンポーネント**: 各キャラクターの GameObject に `FacialController` コンポーネントをアタッチ
- 各キャラクターが独立した PlayableGraph を持つ
- 各キャラクターが独立したプロファイル（FacialProfile）を保持
- **複数プロファイル切替**: ランタイムで `LoadProfile()` API によりプロファイルを切り替え可能

### 14.2 SkinnedMeshRenderer の検出

- **自動検索 + 手動オーバーライド**: デフォルトで FacialController 以下の全子階層から SkinnedMeshRenderer を再帰検索（`GetComponentsInChildren<SkinnedMeshRenderer>()`）
- Inspector で明示的に上書き設定可能
- **複数 Renderer 対応（preview.1）**: 1 つの FacialController が複数の SkinnedMeshRenderer を制御。Renderer ごとに個別の BlendShape 値を指定可能（renderer フィールド）

### 14.3 OSC ポート管理

- キャラクターごとに独立した UDP 受信ポートを使用
- ポート番号で複数キャラクターを識別（VRChat OSC アドレス形式は変更しない）
- Inspector / JSON でポート番号を設定可能

### 14.4 グローバル制御

- グローバルな制御（全員同じ表情等）が必要な場合は別途マネージャーを配置
- preview.1 ではグローバルマネージャーはスコープ外

---

## 15. 公開 API 設計

### 15.1 API スタイル

- **Expression 参照ベース**: 型安全な API。コンパイル時にエラーを検出可能

```csharp
// Expression の操作
controller.Activate(expression);      // Expression をアクティブにする
controller.Deactivate(expression);    // Expression を非アクティブにする

// プロファイルの操作
controller.LoadProfile(profile);      // プロファイルを切り替える（Graph 再構築）
controller.ReloadProfile();           // 現在のプロファイルを JSON から再パース
```

- 文字列ベースの API は提供しない（タイポのリスクを排除）
- ユーザーは初期化時に FacialProfile から Expression を取得して参照を保持

---

## 16. Editor 拡張

### 16.1 Inspector カスタマイズ

- **FacialController コンポーネント**: プロファイル SO 参照、SkinnedMeshRenderer リストを統合表示
- **プロファイル ScriptableObject**: JSON ファイルパスの参照
- UI Toolkit ベースの CustomEditor

### 16.2 プロファイル管理ウィンドウ（EditorWindow）

- プロファイル内の Expression リスト表示
- **データソース**: プロファイル JSON 内の expressions 配列から取得
- **名前検索**: 部分一致検索（レイヤーフィルターは将来拡張）
- **サムネイルプレビュー**: 手動ボタンで生成。PreviewRenderUtility を使用した独立プレビュー（Scene 非依存）。対象モデルの指定が必要
- Expression の CRUD 操作
- JSON インポート / エクスポート
- **保存方式**: Undo 連動 + 自動保存。値変更時に JSON に自動反映。Unity の Undo システムと連動

### 16.3 Expression 作成支援ツール

- **PreviewRenderUtility** を使用した独立プレビューウィンドウで BlendShape スライダー操作
- **モデル指定**: Scene 上のオブジェクト選択と Prefab/FBX アセット指定の両方に対応
- **リアルタイムプレビュー**: スライダー値変更毎に即座にプレビューを更新
- **出力形式は JSON（Expression）**: スライダー操作の結果は JSON プロファイル内の Expression として保存（JSON ファースト方針と一貫）
- **preview.1 では BlendShape のみ**: テクスチャ切り替え / UV アニメーションは将来対応
- テクスチャ / UV は Unity 標準 AnimationWindow で編集

### 16.4 ARKit 検出ツール（廃止）

- 2026-09 に廃止した（HID-34）。Expression / OSC マッピングの自動生成は使われていなかったため
- ARKit 52 / PerfectSync の命名検出（完全一致）は core の `ARKitDetector` として残り、OSC パッケージ（`RuntimeMappingResolver` / `AddressPresetEstimator`）が `ARKit52Names` を使う

### 16.5 Editor ディレクトリ構造

```
Editor/
├── Inspector/          # FacialController, SO の CustomEditor
├── Windows/            # プロファイル管理ウィンドウ
└── Common/             # 共通ユーティリティ、UI Toolkit スタイル
```

---

## 17. テンプレートプロファイル

### 17.1 同梱テンプレート

パッケージにデフォルトプロファイルの JSON テンプレートを同梱:

| テンプレート内 Expression | レイヤー | 説明 |
|--------------------------|---------|------|
| default | emotion | デフォルト表情（ニュートラル） |
| blink | eye | まばたき |
| gaze channel | Gaze セクション | Vector2 入力で目ボーンを駆動する独立チャネル（既定 id は `gaze`） |
| procedural gaze source | Gaze セクション | 視線追従・カメラ目線などを将来追加するための入力 source 拡張点 |

- モデル固有の BlendShape 名はユーザーがカスタマイズ
- ARKit 検出時にモデル固有の Expression もレイヤー単位で自動生成

---

## 18. パフォーマンス設計

### 18.1 GC アロケーション方針

| タイミング | GC 許容 | 説明 |
|-----------|---------|------|
| 初期化時 | 許容 | JSON パース、PlayableGraph 構築、NativeArray プール確保 |
| Expression 切り替え時 | 許容 | AnimationClip 動的生成（LRU キャッシュミス時）、PropertyStreamHandle 取得 |
| プロファイル切替時 | 許容 | Graph 再構築（初期化相当の処理） |
| 遷移割込時 | **ゼロ** | スナップショットバッファの再利用で GC フリー |
| 毎フレーム処理 | **ゼロ目標** | ウェイト更新、補間計算、OSC 送受信、BlendShape 適用 |

### 18.2 データ構造

- 浮動小数点は `float` 基本
- NativeArray ベースのバッファで毎フレーム処理（プール方式で事前確保 + 再利用）
- NativeArray サイズはモデルの BlendShape 総数で決定（初期化時に SkinnedMeshRenderer から取得）
- JSON パース負荷を抑えるデータ構造（初期化時のみ）
- Domain 層でも NativeArray を使用（GC フリー優先）

---

## 19. パッケージ構成

### 19.1 ディレクトリ構造

```
com.hidano.facialcontrol/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE.md
├── Runtime/
│   ├── Domain/                    # ドメインロジック（NativeArray使用、Unity.Collections依存）
│   │   ├── Models/                # FacialProfile, Expression, BlendShapeMapping 等
│   │   ├── Interfaces/            # ILipSyncProvider, IBlinkTrigger, IJsonParser 等
│   │   └── Services/              # 遷移計算、排他ロジック等
│   ├── Application/               # ユースケース
│   │   └── UseCases/              # プロファイル管理、レイヤー制御等
│   └── Adapters/                  # Unity 依存の実装
│       ├── Playable/              # PlayableAPI 関連（ScriptPlayable + AnimationStream）
│       ├── OSC/                   # OSC アダプター（com.hidano.uosc ラッパー、ダブルバッファ）
│       ├── Json/                  # JSON パーサー（System.Text.Json / JsonUtility 実装）
│       ├── ScriptableObject/      # SO マッパー（参照ポインター）
│       └── Input/                 # InputSystem アダプター
├── Editor/
│   ├── Inspector/                 # CustomEditor
│   ├── Windows/                   # EditorWindow（プロファイル管理）
│   ├── Tools/                     # Expression 作成支援、ARKit 検出
│   └── Common/                    # 共通ユーティリティ、スタイル、PreviewRenderUtility
├── Templates/                     # デフォルトプロファイル JSON テンプレート
├── Documentation~/                # ドキュメント（Unity Package Manager 表示用 Markdown）
│   ├── quickstart.md
│   └── json-schema.md
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

### 19.2 Assembly Definition

| asmdef | 依存先 |
|--------|--------|
| Hidano.FacialControl.Domain | Unity.Collections（NativeArray 使用のため） |
| Hidano.FacialControl.Application | Domain |
| Hidano.FacialControl.Adapters | Domain, Application, Unity.Animation |
| Hidano.FacialControl.Editor | Domain, Application, Adapters |
| Hidano.FacialControl.Tests.EditMode | Domain, Application, Adapters |
| Hidano.FacialControl.Tests.PlayMode | Domain, Application, Adapters |

---

## 20. テスト戦略

### 20.1 配置基準

| テスト種別 | 配置先 | 対象 |
|-----------|--------|------|
| 単体テスト | EditMode | Domain 層（Expression、遷移計算、排他ロジック） |
| 単体テスト | EditMode | Application 層（ユースケース） |
| Fake 統合テスト | EditMode | Adapters 層（JSON パーサー、SO マッパー） |
| 統合テスト | PlayMode | PlayableAPI 実装、OSC 送受信 |
| パフォーマンステスト | PlayMode | GC 計測、フレームレート |

### 20.2 TDD サイクル

```
1. Red:    失敗するテストを書く（EditMode 優先）
2. Green:  テストを通す最小限のコードを書く
3. Refactor: リファクタリング（テストは緑を維持）
```

---

## 21. ドキュメント（preview.1 同梱）

| ドキュメント | 内容 |
|------------|------|
| パッケージ README | インストール手順、主な機能、アーキテクチャ概要、API 利用例（`Packages/com.hidano.facialcontrol/README.md`） |
| クイックスタート | 基本的なセットアップ手順、最初のプロファイル作成（`Documentation~/quickstart.md`） |
| JSON スキーマ | プロファイル JSON の全フィールド定義と例（`Documentation~/json-schema.md`） |

> **補足**: 自動生成 API リファレンス（DocFX）は preview.1 スコープから外し、XML コメントは IDE の IntelliSense 用途に限定する。Unity UPM パッケージの一般的な慣習（README + `Documentation~/` の Markdown）に揃える方針。

---

## 22. 決定事項サマリー

### v1.0.0 決定事項（2026-02-02）

| # | 項目 | 決定 | 備考 |
|---|------|------|------|
| 1 | プロファイルの AnimationClip 構造 | 基本 Clip 1 + レイヤー別スロット（固定名） | ランタイムは BlendShape 配列ベース |
| 2 | OSC 方向性 | 送受信両方（preview.1） | VRChat + ARKit プリセット |
| 3 | 排他粒度 | プロファイル単位 | 各プロファイルが個別に排他モードを持つ |
| 4 | スロット定義 | 固定スロット名（辞書型） | emotion, lipsync, eye |
| 5 | ARKit 検出トリガー | 手動（API / Editor ボタン） | 自動検出なし |
| 6 | JSON 変換タイミング | 初期化時 1 回 + メモリキャッシュ | ホットリロード時は再パース |
| 7 | JSON 配置場所 | StreamingAssets | Android 対応時に注意 |
| 8 | マルチキャラクター | キャラクターごとに独立プロファイルセット | メモリ増だが柔軟性優先 |
| 9 | テクスチャ / UV 制御 | PlayableAPI（AnimationClip 再生） | BlendShape も PlayableAPI に統一 |
| 10 | BlendShape 制御 | 全て PlayableAPI に統一 | カスタム PlayableNode で OSC 値を反映 |
| 11 | Editor ツール（preview.1） | BlendShape のみ | テクスチャ / UV は将来 |
| 12 | OSC アドレス | VRChat + ARKit プリセット | 完全カスタムは将来 |
| 13 | OSC → Playable | カスタム PlayableNode（ScriptPlayable） | NativeArray でGCフリー |
| 14 | カテゴリ属性 | レイヤー指定のみ | タグ等は将来 |
| 15 | 入力方式 | ボタン + 修飾キー + 連続値（表情強度） | ゲームパッドトリガー対応 |
| 16 | リップシンク入力 | ILipSyncProvider インターフェース | 固定長バッファで GC フリー |
| 17 | JSON バージョン | schemaVersion フィールドあり | preview 間でもバージョン管理 |
| 18 | サムネイル | 手動ボタンで生成 | 対象モデル指定が必要 |
| 19 | Curve シリアライズ | キーフレーム配列そのまま JSON 化 | Keyframe 全フィールド保持 |
| 20 | 検索 | 名前検索のみ | レイヤーフィルターは将来 |
| 21 | uOsc 同梱 | package.json 依存定義 | npmjs.com 公開確認要 |
| 22 | コントローラー | キャラクターごとにコンポーネント | グローバルマネージャーは別途 |
| 23 | UDP ポート | デフォルト VRChat 標準 + ユーザー設定可 | Inspector / JSON で変更 |
| 24 | PlayableGraph 構成 | 1 Graph / キャラクター + レイヤー分ノード | AnimationLayerMixerPlayable |
| 25 | デフォルトプロファイル | テンプレート JSON 同梱 | モデル固有名はユーザーカスタマイズ |
| 26 | 排他動作（後勝ち） | A → B クロスフェード | 遷移中の新トリガーは現在値から即新遷移 |
| 27 | ブレンド動作 | 加算ブレンド（クランプ） | 0〜1 にクランプ |
| 28 | まばたき | IBlinkTrigger IF 定義（実装は preview.2） | 人間的しぐさアルゴリズム |
| 29 | 視線制御 | Gaze セクションの独立チャネルから目ボーンを駆動 | BlendShape gaze / Vector3 ターゲットは別 spec |
| 30 | GC 許容範囲 | 初期化 + プロファイル切り替え | 毎フレームはゼロ |
| 31 | Graph 再構築 | 動的再構築（GC 許容） | メモリ効率優先 |
| 32 | テスト戦略 | ドメイン EditMode / 統合 PlayMode | CLAUDE.md 基準と整合 |
| 33 | Editor 構造 | 機能別 + 共通層 | Inspector, Windows, Tools, Common |
| 34 | SO 構造 | 別構造（Unity 最適化）+ マッパー | Adapters 層にマッパー配置 |
| 35 | preview.1 スコープ | コア + OSC + ARKit + Editor + Gaze ボーン経路 | 自動まばたき・procedural gaze source は preview.2 以降 |
| 36 | ドキュメント | API + クイックスタート + JSON スキーマ | チュートリアルは将来 |

### v2.0.0 追加決定事項（2026-02-02 dig セッション）

| # | 項目 | 決定 | 備考 |
|---|------|------|------|
| 37 | Expression ID 生成方式 | GUID 自動生成 | System.Guid.NewGuid() |
| 38 | 正規データ | blendShapeValues が正規データ源 | AnimationClip は Adapters 層で動的生成 |
| 39 | layerSlots の意味 | オーバーライド方式 | アクティブ時に他レイヤーへ影響 |
| 40 | layerSlots の JSON 構造 | 配列形式 | `[{"layer": "...", "blendShapeValues": [...]}]` |
| 41 | AnimationClip キャッシュ | LRU キャッシュ（デフォルト 16） | Inspector/JSON で変更可能 |
| 42 | テクスチャ/UV の JSON 対応 | preview.1 では対象外 | AnimationClip 直接参照のみ |
| 43 | OSC 送信頻度 | 全 BlendShape を毎フレーム送信 | VRChat 標準動作 |
| 44 | 入力方式の詳細 | ボタン+アナログ両方対応（バインドで選択） | InputAction の Button/Value |
| 45 | OSC 受信のレイヤー分配 | マッピングテーブルで全レイヤーに分配 | JSON 設定ファイルで管理 |
| 46 | Expression 作成支援の出力 | JSON プロファイルとして保存 | JSON ファースト方針 |
| 47 | サムネイル生成方式 | PreviewRenderUtility の RenderTexture | Scene 非依存 |
| 48 | ARKit Expression 粒度 | レイヤー単位（目/口/眉等） | グルーピングロジック必要 |
| 49 | OSC マッピング保存先 | config.json | StreamingAssets 配置 |
| 50 | ARKit 生成後カスタマイズ | 完全編集可能 | Editor UI で編集 |
| 51 | JSON 配置構造 | config.json + プロファイル JSON（キャラクター単位） | v1.0.0 の Profiles/ ディレクトリ方式を上書き |
| 52 | SO と JSON の同期 | 手動エクスポートのみ | 自動同期なし |
| 53 | レイヤー間ブレンド方式 | レイヤーウェイトによるブレンド | 標準動作 |
| 54 | 排他モード設定単位 | レイヤー単位で固定（混在禁止） | v1.0.0 #3 を上書き |
| 55 | 不正 JSON 処理 | 例外スロー | 開発者向けなので即座に報告 |
| 56 | BlendShape 値の範囲 | 内部 0〜1、Unity 適用時に 0〜100 変換 | Adapters 層で変換 |
| 57 | PlayableGraph ライフサイクル | ハイブリッド（OnEnable 自動 + Initialize() 手動） | OnDisable で破棄 |
| 58 | uOsc 取得先 | com.hidano.uosc を npmjs.com から取得 | package.json 依存定義 |
| 59 | NativeArray 管理 | プール方式（Persistent 確保 + 再利用） | サイズ変更時のみ再確保 |
| 60 | FacialOutputData テクスチャ/UV | インターフェースのみ定義 | 実装は preview.2 以降 |
| 61 | OSC 複数キャラクター | キャラクターごとに個別ポート | ポート番号で識別 |
| 62 | SkinnedMeshRenderer 検出 | 自動検索 + 手動オーバーライド | 全子階層再帰検索 |
| 63 | 複数 Renderer 対応 | preview.1 で対応 | renderer フィールドで個別指定可能 |
| 64 | 補間実装方式 | 全て ScriptPlayable で統一 | AnimationMixerPlayable 不使用 |
| 65 | 遷移割込時 GC | GC フリー必須 | スナップショットバッファ（NativeArray） |
| 66 | AnimationClip の役割 | データソース（ポーズの入れ物） | NativeArray に展開して ScriptPlayable で補間 |
| 67 | PlayableGraph 構成 | ScriptPlayable ベース | v1.0.0 #24 を上書き |
| 68 | AnimationClipRef (preview.1) | JSON スキーマから削除 | blendShapeValues のみ |
| 69 | デフォルト InputAction | 最小限（トリガーのみ） | 強度制御はユーザー設定 |
| 70 | Domain 層の NativeArray | 許容 | Unity.Collections 依存、GC フリー優先 |
| 71 | プレビュー実装 | PreviewRenderUtility | Editor 拡張標準 |
| 72 | ホットリロード | 明示的 API（ReloadProfile()） | 自動検知なし |

### v3.0.0 追加決定事項（2026-02-02 dig セッション第 2 回）

| # | 項目 | 決定 | 備考 |
|---|------|------|------|
| 73 | プロファイル定義 | キャラクター単位の設定一式（レイヤー + Expression 集） | ExpressionProfile → FacialProfile + Expression に分離 |
| 74 | 個々の表情名称 | Expression | 旧 ExpressionProfile が Expression に |
| 75 | JSON 構造 | 1 プロファイル = 1 ファイル（レイヤー + 全 Expression） | config.json + {name}_profile.json |
| 76 | JSON パーサー | System.Text.Json（フォールバック: JsonUtility + ラッパー） | .NET 標準で高速・低 GC |
| 77 | パーサー抽象化 | IJsonParser インターフェース | Domain 層に定義、Adapters 層で実装 |
| 78 | BlendShape 適用 | AnimationStream API | PlayableGraph 内で完結。Animator 必須 |
| 79 | OSC スレッド安全性 | ダブルバッファリング | ロックフリー・低遅延 |
| 80 | OSC 送信 | 別スレッドで非同期 | メインスレッド負荷ゼロ |
| 81 | マッピング構築 | Expression 切替時に必要分 | PropertyStreamHandle の遅延取得 |
| 82 | 同名 BlendShape | renderer フィールド追加（省略時全 Renderer） | 後方互換性あり |
| 83 | レイヤー検証 | 警告ログ + emotion レイヤーにフォールバック | 動作を止めない |
| 84 | ARKit マッチング | 完全一致のみ | 独自命名は手動マッピング |
| 85 | 公開 API | Expression 参照ベース（型安全） | Activate(Expression) |
| 86 | Editor データソース | プロファイル JSON 内の expressions 配列 | JSON ファースト |
| 87 | モデル指定 | Scene + Prefab/FBX 両対応 | PreviewRenderUtility |
| 88 | NativeArray サイズ | モデルの BlendShape 数で決定 | 初期化時に自動取得 |
| 89 | 状態復元 | デフォルトリセット | OnEnable 時にデフォルト表情から |
| 90 | ファイル名 | 推奨: {character_name}_profile.json | ユーザー自由 |
| 91 | OSC vs ローカル | 後勝ち（LastWins） | 入力ソースを区別しない |
| 92 | Editor 保存 | Undo 連動 + 自動保存 | JSON に自動反映 |
| 93 | 範囲外値 | クランプ (0〜1) | 無言でクランプ |
| 94 | Renderer 検索 | 全子階層再帰 | GetComponentsInChildren |
| 95 | layerSlots | オーバーライド値で完全置換 | 排他モード無視 |
| 96 | Animator | 既存 Animator を共有 | ボディアニメと共存 |
| 97 | パッケージドキュメント | README + `Documentation~/` Markdown | Unity UPM 慣習準拠。DocFX 自動生成は preview.1 では採用しない |
| 98 | uOsc フォーク | ポート再利用設定のみ変更 | SO_REUSEADDR 相当 |
| 99 | 実装順序 | Domain → Adapters → Editor | ボトムアップ / TDD |
| 100 | プレビュー更新 | 値変更毎に即座更新 | リアルタイム |
| 101 | OSC Editor UI | JSON 直接編集のみ (preview.1) | Editor UI は将来 |
| 102 | プロファイル切替 | Graph 再構築 + デフォルト状態から | 切替時 GC 許容 |
| 103 | プロファイル指定 | SO 参照（Inspector） | SO は JSON への参照ポインター |
| 104 | 正規データソース | JSON | SO はビュー、JSON が真実 |
| 105 | config.json | OSC + グローバル設定 | キャッシュサイズ等含む |
| 106 | Controller-Profile | 複数プロファイル切替可能 | LoadProfile() API |
