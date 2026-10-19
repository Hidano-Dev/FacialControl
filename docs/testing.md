# テストサイズ（Small / Medium / Large）

Google の「Test size」の考え方をこのリポジトリに導入したときの決まり。**何を守るテストを残すか**は `docs/test-policy.md`、導入時の全テストの分類と阻害要因の調査は `docs/test-size-migration.md` を参照。

## サイズの定義

サイズは「テストが依存してよい実行環境の範囲」を表す。EditMode / PlayMode の別（実行時要件）とは独立した軸で、NUnit の Category（`Small` / `Medium` / `Large`）として宣言する。

| サイズ | 実行モード | 使ってよいもの | 禁止 | 既定 Timeout |
|---|---|---|---|---|
| **Small** | EditMode（同期） | 純粋な C#、インメモリの `ScriptableObject.CreateInstance`、`new GameObject()` とエンジン組み込みコンポーネント（`SkinnedMeshRenderer` 等）、Fake（`ManualTimeProvider` / `InMemorySaveStorage` / `FakeDatagramSender`）、`LogAssert` | シーンロード、MonoBehaviour のライフサイクル（`AddComponent<FacialController>` 等）、`UnityWebRequest`、`PlayerPrefs` / `EditorPrefs`、ファイル I/O、`Resources.*`（`FindObjectsOfTypeAll` を含む）/ `AssetDatabase`、`WaitForSeconds`・`[UnityTest]`、`Time.time` 等の直接参照、`DateTime.Now` / `Stopwatch` / `Thread.Sleep`、`EditorWindow`（`RoutingEditorWindow` 等の派生型の生成を含む）、`InputSystem` / `PlayerLoop` のグローバル状態、ソケット、実デバイス（`Microphone.devices` を読む `DefaultMicrophoneDeviceEnumerator` 等） | 60 秒 |
| **Medium** | PlayMode（エディタ上）、または EditMode でローカル資源を使うもの | シーン、コルーチン、フレーム待ち、Physics、ローカルアセット読み込み（AssetDatabase）、ファイル I/O、PlayerPrefs、MonoBehaviour、ループバック UDP（`127.0.0.1`）、性能・GC 計測 | 外部ネットワーク通信、実デバイス、実機ビルド | 300 秒 |
| **Large** | 実機ビルド、または実サーバー・実デバイス等の外部システム接続 | すべて | — | 900 秒 |

補足:

- EditMode でも AssetDatabase やファイルを触るテストは **Medium**。「EditMode = Small」ではない。
- Small の禁止 API 一覧は `scripts/check-test-sizes.ps1` の `$smallBannedPatterns` と同期させる。表を変えたらスクリプトも変える。
- 現在 Large に分類されたテストはない。属性と CI ジョブだけ用意してある。

## 書き方

### 1. fixture クラスにサイズ属性を付け、`SizedTestFixture` を継承する

```csharp
using Hidano.FacialControl.Testing;
using NUnit.Framework;

[SmallTest]                                   // Category("Small") + Timeout(60_000) を一括付与
public sealed class TransitionCalculatorTests : SizedTestFixture
{
    [Test]
    public void Evaluate_HalfDuration_ReturnsMidpoint() { /* ... */ }
}
```

- 属性は `Hidano.FacialControl.Testing` アセンブリの `[SmallTest]` / `[MediumTest]` / `[LargeTest]`。`[Timeout]` を継承しているので Timeout も同時に効く。短くしたいときは `[SmallTest(5_000)]`。
- 原則 **クラスに 1 つ**。メソッド単位で付けることもできるが、クラスとメソッドで違うサイズを宣言すると SetUp で失敗する（1 テスト = 1 サイズ）。
- 基底クラスのない fixture は `SizedTestFixture` を継承する。`InputTestFixture` など別の基底が必要なら属性だけ付ける。
- `SizedTestFixture` の SetUp メソッド名は `SizedTestFixtureSetUp` なので、派生側で `SetUp()` を定義しても衝突しない。NUnit は基底の SetUp を先に実行する。

### 2. 外部依存は Fake を注入し、`Dependencies` に登録する

