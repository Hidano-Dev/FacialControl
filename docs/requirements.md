# FacialControl 要件定義書

## 文書情報

| 項目 | 内容 |
|------|------|
| プロジェクト名 | FacialControl |
| 文書バージョン | 3.0.0 |
| 作成日 | 2025-10-19 |
| 最終更新日 | 2026-02-03 |
| 作成者 | Hidano |

## 変更履歴

| バージョン | 日付 | 変更者 | 変更内容 |
|-----------|------|--------|----------|
| 1.0.0-preview.1 | 2025-10-19 | Hidano | 初版作成 |
| 2.0.0 | 2026-02-02 | Hidano | QA シート（requirements-qa.md）の回答を元に全面改定 |
| 3.0.0 | 2026-02-03 | Hidano | 技術仕様書 v3.0.0 dig セッションの決定事項を反映。プロファイル概念の再定義（キャラクター単位）、用語統一（Expression）、JSON 構造変更 |

---

## 1. プロジェクト概要

### 1.1 目的

FacialControl は、3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリ（開発者向けアセット）である。npmjs.com でのパッケージ配布を想定する。

### 1.2 背景

3D キャラクターの表情制御を汎用化し、さまざまなプロジェクトで再利用可能なライブラリとして開発するプロジェクト。

### 1.3 ユースケース

| 優先度 | ユースケース | 説明 |
|--------|-------------|------|
| メイン | VTuber 配信用フェイシャルキャプチャ連動 | 入力デバイスやネットワーク経由で受信した表情データを、リアルタイムに 3D キャラクターへ反映する |
| サブ | GUI エディタでの Expression 作成支援 | Editor 上で BlendShape スライダーを操作しながら、表情（Expression）を効率的に作成する |

### 1.4 ターゲットユーザー

Unity エンジニア（開発者）。本プロジェクトはライブラリ（プラグイン）であるため、技術者向けの設計とする。

### 1.5 スコープ

**対象範囲**

- 表情プロファイルの管理（JSON / ScriptableObject）
- マルチレイヤーによるリアルタイム表情制御（PlayableAPI + AnimationStream ベース）
- ブレンドシェイプ・ボーン・テクスチャ切り替え・UV アニメーションの統合制御
- 入力デバイス（コントローラ / キーボード）による表情切り替え
- OSC（UDP）による表情データのネットワーク送受信
- ARKit 52 / PerfectSync の命名検出
- Editor 拡張（プロファイル管理・Expression 作成支援・JSON 入出力）
- 外部リップシンクプラグインとの連携インターフェース

**対象外**

- ランタイム UI の提供
- 音声解析・リップシンクエンジン
- 物理シミュレーション（クロス、髪の毛等）
- シェーダー・レンダリング品質の制御
- Web カメラによるフェイシャルキャプチャ（将来ロードマップ）
- VR ヘッドセットのフェイストラッキング（将来ロードマップ）
- VRM フォーマット対応（リリース後の早期マイルストーン）
- Timeline 統合（将来拡張）
- UnrealEngine / Blender / TouchDesigner 向けプラグイン（将来ロードマップ）

---

## 2. ステークホルダー

| 役割 | 名前 | 責任範囲 |
|------|------|----------|
| 主要開発者 | Hidano | 設計・実装・ドキュメント |
| レビュー・サポート | Junki Hiroi | コードレビュー・テスト |

---

## 3. 機能要件

### 3.1 機能要件一覧

| ID | 機能名 | 説明 |
|----|--------|------|
| FR-001 | 表情プロファイル管理 | キャラクター単位の表情設定を「プロファイル」で管理し、JSON / ScriptableObject で永続化 |
| FR-002 | マルチレイヤー表情制御 | 複数レイヤーによる Expression の同時適用・排他制御 |
| FR-003 | 表情遷移・補間 | 線形補間を基本とした Expression 間のスムーズな遷移 |
| FR-004 | OSC ネットワーク通信 | UDP + uOsc による FacialControl 同士の表情データ送受信（独自プロトコル） |
| FR-005 | 入力デバイス制御 | InputSystem によるコントローラ / キーボードからの Expression 切り替え |
| FR-006 | ARKit / PerfectSync 対応 | ARKit 52 ブレンドシェイプ・PerfectSync の命名検出 |
| FR-007 | リップシンク連携 | 外部リップシンクプラグインからの入力受付インターフェース |
| FR-008 | Editor 拡張 | Inspector カスタマイズ（プロファイル管理・Expression CRUD・検索・インポート/エクスポート統合）、Expression 作成支援 |
| FR-009 | JSON インポート / エクスポート | プロファイルの JSON 形式での入出力 |

