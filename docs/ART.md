# カードイラストと立ち絵（Phase 4・5b-2）

DESIGN §7-1 の実装記録。カードの絵は game-icons.net のシルエット（CC BY 3.0）を土台に、シェーダーで星雲・シルエット・星座を重ねて描く。
クレジットは `docs/CREDITS.md`。

## アイコン選定の方針（4a）

- 50 枚それぞれに、**名前と系統に合う生き物の全身**を 1 つずつ。名前の語源（mouse / kit = 子狐 / hare / lynx / lupus = 狼 / corvus = 烏 / aquila = 鷲 / cygnus = 白鳥 / ursa = 熊 / pegasus / serpens / scorpius …）をまず当て、語源が生き物でない名前は系統（獣・機・霊・竜）と属性から決めた。
- **似た形を並べない**: 人型ロボットは 2 体（robot-golem と megabot。太い胴と細い装甲で形が違う）までに抑え、機械系統の残りは虫・甲殻・海の生き物などの「機械じかけの生き物」に振った。翼を広げた形（wyvern・bat・eagle-emblem・angel-outfit）は 4 つあるが、首と尾の有無・左右対称かどうかで見分けられることをコンタクトシートで確認した。
- **FF シリーズを連想させる形は使わない**: チョコボを連想させるダチョウ・キウイ、ボムを連想させる炎の顔（spark-spirit）、トンベリを連想させるフードの人物（cowled・cultist・hooded-figure）、名前が召喚獣と同じ ifrit は候補から外した。
- 作者は lorc・delapouite・caro-asercion・skoll・cathelineau の 5 名（すべて CC BY 3.0）。
- 個別調整（拡大率・位置・左右反転）は `CardArtTable.Assignments` に書く。4a では、細部の多いアイコン（scarab-beetle・owl など）を少し小さくした程度。

## 対応表

