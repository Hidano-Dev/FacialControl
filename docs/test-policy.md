# テスト方針（何を守るテストを残すか）

1.0.0 リリース前（2026-09-26）にテストコード全体を棚卸しして決めた指針。新しいテストを書くとき、既存テストを直すか消すか迷ったとき、spec 実装で codex や Claude にテストを書かせるときに参照する。

テストの**サイズ**（Small / Medium / Large、`[SmallTest]` 等の宣言と CI での回し方）は `docs/testing.md` で定める。この文書は「残すかどうか」、`testing.md` は「どの環境に依存してよいか」を扱う。

## 基本原則

- テストは「振る舞い」を守るために存在する。**振る舞いを変えずにリファクタしたときに赤くなるテストは、実装をテストしている**。それは拡張の足枷になるので書かない・残さない。
- 量そのものは問題にしない。Domain 層の細かい単体テストは安く、壊れず、重荷にならない。重荷になるのは「実装に結合したテスト」「壊れやすい UI テスト」「放置された赤」の 3 つ。
- 恒常的な赤は許容しない。赤が常態化すると、スイート全体の信号価値が失われる。

## 分類と判定

| 区分 | 守っている対象 | 判定 | 例 |
|---|---|---|---|
| A | 公開契約。JSON スキーマ、OSC ワイヤ形式、遷移の数値結果、ブレンド結果、Inspector から見えるデータ | 残す | JsonParser の round trip、OscMessageSerializer、TransitionCalculator |
| B | 実機で起きた不具合の再発防止 | 残す。症状をコメントに明記し、名前は歴史ではなく振る舞いで付ける | FacialController 二重配置の自動無効化、LateUpdate の出力順、ContributeMask 長不一致 |
| C | 設計上の不変条件。ホットパスの GC ゼロ、asmdef 可視性、レイヤー依存方向 | 計測が成立するものだけ残す。空振りは削除 | FacialControllerGcZeroGate、EditorOnlyVisibility |
| D | 実装詳細。private 状態、ログ文言、UI 要素名 | **原則削除**。B の唯一の防波堤になっている場合だけ、観測可能な振る舞いへ書き換えて残す | private フィールドを reflection で読む、`LogAssert.Expect` で文言を完全一致 |
| E | テスト基盤そのもの。Fake、ヘルパー、TestBase | 削除。Fake が壊れれば本体テストが壊れるので二重 | `Fake*Tests`、`*TestHelperTests` |
| F | 開発史の記録。Probe（計測器の較正）、Spike、Phase、Legacy、preview からの Migration | 削除。現行の振る舞いを検証しているものは名前を振る舞いに改めて残す | `*ProbeTests`、`*MigrationTests`、`Phase2*` |

### D の具体的な判定基準

次のいずれかに当たれば D。

1. **private への reflection 到達**。`BindingFlags.NonPublic` で本番型の private フィールド・メソッド・ネスト型を名前文字列で掴んでいる。
   - 準備のためだけで assert が公開 API の観測結果なら、公開 API 経由に書き換える。
   - assert 自体が private 状態を見ているなら削除。
   - 属性・型階層など公開メタデータの検査、`SerializedObject.FindProperty("_field")` によるシリアライズ契約の検査は D ではない。
2. **ログ文言の完全一致**。`LogAssert.Expect(LogType.X, "完全な文字列")`。
   - ログを出すこと自体が目的のテストは削除。
   - 振る舞いを assert していて Expect が付随（想定される警告で失敗しないため）なら、`new Regex("[ClassName]")` のように文言改善で変わらない断片だけを照合する。

「内部呼び出し順」は一般には D だが、このリポジトリで順序を assert しているテストは rec 再生の排他獲得順と LateUpdate の出力順の 2 つで、どちらも実行時の挙動に直結する B である。

## Editor UI テストは最小限