### 3.2 機能詳細

#### FR-001: 表情プロファイル管理

**概要**

キャラクター単位の表情設定を「プロファイル（FacialProfile）」として管理する機能。プロファイルはレイヤー定義と複数の Expression（個々の表情ポーズ）を保持する。

**プロファイルの構成要素**

| 要素 | 説明 |
|------|------|
| レイヤー定義 | 表情レイヤーの名前、優先度、排他モードの設定 |
| Expression 集 | 複数の表情ポーズ（笑顔、怒り等）の定義 |

**Expression の構成要素**

| 要素 | 説明 |
|------|------|
| BlendShape 値マッピング | BlendShape 名と値の組み合わせ（正規データ） |
| 所属レイヤー | Expression が属するレイヤー名（1 つ） |
| 遷移時間 | 入力時の表情遷移にかける時間 |
| 遷移カーブ | 遷移時の補間方式（線形、イージング、カスタムカーブ） |
| レイヤースロット | 他レイヤーへのオーバーライド値 |

**永続化方式**

- コア機能: JSON フォーマットで保存（Unity 以外の環境でも使用可能にするため）
- **JSON が正規データ**: ランタイムでは JSON をパースしてドメインモデルを構築
- Unity Editor 向け: ScriptableObject は JSON への参照ポインターとして機能（Inspector での操作性のため）
- SO と JSON の同期は手動エクスポートのみ（自動同期なし）
- ビルド後のアプリでも JSON で表情設定を差し替え可能
- **1 プロファイル = 1 JSON ファイル**: レイヤー定義と全 Expression を 1 つのファイルに統合

**JSON プロファイル仕様**

| 項目 | 仕様 |
|------|------|
| 後方互換性 | preview 段階では破壊的変更を許容。1.0.0 以降で互換性を担保 |
| スキーマ検証 | preview 段階では実装しない |
| パーサー | System.Text.Json（フォールバック: JsonUtility + ラッパー）。IJsonParser インターフェースで抽象化 |

**デフォルトテンプレート Expression**

| テンプレート | 説明 |
|-------------|------|
| デフォルト | 無操作時の基本表情。ニュートラルフェイスをユーザーが設定可能（モデルのデフォルトが意図しない表情の場合に上書き用） |
| まばたき | まばたき制御 |
| 目線操作 | Profile の Gaze セクションで構成する独立チャネル。Vector2 入力から目ボーンを制御し、入力 source は Inspector で選択 |
| カメラ目線 | Gaze の入力 source 拡張点として扱う。procedural なカメラ目線 source と Vector3 ターゲット指定は将来対応 |

- Expression 数に依存しない設計とし、今後の追加を容易にする
- ユーザーカスタム Expression の上限は暫定 512。ペイロード数を可変にし通信帯域を節約

**プロファイル切替**

- FacialController は複数のプロファイルを切り替え可能（衣装差分等で BlendShape 構成が変わるケースに対応）
- 切替時は PlayableGraph を再構築し、デフォルト表情から開始

---

#### FR-002: マルチレイヤー表情制御

**概要**

複数のレイヤーで Expression を同時適用する仕組み。レイヤーごとに優先度と排他制御のモードを設定可能。レイヤー定義はプロファイル JSON に含まれる。

**デフォルトレイヤー構成**

| レイヤー | 優先度 | 説明 |
|---------|--------|------|
| 感情ベース（emotion） | 最低 | 喜怒哀楽などの基本表情 |
| リップシンク（lipsync） | 中 | 口の動き（外部プラグインからの入力） |
| 目（eye） | 高 | まばたき等の BlendShape 表情。Gaze（目ボーン駆動）はレイヤー外の独立チャネル |