`SizedTestFixture` は SetUp でサイズを読み、**Small のテストに Fake 以外が注入されていれば `Assert.Fail`** する。Fake は `IFakeDependency` マーカーを実装したもの（Tests/Shared の `ManualTimeProvider`、`InMemorySaveStorage`、`FakeDatagramSender`、lipsync の `FakePlayerPrefsBackend`）。

```csharp
[SmallTest]
public sealed class StalenessMonitorTests : SizedTestFixture
{
    private ManualTimeProvider _time;

    protected override void RegisterDependencies(TestDependencyRegistry registry)
    {
        _time = registry.Register(new ManualTimeProvider(), "ITimeProvider");
        // registry.Register(new UnityTimeProvider()) は Small では失敗する
    }
}
```

派生側の `[SetUp]` の中で `UseDependency(new InMemorySaveStorage())` と書いてもよい。

| 依存 | インターフェース（Domain） | 本番実装（Adapters） | Fake（Tests/Shared） |
|---|---|---|---|
| 時間 | `ITimeProvider` | `UnityTimeProvider` | `ManualTimeProvider` |
| 保存 | `ISaveStorage` | lipsync `DefaultPlayerPrefsBackend`（`IPlayerPrefsBackend : ISaveStorage`） | `InMemorySaveStorage`、`FakePlayerPrefsBackend` |
| 通信 | `IDatagramSender` | `UdpDatagramSender`（`OscSender.SetBundleSender` で差し替え） | `FakeDatagramSender` |

### 3. 置き場所

- **`Tests/Small/`（`Hidano.FacialControl.Tests.Small`）**: core パッケージの Small テストのうち Domain / Application だけに依存するもの。asmdef の参照は Domain / Application / Tests.Shared / Testing / Unity.Collections / TestRunner に限定しており、Adapters・Editor・OSC・InputSystem・VContainer・UnityEditor はコンパイル時に使えない。新しい Domain / Application のテストはここに置く。
- **`Tests/EditMode/`**: Adapters / Editor 層を対象にする Small と、EditMode の Medium。
- **`Tests/PlayMode/`**: Medium（と将来の Large）。
- asmdef ではエンジンモジュール単位の除外（`UnityEngine.Networking` だけを外す等）ができないため、Small の禁止 API は静的チェックと `SizedTestFixture` のガードで守る。

## CI での回し方

CI はオンプレの Ubuntu 機に登録したセルフホストランナー（`unity-ci-ubuntu-facialcontrol`、ラベル `linux-unity`。`docs/backlog.md` S-22）で動く。Unity 6000.3.19f1（Personal ライセンス）を Xvfb の仮想ディスプレイ上で batchmode 実行し、`.github/actions/run-unity-tests` が `ProjectVersion.txt` とエディタのバージョン一致を確認してから起動する。ランナーは 1 台なのでジョブは直列に走り、`FacialControl/Library` は作業ディレクトリに残して再利用する（初回だけ全 import が走る）。fork からの PR ではセルフホストのジョブを起動しない。手動実行（Run workflow）時は `include_medium` で Medium ジョブの有無を選べる。

Windows 固有の挙動（パス区切り等）はこの CI では検出できないため、Windows でのみ再現する不具合は従来どおりローカルの batchmode 実行で確認する。

| ジョブ | 内容 | タイミング |
|---|---|---|
| `test-size-check` | `scripts/check-test-sizes.ps1`（Unity 不要）。サイズ未宣言 fixture、Small ファイルの禁止 API、Small asmdef の参照逸脱を検出 | すべての push / PR |
| `small-tests` / `lipsync-small-tests` | EditMode `-testCategory Small` | すべての push / PR |
| `medium-tests` / `lipsync-medium-tests` | EditMode + PlayMode `-testCategory Medium` | PR（マージ前ゲート）と main への push |
| `ci-large.yml` | EditMode + PlayMode `-testCategory Large`（REC → Timeline 往復検証の全シナリオ調査を含む） | 手動（workflow_dispatch）と夜間 03:00 JST |

