# FacialControl REC

`com.hidano.facialcontrol` に流れ込む入力（表情トリガーの on/off・アナログ値・Gaze）を記録し、あとから同じ入力を再現するパッケージ。再生中は live 入力を遮断するので、キャプチャした演技を配信中にそのまま再生したり、`com.hidano.facialcontrol.timeline` 経由で Timeline へ書き出したりできる。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | 入力観測バス (`IFacialInputObservationBus`) と入力源レジストリ |

OSC / InputSystem / LipSync / iFacialMocap パッケージには依存しない。どの入力源から来た値でも、core の観測バスを通る限り記録できる。

## 使い方

1. `FacialController` を持つ GameObject に **Add Component → FacialControl → REC Character Binding** を追加する（`RecCharacterBinding`。同 GameObject の `FacialController` を自動で拾う）
2. Play 中に Inspector の **Start Recording** を押す。Recording Name を空にすると Default Recording Name（初期値は空）が使われ、それも空なら `take-yyyyMMdd-HHmmss` 形式で命名される
3. **Stop Recording** で `.fcrec` ファイルが確定する。保存先は `StreamingAssets/FacialControl/{キャラクター名}/recordings/{名前}.fcrec`。同名の録画がすでにある場合は上書きせず、`{名前}-2`, `{名前}-3`… と連番を付けて保存する（実際に保存した名前とパスは Inspector の Path 表示と `LastRecordingName` / `LastRecordingPath` で確認できる）。明示的に上書きする手段は API にも Inspector にも用意していない
4. **Load Target** ドロップダウンで再生するテイクを選び、**Load Recording → Start Playback** で再生する。ドロップダウンには保存済み録画が更新日時の新しい順に並び、録画を止めると保存したテイクが選択された状態になる（フォルダへ直接ファイルを置いた場合は **Refresh** で読み直す）。録画が 1 件も無く Load Target が空のまま Load すると直近に録画したテイク（連番付与後の名前）を読み込む。再生中は live の表情トリガーとアナログ入力が遮断され、記録された値だけが反映される

スクリプトからは同じ操作を `RecCharacterBinding` の API で行える。

```csharp
var rec = GetComponent<RecCharacterBinding>();
rec.StartRecording("take01");   // take01.fcrec が既にあれば take01-2.fcrec に保存される
rec.StopRecording();
rec.LoadRecording();            // 名前を省略すると直近に録画したテイク（rec.LastRecordingName）を読み込む
rec.LoadRecording("take01");    // 名前を指定すればそのテイクを読み込む
IReadOnlyList<string> takes = rec.GetRecordingNames(); // 保存済みテイク名（新しい順）。uGUI の Dropdown の選択肢などに使う
if (takes.Count > 0)
{
    rec.LoadRecording(takes[0]);
}
rec.StartPlayback();            // 完了時は rec.Completed イベント
rec.StopPlayback();
```

`GetRecordingNames()` / `GetRecordings()`（テイク名・パス・更新日時）は録画フォルダを読むファイル I/O なので、毎フレームではなく画面を開いたときや録画を止めたときなど、一覧の更新が必要なときだけ呼ぶ。Play モード外でも呼べ、録画中のテイクと、テイク名としてそのまま読み込めない名前（`..` や前後の空白を含む等）のファイルは含めない。

録画と再生は排他で、片方を開始するともう片方は自動停止する。`OnDisable` / `OnDestroy` でも録画・再生は停止され、録画中のファイルは末尾まで書き切られる。

## 記録される内容

| 種別 | 内容 |
|---|---|
| Trigger on/off | 入力源 id と Expression id の組 |
| Analog sample | 入力源 id と 1〜255 軸の float 値。Gaze の Vector2 もこの形式で記録される |
| Baseline | 録画開始時点でアクティブだった Expression と、有効だったアナログ入力源の現在値 |

Expression id や入力源 id は文字列として先頭で 1 度だけ定義され、以降のレコードは番号で参照する。Profile に存在しない Expression id は再生開始時に 1 回だけ警告され、その id のイベントは無視される。

## ファイル形式

`.fcrec` は独自バイナリ形式（little-endian）。マジック `FREC`、`formatVersion = 1`、開始時刻（Unix ms）のヘッダに続けてレコード列、末尾に duration と件数の Footer を持つ。書き込みは専用スレッド（`RecStreamWriter`）でストリーム出力するため、録画中のメインスレッドに GC アロケーションは発生しない。Footer が欠けたファイルは末尾切れとして警告付きで復元される。

## 再生中の入力遮断

再生開始時点でレジストリに存在する入力源をスナップショットし、そのうえで記録を注入する。

- **Trigger**: 各トリガー入力源を suspend し、スタックを baseline に置き換えてから記録イベントを注入する。停止時は suspend を解除するが、スタックは元に戻さない
- **Analog / Gaze**: baseline にある入力源は記録値を seed にした再生用 source で置き換え、その他のアナログ入力源も 0 seed で置き換える。停止時は自分が置き換えたものだけを元に戻す
- 再生開始後に新しく登録された入力源は遮断の対象外
- `com.hidano.facialcontrol.timeline` の状態 sink もトリガー入力源の一種なので、REC 再生中は Timeline からの表情 on/off も抑止される

## 構成

```
Runtime/
├── Domain/        # RecEvent / RecTimeline / RecBinaryFormat / RecPlaybackScheduler（Unity 非依存）
├── Application/   # RecordingUseCase / PlaybackUseCase
└── Adapters/      # RecCharacterBinding (MonoBehaviour) / RecStreamWriter / RecFileReader / 注入用 Injector
Editor/            # RecCharacterBinding の UI Toolkit Inspector
Tests/             # EditMode 単体 + PlayMode E2E / GC ゼロ gate
```

サンプルは同梱しない。Timeline への書き出しは `com.hidano.facialcontrol.timeline` の **Tools → FacialControl → Timeline → REC Export** を使う。

## ライセンス

[MIT License](LICENSE.md)
