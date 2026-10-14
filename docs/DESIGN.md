# EDGEWISE（仮題）設計書

> FF8 のカードゲーム「Triple Triad」へのオマージュとして、**ルールの仕組みだけを継承し、カード・名称・絵はすべてオリジナル**で作る対戦カードゲーム。
> ゲーム会社向けポートフォリオ作品。**Unity 6 / C#** で制作し、WebGL 版を公開して URL を開けば面接官がすぐ遊べることを最優先する。
>
> 改訂履歴: 2026-10-15 に TypeScript 版から Unity/C# に方針転換（ゲーム会社向けに Unity・C# の実務的なスキルを示すため）。TS 版の Phase 1 実装は `D:\Projects\tripletriad-ts-legacy` に退避してあり、移植元として参照する。
>
> この文書が設計の単一の真実（Single Source of Truth）。実装と食い違ったら、まずこの文書を直してから実装を直す。

---

## 0. 作品のねらい

| 見せたいもの | 具体的な形 |
|---|---|
| 面白さ・手触り | カードが裏返る気持ちよさ、連鎖（Combo）が決まった瞬間の演出 |
| ゲームデザイン力 | 原作ルールの再現 ＋ オリジナルメカニクス。**AI 自己対戦でバランスを数値検証**した記録 |
| 技術力 | Unity 非依存の決定論的ルールエンジン（dotnet で単体テスト・自己対戦）、αβ 探索の CPU（フレーム分割）、Unity でのオンライン対戦、CI |

判断に迷ったら **「手触り ＞ 機能の数」**。機能を削っても、置いた瞬間・裏返る瞬間の気持ちよさは削らない。

### 権利の扱い（厳守）
- 使わないもの: FF8 のカード名・キャラクター名・カード絵・BGM/SE・ロゴ・「Triple Triad」という名称（タイトルやゲーム内表記に使わない）。
- 使ってよいもの: ゲームの仕組み（ルール）。README に「Triple Triad（© SQUARE ENIX）へのオマージュ作品であり、公式とは無関係」と明記する。
- カード絵・SE は自作（手続き生成のエンブレム／`AudioClip.Create` による合成音）を基本とし、外部素材（フォント・CC0 素材・パッケージ）を使う場合はライセンスを `docs/CREDITS.md` に記録する。

---

## 1. 技術スタック

- エンジン: **Unity 6000.3.15f1**（Wizard Trial と同じ版）、URP（2D Renderer）
- 言語: C#。**Core は C# 9 の範囲で書く**（Unity と dotnet の両方でコンパイルするため）
- ロジック層（Core）: UnityEngine を参照しない純粋な C#。asmdef の `noEngineReferences: true` で強制する
- テスト: Core は **dotnet + NUnit**（Unity を起動せず数秒で回る）。Unity 側の結合確認は Unity Test Framework の EditMode / PlayMode
- 自己対戦シミュレーター: dotnet のコンソールアプリ（Core のソースをリンクしてビルド）
- 描画: カードはワールド空間のオブジェクト（3D 回転で裏返す）、メニュー類は uGUI ＋ TextMeshPro
- 入力: Input System（マウス／タッチ／キーボード・ゲームパッド）
- Tween: 自作の軽量 Tween か LitMotion（MIT）。Phase 3 で決める
- サウンド: `AudioClip.Create` による合成音、または CC0 素材
- CPU 思考: Core の探索を「一定ノード数ずつ進められる」形にし、Unity 側で 1 フレームあたりの時間予算内に少しずつ進める（WebGL では別スレッドが使えないため）。WebGL 以外のビルドでは別スレッドで回してもよい
- オンライン: Unity Gaming Services の Lobby ＋ Relay ＋ Unity Transport（WebGL は WebSocket 接続）を第一候補とする。ターン制なので Netcode for GameObjects は使わず、Move メッセージだけを送る。Phase 5 で最終決定
- 公開: WebGL ビルドを unityroom と GitHub Pages に置く。ビルドは Editor スクリプト（`BuildScript`）で batchmode 実行できるようにする
- CI: GitHub Actions で **Core の dotnet テストを回す**（Unity ライセンスが不要）。WebGL ビルドはローカルで実行する

