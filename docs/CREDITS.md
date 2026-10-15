# クレジット・ライセンス

本作のカード絵・背景・盤面・アイコンはすべてシェーダーとコードによる手続き生成で、外部の画像素材は使っていません。
効果音もすべて `AudioClip.Create` による合成音（`SfxSynth`。正弦波・三角波・ノイズ＋エンベロープ）で、外部の音素材は使っていません。
外部から取り込んだ素材は以下のとおりです。

## BGM（OpenTracks、旧 DOVA-SYNDROME）

すべて [OpenTracks](https://opentracks.com/) のフリー素材です（2026-10-16 取得）。[音源利用ライセンス](https://opentracks.com/help/articles/license/)に従い、ゲームの背景音楽として使用しています。作曲者の利用条件はいずれも「サイト準拠」です。
ライセンスで音源ファイルそのものの配布が禁止されているため、**曲ファイルはリポジトリに含めていません**（`.gitignore` 対象）。ビルドしたゲームには変換された形で含まれます。手元で鳴らすには、下の入手先から取得して `docs/AUDIO.md` の手順で `Assets/Audio/Bgm/` に置いてください。

| 場面 | 曲名 | 作曲者 | 入手先 | 加工 |
|---|---|---|---|---|
| タイトル | 星空 | Heitaro Ashibe | https://opentracks.com/bgm/detail/22997 | WAV に変換のみ → `bgm_title_hoshizora.wav` |
| 対戦 | ギザなビート（トラック 2・ループ版） | マニーラ | https://opentracks.com/bgm/detail/22412 | WAV に変換し、MP3 の先頭 1105 サンプルの遅延と末尾の余白を除いて 96.000 秒ちょうどのループに切り出し → `bgm_match_gizana_beat.wav`（ffmpeg `atrim=start_sample=1105:end_sample=4609105`） |
| 勝利 | 明日へのファンファーレ | のる | https://opentracks.com/bgm/detail/22159 | WAV に変換のみ → `jingle_victory_ashita_fanfare.wav` |
| 敗北 | Too late... | t.tam | https://opentracks.com/bgm/detail/4016 | WAV に変換のみ → `jingle_defeat_too_late.wav` |

素敵な楽曲をありがとうございます。

## フォント

### Noto Sans JP（Regular）
- 入手元: Google Fonts（https://fonts.google.com/noto/specimen/Noto+Sans+JP）の配布物 `static/NotoSansJP-Regular.ttf`（2026-10-15 取得）
- 著作権: Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'
- ライセンス: SIL Open Font License, Version 1.1（全文: `Assets/Fonts/NotoSansJP-OFL.txt`）
- 使い方: `Assets/Fonts/NotoSansJP-Regular.ttf` から、画面で使う文字（`UiText` の全文字列・ASCII・カード名、276 文字）だけを焼いた
  TextMeshPro の静的な SDF フォントアセット（`Assets/Fonts/NotoSansJP-Regular SDF.asset`）を `ProjectSetup`（`FontBuilder`）が生成する。
  WebGL ビルドに含まれるのはこのフォントアセット（1024×1024 のアトラス）だけで、元の TTF は含まれない。
  文字を足したら `ProjectSetup` を実行し直す（文字の集合が変わったときだけ作り直す）。

## Unity パッケージ同梱の素材

### TextMesh Pro Essential Resources（com.unity.ugui 同梱）
- シェーダーと設定ファイル（`Assets/TextMesh Pro/`）を使用。Unity Companion License に従う。
- 英字フォント LiberationSans と絵文字スプライト EmojiOne は使わないため、`ProjectSetup` が取り込み後に削除している（WebGL のサイズ削減）。
  帰属表示のテキスト（`LiberationSans - OFL.txt`、`EmojiOne Attribution.txt`）はパッケージの取り込み物としてそのまま残している。

## オマージュの表記

本作は Triple Triad（© SQUARE ENIX）へのオマージュ作品であり、公式とは無関係です。ルールの仕組みのみを参考にし、
カード名・キャラクター・絵・音・ロゴ・名称は使っていません（タイトル画面にも同じ表記を出している）。