サイズ未宣言のテストは、`test-size-check` に加えて Small ジョブ内の `TestSizeDeclarationTests`（全テストアセンブリを reflection で走査）でも失敗する。`-testCategory` で絞ったジョブは未宣言テストを実行しないため、この二重の検査が必要。

テストが失敗したジョブは、ログの「失敗したテスト:」に fullname を、「失敗の内容(先頭):」に各テストの失敗メッセージの先頭 15 行を出す（最大 20 テスト）。全文は成果物 `*-test-results` の結果 XML で読む。

ローカルでの実行例（Unity Editor は `AGENTS.md` のバージョン固定に従う）:

```powershell
# 静的チェック
pwsh ./scripts/check-test-sizes.ps1

# Small だけ
& "D:/UnityEditors/6000.3.19f1/Editor/Unity.exe" -batchmode -nographics -projectPath ./FacialControl `
    -runTests -testPlatform EditMode -testCategory Small -testResults ./test-results/small.xml

# Medium（EditMode と PlayMode を別々に）
& "D:/UnityEditors/6000.3.19f1/Editor/Unity.exe" -batchmode -nographics -projectPath ./FacialControl `
    -runTests -testPlatform PlayMode -testCategory Medium -testResults ./test-results/medium-playmode.xml
```

## REC → Timeline 往復検証

REC（`.fcrec`）を Timeline に書き出して再生したとき、収録時と同じ顔の動きになるかを人の目視なしで確かめる仕組み（HID-316。目的と進め方は HID-290）。コードは `com.hidano.facialcontrol.timeline/Tests/*/Equivalence/` にある。

- **正解**: 同じ `.fcrec` を REC 再生（`RecCharacterBinding`）で実ランタイムに流したときの、毎フレームの出力
- **比べるもの**: SkinnedMeshRenderer の全 BlendShape と左右の目ボーンの localRotation を、全フレームで比べる。許容誤差は BlendShape 0.01（0〜100）、目線 0.01°（`EquivalenceComparer`）
- **フレームの刻み**: 1/60 秒、1/120 秒、1/144〜1/40 秒でフレームごとに揺らす（シード付き）の 3 通り

| 層 | テスト | サイズ | 内容 |
|---|---|---|---|
| 全経路 | `RecTimelineEquivalenceTests` | Medium（PlayMode） | REC 再生のキャラクターと Timeline 再生のキャラクターを同じ Profile・メッシュ・目ボーンで作り、毎フレームの出力を比べる |
| 遷移計算 | `ExpressionTransitionEquivalenceTests` | Small（EditMode） | シーンを使わず、REC 再生と同じ順序で回した trigger 入力源と、Export → Bake したカーブを比べる（全経路の近似。食い違ったら Medium が正しい） |
| 仕組み自体 | `EquivalenceHarnessSelfTests`（Small）、`RecTimelineEquivalenceTests.Harness_*`（Medium） | | 1 フレームずらした入力を不一致と判定できること、REC 再生・Timeline 再生が決定的であること |
| 現状調査 | `RecTimelineEquivalenceSurveyTests`（PlayMode）、`ExpressionTransitionEquivalenceSurveyTests`（EditMode） | Large | 全シナリオを回して差分レポートと一覧（`survey-medium.md` / `survey-small.md`）を書く。不一致では失敗しない |

**シナリオ**は `RecEquivalenceScenarios` に並べる。`RecEquivalenceScenarioBuilder` で表情の on/off・目線・Analog・値提供型・レイヤー weight・録画開始時の表情を時刻付きで積み、時刻は `OffGrid` で 1/60・1/120 秒の格子から外す（どのフレームでイベントが効くかが浮動小数点の誤差で揺れないように）。乱数を使うものはシードを固定する。キャラクターの構成（BlendShape・Expression・遷移時間・入力源 id）は `RecEquivalenceSpec` にあり、Small と Medium が同じ定義を使う。

**必須にするシナリオ**は `RecEquivalenceScenarios.EnabledInSmall` / `EnabledInMedium` に名前を載せる。載せたシナリオだけを Small / Medium のジョブが回し、1 件でも食い違えば失敗する。既知の差を直す Issue は、自分の差に当たるシナリオ（タグで探す）をここに足して一致させるところまでを完了条件にする。

