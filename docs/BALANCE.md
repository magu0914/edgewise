# BALANCE — CPU 自己対戦の記録

DESIGN.md §6 の自己対戦シミュレーター（`dotnet/Edgewise.Sim`）による計測結果。Phase 2 のベースライン（CPU 3 難易度の強さと、原作ルールの先攻有利・引き分け率）を記録する。
Pivot のトークン数などの調整（Phase 4）は、この表を基準に差分で判断する。

## 測定条件

| 項目 | 値 |
|---|---|
| 局数 | 1 行あたり 1000 配牌 × 席交代 2 局 = **2000 局**（配牌は 50 枚から重複なしで 5 枚ずつ。Random ON は 8 枚ずつのデッキ） |
| 乱数 | `--seed 2026`。配牌・先攻・各手の AI の seed はすべてこれで決まり、スレッド数によらず同じ結果になる（時間の列を除く） |
| マシン | 12th Gen Intel Core i7-12700F（20 論理コア）、Windows 11（10.0.26200）、.NET 9.0.15、Release ビルド、20 スレッド並列 |
| ルール | Basic = 基本のみ（Open ON）／ Same+Plus+Combo（Open ON）／ All = Random・Same・Same Wall・Plus・Combo・Elemental・Sudden Death・Pivot（Open ON）／ Basic (Open OFF) |
| 勝率 | 引き分けを除いた勝率。[ ] は Wilson 95% 信頼区間 |

AI のパラメータ（`Assets/Game/Core/AI/AiProfile.cs`）:

| 難易度 | 1 手のノード予算 | 深さ | 決定化の世界数（Open OFF） | ノイズ | 見落とし | その他 |
|---|---:|---|---:|---:|---:|---|
| Easy | 2,000 | 1 手 | 1 | ±150（1.5 枚分） | 40%（Same/Plus/Combo を無視して読む） | 置換表なし |
| Normal | 30,000 | 3 手 | 3 | ±40 | 0% | 置換表 2^14 |
| Hard | 400,000 | 制限なし（反復深化で終局まで） | 8 | 0 | 0% | 置換表 2^18、ルートの窓 150、同値手の選び分け（TieBreakDepth 2） |

評価値の単位は 1/100 枚。終局はスコア差×100 ± 300（勝ちボーナス）、引き分け（Sudden Death の再戦を含む）は 0。
途中局面は「スコア差 ＋ 手番ボーナス 50 ＋ 手札の辺の合計×3 ＋ 露出した辺のリスク（相手の手札で取られうるカード 1 枚 −40、相手の手番なら −60、辺の値による一般リスク 0〜−20）＋ Pivot トークン 1 個 15」。

### 再現コマンド

```
# 本表（約 10 分）
dotnet run -c Release --project dotnet/Edgewise.Sim -- --match hard:easy,hard:normal,normal:easy,hard:hard --rules default --deals 1000 --seed 2026 --out Logs/balance-main.md

# 補足実験（予算・世界数・情報の価値。約 25 分）
dotnet run -c Release --project dotnet/Edgewise.Sim -- --match "hard:easy,hard/oracle=1:easy,hard@800000:hard,hard@100000:hard,hard/tiebreak=0:easy,hard/worlds=16:easy" --rules "basic,openoff,spc-openoff,all-openoff" --deals 1000 --seed 2026

# 速度（dotnet）
dotnet run -c Release --project dotnet/Edgewise.Sim -- --bench

# 速度（WebGL）: Builds/WebGL を HTTP で配信し、index.html?bench を開く（結果は画面とブラウザのコンソールに [Edgewise] BENCH ... で出る）
```

`--match` の書式: `hard`、`hard@200000`（ノード予算の上書き）、`hard/worlds=16/margin=300/depth=4/tiebreak=0/threat=40/...`（調整用）。
`oracle=1`（相手の手札を見せる）と `truepool=1`（決定化の候補を相手の本当の手札だけにする）は計測専用で、ゲームでは使わない。

## 結論（要約）