UI Toolkit の要素名で UI ツリーの中身を検証するテストは、最も高コストで壊れやすい。各 Inspector / EditorWindow / PropertyDrawer につき次の smoke だけを残す。

1. 生成できる。`CreateInspectorGUI()` / `CreateGUI()` / `CreatePropertyGUI()` が例外なく VisualElement を返す。
2. 保存が通る。UI から SO への書き込み経路が例外なく通る。
3. 破棄・対象切替で例外を出さない。

Editor 層でも UI ツリーに依存しない純粋ロジック（Routing の Logic、AnimationClip サンプラ、AutoExport など）は通常の A〜D 判定に従う。

Editor で実機再現した不具合（Inspector 切替時の破棄済み SerializedObject による NRE、Inspector 破棄時の自動保存取りこぼし、Suppress が Play で Default に戻る、連続キャプチャが同一画像になる stale RT）は B として残す。

## 性能テスト

`Tests/*/Performance/` は既定スイートに残す。計測器の較正記録（Probe）は性能テストではなく F。`GC.GetAllocatedBytesForCurrentThread` は Unity Mono で常に 0 を返すため、これに依存する確保ゼロ assert は何も検証していない。使うなら `GC.GetTotalMemory` 差分か ProfilerRecorder の `GC.Alloc`。

## ファイル配置と命名

- **テストファイルはテスト対象クラス単位** `{Target}Tests.cs`。spec や task の単位でファイルを増やさない。spec 実装で既存クラスにテストを足すときは、既存の `{Target}Tests.cs` に追記する。
- SetUp の前提が異なる fixture は、同じファイル内に別 `[TestFixture]` クラスとして置き、クラス名は前提を表す（`LayerUseCaseWithOverlayLayerTests`）。spec 名や機能追加の経緯を名前にしない。
- テスト名は `{Method}_{Condition}_{Expected}`。Legacy / V2 / Phase / NewSchema / task 番号などの履歴語をテスト名・コメントに入れない。
- 性能テストとサンプル同期テストは対象クラス単位の例外として独立ファイルでよい。

## 赤の扱い

- 赤いテストを放置しない。直すか、決定的にできないなら消す。
- フレーキー（seed やフレームタイミングに依存）は、まず待ち不足や遷移途中の読み取りなどテスト側の前提を疑う。プロダクト側の共有状態が疑われる場合は、テストを消す前に挙動を確認する。
- pre-existing の赤を spec の FAIL 判定に巻き込まない。

## 棚卸しの記録（2026-09-26）

| 作業 | 内容 |
|---|---|
| spec 分裂ファイルの統合 | 19 クラスタ 53 ファイルを 19 ファイルに統合。履歴由来のテスト名約 45 本を振る舞い名に改名 |
| D の削除 | private reflection 到達 72 ファイル、ログ文言一致 43 ファイルを判定。約 40 テストを削除、約 10 テストを公開 API 観測へ書き換え、LogAssert 約 90 箇所を安定断片の Regex 化。残る NonPublic 到達 19 ファイルは準備専用か B 例外で、各ファイルに理由コメントあり |
| Editor UI の最小化 | Inspector 34→6、Routing 36→7、ExpressionCreator 38→11 など、UI ツリー依存テストを smoke と実機不具合ガードだけに縮小 |
| E / F の削除 | Fake テスト 7 ファイル、Probe 2 ファイル、Migration / Guard / Spike 3 ファイル、Smoke 3 ファイルを削除。計測器の positive control 2 件は残置 |
| 既知の赤 | SampleAssetsAreInSync 4 件は解消済みを確認。heartbeat ハッシュ非決定 2 件はプロダクト側の重複蓄積不具合を修正。TenCharacterIsolation は deltaTime 固定で決定化。ログ Expect のみの 1 件は削除 |
| 結果 | テスト .cs 334 → 273 ファイル、約 76k → 68k 行。EditMode 1,922 件 / PlayMode 418 件すべて緑（Explicit 除く） |