### ディレクトリ構成（リポジトリのルート ＝ Unity プロジェクト）
```
Assets/
  Game/
    Core/            # asmdef: Edgewise.Core（noEngineReferences）
      Engine/        #   ルールエンジン。純関数・決定論
      Data/          #   カード定義（C# の表）と検証
      AI/            #   探索・評価関数
    Presentation/    # asmdef: Edgewise.Game（UnityEngine 依存）。盤面表示・演出・入力・サウンド
    Net/             # asmdef: Edgewise.Net（Phase 5）
    Editor/          # BuildScript など
  Scenes/
  Tests/             # Unity Test Framework（EditMode / PlayMode）
Packages/
ProjectSettings/
dotnet/
  Edgewise.Core.Tests/   # NUnit。Assets/Game/Core/**/*.cs をリンクしてビルド
  Edgewise.Sim/          # 自己対戦シミュレーター（コンソール）
  Edgewise.sln
docs/
```

**依存の向き**: Presentation / Net / AI → Engine のみ。Engine は他に依存しない。同じエンジンを表示・CPU・オンライン・シミュレーターで共有する。
カードの**ルール上のデータ（辺の値・属性・系統）は Core の C# の表が正**。絵・色・説明文など見た目のデータは Phase 3 で ScriptableObject として Presentation 側に持たせ、カード ID で結びつける。

---

## 2. 基本ルール（原作準拠）

- 盤面は 3×3 の 9 マス。各プレイヤーは 5 枚の手札を持ち、交互に 1 枚ずつ置く。合計 9 手で終局。
- カードは上・右・下・左の 4 辺にランク（1〜9 と A=10）を持つ。
- 置いたカードの各辺と、隣接する**相手所有**のカードの向かい合う辺を比べ、こちらが大きければそのカードを自分のものにする（裏返す）。
- 終局時、**盤上の自分のカード枚数 ＋ 手札に残った枚数**がスコア。多い方が勝ち（後攻は手札が 1 枚残るので合計 10 点）。
- 先攻はランダム（seed で決定）。

### ルール修飾（対戦前に ON/OFF を選ぶ）
| ルール | 内容 |
|---|---|
| Open | 互いの手札が見える。OFF のときは相手の手札が伏せられる |
| Random | 手札をデッキから無作為に 5 枚選ぶ |
| Same | 置いたカードの 2 辺以上が、隣接カードの向かい合う辺と**同値**なら、それら（相手所有分）を裏返す |
| Same Wall | Same の判定で盤外の壁を A（10）として扱う |
| Plus | 置いたカードの 2 辺以上で「自辺＋相手辺」の**和が等しい**なら、それら（相手所有分）を裏返す |
| Combo | Same／Plus で裏返ったカードを起点に、通常ルール（大小比較）で連鎖的に裏返す |
| Elemental | 一部のマスに属性が付く。一致する属性のカードは全辺 +1、不一致は −1（無属性カードは −1） |
| Sudden Death | 引き分けなら、終局時に**自分が所有しているカード**を手札にして再戦（上限 5 回） |

#### 判定順序（実装で必ず守る）
1. 置いたカードについて Same / Plus を判定し、成立した辺の相手カードを裏返す（Same と Plus は同時判定・同時適用）。
   - Same / Plus の成立判定では「隣接カードの所有者を問わず」数え、裏返すのは相手所有のものだけ（原作準拠）。
2. 通常ルール（大小比較）で置いたカードの周囲を裏返す。
3. Combo が ON なら、手順 1 で裏返ったカードを起点に、通常ルールで幅優先に連鎖させる。連鎖で裏返ったカードもさらに起点になる（Same/Plus は連鎖では再判定しない）。
4. 走査順は常に「上→右→下→左」、連鎖キューは FIFO。これで結果とイベント列の順序が決定論的になる。
5. Elemental の補正は比較・Same・Plus のすべてで補正後の値を使う（原作は Same/Plus に補正前を使うという説もあるが、本作は**補正後に統一**し、ルール説明に明記する）。補正後の値はクランプしない（A+1=11、1−1=0）。

#### 細部の確定事項（Phase 1 実装時に決定）
- Same Wall は Same が ON のときだけ有効。壁は Plus の判定には数えない。壁で成立した Same の flip の cause は `same`、見出しイベントは `sameWall`。
- Same と Plus の両方で成立したマスは Same として 1 回だけ裏返す。`rule` イベントは実際に 1 枚以上裏返るときだけ出す。
- `chainDepth`: 通常・Same・Plus の裏返しは 0、Combo の n 段目は n。手順 2（通常ルール）で取ったカードは連鎖の起点にならない。
- Sudden Death の「上限 5 回」は再戦 5 回（最大 6 ラウンド）。再戦の先攻は前ラウンドの後攻。Pivot トークンは持ち越し、マス属性は変えない。ラウンド間は `end` を出さず `suddenDeath` → `turn`。
- Random OFF はデッキちょうど 5 枚、ON は 5 枚以上から重複なしで 5 枚。乱数消費順は「先攻 → p0 手札 → p1 手札 → マス属性」。
- `createGame` はカード定義（`cards`）も受け取り、GameState が手札分の定義と `firstPlayer` を持つ（engine を data/ に依存させないため）。カードデータは型安全のためコード上の表（C# 版は `Assets/Game/Core/Data/`）で持つ。