1. **Hard vs Easy**（引き分け除く Hard 勝率）: Basic 96.4% [95.3, 97.2]、Same+Plus+Combo 98.0% [97.2, 98.5]、All 99.2% [98.7, 99.5]、**Basic (Open OFF) 89.6% [87.9, 91.0]**。
   4 条件の合計は 6507 勝 264 敗 = **96.1%**。Open ON の 3 条件は 95% 以上を満たすが、**Open OFF は 95% に届いていない**（下の「Open OFF の情報の価値」を参照）。
2. **Hard vs Normal**: 全条件で Hard が有意に勝ち越し（信頼区間の下限がすべて 50% を大きく上回る。最も小さい Open OFF でも 65.6% [62.9, 68.2]）。
3. **Normal vs Easy**: 83.8%〜97.3%。3 段階の強さの順序は全条件で Hard ＞ Normal ＞ Easy。
4. **先攻有利・引き分け率（Hard vs Hard）**: Basic は先攻 55.3% [52.1, 58.5]・引き分け 53.2%、Same+Plus+Combo は 53.3%・60.6%、Open OFF は 49.7%・42.1%。
   （Open ON の Hard 同士はノイズがなく決定論的なので、席を入れ替えた 2 局が鏡写しになり A の勝率はちょうど 50% になる。見るべきは先攻勝率と引き分け率。）
   強い CPU 同士だと**引き分けが目標（15〜35%）より多い**。All（Pivot・Sudden Death 込み）は引き分け 3.5% だが**先攻勝率 68.2% [66.1, 70.2] と先攻が強すぎる**。Phase 4 の調整対象（下の「Phase 4 への申し送り」）。
5. **読み切り**: Open ON なら Hard は **3 手目から 100% 終局まで読み切る**（Basic・Same+Plus+Combo）。Pivot 込みの All は 4 手目で約 45%、**5 手目から 100%**。Open OFF（8 世界）は 4 手目から全世界で読み切る。
   読み切った時点で「勝ち」と予想した局はすべて実際に勝っている（総当たり検証と合わせて、読み切りの正しさの裏付け）。
6. **思考時間**: WebGL（Chrome、i7-12700F）で Hard の 1 手は平均 0.13 秒・最大 0.32 秒、思考中も 59〜60 FPS（1 フレーム 6 ms 予算）。CPU を 4 倍遅くしたエミュレーションでも最大 1.34 秒・最低 25〜40 FPS（30 FPS 未満のフレームは 1171 中 0〜1）。


## 補足実験

同じ seed・同じ 1000 配牌（2000 局）。行の意味: `Hard/oracle=1` は相手の手札を見せた Hard（情報の価値の上限を測るため）、`Hard@N` はノード予算 N、`tiebreak=0` は同値手の選び分けなし、`worlds=K` は決定化の世界数。

