# uLipSync Adapter 使用手順

## 1. slot とレイヤー

uLipSync の出力は、FacialControl の **音素 Overlay slot**（`a / i / u / e / o`）への入力源として流れる。まず Profile に slot を宣言する。

1. `FacialCharacterProfileSO` の **表情ライブラリ** タブで **Phoneme slots を初期化 (a/i/u/e/o)** を押す（小文字 5 つ。別名や追加音素は対象外）
2. **Adapter Bindings** で **uLipSync** を Add する。`overlay` レイヤー（優先度 10、`blend`）が自動追加され、入力源 id に `overlay:{slot}` と `lipsync-overlay:{slot}` の 10 件が並ぶ

手で書く場合の JSON は次のとおり。

```json
{
  "slots": ["a", "i", "u", "e", "o"],
  "layers": [
    {
      "name": "overlay", "priority": 10, "exclusionMode": "blend",
      "inputSources": [
        { "id": "overlay:a" }, { "id": "lipsync-overlay:a" },
        { "id": "overlay:i" }, { "id": "lipsync-overlay:i" },
        { "id": "overlay:u" }, { "id": "lipsync-overlay:u" },
        { "id": "overlay:e" }, { "id": "lipsync-overlay:e" },
        { "id": "overlay:o" }, { "id": "lipsync-overlay:o" }
      ]
    }
  ]
}
```

`lipsync-overlay` は binding の slug に依存しない固定 prefix。Slots に宣言されていない音素、または Analyzer Profile に存在しない音素は登録されず、1 件も登録できない場合は警告が出る。

## 2. 音素エントリ

| 形式 | 設定 | サンプリング |
|---|---|---|
| Expression | Profile の Expression id。未割当で PhonemeId が A〜O なら id / 名前が一致する Expression に自動リンク | Expression の snapshot |
| AnimationClip | AnimationClip | Clip の **終端時刻**（`length` が 0 なら先頭）を `SampleAnimation` して BlendShape 値を採取。全値 0 なら同名 Expression にフォールバック |
| BlendShape | BlendShape 名 | 名前一致の 1 BlendShape |

MaxWeight は 0〜100 で指定し、`Max Weight Scale` を掛けたうえで 0〜1 に正規化される。AnimationClip 形式は Host GameObject 配下の renderer path と Clip の curve binding が一致している必要がある。詳しい比較は [phoneme-entry-format-guide.md](phoneme-entry-format-guide.md)。

## 3. 表情側の Override / Suppress

各 Expression の **Overlays** で slot `a〜o` を指定すると、その表情がアクティブな間の口形状を切り替えられる。

| 状態 | 動き |
|---|---|
| Default | uLipSync の既定出力（音素エントリ）を使う |
| Override | 指定した snapshot を既定の代わりに使う。駆動 weight（音素比率 × 音量）は同じなので、無音時は何も出力しない |
| Suppress | その slot は出力しない（すでに口を開けている表情など） |

解決順は Expression の Override / Suppress → Profile の Default Overlays → uLipSync 既定出力。合計 weight が閾値（1e-4）未満のフレームは出力せず、下位レイヤーを上書きしない。

## 4. 入力デバイス

- デバイス名は Inspector のポップアップで選ぶ。値は PlayerPrefs（`Hidano.FacialControl.LipSync.MicDevice.Name` / `.Disambiguator`）に保存され、アセットには残らない
- 空のままにすると `Microphone.devices` の先頭を使い、使用デバイスを `Debug.Log` で通知する。マイクが 1 台も無い場合はエラー
- 名前を指定した場合は ASIO ドライバ一覧 → マイク一覧の順で完全一致を探す。見つからなければエラーで起動を中止する（別デバイスへのフォールバックはしない）
- 同名デバイスが複数ある場合は **Disambiguator Index**（0 始まり）で区別する
- 実行中の切り替えは `ULipSyncAdapterBinding.SwapDevice(deviceName, disambiguatorIndex)`

### ASIO

ASIO / マイクの切替トグルはなく、名前が ASIO ドライバ一覧に一致すれば `uLipSyncAsioInput` が使われる。

1. オーディオインターフェース公式の ASIO ドライバ（無ければ ASIO4ALL 等）をインストールする
2. Inspector のデバイスポップアップで ASIO ドライバ名を選ぶ。候補に出ない場合は手動入力欄に名前を入れる
3. Play して Console に未解決デバイスのエラーが出ないことを確認する

ドライバ名は環境ごとに異なるため、ASIO 用サンプルは同梱しない。

## 5. Analyzer Profile

`_analyzerProfile` が未指定のときは `Resources.Load("FacialControl/LipSync/Default uLipSync Profile")` で同梱 Profile（mfcc 12、16 kHz、音素 A / I / U / E / O / `-`）を使う。独自に学習させた Profile を使う場合だけ割り当てる。

## 6. 音量

音量の正規化と平滑化は uLipSync 本体（`uLipSyncBlendShape` 派生の `FacialControlULipSyncBlendShape`）に委ね、本パッケージでは再加工しない。声が小さい・大きい場合はマイク gain や uLipSync の Profile 側で調整する。