---

## 3. オリジナル要素

原作の再現だけで終わらせない。以下を「ルール修飾」として追加し、原作ルールと同じ UI で ON/OFF できるようにする。採用・調整は **AI 自己対戦の数値**（§6）で判断する。

### 3-1. Pivot（回転）— 看板メカニクス
- 各プレイヤーは対戦ごとに **Pivot トークンを 2 個**持つ。
- カードを置くとき、トークンを 1 個消費してそのカードを **90°／180°／270° 回転**させて置ける（辺の値が回る）。
- 狙い: 「強い辺をどこに向けるか」という判断が加わり、手札が弱くても逆転の目が生まれる。トークンが有限なので「いつ切るか」の駆け引きになる。
- AI の分岐数はトークン残数があるときだけ最大 4 倍になる（探索コストは §5 で吸収）。

### 3-2. Resonance（共鳴）
- 同じ**系統**（カードの種族タグ。例: 獣・機械・霊・竜）のカードが、同じ所有者で縦横に隣接していると、互いに向かい合う辺が +1 される（比較時のみ、見た目でも光る）。
- 狙い: 「どこに置くか」に陣形の要素を足す。カードデータに系統タグが必要。

### 3-3. Trait（カード固有能力）— 少数精鋭
上位レベルのカードのみ 1 つ持つ。種類は 6 前後に絞り、説明が 1 行で済むものに限る。
- 例: **Guard**（最初の 1 回だけ裏返されない）、**Pierce**（斜め隣も通常ルールで判定）、**Echo**（置いたターン、隣接する自分のカードの辺 +1 を一時付与）
- 最終的な種類と数値は自己対戦で決める。強すぎる Trait は削る。

優先度: Pivot ＞ Resonance ＞ Trait。Pivot だけでも作品として成立させる。

---

## 4. ルールエンジン仕様

### 4-1. データ型（概念）
以下は概念を示す擬似コード（TS 版の記法）。C# では同じ意味の型を定義する。目安:
- `Sides` などの小さな値は `readonly struct` や固定長配列、`Element` / `Tribe` / `Player` は `enum`。
- `GameState` は外から変更できない形（読み取り専用プロパティ＋新しいインスタンスを返す）。AI 用の高速版の内部では、可変な作業用バッファを使ってよい。
- 判定順序や細部の決定は TS 版（`D:\Projects\tripletriad-ts-legacy\src\engine\`）と同一にする。テストも TS 版の 101 件をすべて移植する。

```ts
type Rank = 1|2|3|4|5|6|7|8|9|10;            // 10 = A
type Sides = [top: Rank, right: Rank, bottom: Rank, left: Rank];
type Element = 'fire'|'ice'|'thunder'|'earth'|'water'|'wind'|'holy'|'dark';

interface CardDef {                            // 不変のカード定義（Core/Data の C# の表）
  id: string; name: string; level: number;     // level 1..10
  sides: Sides; element?: Element; tribe?: Tribe; trait?: TraitId;
}

interface RuleSet {                            // 対戦前に決まる不変の設定
  open: boolean; random: boolean; same: boolean; sameWall: boolean;
  plus: boolean; combo: boolean; elemental: boolean; suddenDeath: boolean;
  pivot: boolean; resonance: boolean; traits: boolean;
}

type Player = 0 | 1;
interface PlacedCard { cardId: string; owner: Player; rotation: 0|1|2|3; /* + 状態フラグ(Guard 消費等) */ }

interface GameState {
  rules: RuleSet;
  board: (PlacedCard | null)[];                // 長さ 9、index = row*3+col
  cellElements: (Element | null)[];            // 長さ 9
  hands: [string[], string[]];                 // cardId の配列
  turn: Player;
  pivotTokens: [number, number];
  rngState: number;                            // mulberry32 の内部状態（32bit 整数）
  cards: CardDef[];                            // 手札に入ったカードの定義（state 単体で完結させる）
  firstPlayer: Player;                         // そのラウンドの先攻（Sudden Death で交代）
  phase: 'playing' | 'ended';
  suddenDeathRound: number;
}