| Lv | カード | 系統 | 属性 | アイコン | 作者 | 理由 |
|---|---|---|---|---|---|---|
| 1 | Glimmouse | 獣 | なし | mouse | Lorc | 名前どおりの鼠。尾まで入った全身 |
| 1 | Dustwisp | 霊 | 風 | ghost | Lorc | wisp（漂う小さな霊）。風になびく形 |
| 1 | Coglet | 機 | なし | mono-wheel-robot | Delapouite | 小さな歯車の子。Lv1 らしい小型の一輪ロボット |
| 1 | Emberkit | 獣 | 火 | fox | Caro Asercion | kit = 子狐。火属性の狐 |
| 1 | Scalefry | 竜 | 水 | axolotl | Caro Asercion | fry = 稚魚。水に棲む「竜の子」としてウーパールーパー |
| 2 | Pyxmoth | 霊 | 聖 | fairy | Lorc | 名前の moth。このアイコンは丸い胴と模様つきの翅で蛾に見える |
| 2 | Bolthare | 獣 | 雷 | rabbit | Delapouite | hare（野うさぎ）の全身 |
| 2 | Rivetcrab | 機 | 水 | sad-crab | Lorc | 鋲打ちの蟹。lorc/crab より脚とはさみがはっきりした全身 |
| 2 | Frostnewt | 竜 | 氷 | gecko | Lorc | newt（イモリ）に近い四つ足のトカゲの全身 |
| 2 | Mirelamp | 霊 | 土 | elysium-shade | Delapouite | 沼の灯（鬼火）。目のある炎の霊 |
| 3 | Lynxlight | 獣 | 聖 | cat | Lorc | 山猫。耳の立った猫の全身 |
| 3 | Gearowl | 機 | なし | owl | Lorc | 歯車の梟。幾何学的な線で機械じかけに見える |
| 3 | Cinderdrake | 竜 | 火 | dimetrodon | Delapouite | 背の帆が燃えさしの炎に見える小型の竜 |
| 3 | Hollowshade | 霊 | 闇 | spectre | Lorc | うつろな影。顔のある亡霊 |
| 3 | Stonetoad | 獣 | 土 | frog | Lorc | ずんぐりした蛙（ヒキガエル）の全身 |
| 4 | Velahound | 獣 | なし | sitting-dog | Delapouite | hound。座った猟犬の全身（lorc/hound は頭部だけ） |
| 4 | Clockwyrm | 機 | 雷 | centipede | Lorc | 時計じかけの長虫。節の連なりが歯車列に見える |
| 4 | Tidewraith | 霊 | 水 | jellyfish | Lorc | 潮に漂う霊。透ける体と垂れた触手 |
| 4 | Marshwyvern | 竜 | 土 | wyvern | Lorc | 名前どおりのワイバーン。翼・首・尾の全身 |
| 4 | Voltsparrow | 機 | 雷 | swallow | Delapouite | 小鳥。Corvusarc（止まった烏）と形が重ならないよう、飛ぶ姿の燕 |
| 5 | Lupusfang | 獣 | 氷 | werewolf | Lorc | lupus = 狼。狼の全身は人狼しかなかった |
| 5 | Orreryx | 機 | 聖 | scarab-beetle | Lorc | 天球儀。太陽を運ぶ聖なる甲虫（スカラベ）を機械じかけの生き物として |
| 5 | Gloamveil | 霊 | 闇 | wing-cloak | Lorc | 黄昏の帳。翼に包まれた人影 |
| 5 | Emberscale | 竜 | 火 | velociraptor | Delapouite | 鱗の小竜。俊敏な二足の全身 |
| 5 | Corvusarc | 獣 | 闇 | raven | Lorc | corvus = 烏。止まった烏の全身 |
| 6 | Aquilaburst | 獣 | 風 | eagle-emblem | Lorc | aquila = 鷲。burst に合わせて翼を広げた形 |
| 6 | Ironcetus | 機 | 水 | dolphin | Delapouite | cetus = 鯨類。sperm-whale は背に島が乗っていて形が崩れるため、全身のイルカ |
| 6 | Lanternmare | 霊 | 火 | cloaked-figure-on-horseback | Caro Asercion | mare（馬）＋灯の霊。外套の騎手を乗せた馬で亡霊の騎行 |
| 6 | Serpenscoil | 竜 | 土 | cobra | Delapouite | serpens（蛇座）＋coil（とぐろ）。とぐろを巻いた蛇 |
| 6 | Rimeloom | 霊 | 氷 | djinn | Delapouite | 霜の中に「ぬっと現れる」霊。宙に浮く精霊の全身 |
| 7 | Leomane | 獣 | 聖 | lion | Lorc | leo = 獅子。**頭部だけ**（獅子の全身が無いため。たてがみが一番の特徴） |
| 7 | Forgecolossus | 機 | 火 | robot-golem | Lorc | 鍛冶場の巨像。太い胴の人型 |
| 7 | Auroraveil | 霊 | 氷 | fairy | Delapouite | オーロラの帳をまとう妖精。杖と翅の全身 |
| 7 | Draconyx | 竜 | 闇 | hydra | Lorc | 闇の竜。複数の首で Lv7 らしい威圧感 |
| 7 | Stormgear | 機 | 雷 | missile-mech | Lorc | 嵐の機械。二足の歩行機械（人型ロボットと重ならない形） |
| 8 | Cygnwraith | 霊 | 風 | swan | Lorc | cygnus = 白鳥 |
| 8 | Taurocrag | 獣 | 土 | bison | Delapouite | taurus = 牡牛。岩のような巨体の野牛の全身 |
| 8 | Gravelith | 機 | 土 | trilobite | Lorc | lith = 石。化石（三葉虫）の装甲の節が機械に見える |
| 8 | Voltwyrm | 竜 | 雷 | sea-serpent | Lorc | wyrm = 大蛇の竜。牙のある竜頭の長い体 |
| 8 | Nocturnis | 霊 | 闇 | bat | Delapouite | 夜（nocturne）の霊。翼を広げた蝙蝠 |
| 9 | Orionmane | 獣 | 火 | tiger | Delapouite | 狩人オリオンにちなむ大きな猛獣。縞の虎の全身 |
| 9 | Astrolabion | 機 | 聖 | praying-mantis | Delapouite | 星の観測器。角度を測る腕のような鎌をもつ蟷螂 |
| 9 | Cassiveil | 霊 | 水 | mermaid | Delapouite | カシオペア（海の神々を怒らせた女王）。海の女の霊 |
| 9 | Scorpidrake | 竜 | 闇 | scorpion | Lorc | scorpius = 蠍 |
| 9 | Polarion | 竜 | 氷 | pterodactylus | Delapouite | 極地の空を飛ぶ竜。翼竜の全身 |
| 10 | Andromede | 霊 | 聖 | angel-outfit | Lorc | アンドロメダの聖なる霊。光輪と大きな翼（**体は翼に隠れている**） |
| 10 | Pegasion | 獣 | 風 | pegasus | Skoll | ペガサス |
| 10 | Ophiwyrm | 竜 | 土 | dragon-spiral | Lorc | ophiuchus（蛇遣い座）の大蛇竜。渦を巻く竜で Lv10 の存在感 |
| 10 | Sextantor | 機 | 雷 | megabot | Lorc | 六分儀の名を持つ最上位の機械。細部の多い大型の人型 |
| 10 | Ursamaw | 獣 | 氷 | polar-bear | Cathelineau | ursa = 熊。氷属性なので白熊 |