| Rules | A vs B | A wins | B wins | Draws | A win rate (excl. draws) |
|---|---|---:|---:|---:|---:|
| Basic (Open) | Hard vs Easy | 1483 | 56 | 461 | 96.4% [95.3, 97.2] |
| Basic (Open) | Hard/tiebreak=0 vs Easy | 1306 | 115 | 579 | 91.9% [90.4, 93.2] |
| Basic (Open) | Hard@800000 vs Hard | 471 | 476 | 1053 | 49.7% [46.6, 52.9] |
| Basic (Open) | Hard@100000 vs Hard | 405 | 563 | 1032 | 41.8% [38.8, 45.0] |
| Basic (Open OFF) | Hard vs Easy | 1350 | 157 | 493 | 89.6% [87.9, 91.0] |
| Basic (Open OFF) | Hard/oracle=1 vs Easy | 1577 | 54 | 369 | 96.7% [95.7, 97.5] |
| Basic (Open OFF) | Hard/tiebreak=0 vs Easy | 1345 | 159 | 496 | 89.4% [87.8, 90.9] |
| Basic (Open OFF) | Hard/worlds=16 vs Easy | 1364 | 166 | 470 | 89.2% [87.5, 90.6] |
| Basic (Open OFF) | Hard@800000 vs Hard | 597 | 533 | 870 | 52.8% [49.9, 55.7] |
| Basic (Open OFF) | Hard@100000 vs Hard | 524 | 615 | 861 | 46.0% [43.1, 48.9] |
| Same+Plus+Combo (Open OFF) | Hard vs Easy | 1392 | 252 | 356 | 84.7% [82.8, 86.3] |
| Same+Plus+Combo (Open OFF) | Hard/oracle=1 vs Easy | 1730 | 27 | 243 | 98.5% [97.8, 98.9] |
| Same+Plus+Combo (Open OFF) | Hard/truepool=1 vs Easy | 1696 | 38 | 266 | 97.8% [97.0, 98.4] |
| Same+Plus+Combo (Open OFF) | Hard/worlds=16 vs Easy | 1420 | 212 | 368 | 87.0% [85.3, 88.6] |
| Same+Plus+Combo (Open OFF) | Hard/worlds=32 vs Easy | 1409 | 227 | 364 | 86.1% [84.4, 87.7] |
| Same+Plus+Combo (Open OFF) | Hard/worlds=64 vs Easy | 1412 | 222 | 366 | 86.4% [84.7, 88.0] |
| Same+Plus+Combo (Open OFF) | Hard/worlds=32/nodes=800000 vs Easy | 1441 | 201 | 358 | 87.8% [86.1, 89.3] |
| Same+Plus+Combo (Open OFF) | Hard@800000 vs Hard | 707 | 689 | 604 | 50.6% [48.0, 53.3] |
| All incl. Pivot (Open OFF) | Hard vs Easy | 1804 | 196 | 0 | 90.2% [88.8, 91.4] |
| All incl. Pivot (Open OFF) | Hard/oracle=1 vs Easy | 1990 | 10 | 0 | 99.5% [99.1, 99.7] |
| All incl. Pivot (Open OFF) | Hard/worlds=16 vs Easy | 1813 | 187 | 0 | 90.7% [89.3, 91.8] |
| All incl. Pivot (Open OFF) | Hard@800000 vs Hard | 1095 | 904 | 1 | 54.8% [52.6, 56.9] |
| All incl. Pivot (Open OFF) | Hard@100000 vs Hard | 909 | 1088 | 3 | 45.5% [43.3, 47.7] |

### Open OFF の情報の価値（Hard vs Easy が 95% に届かない理由）

- 同じ Hard に相手の手札を見せると（oracle）、Open OFF でも 96.7%（Basic）・98.5%（Same+Plus+Combo）・99.5%（All）になる。決定化の候補を相手の本当の手札だけにしても（truepool）97.8% なので、**探索・決定化の仕組みは正しく、差は「相手の手札が見えない」ことによる**。
- 決定化（PIMC: 世界ごとに完全情報として読み、各手の評価を平均）は、世界数を 8 → 16 → 32 → 64 に増やしても、予算を 2 倍にしても 85〜90% で頭打ちになった。
  特に Same / Plus は「相手がちょうどその値のカードを持っているか」で結果が大きく変わるため、見えない情報の影響が大きい（Basic 89.6% に対し Same+Plus+Combo 84.7%）。
- PIMC の限界（各世界で「相手も自分も全部見えている」前提で読む、いわゆる strategy fusion）を超えるには、情報集合単位の探索（IS-MCTS など）や相手の手札の推定が必要で、Phase 2 の範囲を超えると判断した。
- 予算を 2 倍（800k）にすると Open OFF の Hard 同士では 52.8〜54.8% と少し強くなる（Open ON では差なし）。思考時間の余裕（下記）との兼ね合いで既定値は 400k のままにした。

### 同値手の選び分け（TieBreakDepth）

ミニマックスは「相手も最善を尽くす」前提なので、読み切った結果が同じ（例: すべて引き分け、すべて負け）の手を区別できない。
読み切った結果が引き分けか負けのときは、同じ結果になる手の中から「2 手読み（途中局面の評価）の値」が最も良い手を選ぶようにした。
これで Basic の Hard vs Easy は 91.9% → **96.4%**（負けが 115 → 56）。上の「最初に読み切った時点の予想と実際の結果」の表のとおり、Hard の負けはすべて「読み切った時点ですでに理論上負け」の局で、
そこから相手のミスで勝ち・引き分けに持ち込む割合が上がった。勝ちが読み切れているときは何もしない（最大差で勝つ手を選ぶ）。
なお 2000 万ノードで 1 手目から完全に読み切る Hard（選び分けなし）は、Basic で 88.8%（別 seed の 300 配牌。同じ配牌で 40 万ノードの Hard は 95.0%）と**かえって弱かった**（同値手を区別できないため）。