**排他制御**

- 排他モードはレイヤー単位で固定（同一レイヤー内での LastWins と Blend の混在は禁止）
- 「後勝ち（LastWins）」: 同一レイヤー内で新しい Expression が前の Expression をクロスフェードで置換
- 「ブレンド（Blend）」: 同一レイヤー内の複数 Expression が加算ブレンド（0〜1 クランプ）
- レイヤー優先度はユーザーが設定可能（デフォルト優先度は提供する）

**レイヤー間の相互作用**

- レイヤーウェイトによるブレンドで制御
- 高優先度レイヤーが低優先度レイヤーをブレンドウェイトに基づいて上書き
- Expression の layerSlots によるクロスレイヤーオーバーライド（完全置換方式）

**レイヤー検証**

- Expression が未定義のレイヤーを参照した場合、警告ログを出力しデフォルト（emotion）レイヤーにフォールバック

---

#### FR-003: 表情遷移・補間

**概要**

Expression が切り替わる際のスムーズな遷移を提供する。

**補間方式**

| 項目 | 仕様 |
|------|------|
| デフォルト方式 | 線形補間 |
| カスタム方式 | イージング（EaseIn / EaseOut / EaseInOut）、任意の AnimationCurve で上書き可能 |
| 遷移時間の範囲 | 0 秒（瞬時切り替え）〜 1 秒 |
| デフォルト遷移時間 | 0.25 秒 |

**遷移中の割り込み**

遷移中に新しい表情がトリガーされた場合:
1. 現在の遷移を即座に中断する
2. 現在の補間値（中間値）を起点として新しい遷移を開始する
3. 遷移時間は設定値通り一定
4. **GC フリー**: スナップショットバッファ（NativeArray）の再利用で遷移割込時の GC はゼロ

**テクスチャ・UV アニメーション**

- テクスチャ切り替えと UV アニメーションは AnimationClip 内のキーフレームとして定義
- FacialControl は AnimationClip を再生するだけで自動的に対応
- preview.1 では BlendShape のみ JSON 対応。テクスチャ / UV の JSON 対応は preview.2 以降

---

#### FR-004: OSC ネットワーク通信

**概要**

UDP + uOsc を用いた、FacialControl 同士の独自プロトコル（OSC で運ぶ）による表情データの送受信。

> **2026-10-08 改訂（HID-169）**: VRChat OSC 互換（`/avatar/parameters/{name}` 形式）・ARKit の OSC プリセット（`/ARKit/{name}`）・FacialControl 以外の OSC アプリからの受信は要件から外した。いずれも使われていないため。外部 OSC 互換が必要になったら、別パッケージを追加して実装する。

**通信仕様**

| 項目 | 仕様 |
|------|------|
| プロトコル | UDP |
| ライブラリ | uOsc（自前フォーク `com.hidano.uosc`。ポート再利用設定のみ変更） |
| OSC アドレスパターン | FacialControl 同士の独自プロトコル（外部 OSC アプリとの互換は持たない） |
| 送受信頻度 | 1 フレーム間に複数回送受信可能 |

**設計方針**

- Expression はローカル管理の概念。OSC 通信時には Expression 内の BlendShape 値に分解して送信
- OSC 以外の通信プロトコルもインターフェースで抽象化し、将来拡張可能にする
- **受信**: ダブルバッファリング方式（ロックフリー・低遅延）
- **送信**: 別スレッドで非同期送信（メインスレッド負荷ゼロ）
- OSC 受信値とローカル Expression は区別せず、後勝ち（LastWins）で統一
- OSC マッピングテーブルは config.json で管理（preview.1 では JSON 直接編集のみ）

---

#### FR-005: 入力デバイス制御

**概要**

Unity InputSystem による Expression 切り替え入力の管理。

**仕様**

