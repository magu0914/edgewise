# AUDIO — BGM の入れ方

DESIGN.md §8-2 の BGM の再生基盤（Phase 3c）に曲を入れる手順。曲ファイルを置いて `BgmLibrary` に割り当てるだけで鳴る。コードの変更や `ProjectSetup` のやり直しは要らない。

## 手順

1. **曲を置く**: `Assets/Audio/Bgm/` に `.ogg`（推奨）か `.wav` を置く。
   - 置いた瞬間に `BgmImportSettings`（AssetPostprocessor）が WebGL 向けのインポート設定を付ける（下の表）。Console に `[Edgewise] BGM import settings applied: ...` が出る。
   - ループする曲は、ファイル自体が継ぎ目なくつながるように書き出しておく（`.mp3` は頭に無音が入りやすいので避ける）。
2. **割り当てる**: `Assets/Game/Settings/BgmLibrary.asset` を選び、Inspector で場面ごとに曲を入れる。

   | 項目 | 場面 | intro（1 回だけ） | loop（ループ） |
   |---|---|---|---|
   | Title | タイトル・対戦設定 | 空でよい | タイトル曲 |
   | Match | 対戦中 | 空でよい | 対戦曲 |
   | Victory | 勝ったとき | 勝利のジングル | 結果画面の曲 |
   | Defeat | 負けたとき | 敗北のジングル（任意） | 結果画面の曲（任意） |
   | Draw | 引き分け | 引き分けのジングル（任意） | 結果画面の曲（任意） |

   - **空の場面は無音**（前の曲をフェードアウトするだけ）。エラーにはならない。
   - 勝敗の場面に曲があるときは、その曲が 3b の合成音のジングル（効果音）の代わりに鳴る。無いときは今までどおり合成音が鳴る。
   - `volume` で曲ごとの音量差をそろえる。`crossfadeTime`（既定 0.8 秒）、ダッキング（`duckLevel` など）もここで調整する。
3. **ライセンスを記録する**: `docs/CREDITS.md` に曲名・作者・入手先 URL・ライセンスを書く（DESIGN §0）。
4. **サイズを確かめる**: WebGL をビルドし、`Builds/WebGL/Build/` の合計が **16MB 以下**（DESIGN §8-2）か見る。超えたら下の「品質」を下げる。

`BgmLibrary.asset` は `ProjectSetup` が「無ければ空で作る」だけなので、割り当てた曲は `ProjectSetup` を実行し直しても消えない。

## 自動で付くインポート設定

| 設定 | 値 | 理由 |
|---|---|---|
| Load Type | Compressed In Memory | 展開したまま持たない（メモリ節約） |
| Compression / Quality | Vorbis / **0.35** | WebGL では Unity が AAC に変換し、ビットレートはこの品質で決まる |
| Load In Background | ON | 最初の画面で読み込みを待たせない |
| Force To Mono | OFF | BGM はステレオのまま |
| Sample Rate | 元のまま | |

- この版の Unity（6000.3）では WebGL だけの上書き設定（`SetOverrideSampleSettings("WebGL")`）が受け付けられないため、既定の設定に入れている。
- 設定は**最初の取り込み時だけ**付ける（`userData` に `edgewise-bgm-v1` の印）。あとから Inspector で変えた値は上書きしない。
  全部の曲に付け直したいときはメニュー **Edgewise → BGM のインポート設定をやり直す**。
- 目安: 品質 0.35 で 1 分あたり 0.5〜0.7MB 程度。5 場面ぶんで 4MB 前後に収めたい（3b 時点の WebGL は 11.9MB）。

## 鳴り方（BgmPlayer）

- 場面が変わると、前の曲を `crossfadeTime` で下げながら次の曲を上げる（等パワーのクロスフェード）。ジングルなど intro から始まる曲は頭を立たせるため 0.05 秒で上げる。
- 同じ場面をもう一度要求しても頭から鳴らし直さない（「もう一度」で対戦曲が続く）。
- SAME / PLUS / COMBO の瞬間は BGM を一瞬下げる（ダッキング。連鎖が深いほど長く）。演出速度「即時」では下げない。
- 音量 = 全体 × BGM（設定画面）× 曲ごとの `volume` × フェード × ダッキング。全体ミュートで 0。
- ブラウザは最初のクリック・タップまで音を出せない。タイトル曲は最初の操作で鳴り始める。
- 「裏では消音」（設定、初期値 ON）: タブが裏に回ると `Assets/Plugins/WebGL/EdgewiseAudio.jslib` が AudioContext を止め、表に戻ると再開する。

## テスト

曲ファイルがまだ無いので、PlayMode テスト（`Assets/Tests/PlayMode/AudioFlowTests.cs`）の中でだけ `AudioClip.Create` で合成した仮の曲を割り当てて、切り替え・クロスフェード・ジングル → ループ・ダッキング・音量を確かめている。仮の曲はアセットにしないのでビルドには入らない。