## 速度

### dotnet（`--bench`、1 スレッド、ランダム手で 0〜7 手進めた 240 局面）

| 難易度 | 秒間ノード数 | ノード/手 | 1 手の時間（平均 / 最大） | アロケーション/手 |
|---|---:|---:|---:|---:|
| Easy | 0.37 M | 36 | 0.10 ms / 3.0 ms | 7.3 KiB |
| Normal | 4.06 M | 7,526 | 1.85 ms / 8.9 ms | 8.0 KiB |
| Hard | 4.86 M | 157,383 | 32.4 ms / 99.3 ms | 9.9 KiB |

Easy はノードが少なく、セッションの準備（合法手の列挙など）が支配的なので秒間ノード数は小さく出る。
探索中はアロケーションしない（make / unmake 方式の `SearchBoard`。アロケーションはセッション作成時のみ。置換表は `SearchWorkspace` を使い回せば 1 回だけ）。
参考: ApplyMoveFast は 1 回 0.18〜0.25 µs・377 B なので、探索に直接使うと 1 手あたり数十 MB のゴミが出る。

### WebGL（Unity 6000.3.15f1、Chrome 155、i7-12700F + RTX 3070 Ti、1 フレームの思考予算 6 ms）

`index.html?bench` の計測（`DebugBench`）。RAW = フレーム分割なしで探索だけ、SLICED = `AiDriver` で 1 フレーム 6 ms ずつ進めた CPU 対 CPU（Easy/Normal/Hard 同士、All (Open OFF) 2 局 + Hard は All (Open) 1 局）。

| 条件 | 難易度 | 秒間ノード数（RAW） | 1 手の思考時間（平均 / 最大） | 思考中の最も遅いフレーム | 30 FPS 未満のフレーム |
|---|---|---:|---:|---:|---:|
| ヘッドレス | Easy | 0.25 M | 15 ms / 17 ms | 17 ms（59 FPS） | 0 / 17 |
| ヘッドレス | Normal | 4.67 M | 20 ms / 33 ms | 17 ms（59 FPS） | 0 / 19 |
| ヘッドレス | Hard (Open OFF) | 4.55 M | 132 ms / 317 ms | 17 ms（59 FPS） | 0 / 285 |
| ヘッドレス | Hard (Open) | — | 117 ms / 287 ms | 17 ms（59 FPS） | 0 / 125 |
| ウィンドウ表示 | Hard (Open OFF) | 4.61 M | 132 ms / 302 ms | 18 ms（56 FPS） | 0 / 284 |
| CPU 4 倍遅く（エミュレーション） | Normal | 1.08 M | 53 ms / 88 ms | 18 ms（56 FPS） | 0 / 55 |
| CPU 4 倍遅く（エミュレーション） | Hard (Open OFF) | 0.96 M | 543 ms / 1256 ms | 28 ms（36 FPS） | 0 / 1171 |
| CPU 4 倍遅く（エミュレーション） | Hard (Open) | — | 472 ms / 1201 ms | 25 ms（40 FPS） | 0 / 508 |

- WebGL（IL2CPP → WebAssembly）の秒間ノード数は dotnet とほぼ同じ（4.5〜4.7 M/s）。
- 1 フレーム 6 ms のうち探索に使えるのは 60 FPS で約 36% なので、Hard（40 万ノード）の思考は最大 0.3 秒。CPU が 4 倍遅い端末でも最大 1.3 秒で、DESIGN の目標（Easy・Normal 0.8 秒、Hard 1.5 秒、思考中 30 FPS 以上）に収まる。
  4 倍遅い条件の別の実行では 40 ms（25 FPS）のフレームが 1183 中 1 回あった（GC かセッション開始時の準備と思われる）。