interface Move { handIndex: number; cell: number; rotation: 0|1|2|3; }
```

### 4-2. API（すべて純関数）
```ts
createGame(config: { rules: RuleSet; decks: [string[], string[]]; cards: CardDef[]; seed: number }): GameState
legalMoves(state: GameState): Move[]
applyMove(state: GameState, move: Move): { state: GameState; events: GameEvent[] }
score(state: GameState): [number, number]
result(state: GameState): 'p0' | 'p1' | 'draw' | null
hashState(state: GameState): string            // オンライン同期チェック・置換表で使用
```
- `applyMove` は入力の state を**変更しない**（新しい state を返す）。不正な手は例外を投げる。
- 乱数はすべて `rngState` 経由（mulberry32 を移植し、TS 版と同じ seed で同じ乱数列になること）。Core 内で `System.Random`・`DateTime`・`Stopwatch`・`UnityEngine` を使うことを禁止する（テストでソースを静的検査）。
- 浮動小数点を使わない（環境による差をなくし、決定論を保つ）。

### 4-3. イベント列（UI はこれを再生するだけ）
```ts
type GameEvent =
  | { type: 'place'; player: Player; cell: number; cardId: string; rotation: number }
  | { type: 'rule'; rule: 'same' | 'plus' | 'sameWall'; cells: number[] }   // 演出の見出し
  | { type: 'flip'; cell: number; to: Player; cause: 'basic' | 'same' | 'plus' | 'combo'; chainDepth: number; from: number }
  | { type: 'resonance'; cells: number[] }
  | { type: 'trait'; cell: number; trait: TraitId }
  | { type: 'turn'; player: Player }
  | { type: 'end'; score: [number, number]; result: 'p0' | 'p1' | 'draw' }
  | { type: 'suddenDeath'; round: number };