### 迷ったカード
- **Leomane**（lion）: 獅子の全身のアイコンが無い。Sphinx（獅子の体に人の頭）も候補だったが、一目で獅子とわかる頭部を優先した。
- **Andromede**（angel-outfit）: 聖属性 Lv10 にふさわしい全身の天使が無い。octoman（翼のある人型）は触手にも見え、harpy は怪物の印象が強い。光輪と翼で一目で「天の存在」とわかるものを選んだ。
- **Orreryx / Astrolabion / Sextantor**: 名前が観測器具で、器具のアイコン（orrery・astrolabe・sextant）は「道具だけ」になるので使わない。機械系統の生き物（甲虫・蟷螂・大型ロボット）に置き換えた。
- **Lupusfang**（werewolf）: 狼の全身が無く、人狼が唯一の全身。
- **Nocturnis**（bat）と Aquilaburst（eagle-emblem）・Marshwyvern（wyvern）: 翼を広げた形が 3 つ重なる。コンタクトシートで見分けられることを確認して残した。気になる場合の代わりの候補は delapouite/floating-ghost。
- **Lanternmare**: 馬の全身として centaur（Orionmane の候補にもした）と迷い、騎手が外套の亡霊に見える cloaked-figure-on-horseback を霊に回した。

## 変換パイプライン

すべて Editor から batchmode で何度でも再生成でき、同じ入力なら同じ出力（PNG とアセットのバイト列）になる。

```
CardArtTable.Assignments（Presentation のコード。正本）
  → Assets/Game/Settings/CardArtTable.asset
  → Assets/Art/Icons/Source/<作者>/<名前>.svg       （素材フォルダからコピー。リポジトリに入れる）
  → Assets/Art/Icons/Masks/<作者>/<名前>.png        （tools/rasterize_icons.mjs: ヘッドレス Chrome の canvas、512px、白い図形だけ）
  → CardArtBaker: 外接矩形をセルに合わせて 512px で二値化 → 厳密な距離変換（Felzenszwalb）→ 4×4 平均で 128px の SDF
                  輪郭から星 7〜14 個（Lv1 = 7 … Lv10 = 14。先端を優先した最遠点サンプリング、揺らぎはカード ID の FNV-1a）
                  線は「外にはみ出すほど重い」最小全域木＋ループ 1 本
  → Assets/Art/Icons/Generated/CardArtAtlas.png（1024×1024、R8、8×8 セル（5b-2 で立ち絵 12 人を足した）、ミップマップあり）＋ CardArtAtlas.asset（星と線）
```

| 操作 | コマンド |
|---|---|
| 作り直す（マスクが無いときだけラスタライズ） | `tools/RunUnity.ps1 -LogName art.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtPipeline.Run'` |
| マスクも作り直す | 上に `'-rasterize'` を足す |
| ラスタライズの決定論を確かめる | `-executeMethod Edgewise.EditorTools.CardArtPipeline.VerifyRasterize`（一時フォルダに作り直してバイト比較） |
| コンタクトシート | `tools/RunUnity.ps1 -Graphics -LogName sheet.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtContactSheet.Render'` → `Logs/shots/4a/contact_sheet.png` |