- Easy の思考が 1 フレーム（約 15 ms）で終わるのは即座に指しているだけで、「考える間」は Phase 3 の演出で取る（DESIGN §5-1）。

## 予算の決め方

- **Hard 400,000 ノード**: 強さは 40 万で頭打ち（80 万 vs 40 万は Open ON で 49.7%、10 万 vs 40 万は 41.8% と弱い）。WebGL の実測で最大 0.3 秒、4 倍遅い端末でも 1.3 秒に収まる値。Open OFF では 80 万の方が少し強い（52.8〜54.8%）ので、遅い端末を切り捨ててよいなら上げる余地がある。
- **Normal 30,000 ノード・3 手読み・3 世界**: 3 手読みは平均 7 千ノード程度で収まり、Pivot 込みでも予算 3 万で足りる（Normal の性格は予算でなく深さで決めた）。
- **Easy 2,000 ノード・1 手読み・ノイズ ±1.5 枚・見落とし 40%**: 1 手読みなので予算は形式的。ノイズと見落としで「ときどき悪手を指すが、目先の取り合いはする」強さにした（Normal に 84〜97% 負ける）。
  95% の条件を満たすために Easy を弱くすることはしていない（ノイズ・見落とし率は最初に決めた値のまま。評価関数の改良は全難易度に共通）。

## Phase 4 への申し送り

- **引き分けが多い**（強い CPU 同士で Basic 53%、Same+Plus+Combo 61%）。原作ルール由来の性質で、目標 15〜35% より多い。
- **All（Pivot・Sudden Death 込み）は先攻が強すぎる**（Hard 同士で先攻 68.2%）。Sudden Death の再戦は先攻が交代するが、それでも本戦の先攻が有利。Pivot トークン数（後攻だけ 1 個多くする等）で調整する候補。
- **原因の切り分け（監査時の追加計測、Hard 同士・400 配牌×席交代・seed 99、先攻勝率は引き分け除く）**: 主因は **Pivot**。

  | ルール | 先攻勝率 | 引き分け率 |
  |---|---:|---:|
  | Basic (Open) | 62.6% [57.6, 67.5] | 54.5% |
  | Pivot (Open) | **80.6% [76.7, 84.0]** | 43.3% |
  | Sudden Death (Open) | 57.3% [53.7, 60.9] | 11.5% |

  再現: `dotnet run -c Release --project dotnet/Edgewise.Sim -- --match hard:hard --rules basic,pivot,suddendeath --deals 400 --seed 99`
  Pivot は看板メカニクスなので、Phase 4 の最優先課題とする（候補: 後攻のトークンを多くする／先攻の 1 手目は回転不可／トークン数を 1 に減らす）。
- Pivot の使用タイミング: Hard は序盤にほとんど使わず（1 手目 3〜4%）、6 手目以降に 30〜47% で使う。Easy は序盤から乱用する（2〜4 手目で 50% 超）。

---

以下は本表の出力そのもの（`--out` の Markdown）。

#### 勝敗（配牌ごとに席を入れ替えて 2 局。勝率は引き分けを除く。[ ] は Wilson 95% 信頼区間）