| 項目 | 仕様 |
|------|------|
| 対応デバイス | ゲームコントローラ、キーボード |
| 入力方式 | ボタン（トグル）でアクティブ/非アクティブ切り替え + アナログ（強度）で 0〜1 制御 |
| デフォルトバインディング | 最小限のトリガーバインディングのみ同梱。強度制御はユーザーがカスタマイズ |
| カスタマイズ | InputAction Asset の差し替えで変更可能 |
| 抽象化 | 入力インターフェースを抽象化し、ユーザーが独自実装に差し替え可能 |

---

#### FR-006: ARKit / PerfectSync 対応

**概要**

ARKit 52 ブレンドシェイプおよび PerfectSync に対応したモデルの命名検出。

> **2026-09 改訂（HID-34）**: 当初の「Expression 自動生成 + OSC マッピング自動生成」（ARKit 検出ツール）は廃止した。生成される Expression はグループ内の BlendShape を全部 1.0 にした塊で、Clip ベース + キャプチャ入力の運用では使われなかったため。キャプチャ連動は OSC Receiver / iFacialMocap binding が BlendShape 名で直接駆動し、OSC マッピングは `com.hidano.facialcontrol.osc` の heartbeat 自動マッピングと ARKit プリセットで代替する。

**仕様**

| 項目 | 仕様 |
|------|------|
| 対応範囲 | ARKit 52 ブレンドシェイプ + PerfectSync |
| マッチング方式 | 完全一致のみ（誤検出リスクゼロ。独自命名モデルは手動マッピング） |
| 命名検出 | core の `ARKitDetector`（ARKit 52 + PerfectSync 13 の名前表・レイヤーグループ分類・完全一致検出）を API として提供 |
| OSC 対応 | ARKit の OSC プリセット（`/ARKit/{name}`）は持たない（2026-10-08 改訂、HID-169） |
| 未対応パラメータ | 警告なしでスキップ |
| リリース対応 | 初回プレリリースから完全対応 |

---

#### FR-007: リップシンク連携

**概要**

FacialControl はリップシンク用レイヤーの管理のみを提供する。音声解析は行わない。

**仕様**

- 外部リップシンクプラグイン（uLipSync 等）からの入力を受け付ける `ILipSyncProvider` インターフェースを提供
- 外部から受け取った口の形（BlendShape 値）をリップシンクレイヤーに適用
- 内部実装は固定長バッファで GC フリー

---

#### FR-008: Editor 拡張

**概要**

UI Toolkit で実装する Editor 専用の拡張機能群。ランタイム UI は提供しない。

**機能一覧**

| 機能 | 説明 |
|------|------|
| Inspector カスタマイズ | FacialController コンポーネントの編集 UI + FacialProfileSO Inspector でのプロファイル管理（Expression の追加・編集・削除・検索、JSON インポート/エクスポート、新規プロファイル作成）。データソースはプロファイル JSON |
| Expression 作成支援ツール | 専用プレビューウィンドウ（Scene とは独立）で 3D モデルを表示し、BlendShape スライダーでリアルタイムプレビューしながら Expression を作成。Scene オブジェクトと Prefab/FBX の両方から対象モデルを指定可能。プレビューは値変更毎に即座更新 |
| JSON インポート / エクスポート | プロファイル JSON の入出力。SO と JSON の同期は手動エクスポートのみ |

**保存方式**

- Editor での編集は Unity の Undo システムと連動し、値変更時に JSON へ自動保存

---

#### FR-009: JSON インポート / エクスポート

**概要**

プロファイルデータの JSON 形式での入出力。

**仕様**

| 環境 | 保存形式 |
|------|----------|
| Unity Editor | ScriptableObject（JSON への参照ポインター）+ JSON ファイル |
| スタンドアロン（ビルド後） | JSON のみ |

- AnimationClip としてのエクスポートは不要
- JSON 以外のフォーマットも不要
- JSON が正規データ。SO は参照用ビュー

---

## 4. 非機能要件

### 4.1 パフォーマンス