`ProjectSetup.Run` も中でパイプラインを呼ぶ（マスクがあればラスタライズは省く）。アイコンを差し替えるときは `CardArtTable.Assignments` を書き換えて `ProjectSetup` を実行する（使わなくなった SVG とマスクは自動で消える）。

## カード表面の描画（Card.shader）

1. 背景: 属性色の星雲（3 オクターブの値ノイズが 2 方向にゆっくり流れる）、遠くの小さな星、周辺減光。星雲の模様はセル位置で変え、カードごとに違う。
2. シルエット: SDF から塗る。系統ごとのグラデーション（獣 = 琥珀、機 = 鋼、霊 = 菫、竜 = 翡翠）に属性色を少し混ぜる。内側の縁の光、外側のぼんやりした光、内側にちりばめた細かな星。
3. 星座: 輪郭上の星（主星は大きく十字の光）と細い線。星はまたたく。
4. レベル加工（`_LevelFx` = `CardLevelFx.TierOf(level)`）: Lv1〜3 は素朴、Lv4〜6 は枠の内側の金の線と四隅の菱形、Lv7〜9 は金の縁＋ホロ（斜めの虹色の帯が傾きに連動して流れる。シルエットに強く背景に弱く）、Lv10 は金の縁＋動くオーロラ（背景とシルエットの間）＋昇るきらめきの粒。ホロと粒はランクのプレートと名前の帯にかけない（`fxMask`）。
5. 傾き（4b）: `CardView.SetTiltTarget` / `TiltToward`。カードの見た目の子（Visual）だけをカーソルの方へ最大 12° 回し（カーソル側が奥へ沈む）、シェーダーは層ごとにずらす（背景 0.012・シルエット 0.03・星 0.05）。手札にカーソルを乗せたとき・持っているとき（動く向きへ）・キーボードで選んだとき（ゆっくり揺れる）・図鑑の拡大表示で傾く。盤に置いたカードは `ResetTilt` で静止させる。

ランクのプレートは絵の上に最後に重ね、まわりを少し暗くしてから 95% の濃さで塗る（どの絵の上でも数字が一番読める）。
Pivot で回したカードは、カード絵（シルエットと星座）ごと回り、金の弧で回転量を示す。

## 見せ場（4b）

- **カード図鑑**（`CardDex`。タイトルの「カード図鑑」から）: 対戦と同じプレハブ・シェーダーのカード 50 枚を並べる。横画面は 10 列で 1 画面、縦画面は 5 列でスクロール（ホイール・ドラッグ・キーボードの選択に追従）。乗せると拡大して傾き、名前が出る。クリック・タップ・Enter で拡大表示（横は左にカード・右に説明、縦は上にカード・下に説明）。拡大中はカーソルの方へ傾き、動かさなければゆっくり揺れる。タッチはドラッグで傾ける。← → と「前」「次」で前後のカード、Esc・「一覧へ」・カードの外のクリックで一覧へ。説明は名前・レベル・属性・系統とアイコンのクレジット 1 行（`CardArtTable.CreditLine`）。選択とスクロールの計算は `DexGrid`（EditMode でテスト）。
- **タイトルの扇**（`TitleFan`）: Lv8〜10 の 10 枚が、ロゴの下を要にした扇の弧に沿ってゆっくり回り（3.2°/秒）、端で薄れて反対の端から戻る。ロゴと「はじめる」を読みやすく保つため半透明（最大 50%）。カードはそれぞれゆっくり傾き、ホロやオーロラが流れて見える。
- **手札**: カーソルを乗せた（キーボードで選んだ）手札は 1.12 倍で前に出て、重なりに隠れずに全体が見える。横画面の重なりも少し減らした（間隔の上限 0.74 → 0.86）。
- **クレジット画面**（`CreditsScreen`）: フォント・カード絵のアイコン（50 枚の作者と CC BY 3.0）・BGM・効果音・オマージュの表記。文字は `UiText` から作り、フォントに焼いた文字だけで表示できることを EditMode でテストしている。
## 立ち絵（5b-2。DESIGN §12-7）