| Rules | A vs B | Games | A wins | B wins | Draws | A win rate (excl. draws) | First-player win rate (excl. draws) | Draw rate |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Basic (Open) | Hard vs Easy | 2000 | 1483 | 56 | 461 | 96.4% [95.3, 97.2] | 47.5% [45.0, 50.0] | 23.1% [21.3, 24.9] |
| Basic (Open) | Hard vs Normal | 2000 | 893 | 239 | 868 | 78.9% [76.4, 81.2] | 48.1% [45.2, 51.0] | 43.4% [41.2, 45.6] |
| Basic (Open) | Normal vs Easy | 2000 | 1356 | 131 | 513 | 91.2% [89.6, 92.5] | 45.5% [43.0, 48.1] | 25.7% [23.8, 27.6] |
| Basic (Open) | Hard vs Hard | 2000 | 468 | 468 | 1064 | 50.0% [46.8, 53.2] | 55.3% [52.1, 58.5] | 53.2% [51.0, 55.4] |
| Same+Plus+Combo (Open) | Hard vs Easy | 2000 | 1690 | 35 | 275 | 98.0% [97.2, 98.5] | 48.3% [45.9, 50.6] | 13.8% [12.3, 15.3] |
| Same+Plus+Combo (Open) | Hard vs Normal | 2000 | 1026 | 146 | 828 | 87.5% [85.5, 89.3] | 46.2% [43.3, 49.0] | 41.4% [39.3, 43.6] |
| Same+Plus+Combo (Open) | Normal vs Easy | 2000 | 1664 | 74 | 262 | 95.7% [94.7, 96.6] | 46.9% [44.6, 49.2] | 13.1% [11.7, 14.6] |
| Same+Plus+Combo (Open) | Hard vs Hard | 2000 | 394 | 394 | 1212 | 50.0% [46.5, 53.5] | 53.3% [49.8, 56.8] | 60.6% [58.4, 62.7] |
| All incl. Pivot (Open) | Hard vs Easy | 2000 | 1984 | 16 | 0 | 99.2% [98.7, 99.5] | 50.0% [47.8, 52.2] | 0.0% [0.0, 0.2] |
| All incl. Pivot (Open) | Hard vs Normal | 2000 | 1822 | 170 | 8 | 91.5% [90.2, 92.6] | 52.6% [50.4, 54.8] | 0.4% [0.2, 0.8] |
| All incl. Pivot (Open) | Normal vs Easy | 2000 | 1946 | 54 | 0 | 97.3% [96.5, 97.9] | 50.1% [47.9, 52.3] | 0.0% [0.0, 0.2] |
| All incl. Pivot (Open) | Hard vs Hard | 2000 | 965 | 965 | 70 | 50.0% [47.8, 52.2] | 68.2% [66.1, 70.2] | 3.5% [2.8, 4.4] |
| Basic (Open OFF) | Hard vs Easy | 2000 | 1350 | 157 | 493 | 89.6% [87.9, 91.0] | 47.7% [45.2, 50.2] | 24.7% [22.8, 26.6] |
| Basic (Open OFF) | Hard vs Normal | 2000 | 805 | 422 | 773 | 65.6% [62.9, 68.2] | 48.9% [46.1, 51.7] | 38.7% [36.5, 40.8] |
| Basic (Open OFF) | Normal vs Easy | 2000 | 1224 | 236 | 540 | 83.8% [81.9, 85.6] | 48.6% [46.0, 51.1] | 27.0% [25.1, 29.0] |
| Basic (Open OFF) | Hard vs Hard | 2000 | 588 | 571 | 841 | 50.7% [47.9, 53.6] | 49.7% [46.8, 52.6] | 42.1% [39.9, 44.2] |

#### 盤上の出来事（1 局あたり）と思考コスト（dotnet。20 スレッド並列で回しているので ms は 1 スレッド時より遅く出る）