| 指標 | 基準 |
|------|------|
| GC アロケーション | 毎フレーム処理でゼロ目標 |
| 初期化時 / Expression 切替時 / プロファイル切替時 | GC 許容 |
| 遷移割込時 | GC ゼロ必須（NativeArray 再利用） |
| 浮動小数点 | `float` 基本 |
| BlendShape 値範囲 | 内部 0〜1（Adapters 層で 0〜100 に変換）。範囲外値は無言でクランプ |
| 表情遷移 | 線形補間 0〜1 秒対応 |
| ネットワーク | 1 フレーム間に複数回 UDP 送受信可能 |
| Expression 上限 | ユーザー Expression 最大 512（ペイロード可変） |
| 同時キャラクター | 10 体以上の同時制御を想定 |
| 最適化戦略 | プレリリースは通常 C#。インターフェース設計で Jobs / Burst に差し替え可能 |
| JSON パース | パース負荷を抑えるデータ構造。初期化時のみ |
| UDP 送受信 | メインスレッドに依存しない設計（ダブルバッファリング + 非同期送信） |
| NativeArray | モデルの BlendShape 数で初期サイズ決定。プール方式で再利用 |

### 4.2 対応プラットフォーム

| プラットフォーム | 対応状況 |
|----------------|----------|
| Windows PC | 対象（現行） |
| macOS / Linux | 将来拡張の余地を残す |
| Android / iOS | 将来拡張の余地を残す |
| WebGL | 将来拡張の余地を残す |
| VR | 将来拡張の余地を残す |

### 4.3 互換性

| 項目 | 仕様 |
|------|------|
| レンダーパイプライン | 非依存（URP / HDRP / Built-in いずれでも動作） |
| シェーダー | 非依存（lilToon 等は開発環境のみ） |
| 物理演算 | 非依存（MagicaCloth2 は開発環境のみ） |
| GPU | 非依存 |
| Animator | 既存 Animator と共有（ボディアニメーションと共存可能） |

### 4.4 対応モデルフォーマット

| フォーマット | 対応状況 |
|-------------|----------|
| FBX | プロトタイプから標準対応 |
| VRM | リリース後の早期マイルストーンで対応予定 |
| glTF | スコープ外 |

**ブレンドシェイプ仕様**

- 特定のブレンドシェイプ仕様には準拠しない（様々なモデルデータに対応するため）
- 命名規則は固定しない
- 2 バイト文字（日本語等）や特殊記号を正しく扱える実装が必要

**複数 Renderer 対応**

- 1 つの FacialController が複数の SkinnedMeshRenderer を制御可能
- 全子階層を再帰検索（`GetComponentsInChildren`）+ Inspector で手動オーバーライド可能
- 同名 BlendShape は renderer フィールドで Renderer ごとに個別指定可能（省略時は全 Renderer に同一値適用）

### 4.5 テスト方針

| 項目 | 方針 |
|------|------|
| 開発手法 | TDD（Red-Green-Refactor）を厳密に遵守 |
| カバレッジ目標 | 数値目標は設定しない。TDD 運用で自然にカバレッジを確保 |
| ビジュアルリグレッション | 不要 |
| CI/CD | GitHub Actions + セルフホストランナー（Windows マシン） |

**テスト配置基準**

| 配置先 | 基準 | 例 |
|--------|------|-----|
| EditMode | モック・Fake のみ、同期実行、PlayMode 機能不要 | JSON パーステスト、Expression モデルテスト |
| PlayMode | MonoBehaviour ライフサイクル、コルーチン、実 UDP/OSC 通信、フレーム同期が必要 | 表情遷移の補間テスト、OSC 送受信テスト |

### 4.6 エラーハンドリング

| 項目 | 方針 |
|------|------|
| ログ出力 | Unity 標準（Debug.Log / Debug.LogWarning / Debug.LogError）のみ |
| カスタムロガー | 実装しない |
| クラッシュレポート | 収集しない |
| エラー確認方法 | ユーザーは Unity Console で確認 |
| 不正 JSON | 例外スロー（開発者向けライブラリのため即座に報告） |
| 未定義レイヤー参照 | 警告ログ + デフォルトレイヤーにフォールバック |

---

## 5. アーキテクチャ

### 5.1 設計方針

クリーンアーキテクチャを採用。Domain / Application / Adapters / Presentation のレイヤー分離。Unity 依存を Adapters 層に封じ込める。