**レポートの読み方**: 不一致のテストは、失敗メッセージの先頭に次の要約を出す（CI のジョブログの「失敗の内容(先頭):」に出る）。

```
[REC⇔Timeline Medium] scenario=expr-switch-basic seed=0 step=Fixed60 tags=expression-transition
最初の不一致: frame 13 t=0.21667 channel=Smile expected(REC)=5.556 actual(Timeline)=0 diff=5.556 (許容 0.01)
不一致フレーム数: 40 / 193
差の大きいチャンネル: Smile 5.556@t=0.217, ...
```

続けて、Markdown の差分レポート（チャンネルごとの最大差、最初の不一致の前後 0.15 秒の REC イベント、Timeline のトラックと Clip）を出す。同じ内容を `test-results/rec-timeline-equivalence/{層}_{シナリオ}_{刻み}.md` / `.json` にも書き、CI は Medium と Large の成果物に含める。出力先は環境変数 `FACIALCONTROL_EQUIVALENCE_REPORT_DIR` で変えられる。

**全シナリオを回す**: GitHub Actions の `CI (Large)` を手動実行する（夜間にも回る）。成果物 `large-test-results` の `rec-timeline-equivalence/survey-medium.md` が全シナリオの一覧になる。ローカルでは `-testCategory Large` で同じテストを回せる。

## Small で書けないとき（Humble Object パターン）

Small にしたいのに MonoBehaviour・ファイル・ソケット・時刻が必要になったら、**ロジックを Unity 非依存の型に出し、Unity に触る部分を「判断を持たない薄い層（Humble Object）」に残す**。薄い層は Medium で smoke だけ、ロジックは Small で網羅する。

1. **MonoBehaviour に埋まったロジック**: `Update()` の中身を Domain / Application のクラス（例: `LayerUseCase.UpdateWeights(deltaTime)`）へ移し、MonoBehaviour は `Time.deltaTime` を渡すだけにする。`AdapterBindingHost` と各 `AdapterBindingBase` がこの形。
2. **時間**: `Time.unscaledTime` を直接読まず、`ITimeProvider` を受け取る。テストでは `ManualTimeProvider.UnscaledTimeSeconds` を進める。
3. **保存**: `PlayerPrefs` を直接呼ばず `ISaveStorage` を受け取る。テストでは `InMemorySaveStorage`。
4. **通信**: ソケット送信は `IDatagramSender`。受信側はメッセージ単位のハンドラ（`IOscResolvedMessageHandler` など）に直接メッセージを渡してテストする。
5. **ファイル**: `RecStreamWriter` のように `Func<string, Stream>` を受け取り、テストでは `MemoryStream` を返す。
6. **AssetDatabase / EditorWindow**: SO は `ScriptableObject.CreateInstance` で作って渡し、アセット保存・ウィンドウ表示は Editor 側の薄いメソッドに寄せて Medium の smoke で守る（`docs/test-policy.md`「Editor UI テストは最小限」）。

どうしても分離できないものは **Medium にして理由をコメントに書く**。Small に偽装するために禁止 API を隠すことはしない。静的チェックはファイル単位で見ているため、同じ対象クラスでも Small の fixture と Medium の fixture（実デバイスの smoke 等）は別ファイルに分ける（例: `DefaultDeviceEnumeratorTests.cs` と `DefaultMicrophoneDeviceEnumeratorTests.cs`）。

## 新しくテストを書くときのチェックリスト

- [ ] fixture クラスに `[SmallTest]` / `[MediumTest]` / `[LargeTest]` を 1 つ付けた
- [ ] 基底クラスがなければ `SizedTestFixture` を継承した
- [ ] Small なら禁止 API を使っていない（`pwsh ./scripts/check-test-sizes.ps1` が通る）
- [ ] 外部依存は Fake を `Dependencies` に登録した
- [ ] Domain / Application だけに依存する Small は `Tests/Small/` に置いた
- [ ] テスト名は `{Method}_{Condition}_{Expected}`（`docs/test-policy.md`）