| Rules | A vs B | Flips basic / same / plus / combo | Max chain (max / avg) | Sudden Death rounds | Nodes/move A / B (max) | ms/move A / B (max) | Wall s |
|---|---|---:|---:|---:|---:|---:|---:|
| Basic (Open) | Hard vs Easy | 5.22 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 102855 (400000) / 19 (45) | 36.81 (341.0) / 0.07 (54.4) | 17.0 |
| Basic (Open) | Hard vs Normal | 4.93 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 105864 (400000) / 2574 (14268) | 37.46 (194.8) / 0.88 (9.4) | 17.4 |
| Basic (Open) | Normal vs Easy | 4.94 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 2592 (14268) / 19 (45) | 0.87 (11.2) / 0.02 (3.9) | 0.4 |
| Basic (Open) | Hard vs Hard | 4.81 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 106671 (400000) / 106671 (400000) | 37.64 (205.5) / 37.67 (206.0) | 34.7 |
| Same+Plus+Combo (Open) | Hard vs Easy | 4.76 / 0.16 / 0.98 / 0.73 | 5 / 0.53 | 0.00 | 106293 (400000) / 19 (45) | 41.76 (208.5) / 0.03 (5.0) | 19.1 |
| Same+Plus+Combo (Open) | Hard vs Normal | 4.27 / 0.13 / 0.67 / 0.31 | 5 / 0.22 | 0.00 | 108437 (400000) / 2658 (14268) | 42.57 (242.0) / 0.95 (12.3) | 20.0 |
| Same+Plus+Combo (Open) | Normal vs Easy | 4.29 / 0.21 / 1.22 / 0.98 | 5 / 0.67 | 0.00 | 2696 (14268) / 19 (45) | 0.94 (10.3) / 0.03 (5.8) | 0.4 |
| Same+Plus+Combo (Open) | Hard vs Hard | 4.67 / 0.08 / 0.39 / 0.21 | 4 / 0.16 | 0.00 | 109295 (400000) / 109295 (400000) | 43.39 (220.1) / 43.41 (209.6) | 39.7 |
| All incl. Pivot (Open) | Hard vs Easy | 5.15 / 0.50 / 1.85 / 2.07 | 6 / 1.16 | 0.07 | 172185 (400000) / 69 (180) | 65.09 (265.3) / 0.06 (7.2) | 32.2 |
| All incl. Pivot (Open) | Hard vs Normal | 6.10 / 0.41 / 1.68 / 1.10 | 5 / 0.65 | 0.46 | 165199 (400000) / 12093 (30000) | 63.50 (200.8) / 4.04 (22.4) | 45.8 |
| All incl. Pivot (Open) | Normal vs Easy | 5.00 / 0.37 / 1.91 / 2.07 | 5 / 1.19 | 0.10 | 13461 (30000) / 68 (180) | 4.40 (21.8) / 0.05 (7.2) | 2.3 |
| All incl. Pivot (Open) | Hard vs Hard | 8.08 / 0.56 / 2.25 / 1.88 | 5 / 1.01 | 0.79 | 163563 (400000) / 163563 (400000) | 62.49 (243.8) / 62.51 (238.8) | 106.3 |
| Basic (Open OFF) | Hard vs Easy | 5.61 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 149201 (400000) / 19 (45) | 51.59 (213.9) / 0.03 (10.4) | 23.8 |
| Basic (Open OFF) | Hard vs Normal | 5.79 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 150562 (400000) / 7416 (30000) | 53.64 (190.5) / 2.56 (21.0) | 25.8 |
| Basic (Open OFF) | Normal vs Easy | 5.68 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 7433 (30000) / 19 (45) | 2.51 (20.8) / 0.03 (5.8) | 1.2 |
| Basic (Open OFF) | Hard vs Hard | 5.91 / 0.00 / 0.00 / 0.00 | 0 / 0.00 | 0.00 | 150721 (400000) / 150945 (400000) | 53.94 (200.8) / 54.06 (204.6) | 49.2 |

#### Pivot を使った手番（ラウンド内の何手目か。その手番の手のうち回転して置いた割合）

| Rules | A vs B | Player | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | Tokens used / game |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| All incl. Pivot (Open) | Hard vs Easy | A | 4.1% | 20.8% | 28.9% | 24.5% | 33.3% | 46.6% | 38.8% | 40.1% | 36.3% | 1.47 |
| All incl. Pivot (Open) | Hard vs Easy | B | 29.4% | 54.2% | 52.0% | 57.0% | 46.7% | 42.1% | 30.4% | 20.6% | 17.7% | 1.88 |
| All incl. Pivot (Open) | Hard vs Normal | A | 3.6% | 16.9% | 16.1% | 14.2% | 16.9% | 30.5% | 24.9% | 35.2% | 29.3% | 1.36 |
| All incl. Pivot (Open) | Hard vs Normal | B | 22.1% | 20.3% | 24.6% | 27.2% | 25.3% | 22.5% | 36.0% | 41.0% | 29.1% | 1.80 |
| All incl. Pivot (Open) | Normal vs Easy | A | 25.5% | 34.9% | 36.4% | 37.3% | 34.7% | 35.5% | 39.9% | 47.9% | 31.2% | 1.78 |
| All incl. Pivot (Open) | Normal vs Easy | B | 29.0% | 45.2% | 52.4% | 55.0% | 45.9% | 41.7% | 29.0% | 23.1% | 17.4% | 1.86 |
| All incl. Pivot (Open) | Hard vs Hard | A | 3.1% | 8.2% | 10.4% | 10.3% | 15.9% | 29.3% | 25.4% | 29.1% | 30.1% | 1.45 |
| All incl. Pivot (Open) | Hard vs Hard | B | 3.1% | 8.2% | 10.4% | 10.3% | 15.9% | 29.3% | 25.4% | 29.1% | 30.1% | 1.45 |