### 5.2 ディレクトリ構成

```
Runtime/
├── Domain/             # ドメインロジック（NativeArray 使用、Unity.Collections 依存）
│   ├── Models/         # FacialProfile, Expression, BlendShapeMapping 等
│   ├── Interfaces/     # ILipSyncProvider, IBlinkTrigger, IJsonParser 等
│   └── Services/       # 遷移計算、排他ロジック等
├── Application/        # ユースケース
│   └── UseCases/       # プロファイル管理、レイヤー制御等
└── Adapters/           # Unity 依存の実装
    ├── Playable/       # PlayableAPI（ScriptPlayable + AnimationStream）
    ├── OSC/            # OSC アダプター（ダブルバッファ、非同期送信）
    ├── Json/           # JSON パーサー（System.Text.Json / JsonUtility 実装）
    ├── ScriptableObject/ # SO マッパー（参照ポインター）
    └── Input/          # InputSystem アダプター
Editor/                 # Editor 拡張（UI Toolkit）
Tests/
├── EditMode/           # PlayMode 不要なテスト
│   ├── Domain/
│   ├── Application/
│   └── Adapters/
├── PlayMode/           # PlayMode 必須なテスト
│   ├── Integration/
│   └── Performance/
└── Shared/             # EditMode / PlayMode 共用（Fakes 等）
```

各レイヤーは asmdef で依存方向を強制する。

### 5.3 表情制御方式

ブレンドシェイプ + ボーン + テクスチャ切り替え + UV アニメーションの組み合わせ。Gaze の目ボーン駆動は Expression / レイヤー合成とは独立した経路である。

| 制御方式 | 用途 |
|---------|------|
| ブレンドシェイプ | 表情の主要な変形（目、眉、口等） |
| ボーン | 目線操作等 |
| テクスチャ切り替え | AnimationClip 内のキーフレームとして定義 |
| UV アニメーション | AnimationClip 内のキーフレームとして定義 |

**適用方式**

- AnimationStream API で BlendShape を制御（PlayableGraph 内で完結）
- 既存の Animator コンポーネントと共有（ボディアニメーションと共存）
- ScriptPlayable ベースで全補間処理を統一

### 5.4 データフロー

```
JSON (FacialProfile)
  → ドメインモデル (FacialProfile + Expression[])
  → AnimationClip (Adapters層で動的生成, LRUキャッシュ)
  → NativeArray に BlendShape 値を展開
  → ScriptPlayable 内で NativeArray 間の補間計算
  → AnimationStream API で BlendShape に適用
```

### 5.5 パッケージ情報

| 項目 | 値 |
|------|-----|
| パッケージ名 | `com.hidano.facialcontrol` |
| C# 名前空間 | `Hidano.FacialControl`（例: `Hidano.FacialControl.Domain`） |
| ライセンス | MIT |
| 配布先 | npmjs.com |
| uOsc | 自前フォーク `com.hidano.uosc` を必須依存として npmjs.com から取得 |

---

## 6. 開発環境

| 項目 | 値 |
|------|-----|
| Unity バージョン | 6000.3.2f1 (Unity 6) |
| レンダリングパイプライン | URP v17.3.0（PC / モバイル別設定あり） |
| カラースペース | Linear |
| 入力システム | Input System v1.17.0 |
| テストフレームワーク | Unity Test Framework v1.6.0 |
| タイムライン | Timeline v1.8.9 |

**開発環境専用パッケージ（リリース非同梱）**

| パッケージ | 用途 |
|-----------|------|
| lilToon | トゥーンシェーダー（サンプルモデル用） |
| MagicaCloth2 | クロスシミュレーション（サンプルモデル用） |

---

## 7. リリース計画

### 7.1 リリース方式

機能単位で段階的にリリース: preview.1 → preview.2 → ... → 1.0.0

### 7.2 プレリリース（preview.1）スコープ

