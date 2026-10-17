# PUBLISH — 公開の手順（GitHub Pages と unityroom）

DESIGN §14-3。WebGL 版を **GitHub Pages** と **unityroom** の両方に置く（2026-10-18 magu 決定）。
push とアップロードは、どちらも magu の確認を取ってから行う。スクリプトはファイルの用意と確認までを受け持つ。

| | GitHub Pages | unityroom |
|---|---|---|
| ビルド | `BuildScript.BuildWebGL` → `Builds/WebGL` | `BuildScript.BuildWebGLUnityroom` → `Builds/unityroom-build/edgewise` |
| 圧縮 | Gzip ＋ **Decompression Fallback ON**（`.unityweb`） | Gzip ＋ **Decompression Fallback OFF**（`.gz`） |
| 理由 | GitHub Pages は `Content-Encoding` を付けられないので、ローダーが JS で解凍する | unityroom の公式の説明（下記）どおり。サーバーが `Content-Encoding: gzip` で返し、ブラウザが解凍する |
| 置き方 | `tools/deploy_pages.ps1`（gh-pages ブランチに commit → `-Push` で push） | `tools/package_unityroom.ps1` が 4 ファイルを `Builds/unityroom/` にまとめる → magu が手でアップロード |
| URL | https://magu0914.github.io/edgewise/ | {{UNITYROOM_URL}} |

Player 設定（`ProjectSettings`）は GitHub Pages 用のまま。unityroom 用のビルドは、ビルドの間だけ設定を切り替えて、終わったら元に戻す（ProjectSettings に差分を残さない）。

**曲ファイルは公開しない**（OpenTracks のライセンスで音源ファイルそのものの配布が禁止。DESIGN §8-2）。ビルドには変換された形で含まれるので問題ない。両方のスクリプトは、ビルドの中に `.wav` / `.mp3` / `.ogg` などがまぎれていたら止まる。

---

## 1. GitHub Pages

### 準備（初回のみ。magu）
1. GitHub にリポジトリ `magu0914/edgewise` があり、ローカルの `origin` がそこを指していること（`git remote -v`）。
2. main を push 済みであること（README の画像などが表示されるように）。

### 手順
```powershell
# 1. ビルド（Builds/WebGL）
& .\tools\RunUnity.ps1 -LogName build.log -Extra '-buildTarget','WebGL','-executeMethod','Edgewise.EditorTools.BuildScript.BuildWebGL'

# 2. 空実行: ローカルの gh-pages ブランチに commit するところまで（push はしない）
powershell -ExecutionPolicy Bypass -File tools/deploy_pages.ps1
#   → 最後に gh-pages の中身の一覧と「STOPPED before push」が出る。中身を確かめる。

# 3. magu の承認を得てから push
powershell -ExecutionPolicy Bypass -File tools/deploy_pages.ps1 -Push
```
- main の作業ツリーには触れない（一時フォルダに `git worktree` を作って gh-pages を組み立て、終わったら消す）。
- gh-pages は履歴を持たない（orphan）ブランチとして作る。中身は `index.html`・`Build/`（4 ファイル）・`.nojekyll` だけ。毎回まるごと置き換えて 1 コミット足す。
- commit のメッセージは「Deploy WebGL build from main <sha>」。main に未コミットの変更があれば「(with uncommitted changes)」が付く（公開するビルドは main のコミットから作ること）。

### 初回の push の後（magu）
1. GitHub のリポジトリの **Settings → Pages** で、Source を「Deploy from a branch」、Branch を `gh-pages` / `/ (root)` にして Save。
2. 数分後に https://magu0914.github.io/edgewise/ を開き、タイトルが出て音が鳴る（クリック後）ことを確かめる。
3. 確かめるところ: タイトル → 遊び方 → フリー対戦 1 局、スマホの縦画面。

---

## 2. unityroom

### 確認した要件（2026-10-18 に公式の説明を確認）