```
- UI はエンジンの内部を読まずに、イベント列だけで演出を組み立てる。`chainDepth` で連鎖の段数に応じて演出を強める。
- エンジンが 1 手を完全に解決してからイベントを返す（コマンド → イベント列）。オンラインではコマンド（Move）だけを送る。

### 4-4. 性能目標
- `applyMove` は 1 回 10µs 程度を目指す（探索で数百万回呼ぶため）。WebGL（IL2CPP → WebAssembly）ではネイティブより数倍遅くなる前提で、AI の時間予算を決める。AI 用に、イベント生成を省いた内部高速版（`applyMoveFast`）を別に用意してよい。ただし**両者の結果が一致することをテストで保証**する。

---

## 5. CPU（AI）

| 難易度 | 方式 |
|---|---|
| Easy | 1 手先の貪欲＋ランダム性（時々悪手を指す） |
| Normal | 深さ制限つき αβ 探索 |
| Hard | 反復深化 αβ ＋置換表（Zobrist ハッシュ）＋手順序付け。終盤は完全読み |

- 評価関数: 終局ならスコア差。途中局面は「盤上の所有差 ＋ 手札の強さ ＋ 辺の露出リスク」を重み付け。
- 時間予算（例: Hard で 1 手 1.5 秒）を超えたら、その時点の最善手を返す。
- **Open が OFF（相手手札が見えない）とき**: 相手の手札を「まだ見えていないカード」からサンプリングする決定化（determinization）を複数回行い、多数決で手を選ぶ。CPU がズルをしない（見えない情報を使わない）ことをテストで保証する。
- **ヒント機能**: プレイヤーの手番で同じ探索を回し、「この手が最善（予想結果 6-4 で勝ち）」を表示。対戦後の振り返り（各手の悪手度表示）にも使う。
- 探索は Core 内で `Step(maxNodes)` のように**少しずつ進められる**形にする（反復深化の 1 段ずつが自然な区切り）。Unity 側は 1 フレームあたり数ミリ秒の予算で進め、その間 UI は「考え中」の演出を出す（WebGL ではスレッドが使えないため）。
- 探索結果が同じ局面・同じ設定で常に同じになること（決定論）をテストで保証する。

---

## 6. バランス検証（自己対戦シミュレーター）

`dotnet/Edgewise.Sim`（`dotnet run` で実行、Unity 不要）:
- 指定ルールの組み合わせで、CPU 同士を数千局対戦させ、**先攻勝率・引き分け率・平均連鎖数・Pivot 使用タイミング**などを集計して Markdown 表で出力する。
- 目標値（暫定）: 先攻勝率 45〜55%、引き分け率 15〜35%。
- Pivot のトークン数、Trait の数値などはこの結果で決め、結果を `docs/BALANCE.md` に記録する。**「なぜこの数値か」をデータで説明できる**ことがポートフォリオ上の主張になる。

---

## 7. カードデータ

- オリジナルのカード 50 枚前後（レベル 1〜10、各レベル 5 枚程度）。
- 名前・系統・属性はオリジナル。世界観は後で決める（仮: 星座と幻獣をモチーフにした「星図カード」など）。
- **強さの予算**: レベルごとに 4 辺の合計値の範囲を決める（例: Lv1 は合計 10〜13、Lv10 は合計 30〜33）。テストで全カードが予算内であることを検査する。
- 50 枚の名前・値は TS 版（`tripletriad-ts-legacy/src/data/cards.ts`）をそのまま移植する。
- カード絵: 最初はレベル・属性・系統から**手続き的に生成するエンブレム**（シェーダーまたは生成テクスチャ）で成立させる。絵がなくても数字と色で遊べる見た目を先に作る。

---

## 8. UI・手触り（ジューシーさ）

最低限ここまでは「気持ちよさ」として作り込む。実装の監査はコードの正しさだけでなく、この 5 項目との突き合わせで行う。

1. **置く**: カードをドラッグ（クリックでも可）→ マスに吸い付く。置いた瞬間に軽い沈み込みと着地音。
2. **裏返る**: Y 軸 180° の 3D 回転で色が変わる。裏返し 1 枚ごとに音程が上がる。
3. **連鎖**: `chainDepth` に応じて、待ち時間・画面の揺れ・文字演出（SAME! / PLUS! / COMBO ×3）を段階的に強くする。
4. **予告**: 置く前にマスへカーソルを乗せると、そこに置いた場合に裏返るカードがうっすら光る（エンジンの `ApplyMove` で先読みするだけで作れる）。初心者がルールを理解できる最重要機能。
5. **決着**: スコアのカウントアップ、勝敗演出、ワンボタンで再戦。

その他:
- 操作は マウス／タッチ／キーボード すべて対応。スマホ縦画面でも遊べるレイアウト。
- 演出スキップ（速度 ×2 / 即時）の設定。
- 色覚に配慮し、所有者の区別を色だけに頼らない（枠の模様・アイコンも変える）。

---

## 9. オンライン対戦

- Unity Gaming Services の Lobby で部屋を作り、Relay 経由で接続する（第一候補。§1 参照）。ホストが部屋を作り、**部屋コード**を相手に共有して参加。
- ホストがエンジンを正とし（ホスト権威）、両者は Move だけを送り合う。毎手 `hashState` を交換して食い違いを検出する。
- 先攻決定・Random ルールの乱数 seed はホストが決めて共有する。
- 隠し情報（Open OFF）はホストには見えてしまう。カジュアル対戦として割り切り、その旨を画面と README に明記する。
- 切断時: 再接続を 30 秒待ち、戻らなければ不戦勝。

---

## 10. 開発フェーズ

| Phase | 内容 | 完了条件 |
|---|---|---|
| 0 | 土台: Unity プロジェクト（URP 2D・Input System・TMP・Test Framework）、asmdef 構成、dotnet テスト／シミュレーターのプロジェクト、GitHub Actions で dotnet テスト、WebGL の `BuildScript` | Unity が batchmode でエラーなくコンパイルし、WebGL ビルドが成功する |
| 1 | ルールエンジン（基本＋全原作ルール修飾＋Pivot）＋カードデータを TS 版から C# へ移植 | TS 版のテスト 101 件相当が dotnet で緑。同じ seed・手順で TS 版と C# 版の `hashState` 列が一致する |
| 2 | CPU（3 難易度、フレーム分割できる探索）＋ 自己対戦シミュレーター | Hard が Easy に 95% 以上勝つ。WebGL 版で 1 手の思考が予算時間内、思考中もフレームが落ちない |
| 3 | 遊べる UI ＋ 手触り §8 の 5 項目 | 自分で 10 局遊んで「気持ちいい」と言える。スマホで遊べる |
| 4 | オリジナル要素（Pivot → Resonance → Trait）＋バランス調整 | `docs/BALANCE.md` に数値根拠つきで記録 |
| 5 | オンライン対戦 | 別端末同士で 1 局最後まで遊べる |
| 6 | 仕上げ: タイトル画面、ルール説明（チュートリアル）、README、プレイ動画/GIF | 面接官が説明なしで遊べる |

各フェーズの終わりで遊べる状態を保つ。Phase 3 を早めに迎え、以降は常に「遊んで面白いか」で判断する。

---

## 11. 未決事項
- 正式タイトル（「EDGEWISE」は仮）
- 世界観・カードのモチーフ
- Trait の最終的な種類と数値
- 1 人用の目標（カード収集・段位戦など）を入れるか。入れるならカード交換ルール（One/Diff/Direct/All）相当も検討