#### 読み切り率（ラウンド内の何手目か。選んだ手が全世界で終局まで読み切れていた割合）

| Rules | A vs B | Player | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Basic (Open) | Hard vs Easy | A | 0.0% | 0.5% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open) | Hard vs Normal | A | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open) | Hard vs Hard | A | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open) | Hard vs Hard | B | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Same+Plus+Combo (Open) | Hard vs Easy | A | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Same+Plus+Combo (Open) | Hard vs Normal | A | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Same+Plus+Combo (Open) | Hard vs Hard | A | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Same+Plus+Combo (Open) | Hard vs Hard | B | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| All incl. Pivot (Open) | Hard vs Easy | A | 0.0% | 0.0% | 3.1% | 45.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| All incl. Pivot (Open) | Hard vs Normal | A | 0.0% | 0.0% | 18.1% | 42.1% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| All incl. Pivot (Open) | Hard vs Hard | A | 0.0% | 0.0% | 22.2% | 46.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| All incl. Pivot (Open) | Hard vs Hard | B | 0.0% | 0.0% | 22.2% | 46.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open OFF) | Hard vs Easy | A | 0.0% | 0.0% | 0.1% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open OFF) | Hard vs Normal | A | 0.0% | 0.0% | 0.1% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open OFF) | Hard vs Hard | A | 0.0% | 0.0% | 0.1% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |
| Basic (Open OFF) | Hard vs Hard | B | 0.0% | 0.0% | 0.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% | 100.0% |

#### 最初に読み切った時点の予想と実際の結果（Open ON・本戦のみ。予想は「相手も最善を尽くす」前提の値）

| Rules | A vs B | Player | 予想: 勝ち → 勝 / 分 / 負 | 予想: 引き分け → 勝 / 分 / 負 | 予想: 負け → 勝 / 分 / 負 |
|---|---|---|---:|---:|---:|
| Basic (Open) | Hard vs Easy | A | 911 / 0 / 0 | 535 / 327 / 0 | 37 / 134 / 56 |
| Basic (Open) | Hard vs Normal | A | 753 / 0 / 0 | 139 / 816 / 0 | 1 / 52 / 239 |
| Basic (Open) | Hard vs Hard | A | 468 / 0 / 0 | 0 / 1064 / 0 | 0 / 0 / 468 |
| Basic (Open) | Hard vs Hard | B | 468 / 0 / 0 | 0 / 1064 / 0 | 0 / 0 / 468 |
| Same+Plus+Combo (Open) | Hard vs Easy | A | 933 / 0 / 0 | 695 / 212 / 0 | 62 / 63 / 35 |
| Same+Plus+Combo (Open) | Hard vs Normal | A | 742 / 0 / 0 | 270 / 779 / 0 | 14 / 49 / 146 |
| Same+Plus+Combo (Open) | Hard vs Hard | A | 394 / 0 / 0 | 0 / 1212 / 0 | 0 / 0 / 394 |
| Same+Plus+Combo (Open) | Hard vs Hard | B | 394 / 0 / 0 | 0 / 1212 / 0 | 0 / 0 / 394 |
| All incl. Pivot (Open) | Hard vs Easy | A | 1518 / 0 / 0 | 438 / 0 / 2 | 28 / 0 / 14 |
| All incl. Pivot (Open) | Hard vs Normal | A | 1123 / 0 / 0 | 694 / 8 / 58 | 5 / 0 / 112 |
| All incl. Pivot (Open) | Hard vs Hard | A | 588 / 0 / 0 | 377 / 70 / 377 | 0 / 0 / 588 |
| All incl. Pivot (Open) | Hard vs Hard | B | 588 / 0 / 0 | 377 / 70 / 377 | 0 / 0 / 588 |