| 機能 | 対応状況 |
|------|----------|
| コア機能（プロファイル管理、Expression 遷移） | 含む |
| Editor 拡張（フルエディタ） | 含む |
| OSC 通信 | 含む |
| ARKit / PerfectSync 完全対応 | 含む |
| パッケージドキュメント（README / Documentation~ の Markdown） | 含む |
| サンプルシーン | `com.hidano.facialcontrol.inputsystem` の `Multi Source Blend Demo` を同梱（Scene + FacialProfileSO + InputBindingProfileSO + JSON + HUD 一式、モデルはユーザー持ち込み） |
| OSC マッピング Editor UI | 含まない（JSON 直接編集のみ） |

### 7.3 実装順序

Domain → Adapters → Editor のボトムアップ順。TDD（Red-Green-Refactor）に最適な順序。

### 7.4 目標スケジュール

- 2026 年 2 月末: プレリリース（preview.1）

### 7.5 将来ロードマップ

| 優先度 | 機能 |
|--------|------|
| 高 | VRM フォーマット対応 |
| 中 | Timeline 統合（カスタム Track / Clip） |
| 中 | Web カメラによるフェイシャルキャプチャ（ARKit / MediaPipe） |
| 中 | VR ヘッドセットのフェイストラッキング |
| 中 | 外部ソフトウェア連携（VSeeFace、iFacialMocap 等） |
| 低 | モバイル / WebGL / VR プラットフォーム対応 |
| 低 | VRoid Studio 対応 |
| 低 | UnrealEngine / Blender / TouchDesigner 向けプラグイン |

---

## 8. コーディング規約

### 8.1 基本ルール

| 項目 | 規約 |
|------|------|
| 言語 | C# |
| インデント | 4 スペース |
| 中括弧 | 改行して記述 |
| ドキュメント言語 | 日本語 |
| アクセス修飾子 | 明示的な `public` / `private` を推奨 |

### 8.2 命名規則

| 対象 | 規則 | 例 |
|------|------|-----|
| クラス / 構造体 / enum | PascalCase | `FacialProfile`, `Expression` |
| インターフェース | `I` プレフィックス | `IJsonParser`, `ILipSyncProvider` |
| プライベートフィールド | `_camelCase` | `_blendShapeWeight` |
| メソッド | PascalCase | `Activate`, `LoadProfile` |

### 8.3 テスト命名規則

| 対象 | 規則 | 例 |
|------|------|-----|
| テストクラス | `{対象クラス}Tests` | `ExpressionTests`, `FacialProfileTests` |
| テストメソッド | `{メソッド}_{条件}_{期待結果}` | `Activate_ValidExpression_TransitionsToNewExpression` |

---

## 9. 用語集

| 用語 | 説明 |
|------|------|
| プロファイル（FacialProfile） | キャラクター単位の表情設定一式。レイヤー定義と複数の Expression を包含する。1 プロファイル = 1 JSON ファイル |
| Expression（表情） | 個々の表情ポーズ（笑顔、怒り等）。BlendShape 値の組み合わせで 1 つの表情を定義する |
| レイヤー | 表情制御の優先度階層。複数レイヤーの同時適用で最終的な表情が決まる |
| テンプレート | パッケージに同梱されるデフォルトの Expression 定義 |
| ブレンドシェイプ | 3D メッシュの変形ターゲット（モーフターゲット）。表情の主要な表現手段 |
| ARKit 52 | Apple の ARKit が定義する 52 種類の顔のブレンドシェイプ |
| PerfectSync | ARKit 52 を拡張した表情パラメータ仕様 |
| OSC | Open Sound Control。UDP ベースの通信プロトコル |
| uOsc | Unity 向け OSC 通信ライブラリ |
| InputAction Asset | Unity InputSystem の入力バインディング定義ファイル |
| AnimationStream API | PlayableGraph 内で BlendShape にアクセスするための Unity API |
| ダブルバッファリング | スレッド間でデータを安全にやり取りするためのロックフリー方式 |

---

## 10. 参考資料

| 資料 | 場所 |
|------|------|
| 技術仕様書 | `docs/technical-spec.md` |
| QA シート | `docs/requirements-qa.md` |
| CLAUDE.md | `CLAUDE.md` |
| Copilot 指示 | `.github/copilot-instructions.md` |