星図巡りの対戦相手 12 人の立ち絵。カード絵と同じパイプライン（SVG → マスク → 128px の SDF ＋星座）で、**同じアトラス**（8×8 セル・1024×1024。カード 50 枚の後ろに 12 人）に ID `@<相手の ID>` で入れる。
正本は `CardArtTable.PortraitAssignments`。星の数は星域が奥ほど・主ほど多い（`CardArtPipeline.StarLevel`: 星域 1 の相手 7 個 … 星域 4 の主 14 個）。

### 選定の方針
- 肩書きと性格が一目で伝わる人物・顔のシルエット。12 人の形（全身・半身・顔・兜・胸像・2 人組）がばらけるようにし、カード絵のアイコンとも重ねない。
- FF シリーズの人物、特に原作でカードゲームに関わる人物（トランプの絵札、2 人組の少女など）を連想させない。トランプの絵札（card-king / card-queen）と、トンベリを連想させるフードの人物（hood・robe・cowled）は使わない。
- 双子は 2 人が並んだアイコン（face-to-face）にした。

| 相手 | アイコン | 作者 | 理由 |
|---|---|---|---|
| 見習い航海士 ミラ | figurehead | Delapouite | 船首像の乙女。前を向いて海へ乗り出す姿が、見習いの元気さと航海を表す |
| 灯台守 オルド | monk-face | Delapouite | いかつい老人の顔。岬で灯を守り続ける頑固な守り型 |
| 港の親方 マーロウ | lucky-fisherman | Delapouite | 大魚を抱えた港の男。腕っぷしと攻め気（主） |
| 写本係 リーフ | read | Skoll | 本を広げて読む人。一字一句写す書庫の係 |
| 双子の司書 ユンとシア | face-to-face | Delapouite | 腕を組んで並ぶ 2 人。双子の息の合った掛け合い |
| 館長 セレスト | philosopher-bust | Caro Asercion | 台座の上の胸像。書庫の奥に構える知の番人（主） |
| 気象官 ノア | person-in-blizzard | Lorc | 雪の結晶の舞う中に立つ人。天気を読む |
| 霧の占い師 ヴェイル | witch-face | Cathelineau | とんがり帽子の魔女の横顔。読みにくい気まぐれ |
| 観測所長 アストラ | doctor-face | Lorc | 大きなゴーグルで表情の見えない顔。感情を排した観測者（主） |
| 回転の旅人 テオ | hiking | Delapouite | 杖をついて坂を登る旅人 |
| 星図の騎士 ローレル | black-knight-helm | Delapouite | 隙のない閉じた大兜。門を守る守り型（全身の騎馬 mounted-knight はカード Lanternmare の騎手と形が重なるため外した） |
| 星詠みの長 イオ | sun-priest | Delapouite | 光の冠を戴き、両手を天に掲げる長。星を詠む最強の主 |

### 描画（uGUI）
- 立ち絵と、デッキ編集・星域の画面の小さなカード（`MiniCardView`）は uGUI に描くので、`ArtGraphic`（MaskableGraphic）と `Edgewise/UIArt` シェーダーで描く。
  1 つのメッシュに「本体（星雲＋シルエット）」「星」「線」の四角形を並べ、頂点の uv1〜uv3 で描き分ける。マテリアルとテクスチャ（SDF アトラス）を共有するので、小さなカードが 50 枚並んでも描画はまとまる。
  Canvas の `additionalShaderChannels` に TexCoord1〜3 が必要（`SceneBuilder`）。RectMask2D（デッキの一覧のスクロール）の切り抜きに対応。
- `PortraitView`: 小さな印（円の中の立ち絵・星域の色の輪と光・主は冠）と、大きな立ち絵（柔らかい光の中に浮かぶ立ち絵と星座。会話の画面）。
- 小さなカード: 属性色の星雲と系統の色のシルエット。数字の読みやすさを優先し、ランクの下に濃いプレート、名前と Lv の下に濃い帯を敷く。
- 図鑑の未入手のカード: `Card.shader` の `_Unknown`（暗い背景に暗いシルエットだけ。数字・名前・星座・レベル加工を出さない）。

### コンタクトシート
`index.html?log&portraits` を開くと、タイトルの上に 12 人（大きな立ち絵・小さな印・名前・アイコン）を並べる（`PortraitSheet`）。
`node tools/webgl_e2e.mjs --scenario 5b2 ...` が `Logs/shots/5b2/portraits_sheet.png` に撮る。