| 項目 | 要件 | 出典 |
|---|---|---|
| Development Build | **オフ** | unityroom ヘルプ「unityroomにアップロードするためのビルド方法」（最終更新 2026-10-15、Unity 6.3 LTS 6000.3.11f1 で説明）https://help.unityroom.com/unityroom-351dc3ed5de980eebd79eef3b153be31 |
| Compression Format | **Gzip** | 同上 |
| Decompression Fallback | **オフ** | 同上 |
| Unity のバージョン | ゲームの「WebGL設定」で、ビルドに使ったバージョンを選ぶ（本作は 6000.3.15f1 なので **6000.3.x**） | unityroom の運営者のブログ「unityroomに投稿する方法」http://blog.naichilab.com/entry/how-to-upload-unityroom （unityroom ヘルプ「ゲームの投稿方法」https://help.unityroom.com/2e9dc3ed5de9803ab110fe3b05138bd5 からのリンク）。Unity 6 での選び方は Qiita の解説 https://qiita.com/kurisaka_konabe/items/73471f00439befea807d でも確認 |
| アップロードするファイル | ビルドの `Build/` にできる **4 ファイル**（`.loader.js` / `.data.gz` / `.framework.js.gz` / `.wasm.gz`）を「WebGLアップロード」で 1 つずつ選ぶ。拡張子を取り違えないこと | 運営者のブログ（上記。「拡張子をよく見て、間違えない様に選択してください」）、Qiita の解説（「これら 4 つのファイルが unityroom の投稿で必要」） |
| 公開に最低限必要なもの | アイコンと WebGL のアップロード | 運営者のブログ（上記） |
| 日本語の表示 | 既定のフォントでは日本語が出ないので、フォントを入れておく | unityroom ヘルプ「Unity WebGLにビルドすると日本語が表示されません」https://help.unityroom.com/Unity-WebGL-2e9dc3ed5de9800583c6fd847a715747 。本作は Noto Sans JP の静的 SDF を同梱しているので対応済み |

- `index.html` と `TemplateData/` はアップロードしない（unityroom が自分のページでローダーを読み込む）。本作の `index.html` は GitHub Pages 用のテンプレート。
- ファイル名は出力先のフォルダ名から付く（`edgewise.*`）。Name Files As Hashes はオフのまま。
- 画面の大きさ: unityroom の既定の表示サイズは 960×540（16:9）。本作は画面の比率に合わせて横・縦のレイアウトを切り替え、16:9 で設計しているので、既定のままでよい（大きくしたい場合は 16:9 で指定する。Qiita の解説より）。
- ヘルプは Notion で書かれていて、ページの表示に JavaScript が要る（`curl` などでは本文が取れない）。

### ファイルを用意する
```powershell
powershell -ExecutionPolicy Bypass -File tools/package_unityroom.ps1             # ビルドしてまとめる（数分）
powershell -ExecutionPolicy Bypass -File tools/package_unityroom.ps1 -SkipBuild  # 既にあるビルドをまとめ直すだけ
```
- `Builds/unityroom/` に 4 ファイルができる（`edgewise.loader.js` / `edgewise.data.gz` / `edgewise.framework.js.gz` / `edgewise.wasm.gz`）。
- スクリプトが確かめること: ちょうど 4 ファイル、`.unityweb` / `.br` が無い（圧縮形式の取り違え）、`.gz` の先頭が gzip（1F 8B）、曲ファイルが無い。最後にサイズと SHA-256 の先頭を出す。
- 手元での動作確認: `node tools/webgl_e2e.mjs --scenario title --serve Builds/unityroom-build/edgewise --serve-port <空き> --out Logs/shots/unityroom`（E2E のサーバーは `.gz` に `Content-Encoding: gzip` を付けて返すので、unityroom と同じ形で読み込める）。

### アップロードの手順（magu が自分のアカウントで行う）
1. https://unityroom.com/ にログインし、ヘッダーから新しいゲームを作る（タイトル・ゲーム ID（英数字・`-`・`_`。URL の一部になる）・短い説明）。
2. **基本情報**: アイコンを設定する（必須。正方形に近い画像。例: `docs/media/title.jpg` の扇の部分を切り出したもの）。
3. **WebGL設定**: Unity のバージョンで **6000.3.x** を選ぶ（**間違えないこと**）。画面サイズは既定の 960×540 のまま。
4. **WebGLアップロード**: `Builds/unityroom/` の 4 ファイルを、欄の拡張子に合わせて 1 つずつ選んでアップロードする。成功するとページの下にファイルのサイズなどが出る。
5. 説明文に、オマージュの表記（「本作は Triple Triad（© SQUARE ENIX）へのオマージュ作品であり、公式とは無関係です。」）と、クレジット（game-icons.net CC BY 3.0 の作者、OpenTracks の曲、Noto Sans JP）、リポジトリの URL を書く（README の「クレジットとオマージュの表記」と同じ内容）。
6. **公開設定**で公開する前に、限定公開などで一度遊んで確かめる（タイトル → 遊び方 → フリー対戦 1 局、音が鳴るか）。
7. 公開したら、README と この文書の `{{UNITYROOM_URL}}` を実際の URL に置き換える。

---

## 3. 公開前の確認（両方共通）
- [ ] `dotnet test dotnet/Edgewise.sln`、Unity の EditMode・PlayMode がすべて成功
- [ ] WebGL の E2E: `node tools/webgl_e2e.mjs --scenario 8a --serve Builds/WebGL --serve-port <空き> --out Logs/shots/8a`
- [ ] `git status` が clean（公開するビルドが main のコミットと一致する）
- [ ] `ProjectSettings/QualitySettings.asset` の antiAliasing が 2 のまま
- [ ] ビルドの中に曲ファイルが無い（スクリプトが確認する）
