// WebGL 版の E2E: ヘッドレス Chrome（専用の一時プロファイル・ウィンドウなし）を CDP で操作して 1 局を最後まで遊び、
// スクリーンショットを Logs/shots/3a/ に保存する。Unity 側は URL の ?log で操作用のログ（[EW] ...）を console に出す。
//
// 使い方（先に Builds/WebGL を HTTP で配信しておく）:
//   node tools/webgl_e2e.mjs --url http://127.0.0.1:8765/ --out Logs/shots/3a [--seed 7] [--slow 4]
// 終了時に Chrome を必ず終了する（例外時も）。
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync, mkdtempSync, readFileSync, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, resolve, extname, normalize } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, arr) => (v.startsWith('--') ? [...a, [v.slice(2), arr[i + 1]]] : a), []));
// --serve DIR: このスクリプトの中で DIR を HTTP で配信する（--serve-port、既定 8772。終了時に必ず止める）
const SERVE_PORT = Number(args['serve-port'] ?? 8772);
const BASE = args.serve ? `http://127.0.0.1:${SERVE_PORT}/` : (args.url ?? 'http://127.0.0.1:8765/');
let server = null;
if (args.serve) {
  const root = resolve(args.serve);
  const types = { '.html': 'text/html', '.js': 'application/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.css': 'text/css', '.ico': 'image/x-icon' };
  server = createServer((req, res) => {
    let p = decodeURIComponent((req.url ?? '/').split('?')[0]);
    if (p.endsWith('/')) p += 'index.html';
    const file = normalize(join(root, p));
    if (!file.startsWith(root) || !existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
    // .unityweb は圧縮のまま返す（ビルドは decompressionFallback = true なのでローダーが JS で解凍する）
    const ext = extname(file).toLowerCase();
    res.writeHead(200, { 'Content-Type': types[ext] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(readFileSync(file));
  });
  await new Promise((r) => server.listen(SERVE_PORT, '127.0.0.1', r));
  console.log('[e2e] serving', root, 'on', BASE);
}
const OUT = resolve(args.out ?? 'Logs/shots/3b');
const COMBO_SEEDS = (args['combo-seeds'] ?? '2,12,16,17,18').split(',');
const SEED = args.seed ?? '7';
const SLOW = args.slow ?? '4';
const CHROME = args.chrome ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = Number(args.port ?? 9333);
mkdirSync(OUT, { recursive: true });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[e2e]', ...a);

const profile = mkdtempSync(join(tmpdir(), 'edgewise-chrome-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
  '--window-size=1280,720', '--hide-scrollbars', '--mute-audio', '--enable-gpu', '--ignore-gpu-blocklist', '--use-angle=d3d11',
  'about:blank',
], { stdio: 'ignore', windowsHide: true });

let ws;
let nextId = 1;
const pending = new Map();
const consoleLines = [];
const rawConsole = [];
let sessionId;

function send(method, params = {}, sid = sessionId) {
  const id = nextId++;
  const msg = { id, method, params };
  if (sid) msg.sessionId = sid;
  ws.send(JSON.stringify(msg));
  return new Promise((res, rej) => pending.set(id, { res, rej, method }));
}

async function connect() {
  for (let i = 0; i < 100; i++) {
    try {
      const v = await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json();
      ws = new WebSocket(v.webSocketDebuggerUrl);
      await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
      ws.onmessage = (ev) => {
        const m = JSON.parse(ev.data);
        if (m.id && pending.has(m.id)) {
          const p = pending.get(m.id);
          pending.delete(m.id);
          if (m.error) p.rej(new Error(p.method + ': ' + m.error.message));
          else p.res(m.result);
        } else if (m.method === 'Runtime.consoleAPICalled') {
          if (rawConsole.length < 400) rawConsole.push(m.params.type + ' ' + m.params.args.map((a) => a.value ?? a.description ?? '').join(' '));
          const text = m.params.args.map((a) => a.value ?? a.description ?? '').join(' ');
          const t = Date.now();
          if (text.includes('[EW]')) {
            for (const line of text.split('\n')) if (line.includes('[EW]')) consoleLines.push({ t, line: line.slice(line.indexOf('[EW]') + 5) });
          } else if (/error|exception/i.test(text) || m.params.type === 'error') {
            consoleLines.push({ t, line: 'CONSOLE ' + JSON.stringify(m.params.args).replace(/\s+/g, ' ').slice(0, 800) });
          }
        } else if (m.method === 'Runtime.exceptionThrown') {
          consoleLines.push({ t: Date.now(), line: 'EXCEPTION ' + JSON.stringify(m.params.exceptionDetails).slice(0, 400) });
        }
      };
      return;
    } catch {
      await sleep(200);
    }
  }
  throw new Error('could not connect to Chrome');
}

let cursor = 0; // consoleLines の読み位置
async function waitLine(re, timeoutMs = 60000, from = cursor) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeoutMs) {
    for (let i = from; i < consoleLines.length; i++) {
      const m = consoleLines[i].line.match(re);
      if (m) { cursor = i + 1; return { m, index: i, line: consoleLines[i].line }; }
    }
    await sleep(30);
  }
  throw new Error('timeout waiting for ' + re + '\nlast lines:\n' + consoleLines.slice(-15).map((l) => l.line).join('\n'));
}

function linesSince(index) { return consoleLines.slice(index).map((l) => l.line); }

async function shot(name) {
  const r = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join(OUT, name + '.png'), Buffer.from(r.data, 'base64'));
  log('shot', name);
}

async function mouse(type, x, y, button = 'left', clickCount = 1, buttons = undefined) {
  const b = buttons ?? (type === 'mouseReleased' ? 0 : button === 'left' ? 1 : 2);
  await send('Input.dispatchMouseEvent', { type, x, y, button, clickCount, buttons: b });
}
async function move(x, y) { await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none', buttons: 0 }); }
async function drag(x0, y0, x1, y1, steps = 12, holdMs = 16) {
  for (let i = 1; i <= steps; i++) {
    const k = i / steps;
    await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x0 + (x1 - x0) * k, y: y0 + (y1 - y0) * k, button: 'left', buttons: 1 });
    await sleep(holdMs);
  }
}
async function click(x, y) {
  await move(x, y);
  await sleep(50);
  await mouse('mousePressed', x, y);
  await sleep(60);
  await mouse('mouseReleased', x, y);
}

function uiRect(screen, name, from = 0) {
  let hit = null;
  for (let i = from; i < consoleLines.length; i++) {
    const m = consoleLines[i].line.match(new RegExp('^ui ' + screen + '/' + name + ' (-?\\d+) (-?\\d+) (\\d+) (\\d+)'));
    if (m) hit = { x: +m[1] + +m[3] / 2, y: +m[2] + +m[4] / 2 };
  }
  if (!hit) throw new Error('no ui rect for ' + screen + '/' + name);
  return hit;
}

/** 直近の await ブロック（手札・マス・候補手）を読む。 */
async function readTurn() {
  const a = await waitLine(/^await turn=(\d) hand=(\d+) pivot=(\d+)/, 120000);
  const opt = await waitLine(/^options (.*)$/, 10000, a.index);
  const block = consoleLines.slice(a.index, opt.index + 1).map((l) => l.line);
  const hand = [], cells = [];
  for (const l of block) {
    let m = l.match(/^hand (\d+) (-?\d+) (-?\d+) (\S+)/);
    if (m) hand[+m[1]] = { x: +m[2], y: +m[3], id: m[4] };
    m = l.match(/^cell (\d+) (-?\d+) (-?\d+) (empty|full)/);
    if (m) cells[+m[1]] = { x: +m[2], y: +m[3], empty: m[4] === 'empty' };
  }
  const options = opt.m[1].trim().split(' ').filter(Boolean).map((s) => {
    const [h, c, rest] = s.split(':');
    return { h: +h, c: +c, flips: parseInt(rest, 10), same: rest.includes('S'), plus: rest.includes('P') };
  });
  return { pivot: +a.m[3], hand, cells, options, index: a.index };
}

const KEYS = {
  ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Enter: 13, Escape: 27,
};
async function key(k) {
  const vk = KEYS[k];
  await send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: k, code: k, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
  await sleep(40);
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code: k, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
  await sleep(90);
}

/** キーボードだけで 1 手指す。置けたら true。 */
async function keyboardMove(choice, turn) {
  const before = consoleLines.length;
  await key('ArrowRight'); // キーボード操作の開始（手札 0 を選ぶ）
  for (let i = 0; i < choice.h; i++) await key('ArrowRight');
  await key('Enter');
  let start;
  try { start = await waitLine(/^preview cell=(\d)/, 3000, before); } catch { log('keyboard: no hold'); return false; }
  let cur = +start.m[1];
  const dc = (choice.c % 3) - (cur % 3), dr = Math.floor(choice.c / 3) - Math.floor(cur / 3);
  for (let i = 0; i < Math.abs(dc); i++) await key(dc > 0 ? 'ArrowRight' : 'ArrowLeft');
  for (let i = 0; i < Math.abs(dr); i++) await key(dr > 0 ? 'ArrowDown' : 'ArrowUp');
  await sleep(200);
  await key('Enter');
  try { await waitLine(/^place /, 3000, before); } catch { log('keyboard: no place'); return false; }
  log(`keyboard move: hand ${choice.h} -> cell ${choice.c}`);
  return true;
}
const results = { shots: [], fps: [], errors: [] };

async function playMatch({ wantPreview, wantFlip, wantRule, wantPivot, wantKeyboard, wantMenu, wantHint, wantCombo, alwaysSP, onTurn }) {
  let moves = 0;
  const got = { preview: !wantPreview, flip: !wantFlip, rule: !wantRule, pivot: !wantPivot, keyboard: !wantKeyboard, menu: !wantMenu,
    hint: !wantHint, combo: !wantCombo };
  while (true) {
    // 自分の手番か終局を待つ
    const start = cursor;
    let turn;
    const t0 = Date.now();
    while (true) {
      const end = consoleLines.slice(start).findIndex((l) => /^end /.test(l.line));
      const aw = consoleLines.slice(start).findIndex((l) => /^await turn=/.test(l.line));
      // CPU の手で SAME / PLUS が出たら撮る
      if (!got.rule) {
        const rb = consoleLines.slice(start).findIndex((l) => /^step RuleBanner/.test(l.line));
        if (rb >= 0) { await sleep(120 * Number(SLOW)); await shot('06_same_or_plus'); got.rule = true; }
      }
      // COMBO ×2 以上の文字が出た瞬間を撮る（CPU の手でも自分の手でも）
      if (!got.combo) {
        const cb = consoleLines.slice(start).findIndex((l) => /^step ComboBanner x([2-9])/.test(l.line));
        // ログは段の始まり（0.2 秒の待ちの前）に出るので、待ちと文字の弾みが終わるころに撮る
        if (cb >= 0) { await sleep(300 * Number(SLOW)); await shot('11_combo_depth2'); got.combo = true; }
      }
      if (end >= 0 && (aw < 0 || end < aw)) { cursor = start + end + 1; return { moves, got }; }
      if (aw >= 0) { cursor = start + aw; turn = await readTurn(); break; }
      if (Date.now() - t0 > 180000) throw new Error('timeout waiting for my turn');
      await sleep(40);
    }
    await sleep(150);
    if (onTurn) await onTurn(moves, turn);
    // 手を選ぶ: Same/Plus（撮影用）→ 裏返す枚数が多い手 → 先頭
    let choice = null;
    if (!got.rule || alwaysSP) choice = turn.options.find((o) => o.same || o.plus);
    if (!choice) choice = [...turn.options].sort((a, b) => b.flips - a.flips)[0];
    const card = turn.hand[choice.h];
    const cell = turn.cells[choice.c];
    log(`move ${moves}: hand ${choice.h} (${card.id}) -> cell ${choice.c} flips ${choice.flips}${choice.same ? ' SAME' : ''}${choice.plus ? ' PLUS' : ''}`);

    if (wantMenu && !got.menu && moves === 3) {
      // 右上のメニュー（ルール確認・速度・ミュート・降参）を開いて撮り、閉じる
      await click(...Object.values(uiRect('Hud', 'MenuButton')));
      const mAt = (await waitLine(/^screen Menu/, 5000)).index;
      await sleep(200);
      await click(...Object.values(uiRect('Menu', 'RulesButton', mAt)));
      await sleep(400);
      await shot('x_menu');
      // 設定（速度・効果音の音量とミュート・画面の揺れ）
      await click(...Object.values(uiRect('Menu', 'SettingsButton', mAt)));
      const sAt = (await waitLine(/^screen Settings/, 5000)).index;
      await sleep(400);
      await shot('12_settings');
      await click(...Object.values(uiRect('Settings', 'CloseButton', sAt)));
      await sleep(300);
      await click(...Object.values(uiRect('Menu', 'CloseButton', mAt)));
      await sleep(300);
      got.menu = true;
    }
    if (!got.hint && moves === 2) {
      // ヒント: ボタンを押すと最善手のカードとマスが光り、「予想 a-b」が出る
      const hb = consoleLines.length;
      await click(...Object.values(uiRect('Hud', 'HintButton')));
      const h = await waitLine(/^hint (\d+):(\d+) r(\d) (\d+)-(\d+)/, 30000, hb);
      log('hint', h.line);
      await sleep(600);
      await shot('10_hint');
      got.hint = true;
    }
    if (wantKeyboard && !got.keyboard && moves === 2) {
      // キーボード: ←→ で手札を選ぶ → Enter で持つ → 矢印でマス → Enter で置く
      const kbOk = await keyboardMove(choice, turn);
      got.keyboard = kbOk;
      if (kbOk) { moves++; await sleep(200); continue; }
    }
    await move(card.x, card.y);
    await sleep(120);
    await mouse('mousePressed', card.x, card.y);
    await sleep(120);

    let rotated = false;
    if (!got.pivot && turn.pivot > 0 && !(choice.same || choice.plus)) {
      // 持ったまま盤の手前に動かし、右クリックで回す
      const mx = (card.x + cell.x) / 2, my = (card.y + cell.y) / 2;
      await drag(card.x, card.y, mx, my, 10);
      // 左ボタンを押したまま右クリック（buttons のビットで左を押しっぱなしにする）
      await mouse('mousePressed', mx, my, 'right', 1, 3);
      await sleep(40);
      await mouse('mouseReleased', mx, my, 'right', 1, 1);
      await sleep(500);
      await shot('09_pivot');
      got.pivot = true;
      rotated = true;
      await drag(mx, my, cell.x, cell.y, 10);
    } else {
      await drag(card.x, card.y, cell.x, cell.y, 14);
    }
    await sleep(450);
    if (!got.preview && choice.flips > 0 && !rotated) {
      await shot('04_preview');
      got.preview = true;
    }
    const before = consoleLines.length;
    await mouse('mouseReleased', cell.x, cell.y);
    moves++;
    if (!got.rule && (choice.same || choice.plus)) {
      await waitLine(/^step RuleBanner/, 15000, before);
      await sleep(110 * Number(SLOW));
      await shot('06_same_or_plus');
      got.rule = true;
    } else if (!got.flip && choice.flips > 0 && !rotated) {
      await waitLine(/^step FlipGroup/, 15000, before);
      await sleep(75 * Number(SLOW));
      await shot('05_flip_midway');
      got.flip = true;
    }
    await sleep(200);
  }
}

// ---------------------------------------------------------------------------
// Phase 3c: 音のオプション（設定画面の操作・横/縦の撮影・タブが裏に回ったら消音）と、Pivot ON の対戦設定、1 局。
//   node tools/webgl_e2e.mjs --scenario 3c --url http://127.0.0.1:8765/ --out Logs/shots/3c --pivot-first 1 --pivot-second 2
// ---------------------------------------------------------------------------

/** スライダーの左端から ratio（0..1）の位置をクリックする（uGUI の Slider はその位置へ値が飛ぶ）。 */
async function clickSlider(screen, name, ratio, from) {
  let r = null;
  for (let i = from; i < consoleLines.length; i++) {
    const m = consoleLines[i].line.match(new RegExp('^ui ' + screen + '/' + name + ' (-?\\d+) (-?\\d+) (\\d+) (\\d+)'));
    if (m) r = { x: +m[1], y: +m[2], w: +m[3], h: +m[4] };
  }
  if (!r) throw new Error('no slider rect for ' + screen + '/' + name);
  // つまみの可動域は両端からつまみの幅の半分だけ内側（横 30・縦 52 の基準。画面の倍率を掛けて概算）
  const inset = r.h * 0.5;
  const x = r.x + inset + (r.w - 2 * inset) * ratio;
  await click(x, r.y + r.h / 2);
}

/** 直近の "settings master=.. bgm=.. sfx=.. mute=.. bgmute=.." を待って値を返す。 */
async function waitSettings(pred, from, what) {
  const t0 = Date.now();
  while (Date.now() - t0 < 8000) {
    for (let i = consoleLines.length - 1; i >= from; i--) {
      const m = consoleLines[i].line.match(/^settings master=(\d+) bgm=(\d+) sfx=(\d+) mute=(\d) bgmute=(\d)/);
      if (m) {
        const s = { master: +m[1], bgm: +m[2], sfx: +m[3], mute: +m[4], bgmute: +m[5], index: i };
        if (pred(s)) return s;
        break;
      }
    }
    await sleep(40);
  }
  throw new Error('settings did not reach: ' + what + '\nlast lines:\n' + consoleLines.slice(-10).map((l) => l.line).join('\n'));
}

async function setHidden(hidden) {
  const state = hidden ? 'hidden' : 'visible';
  await send('Runtime.evaluate', {
    expression: `Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => '${state}' });`
      + `Object.defineProperty(document, 'hidden', { configurable: true, get: () => ${hidden} });`
      + `document.dispatchEvent(new Event('visibilitychange')); 1`,
  });
}

async function run3c() {
  const checks = {};
  const url = `${BASE}?log&seed=${SEED}&slow=1`;
  log('open', url);
  await send('Page.navigate', { url });

  // 1. タイトル → 設定（横）
  const tAt = (await waitLine(/^screen Title/, 120000)).index;
  checks.titleBgm = consoleLines.slice(0, tAt + 1).map((l) => l.line).filter((l) => /^bgm /.test(l));
  await sleep(1200);
  await click(...Object.values(uiRect('Title', 'TitleSettingsButton', tAt)));
  const sAt = (await waitLine(/^screen Settings/, 5000)).index;
  await sleep(500);
  await shot('01_settings_landscape');

  // スライダー（マウスでトラックをクリック → その位置へ。5% 刻みで保存）
  await clickSlider('Settings', 'BgmSlider', 0.25, sAt);
  const s1 = await waitSettings((s) => s.bgm >= 15 && s.bgm <= 35 && s.bgm % 5 === 0, sAt, 'bgm ≈ 25%');
  await clickSlider('Settings', 'MasterSlider', 0.5, sAt);
  const s2 = await waitSettings((s) => s.master >= 40 && s.master <= 60, s1.index, 'master ≈ 50%');
  // キーボード: 選ばれているスライダーを → で 1 段（5%）上げる
  await key('ArrowRight');
  const s3 = await waitSettings((s) => s.master === s2.master + 5, s2.index, 'master +5 by keyboard');
  await click(...Object.values(uiRect('Settings', 'MuteButton', sAt)));
  const s4 = await waitSettings((s) => s.mute === 1, s3.index, 'mute ON');
  await sleep(250);
  await shot('02_settings_muted');
  await click(...Object.values(uiRect('Settings', 'MuteButton', sAt)));
  const s5 = await waitSettings((s) => s.mute === 0, s4.index, 'mute OFF');
  checks.settingsLandscape = { bgm: s1.bgm, master: s2.master, keyboard: s3.master, mute: [s4.mute, s5.mute] };

  // タブが裏に回ったら消音（初期値 ON）: visibilitychange で AudioContext を suspend → 表に戻ったら resume
  const hAt = consoleLines.length;
  await setHidden(true);
  checks.hidden = (await waitLine(/^audio (suspended|tab hidden)/, 5000, hAt)).line;
  if (!/^audio suspended/.test(checks.hidden)) throw new Error('the AudioContext was not suspended: ' + checks.hidden);
  await setHidden(false);
  try { checks.visible = (await waitLine(/^audio resumed/, 5000, hAt)).line; } catch { checks.visible = 'no resume line (context was not running)'; }
  // OFF にすると裏に回っても止めない
  await click(...Object.values(uiRect('Settings', 'BackgroundMuteButton', sAt)));
  const s6 = await waitSettings((s) => s.bgmute === 0, s5.index, 'bgmute OFF');
  const h2 = consoleLines.length;
  await setHidden(true);
  checks.hiddenWhenOff = (await waitLine(/^audio /, 5000, h2)).line;
  await setHidden(false);
  await click(...Object.values(uiRect('Settings', 'BackgroundMuteButton', sAt)));
  await waitSettings((s) => s.bgmute === 1, s6.index, 'bgmute ON again');

  // 2. 縦画面 360x640 の設定画面（操作もする）
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  const pAt = (await waitLine(/^screen Portrait/, 10000, sAt)).index;
  await sleep(600);
  await shot('03_settings_portrait_360x640');
  const before = consoleLines.length;
  await clickSlider('Portrait', 'SfxSlider', 0.75, pAt);
  const p1 = await waitSettings((s) => s.sfx >= 65 && s.sfx <= 85, before, 'sfx ≈ 75% (portrait)');
  await click(...Object.values(uiRect('Portrait', 'ShakeButton', pAt)));
  await sleep(150);
  await click(...Object.values(uiRect('Portrait', 'ShakeButton', pAt)));
  checks.settingsPortrait = { sfx: p1.sfx };
  await sleep(300);
  await shot('04_settings_portrait_after');
  // 横に戻して閉じる
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  const lAt = (await waitLine(/^screen Landscape/, 10000, pAt)).index;
  await sleep(300);
  await click(...Object.values(uiRect('Landscape', 'CloseButton', lAt)));
  await sleep(300);

  // 3. 対戦設定（Pivot ON）
  await click(...Object.values(uiRect('Landscape', 'StartButton', lAt)));
  const setupAt = (await waitLine(/^screen Setup/, 10000, lAt)).index;
  await sleep(600);
  await shot('05_setup_pivot_on');
  await click(...Object.values(uiRect('Setup', 'BeginButton', setupAt)));

  // 4. 1 局（マウス・Pivot・メニューから設定を開く）
  const mAt = (await waitLine(/^match seed=\d+ rules=(\d+) first=(\d)/, 20000, setupAt));
  const first = +mAt.m[2];
  const firstTurn = await waitLine(/^await turn=(\d) hand=(\d+) pivot=(\d+)/, 120000, mAt.index);
  cursor = firstTurn.index;
  const myTokens = +firstTurn.m[3];
  const expectFirst = args['pivot-first'] !== undefined ? +args['pivot-first'] : null;
  const expectSecond = args['pivot-second'] !== undefined ? +args['pivot-second'] : null;
  checks.pivot = { humanIsFirst: first === 0, tokensAtFirstTurn: myTokens, expectFirst, expectSecond };
  if (expectFirst !== null) {
    const want = first === 0 ? expectFirst : expectSecond;
    // 後攻なら CPU の 1 手目は自分のトークンを減らさない
    if (myTokens !== want) throw new Error(`pivot tokens: got ${myTokens}, want ${want} (human first=${first === 0})`);
  }
  await sleep(300);
  await shot('06_match_start');
  const r = await playMatch({ wantPreview: false, wantFlip: false, wantRule: false, wantPivot: myTokens > 0, wantKeyboard: false, wantMenu: true });
  log('match finished', r.moves, 'moves', JSON.stringify(r.got));
  const resAt = (await waitLine(/^screen Result/, 60000)).index;
  await sleep(800);
  await shot('07_result');
  checks.bgmLines = consoleLines.map((l) => l.line).filter((l) => /^bgm /.test(l));
  checks.match = r.got;
  results.checks = checks;
  log('checks', JSON.stringify(checks));
}

// ---------------------------------------------------------------------------
// Phase 4a: カード絵（星雲・シルエット・星座）の入った対戦を 1 局。横の対戦中・カード 1 枚の拡大・縦 360x640 の対戦中を撮り、FPS を集める。
//   node tools/webgl_e2e.mjs --scenario 4a --url http://127.0.0.1:8771/ --out Logs/shots/4a
// ---------------------------------------------------------------------------
async function clipCard(name, cell, spacing, scale) {
  const w = spacing * 0.98, h = spacing * 1.25;
  const r = await send('Page.captureScreenshot', { format: 'png', clip: { x: Math.max(0, cell.x - w / 2), y: Math.max(0, cell.y - h / 2), width: w, height: h, scale } });
  writeFileSync(join(OUT, name + '.png'), Buffer.from(r.data, 'base64'));
  log('shot', name);
}

async function run4a() {
  const checks = {};
  const url = `${BASE}?log&seed=${SEED}&slow=1`;
  log('open', url);
  await send('Page.navigate', { url });
  const tAt = (await waitLine(/^screen Title/, 120000)).index;
  await sleep(1500);
  await shot('00_title');
  await click(...Object.values(uiRect('Title', 'StartButton', tAt)));
  const setupAt = (await waitLine(/^screen Setup/, 10000, tAt)).index;
  await sleep(500);
  await click(...Object.values(uiRect('Setup', 'BeginButton', setupAt)));
  await waitLine(/^match seed=/, 20000, setupAt);

  let closeup = false;
  const r = await playMatch({
    onTurn: async (moves, turn) => {
      const full = turn.cells.filter((c) => c && !c.empty);
      if (!closeup && full.length >= 4) {
        await sleep(400);
        await shot('01_match_landscape');
        const spacing = Math.abs(turn.cells[1].x - turn.cells[0].x);
        // 盤の中央寄りのカードを拡大（数字・シルエット・星座の読みやすさの確認）
        const pick = full.sort((a, b) => Math.hypot(a.x - turn.cells[4].x, a.y - turn.cells[4].y) - Math.hypot(b.x - turn.cells[4].x, b.y - turn.cells[4].y))[0];
        await clipCard('03_card_closeup', pick, spacing, 3);
        // 手札の 1 枚（小さく表示されるカード）も拡大
        const hand = turn.hand.find((c) => c);
        if (hand) await clipCard('03b_hand_card_closeup', hand, spacing * 0.8, 3);
        checks.boardCellSpacingPx = spacing;
        closeup = true;
      }
    },
  });
  log('match finished', r.moves, 'moves');
  const resAt = (await waitLine(/^screen Result/, 60000)).index;
  await sleep(800);
  await shot('04_result');
  checks.fpsMatch = consoleLines.map((l) => l.line).filter((l) => /^fps-match/.test(l));

  // 縦画面 360x640 で再戦し、2 手目のあとで撮る
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  const pAt = (await waitLine(/^screen Portrait/, 10000, resAt)).index;
  await sleep(500);
  await click(...Object.values(uiRect('Portrait', 'AgainButton', pAt)));
  await waitLine(/^match seed=/, 20000, pAt);
  let portraitMoves = 0;
  const pFps = consoleLines.length;
  const r2 = await playMatch({
    onTurn: async (moves, turn) => {
      if (moves === 2 && portraitMoves === 0) {
        await sleep(500);
        await shot('02_match_portrait_360x640');
        const spacing = Math.abs(turn.cells[1].x - turn.cells[0].x);
        const full = turn.cells.filter((c) => c && !c.empty);
        if (full.length) await clipCard('03c_portrait_card_closeup', full[0], spacing, 4);
        portraitMoves = moves;
      }
    },
  });
  log('portrait match finished', r2.moves, 'moves');
  await waitLine(/^screen Result/, 60000);
  checks.fpsPortrait = consoleLines.slice(pFps).map((l) => l.line).filter((l) => /^fps-match/.test(l));
  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
  results.checks = checks;
  log('checks', JSON.stringify(checks));
}

// ---------------------------------------------------------------------------
// Phase 4b: タイトルの扇・カード図鑑（一覧・拡大・傾き・キーボード・縦画面）・クレジット・対戦中の手札ホバー・1 局。
//   node tools/webgl_e2e.mjs --scenario 4b --url http://127.0.0.1:8771/ --out Logs/shots/4b
// ---------------------------------------------------------------------------
function dexCards(from) {
  const cards = {};
  for (let i = from; i < consoleLines.length; i++) {
    const m = consoleLines[i].line.match(/^dex card (\d+) (-?\d+) (-?\d+) (\S+)/);
    if (m) cards[m[4]] = { i: +m[1], x: +m[2], y: +m[3] };
  }
  return cards;
}

async function zoomCard(id, listAt, name, tilt) {
  const cards = dexCards(listAt);
  const c = cards[id];
  if (!c) throw new Error('dex card not on screen: ' + id);
  const before = consoleLines.length;
  await click(c.x, c.y);
  const z = await waitLine(new RegExp('^dex zoom ' + id + ' lv(\\d+) (-?\\d+) (-?\\d+) (-?\\d+) (-?\\d+)'), 5000, before);
  const cx = +z.m[2], cy = +z.m[3], hw = +z.m[4], hh = +z.m[5];
  if (tilt) {
    // カードの右上の縁へカーソルを動かして傾ける
    for (let k = 1; k <= 8; k++) { await move(cx + hw * 0.9 * k / 8, cy - hh * 0.75 * k / 8); await sleep(30); }
  } else {
    await move(cx, cy);
  }
  await sleep(900);
  await shot(name);
  return { cx, cy, hw, hh };
}

async function run4b() {
  const checks = {};
  const url = `${BASE}?log&seed=${SEED}&slow=1`;
  log('open', url);
  await send('Page.navigate', { url });
  const tAt = (await waitLine(/^screen Title/, 120000)).index;
  await sleep(3000);
  await shot('01_title_fan');

  // ---- カード図鑑（横） ----
  await click(...Object.values(uiRect('Title', 'DexButton', tAt)));
  const dAt = (await waitLine(/^dex list landscape/, 10000, tAt)).index;
  await move(5, 5);
  await sleep(4000); // 一覧の FPS を測る（50 枚）
  await shot('02_dex_list_landscape');
  // 1 枚にカーソルを乗せる（傾いて名前が出る）
  {
    const c = dexCards(lastIndex(/^screen Dex \d/))['orionmane'];
    for (let k = 0; k <= 6; k++) { await move(c.x - 20 + k * 6, c.y - 20 + k * 4); await sleep(30); }
    await sleep(600);
    await shot('02b_dex_list_hover');
  }
  // 拡大: Lv1・Lv5・Lv8（傾き）・Lv10（傾き）
  const zooms = [['glimmouse', '03_dex_zoom_lv1', false], ['lupusfang', '04_dex_zoom_lv5', false], ['cygnwraith', '05_dex_zoom_lv8_tilt', true], ['pegasion', '06_dex_zoom_lv10_tilt', true]];
  for (const [id, name, tilt] of zooms) {
    const listAt = lastIndex(/^screen Dex \d/);
    await zoomCard(id, listAt, name, tilt);
    const back = consoleLines.length;
    await key('Escape');
    await waitLine(/^dex list/, 5000, back);
    await sleep(300);
  }
  checks.fpsDex = consoleLines.map((l) => l.line).filter((l) => /^fps-dex/.test(l));

  // キーボード: → → Enter で 2 枚目（Dustwisp）を拡大、→ で次、Esc で一覧
  {
    const kAt = consoleLines.length;
    await move(5, 5);
    await key('ArrowRight');
    await key('ArrowRight');
    await key('Enter');
    const z = await waitLine(/^dex zoom (\S+)/, 5000, kAt);
    await key('ArrowRight');
    const z2 = await waitLine(/^dex zoom (\S+)/, 5000, z.index + 1);
    checks.keyboard = [z.m[1], z2.m[1]];
    await key('Escape');
    await waitLine(/^dex list/, 5000, z2.index);
  }

  // ---- カード図鑑（縦 360x640） ----
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  const pAt = (await waitLine(/^dex list portrait/, 10000)).index;
  await sleep(900);
  await shot('07_dex_list_portrait_360x640');
  {
    const c = dexCards(lastIndex(/^screen Dex \d/))['emberkit'];
    await click(c.x, c.y);
    await waitLine(/^dex zoom emberkit/, 5000, pAt);
    await sleep(900);
    await shot('08_dex_zoom_portrait_360x640');
    const b = consoleLines.length;
    await key('Escape');
    await waitLine(/^dex list/, 5000, b);
  }
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  const lAt = (await waitLine(/^dex list landscape/, 10000, pAt)).index;
  await sleep(400);
  // 戻る → タイトル
  await click(...Object.values(uiRect('Dex', 'DexBackButton', lastIndex(/^screen Dex \d/))));
  const t2 = (await waitLine(/^screen Title/, 5000, lAt)).index;
  await sleep(500);

  // ---- クレジット ----
  await click(...Object.values(uiRect('Title', 'CreditsButton', t2)));
  const cAt = (await waitLine(/^screen Credits/, 5000, t2)).index;
  await sleep(700);
  await shot('09_credits');
  await click(...Object.values(uiRect('Credits', 'CreditsCloseButton', cAt)));
  const t3 = (await waitLine(/^screen Title/, 5000, cAt)).index;
  await sleep(500);

  // ---- 対戦（手札にカーソルを乗せる） ----
  await click(...Object.values(uiRect('Title', 'StartButton', t3)));
  const setupAt = (await waitLine(/^screen Setup/, 10000, t3)).index;
  await sleep(500);
  await click(...Object.values(uiRect('Setup', 'BeginButton', setupAt)));
  await waitLine(/^match seed=/, 20000, setupAt);
  let hovered = false;
  const r = await playMatch({
    onTurn: async (moves, turn) => {
      if (!hovered && moves >= 1) {
        // 2 枚目（重なっている札）にカーソルを乗せる: 前に出て全体が見える
        const h = turn.hand[1] ?? turn.hand[0];
        for (let k = 0; k <= 6; k++) { await move(h.x - 15 + k * 5, h.y + 10 + k * 3); await sleep(30); }
        await sleep(700);
        await shot('10_match_hand_hover');
        hovered = true;
      }
    },
  });
  log('match finished', r.moves, 'moves');
  await waitLine(/^screen Result/, 60000);
  await sleep(600);
  await shot('11_result');
  checks.fpsMatch = consoleLines.map((l) => l.line).filter((l) => /^fps-match/.test(l));
  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
  results.checks = checks;
  log('checks', JSON.stringify(checks));
}

function lastIndex(re) {
  for (let i = consoleLines.length - 1; i >= 0; i--) if (re.test(consoleLines[i].line)) return i;
  return 0;
}

// タイトルの構図の確認（4b 追加）: 横と縦で、扇の角度が違う 2 時点を撮る
//   node tools/webgl_e2e.mjs --scenario title --url http://127.0.0.1:8771/ --out Logs/shots/4b
async function runTitle() {
  await send('Page.navigate', { url: `${BASE}?log&seed=${SEED}` });
  const tAt = (await waitLine(/^screen Title/, 120000)).index;
  await move(5, 5);
  await sleep(3000);
  await shot('01_title_fan_v2');
  await sleep(6000);
  await shot('01_title_fan_v2_later');
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  await waitLine(/^screen Portrait/, 10000, tAt);
  await sleep(1500);
  await shot('01_title_fan_v2_portrait');
  await sleep(6000);
  await shot('01_title_fan_v2_portrait_later');
}

// ---------------------------------------------------------------------------
// Phase 5b-1: 星図巡りの通し（横 1280×720 と縦 360×640 の撮影つき）。
//   node tools/webgl_e2e.mjs --scenario 5b1 --serve Builds/WebGL --serve-port 8772 --out Logs/shots/5b1
// A（横・x1）: 新しく始める → 導入（7 枚）→ 星図 → 星域 1 → ミラにデッキを組んで挑む → ヒントの手で勝つ → 報酬 → 星図 → タイトル（続きあり）
//             → 設定（星図巡りのタブ）でデータを消す（2 段階の確認）。
// B（横・x2・?grant）: 新しく始める → 主マーロウに勝つまで挑む（デッキの上限超えを撮る）→ 巡り終えた帯 → 星図で星域 2 の鍵が外れる。
// C（縦 360×640・x2・?grant）: タイトル（続きあり）→ 設定でデータを消す → A・B と同じ画面を縦で撮る。
// ---------------------------------------------------------------------------
const GRANT = 'lynxlight,gearowl,cinderdrake,hollowshade,stonetoad,emberscale,aquilaburst,leomane';
const STARTER = ['glimmouse', 'dustwisp', 'coglet', 'scalefry', 'pyxmoth', 'rivetcrab', 'mirelamp'];

function lastLine(re, from = 0) {
  for (let i = consoleLines.length - 1; i >= from; i--) {
    const m = consoleLines[i].line.match(re);
    if (m) return { m, index: i, line: consoleLines[i].line };
  }
  return null;
}

async function clickUi(screen, name, from) {
  const r = uiRect(screen, name, from);
  await click(r.x, r.y);
}

/** 直近の "screen X" の行（なければ待つ）。 */
async function waitScreen(name, from, timeout = 30000) {
  return (await waitLine(new RegExp('^screen ' + name + ' '), timeout, from)).index;
}

function rewardCards(from) {
  const cards = [];
  for (let i = from; i < consoleLines.length; i++) {
    const m = consoleLines[i].line.match(/^reward card (\d+) (-?\d+) (-?\d+)/);
    if (m) cards[+m[1]] = { x: +m[2], y: +m[3] };
  }
  return cards;
}

/** 会話を待ってスキップする（会話が出なければ null）。 */
async function skipDialogue(from, shotName) {
  const d = await waitLine(/^dialogue 0\/(\d+)/, 20000, from);
  const at = (await waitLine(/^screen Dialogue /, 5000, d.index)).index;
  if (shotName) {
    await sleep(1400);
    await shot(shotName);
  }
  await clickUi('Dialogue', 'SkipButton', at);
  await waitLine(/^dialogue end/, 10000, d.index);
}

/** 自分の手番ごとにヒントを押して、その手（手札 → マス）をマウスで置く。終局したら結果の行を返す。 */
async function playHinted(firstTurnShot) {
  let moves = 0;
  while (true) {
    const start = cursor;
    const t0 = Date.now();
    let turn = null;
    while (true) {
      const end = consoleLines.slice(start).findIndex((l) => /^end /.test(l.line));
      const aw = consoleLines.slice(start).findIndex((l) => /^await turn=/.test(l.line));
      if (end >= 0 && (aw < 0 || end < aw)) {
        cursor = start + end + 1;
        const m = consoleLines[start + end].line.match(/^end (\d+)-(\d+) (\S+)/);
        return { moves, score: [+m[1], +m[2]], result: m[3], index: start + end };
      }
      if (aw >= 0) { cursor = start + aw; turn = await readTurn(); break; }
      if (Date.now() - t0 > 240000) throw new Error('timeout waiting for my turn');
      await sleep(40);
    }
    await sleep(200);
    if (moves === 0 && firstTurnShot) { await sleep(300); await shot(firstTurnShot); }
    const hb = consoleLines.length;
    // HUD のボタンの位置は手番の直前（await の前）にログに出る
    await clickUi('Hud', 'HintButton', Math.max(0, turn.index - 60));
    const h = await waitLine(/^hint (\d+):(\d+) r(\d) (\d+)-(\d+)/, 60000, hb);
    const hi = +h.m[1], ci = +h.m[2];
    const card = turn.hand[hi], cell = turn.cells[ci];
    await move(card.x, card.y);
    await sleep(80);
    await mouse('mousePressed', card.x, card.y);
    await sleep(80);
    await drag(card.x, card.y, cell.x, cell.y, 10);
    await sleep(150);
    await mouse('mouseReleased', cell.x, cell.y);
    moves++;
    await sleep(150);
  }
}

/** 星図から: 星域 1 → 相手 opp → デッキ（onDeck で操作）→ 挑む → 会話 → 対戦（ヒント）→ 結果。勝つまで maxTries 回。 */
async function challengeUntilWin(opp, maxTries, shots) {
  for (let attempt = 1; attempt <= maxTries; attempt++) {
    const mapAt = await waitScreen('Map', Math.max(0, consoleLines.length - 400));
    await sleep(600);
    if (attempt === 1 && shots.map) await shot(shots.map);
    await clickUi('Map', 'Region1', mapAt);
    const rAt = await waitScreen('Region', mapAt);
    await sleep(700);
    if (attempt === 1 && shots.region) await shot(shots.region);
    await clickUi('Region', 'Challenge' + opp, rAt);
    let dAt = await waitScreen('Deck', rAt);
    await sleep(500);
    // 一覧の位置は次のフレームのログが正しい
    dAt = lastLine(/^screen Deck /, dAt).index;
    if (attempt === 1 && shots.onDeck) dAt = await shots.onDeck(dAt);
    await clickUi('Deck', 'BeginButton', dAt);
    const talkFrom = consoleLines.length;
    await skipDialogue(talkFrom, attempt === 1 ? shots.dialogue : null);
    await waitLine(/^campaign match /, 20000, talkFrom);
    const r = await playHinted(attempt === 1 ? shots.match : null);
    log(`match vs ${opp}: ${r.score.join('-')} ${r.result} (${r.moves} moves)`);
    results.matches.push({ opp, score: r.score, result: r.result });
    const resAt = await waitScreen('Result', r.index, 60000);
    await sleep(600);
    await clickUi('Result', 'NextButton', resAt);
    if (r.result === 'p0') return resAt;
    // 引き分け・負け: 会話（負け）→ 星図
    const after = consoleLines.length;
    if (r.result === 'p1') await skipDialogue(after, null);
    await waitLine(/^campaign \S+ -> Map/, 20000, after);
  }
  throw new Error('could not win in ' + maxTries + ' tries');
}

/** 勝った後: 会話 → 報酬（裏向き → めくれた NEW! の瞬間）→ 次へ。 */
async function takeReward(resAt, shots) {
  await skipDialogue(resAt, shots.dialogueAfter ?? null);
  const shown = await waitLine(/^reward shown (\d+)/, 30000, resAt);
  const count = +shown.m[1];
  let at = shown.index;
  for (let p = 0; p < count; p++) {
    await waitLine(/^reward card \d+ /, 10000, at);
    await sleep(500);
    const cards = rewardCards(at);
    const open = cards.map((c, i) => (c ? i : -1)).filter((i) => i >= 0);
    // 撮影用: 対戦のログ（相手のデッキの並び ＝ 報酬の並び）から、まだ持っていないカードを選ぶ（NEW! を撮るため）
    const m = lastLine(/^campaign match \S+ deck \S+ vs (\S+)/);
    const ids = m ? m.m[1].split(',') : [];
    const fresh = open.filter((i) => ids[i] && !(shots.owned ?? []).includes(ids[i]));
    const pick = fresh.length ? fresh[0] : open[Math.min(open.length - 1, 2)];
    // カーソルを乗せる（傾いて浮く）
    const c = cards[pick];
    for (let k = 0; k <= 8; k++) { await move(c.x - 30 + k * 5, c.y - 25 + k * 3); await sleep(30); }
    await sleep(700);
    if (p === 0 && shots.facedown) await shot(shots.facedown);
    const before = consoleLines.length;
    await mouse('mousePressed', c.x, c.y);
    await sleep(60);
    await mouse('mouseReleased', c.x, c.y);
    const rv = await waitLine(/^reward reveal (\S+) (\S+)/, 15000, before);
    log('reward', rv.line);
    results.rewards.push(rv.line);
    await sleep(250);
    if (p === 0 && shots.reveal) await shot(shots.reveal);
    at = (await waitLine(/^reward landed /, 15000, before)).index;
  }
  const rAt = await waitScreen('Reward', at, 20000);
  await sleep(500);
  if (shots.done) await shot(shots.done);
  const nextAt = consoleLines.length;
  await clickUi('Reward', 'RewardNextButton', rAt);
  return nextAt;
}

async function newGameIntro(from, shots, titleScreen = 'Title', titleAt = null) {
  const t = titleAt ?? await waitScreen('Title', from, 120000);
  await sleep(1500);
  if (shots.titleNew) await shot(shots.titleNew);
  await clickUi(titleScreen, 'CampaignButton', t);
  const iAt = await waitScreen('Intro', t, 30000);
  await sleep(300);
  const cards = rewardCards(t);
  for (const i of [0, 2, 4]) {
    const b = consoleLines.length;
    await click(cards[i].x, cards[i].y);
    await waitLine(new RegExp('^intro reveal ' + i + ' '), 8000, b);
    await sleep(250);
  }
  await sleep(500);
  if (shots.intro) await shot(shots.intro);
  await clickUi('Intro', 'RevealAllButton', iAt);
  await waitLine(/^intro received/, 20000, iAt);
  const nAt = await waitScreen('Reward', iAt, 20000);
  await sleep(400);
  await clickUi('Reward', 'RewardNextButton', nAt);
  await waitLine(/^campaign Intro -> Map/, 10000, nAt);
}

async function openSettingsAndErase(titleAt, shots, titleScreen = 'Title') {
  await clickUi(titleScreen, 'TitleSettingsButton', titleAt);
  const sAt = await waitScreen('Settings', titleAt);
  await sleep(300);
  await clickUi('Settings', 'TabCampaign', sAt);
  const cAt = lastLine(/^screen Settings /).index;
  await sleep(500);
  if (shots.settings) await shot(shots.settings);
  await clickUi('Settings', 'EraseButton', cAt);
  const c1 = await waitScreen('Confirm1', cAt);
  await sleep(300);
  await clickUi('Confirm1', 'EraseYes', c1);
  const c2 = await waitScreen('Confirm2', c1);
  await sleep(400);
  if (shots.confirm) await shot(shots.confirm);
  await clickUi('Confirm2', 'EraseYes', c2);
  await waitLine(/^campaign erased/, 5000, c2);
  await waitLine(/^title progress -/, 5000, c2);
  await sleep(300);
  await clickUi('Settings', 'CloseButton', cAt);
  await sleep(300);
}

/** デッキ: 枠 0 を外して Lv7 を入れ、上限超え（赤）を撮る → おまかせを撮る。 */
function deckOverThenAuto(overShot, autoShot) {
  return async (dAt) => {
    await sleep(400);
    await clickUi('Deck', 'Slot0', dAt);
    await sleep(250);
    await clickUi('Deck', 'Own_leomane', dAt);
    await waitLine(/^deck \S+ total \d+\/\d+ over/, 5000, dAt);
    await sleep(500);
    await shot(overShot);
    const b = consoleLines.length;
    await clickUi('Deck', 'AutoButton', dAt);
    await waitLine(/^deck \S+ total \d+\/\d+ ready/, 5000, b);
    await sleep(600);
    await shot(autoShot);
    return dAt;
  };
}

async function run5b1() {
  results.matches = [];
  results.rewards = [];
  const checks = {};

  // ---------------- A: 横・x1 ----------------
  let navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${SEED}&speed=x1` });
  await waitLine(/^campaign load (\S+) nosave/, 120000, navAt);
  await newGameIntro(navAt, { titleNew: 'L00_title_new', intro: 'L02_intro_receive_7' });
  const resAt = await challengeUntilWin(0, 4, {
    map: 'L03_map_region1_only', region: 'L05_region', dialogue: 'L07_dialogue', match: 'L08_match_opponent_hud',
    onDeck: async (dAt) => {
      await sleep(400);
      await clickUi('Deck', 'AutoButton', dAt);
      await sleep(600);
      await shot('L06b_deck_auto');
      return dAt;
    },
  });
  await takeReward(resAt, { facedown: 'L09a_reward_facedown', reveal: 'L09b_reward_new', done: 'L09c_reward_done', owned: STARTER });
  const mAt = await waitScreen('Map', resAt, 30000);
  await sleep(600);
  checks.mapAfterWin = lastLine(/^campaign \S+ -> Map/).line;
  await clickUi('Map', 'MapBackButton', mAt);
  const tAt = await waitScreen('Title', mAt);
  const prog = await waitLine(/^title progress (.+)/, 5000, mAt);
  checks.titleProgress = prog.m[1];
  await sleep(1500);
  await shot('L01_title_continue');
  await openSettingsAndErase(tAt, { settings: 'L12_settings', confirm: 'L12b_settings_erase_confirm' });
  checks.erased = true;
  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);

  // ---------------- B: 横・x2・主を倒す ----------------
  navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${+SEED + 1}&speed=x2&grant=${GRANT}` });
  await waitLine(/^campaign load (\S+) nosave/, 120000, navAt);
  await newGameIntro(navAt, {});
  const bossAt = await challengeUntilWin(2, 6, { onDeck: deckOverThenAuto('L06a_deck_over_cap', 'L06b2_deck_auto_boss') });
  const bNext = await takeReward(bossAt, {});
  await skipDialogue(bNext, null); // 主の「星域を抜けた」台詞
  await waitLine(/^cleared shown/, 20000, bossAt);
  await sleep(250);
  await shot('L10_cleared_band');
  const ub = await waitLine(/^map unlock library begin/, 20000, bossAt);
  await sleep(950);
  await shot('L04_map_unlock_moment');
  await waitLine(/^map unlock library done/, 20000, ub.index);
  await sleep(300);
  await shot('L04b_map_region2_open');
  checks.unlocked = true;

  // ---------------- C: 縦 360×640 ----------------
  navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${+SEED + 2}&speed=x2&grant=${GRANT}` });
  await waitLine(/^campaign load (\S+) save/, 120000, navAt);
  const lt = await waitScreen('Title', navAt, 60000);
  await sleep(800);
  // 縦に切り替えると、Unity がボタンの新しい位置を出す（screen Portrait）
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  const pt = await waitScreen('Portrait', lt, 10000);
  await sleep(1500);
  await shot('P01_title_continue');
  await openSettingsAndErase(pt, { settings: 'P12_settings', confirm: 'P12b_settings_erase_confirm' }, 'Portrait');
  await newGameIntro(pt, { intro: 'P02_intro_receive_7' }, 'Portrait', pt);
  const pBoss = await challengeUntilWin(2, 6, {
    map: 'P03_map_region1_only', region: 'P05_region', dialogue: 'P07_dialogue', match: 'P08_match_opponent_hud',
    onDeck: deckOverThenAuto('P06a_deck_over_cap', 'P06b_deck_auto'),
  });
  const pNext = await takeReward(pBoss, { facedown: 'P09a_reward_facedown', reveal: 'P09b_reward_new', done: 'P09c_reward_done', owned: [...STARTER, ...GRANT.split(',')] });
  await skipDialogue(pNext, null);
  await waitLine(/^cleared shown/, 20000, pBoss);
  await sleep(250);
  await shot('P10_cleared_band');
  const pub = await waitLine(/^map unlock library begin/, 20000, pBoss);
  await sleep(950);
  await shot('P04_map_unlock_moment');
  await waitLine(/^map unlock library done/, 20000, pub.index);
  await sleep(300);
  await shot('P04b_map_region2_open');
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
  results.checks = checks;
  log('checks', JSON.stringify(checks));
}

// ---------------------------------------------------------------------------
// Phase 5b-2: 立ち絵のコンタクトシート、星図巡りの 1 局（立ち絵・大きな立ち絵の会話・絵つきの小さなカード・報酬）、図鑑の収集状況。
//   node tools/webgl_e2e.mjs --scenario 5b2 --serve Builds/WebGL --serve-port 8773 --out Logs/shots/5b2
// 横 1280×720 で一通り → タイトルに戻って縦 360×640 でもう一度（星域の画面・デッキ・会話・対戦・報酬・図鑑）。
// ---------------------------------------------------------------------------
async function campaignRound(prefix, titleScreen, titleAt) {
  await clickUi(titleScreen, 'CampaignButton', titleAt);
  const resAt = await challengeUntilWin(0, 4, {
    region: prefix + '02_region_portraits',
    dialogue: prefix + '04a_dialogue_before',
    match: prefix + '05_match',
    onDeck: async (dAt) => {
      await sleep(700);
      await shot(prefix + '03_deck_art_cards');
      return dAt;
    },
  });
  const next = await takeReward(resAt, {
    dialogueAfter: prefix + '04b_dialogue_after', facedown: prefix + '06a_reward_facedown', reveal: prefix + '06b_reward_reveal',
    done: prefix + '06c_reward_done', owned: STARTER,
  });
  const mAt = await waitScreen('Map', next, 30000);
  await sleep(500);
  // 図鑑（収集状況。未入手は暗いシルエット）
  await clickUi('Map', 'MapDexButton', mAt);
  const dl = await waitLine(/^dex list (landscape|portrait)/, 10000, mAt);
  await move(5, 5);
  await sleep(1200);
  await shot(prefix + '07_dex_collection');
  const dAt = lastLine(/^screen Dex /).index;
  // 未入手のカードを 1 枚拡大（数字と名前が伏せられている）
  const cards = dexCards(dl.index - 200 > 0 ? dl.index - 200 : 0);
  const picked = results.rewards.map((l) => l.split(' ')[2]);
  const unknownId = Object.keys(cards).find((id) => !STARTER.includes(id) && !picked.includes(id)) ?? Object.keys(cards).pop();
  const c = cards[unknownId];
  const zb = consoleLines.length;
  await click(c.x, c.y);
  const z = await waitLine(/^dex zoom (\S+)( unknown)?/, 5000, zb);
  results.dexZoom = (results.dexZoom ?? []).concat(z.line);
  await sleep(900);
  await shot(prefix + '08_dex_unknown_zoom');
  await key('Escape');
  await waitLine(/^dex list/, 5000, z.index);
  await sleep(300);
  await clickUi('Dex', 'DexBackButton', lastLine(/^screen Dex /).index);
  const m2 = await waitScreen('Map', dAt, 10000);
  await sleep(400);
  await clickUi('Map', 'MapBackButton', m2);
  return await waitScreen('Title', m2, 10000);
}

async function run5b2() {
  results.matches = [];
  results.rewards = [];
  // 1. 立ち絵のコンタクトシート
  let navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&portraits` });
  await waitLine(/^portraits ready 12/, 120000, navAt);
  await move(5, 5);
  await sleep(2500);
  await shot('portraits_sheet');

  // 1b. 絵つきの小さなカードが多いデッキ編集（手持ち 50 種類）の FPS。終わったらデータを消して、以降は新しく始める
  {
    const all = ['glimmouse', 'dustwisp', 'coglet', 'emberkit', 'scalefry', 'pyxmoth', 'bolthare', 'rivetcrab', 'frostnewt', 'mirelamp', 'lynxlight', 'gearowl',
      'cinderdrake', 'hollowshade', 'stonetoad', 'velahound', 'clockwyrm', 'tidewraith', 'marshwyvern', 'voltsparrow', 'lupusfang', 'orreryx', 'gloamveil',
      'emberscale', 'corvusarc', 'aquilaburst', 'ironcetus', 'lanternmare', 'serpenscoil', 'rimeloom', 'leomane', 'forgecolossus', 'auroraveil', 'draconyx',
      'stormgear', 'cygnwraith', 'taurocrag', 'gravelith', 'voltwyrm', 'nocturnis', 'orionmane', 'astrolabion', 'cassiveil', 'scorpidrake', 'polarion',
      'andromede', 'pegasion', 'ophiwyrm', 'sextantor', 'ursamaw'].filter((id) => !STARTER.includes(id));
    const n0 = consoleLines.length;
    await send('Page.navigate', { url: `${BASE}?log&seed=${SEED}&speed=x2&grant=${all.join(',')}` });
    await waitLine(/^campaign load (\S+) nosave/, 120000, n0);
    await newGameIntro(n0, {});
    const mapAt = await waitScreen('Map', n0, 30000);
    await sleep(800);
    await clickUi('Map', 'Region1', lastLine(/^screen Map /).index);
    const rAt = await waitScreen('Region', mapAt);
    await sleep(400);
    await clickUi('Region', 'Challenge0', rAt);
    await waitScreen('Deck', rAt);
    await move(400, 500);
    await sleep(4000);
    await shot('L03b_deck_50_cards');
    const dAt = lastLine(/^screen Deck /).index;
    await clickUi('Deck', 'DeckBackButton', dAt);
    const fd = await waitLine(/^fps-deck (.*)/, 5000, dAt);
    results.fpsDeck50 = fd.line;
    const r2 = await waitScreen('Region', dAt);
    await clickUi('Region', 'RegionBackButton', r2);
    const m2 = await waitScreen('Map', r2);
    await sleep(300);
    await clickUi('Map', 'MapBackButton', m2);
    const tt = await waitScreen('Title', m2);
    await sleep(500);
    await openSettingsAndErase(tt, {});
  }

  // 2. 横: 新しく始める → 星図巡りの 1 局 → 図鑑
  navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${SEED}&speed=x1` });
  await waitLine(/^campaign load (\S+) nosave/, 120000, navAt);
  await newGameIntro(navAt, {});
  // 星図 → タイトル（campaignRound はタイトルから入る）
  const mAt = await waitScreen('Map', navAt, 30000);
  await sleep(500);
  await clickUi('Map', 'MapBackButton', mAt);
  const t1 = await waitScreen('Title', mAt, 10000);
  await sleep(600);
  const t2 = await campaignRound('L', 'Title', t1);

  // 3. 縦 360×640 でもう一度
  await sleep(600);
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  const pt = await waitScreen('Portrait', t2, 10000);
  await sleep(1200);
  await campaignRound('P', 'Portrait', pt);
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
}

// ---------------------------------------------------------------------------
// Phase 5c: 台詞（初回・再戦）、エンディング（勝敗を注入）、図鑑の未入手カードのクレジット、デッキ編集の FPS（50 種類）。
//   node tools/webgl_e2e.mjs --scenario 5c --serve Builds/WebGL --serve-port 8774 --out Logs/shots/5c
// A（横・x2）: 新しく始める → 図鑑で未入手のカードを拡大（クレジットが伏せてある）→ ミラと 1 局（ヒントの手で勝つまで）→ 報酬
//             → もう一度ミラに挑む（再戦の登場台詞）→ 設定でデータを消す。
// B（横・x1・?grant=全カード&unlock=all&forcewin=gate-elder）: デッキ編集（手持ち 50 種類）の FPS を計る → イオに挑む（初回の台詞）
//             → 勝ちを注入 → 報酬 → 主の台詞 → 帯 → エンディングの文 → クレジットが流れる → スキップ → 星図
//             → もう一度イオに勝つ（再戦の台詞。エンディングは出ない）。
// ---------------------------------------------------------------------------
const ALL_CARDS = ['glimmouse', 'dustwisp', 'coglet', 'emberkit', 'scalefry', 'pyxmoth', 'bolthare', 'rivetcrab', 'frostnewt', 'mirelamp', 'lynxlight', 'gearowl',
  'cinderdrake', 'hollowshade', 'stonetoad', 'velahound', 'clockwyrm', 'tidewraith', 'marshwyvern', 'voltsparrow', 'lupusfang', 'orreryx', 'gloamveil',
  'emberscale', 'corvusarc', 'aquilaburst', 'ironcetus', 'lanternmare', 'serpenscoil', 'rimeloom', 'leomane', 'forgecolossus', 'auroraveil', 'draconyx',
  'stormgear', 'cygnwraith', 'taurocrag', 'gravelith', 'voltwyrm', 'nocturnis', 'orionmane', 'astrolabion', 'cassiveil', 'scorpidrake', 'polarion',
  'andromede', 'pegasion', 'ophiwyrm', 'sextantor', 'ursamaw'];

/** 会話の line 行目（0 始まり）まで進めて撮る → スキップ。 */
async function talkShot(from, shotName, line = 1) {
  const d = await waitLine(/^dialogue 0\/(\d+)/, 20000, from);
  const at = (await waitLine(/^screen Dialogue /, 5000, d.index)).index;
  const n = +d.m[1];
  const target = Math.min(line, n - 1);
  for (let i = 0; i < target; i++) {
    await sleep(900);
    // 1 回目: 全文を出す、2 回目: 次の行へ
    const b = consoleLines.length;
    await key('Enter');
    await sleep(150);
    if (!consoleLines.slice(b).some((l) => new RegExp('^dialogue ' + (i + 1) + '/').test(l.line))) await key('Enter');
    await waitLine(new RegExp('^dialogue ' + (i + 1) + '/'), 5000, b);
  }
  await sleep(1800);
  await shot(shotName);
  await clickUi('Dialogue', 'SkipButton', at);
  const e = await waitLine(/^dialogue end/, 10000, d.index);
  return e.index + 1;
}

/** 星図から: 星域 region → 相手 opp → デッキ（onDeck）→ 挑む。挑んだ時点のログ位置を返す。 */
async function openDeck(region, opp, onDeck) {
  const mapAt = await waitScreen('Map', Math.max(0, consoleLines.length - 400));
  await sleep(600);
  await clickUi('Map', 'Region' + region, mapAt);
  const rAt = await waitScreen('Region', mapAt);
  await sleep(500);
  await clickUi('Region', 'Challenge' + opp, rAt);
  let dAt = await waitScreen('Deck', rAt);
  if (onDeck) dAt = await onDeck(dAt);
  await waitLine(/^deck listed /, 10000, dAt);
  await sleep(300);
  dAt = lastLine(/^screen Deck /, dAt).index;
  const ab = consoleLines.length;
  await clickUi('Deck', 'AutoButton', dAt);
  await waitLine(/^deck \S+ total \d+\/\d+ ready/, 5000, ab);
  await sleep(300);
  const talkFrom = consoleLines.length;
  await clickUi('Deck', 'BeginButton', dAt);
  return talkFrom;
}

async function run5c() {
  results.matches = [];
  results.rewards = [];
  const checks = {};

  // ---------------- A ----------------
  let navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${SEED}&speed=x2` });
  await waitLine(/^campaign load (\S+) nosave/, 120000, navAt);
  await newGameIntro(navAt, {});
  // 図鑑: 未入手のカードを拡大 → クレジットが伏せてある
  {
    const mAt = await waitScreen('Map', navAt, 30000);
    await sleep(500);
    await clickUi('Map', 'MapDexButton', mAt);
    const dl = await waitLine(/^dex list (landscape|portrait)/, 10000, mAt);
    await move(5, 5);
    await sleep(900);
    const cards = dexCards(Math.max(0, dl.index - 200));
    const unknownId = Object.keys(cards).find((id) => !STARTER.includes(id) && cards[id].y > 140 && cards[id].y < 600);
    const c = cards[unknownId];
    const zb = consoleLines.length;
    await click(c.x, c.y);
    const z = await waitLine(/^dex zoom (\S+)( unknown)?/, 5000, zb);
    checks.dexZoom = z.line;
    await sleep(1000);
    await shot('06_dex_unknown_zoom_credit_hidden');
    await key('Escape');
    await waitLine(/^dex list/, 5000, z.index);
    await sleep(300);
    await clickUi('Dex', 'DexBackButton', lastLine(/^screen Dex /).index);
    await waitScreen('Map', z.index, 10000);
  }
  // ミラと 1 局（勝つまで。初回の台詞はルールの行で撮る）
  let resAt = null;
  for (let attempt = 1; attempt <= 4 && !resAt; attempt++) {
    const talkFrom = await openDeck(1, 0, null);
    const talk = await waitLine(/^talk (first|rematch) harbor-apprentice/, 10000, talkFrom);
    if (attempt === 1) {
      checks.firstTalk = talk.line;
      await talkShot(talkFrom, '01_dialogue_first_mira', 1);
    } else await skipDialogue(talkFrom, null);
    await waitLine(/^campaign match /, 20000, talkFrom);
    const r = await playHinted(null);
    log(`match vs mira: ${r.score.join('-')} ${r.result} (${r.moves} moves)`);
    results.matches.push({ opp: 'mira', score: r.score, result: r.result });
    const rAt = await waitScreen('Result', r.index, 60000);
    await sleep(600);
    await clickUi('Result', 'NextButton', rAt);
    if (r.result === 'p0') { resAt = rAt; break; }
    const after = consoleLines.length;
    if (r.result === 'p1') await skipDialogue(after, null);
    await waitLine(/^campaign \S+ -> Map/, 20000, after);
  }
  if (!resAt) throw new Error('could not beat Mira');
  await takeReward(resAt, { owned: STARTER });
  // もう一度ミラ: 再戦の登場台詞
  {
    await waitScreen('Map', resAt, 30000);
    const talkFrom = await openDeck(1, 0, null);
    const talk = await waitLine(/^talk (first|rematch) harbor-apprentice/, 10000, talkFrom);
    checks.rematchTalk = talk.line;
    if (!/rematch/.test(talk.line)) throw new Error('expected a rematch talk: ' + talk.line);
    await talkShot(talkFrom, '02_dialogue_rematch_mira', 0);
  }

  // ---------------- B ----------------
  navAt = consoleLines.length;
  await send('Page.navigate', { url: `${BASE}?log&seed=${+SEED + 1}&speed=x1` });
  await waitLine(/^campaign load (\S+) save/, 120000, navAt);
  const tAt = await waitScreen('Title', navAt, 60000);
  await sleep(800);
  await openSettingsAndErase(tAt, {});
  navAt = consoleLines.length;
  const grant = ALL_CARDS.filter((id) => !STARTER.includes(id)).join(',');
  await send('Page.navigate', { url: `${BASE}?log&seed=${+SEED + 1}&speed=x1&grant=${grant}&unlock=all&forcewin=gate-elder` });
  await waitLine(/^campaign load (\S+) nosave/, 120000, navAt);
  await newGameIntro(navAt, {});
  // デッキ編集（50 種類）の FPS: 5b-2 と同じ計り方（開いて 4 秒、カーソルは一覧の上）
  {
    const mapAt = await waitScreen('Map', navAt, 30000);
    await sleep(1500); // 星図にいる間に小さなカードを作っておく
    await clickUi('Map', 'Region1', lastLine(/^screen Map /).index);
    const rAt = await waitScreen('Region', mapAt);
    await sleep(400);
    await clickUi('Region', 'Challenge0', rAt);
    const dAt = await waitScreen('Deck', rAt);
    await move(400, 500);
    await sleep(4000);
    await shot('07_deck_50_cards');
    const listed = await waitLine(/^deck listed (\d+)/, 1000, dAt);
    checks.deckListed = listed.line;
    await clickUi('Deck', 'DeckBackButton', lastLine(/^screen Deck /).index);
    const fd = await waitLine(/^fps-deck (.*)/, 5000, dAt);
    results.fpsDeck50 = fd.line;
    log('fps deck 50:', fd.line);
    const r2 = await waitScreen('Region', dAt);
    await sleep(300);
    // 2 回目に開いたとき（作り直しなし）
    await clickUi('Region', 'Challenge0', r2);
    const d2 = await waitScreen('Deck', r2);
    await move(400, 500);
    await sleep(4000);
    await clickUi('Deck', 'DeckBackButton', lastLine(/^screen Deck /).index);
    const fd2 = await waitLine(/^fps-deck (.*)/, 5000, d2);
    results.fpsDeck50Again = fd2.line;
    log('fps deck 50 (again):', fd2.line);
    const r3 = await waitScreen('Region', d2);
    await sleep(300);
    await clickUi('Region', 'RegionBackButton', r3);
    await waitScreen('Map', r3);
  }
  // イオ: 初回の台詞 → 勝ちを注入 → 報酬 → 主の台詞 → 帯 → エンディング → クレジット → 星図
  {
    const talkFrom = await openDeck(4, 2, null);
    const talk = await waitLine(/^talk (first|rematch) gate-elder/, 10000, talkFrom);
    checks.ioTalk = talk.line;
    const afterIntro = await talkShot(talkFrom, '03_dialogue_first_io', 1);
    const nextAt = await takeReward(afterIntro, { owned: ALL_CARDS });
    await talkShot(nextAt, '04a_dialogue_region_clear_io', 0);
    await waitLine(/^cleared shown/, 20000, nextAt);
    await sleep(400);
    await shot('04b_cleared_band_gate');
    const es = await waitLine(/^ending shown (\d+)/, 30000, nextAt);
    checks.ending = es.line;
    await sleep(900);
    await shot('04c_ending_text');
    await clickUi('Ending', 'EndingTap', es.index);
    const cr = await waitLine(/^credits rolling/, 60000, es.index);
    await sleep(1500);
    await shot('05a_credits_rolling');
    await sleep(3000);
    await shot('05b_credits_rolling_later');
    await clickUi('Credits', 'CreditsCloseButton', lastLine(/^screen Credits /, es.index).index);
    const end = await waitLine(/^credits roll end( skipped)?/, 10000, cr.index);
    checks.creditsEnd = end.line;
    await waitLine(/^campaign EndCredits -> Map/, 10000, cr.index);
    await sleep(800);
    await shot('05c_map_after_ending');
    checks.flow = consoleLines.slice(talkFrom).map((l) => l.line).filter((l) => /^campaign \S+ -> /.test(l));
  }
  // もう一度イオ: 再戦の台詞、エンディングは出ない
  {
    const talkFrom = await openDeck(4, 2, null);
    const talk = await waitLine(/^talk (first|rematch) gate-elder/, 10000, talkFrom);
    checks.ioRematch = talk.line;
    const afterIntro = await talkShot(talkFrom, '02b_dialogue_rematch_io', 0);
    const nextAt = await takeReward(afterIntro, { owned: ALL_CARDS });
    await waitLine(/^campaign Reward -> Map/, 20000, nextAt);
    await sleep(500);
    const lines = consoleLines.slice(talkFrom).map((l) => l.line);
    checks.secondWinEnding = lines.some((l) => /^ending begin|^credits roll begin|-> Ending/.test(l));
    if (checks.secondWinEnding) throw new Error('the ending played on the second win');
  }
  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
  results.checks = checks;
  log('checks', JSON.stringify(checks));
}

let exitCode = 0;
try {
  await connect();
  const { targetId } = await send('Target.createTarget', { url: 'about:blank' }, undefined);
  ({ sessionId } = await send('Target.attachToTarget', { targetId, flatten: true }, undefined));
  await send('Runtime.enable');
  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  if (args.scenario === 'title') {
    await runTitle();
    throw { done: true };
  }
  if (args.scenario === '5c') {
    await run5c();
    throw { done: true };
  }
  if (args.scenario === '5b2') {
    await run5b2();
    throw { done: true };
  }
  if (args.scenario === '5b1') {
    await run5b1();
    throw { done: true };
  }
  if (args.scenario === '4b') {
    await run4b();
    throw { done: true };
  }
  if (args.scenario === '4a') {
    await run4a();
    throw { done: true };
  }
  if (args.scenario === '3c') {
    await run3c();
    throw { done: true };
  }
  const url = `${BASE}?log&seed=${SEED}&slow=${SLOW}`;
  log('open', url);
  await send('Page.navigate', { url });

  // 1. タイトル
  await waitLine(/^screen Title/, 120000);
  await sleep(1500);
  await shot('01_title');
  await click(...Object.values(uiRect('Title', 'StartButton')));

  // 2. 対戦設定（既定: Open / Same / Plus / Combo / Pivot が ON）
  const setupAt = (await waitLine(/^screen Setup/)).index;
  await sleep(600);
  await shot('02_setup');
  await click(...Object.values(uiRect('Setup', 'BeginButton', setupAt)));

  // 3. 対戦開始
  await waitLine(/^match seed=/);
  await waitLine(/^await turn=|^step Place/, 60000);
  cursor = Math.max(0, cursor - 1);
  await sleep(300);
  await shot('03_match_start');

  // 4〜6, 9. 1 局を最後まで（マウスで手札を置く）
  let r = await playMatch({ wantPreview: true, wantFlip: true, wantRule: true, wantPivot: true, wantKeyboard: true, wantMenu: true, wantHint: true });
  log('match 1 finished', r.moves, 'moves', JSON.stringify(r.got));

  // 7. 結果画面
  const resAt = (await waitLine(/^screen Result/, 60000)).index;
  await sleep(500);
  await shot('07_result');

  // SAME / PLUS が出なかったら、seed を変えて出るまでもう一度（最大 4 局）
  let tries = 0;
  let resultIndex = resAt;
  while (!r.got.rule && tries < 4) {
    tries++;
    await click(...Object.values(uiRect('Result', 'AgainButton', resultIndex)));
    r = await playMatch({ wantRule: true });
    resultIndex = (await waitLine(/^screen Result/, 60000)).index;
    log('extra match', tries, JSON.stringify(r.got));
  }

  // 11. COMBO ×2 以上: ComboSeedFinder で見つけた seed で開き直し、同じ方針（Same/Plus → 枚数の多い手）で指す
  for (const cs of COMBO_SEEDS) {
    const navAt = consoleLines.length;
    log('combo game, seed', cs);
    await send('Page.navigate', { url: `${BASE}?log&seed=${cs}&slow=${SLOW}` });
    const tAt = (await waitLine(/^screen Title/, 120000, navAt)).index;
    await sleep(800);
    await click(...Object.values(uiRect('Title', 'StartButton', tAt)));
    const sAt2 = (await waitLine(/^screen Setup/, 10000, tAt)).index;
    await sleep(300);
    await click(...Object.values(uiRect('Setup', 'BeginButton', sAt2)));
    await waitLine(/^match seed=/, 10000, sAt2);
    const rc = await playMatch({ wantCombo: true, alwaysSP: true });
    resultIndex = (await waitLine(/^screen Result/, 60000)).index;
    log('combo game finished', JSON.stringify(rc.got));
    if (rc.got.combo) break;
  }

  // 8. 縦画面 360x640 で対戦中
  await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 640, deviceScaleFactor: 1, mobile: false });
  await sleep(800);
  // 縦画面に切り替わると、Unity がボタンの新しい位置をログに出す（screen Portrait）
  const portraitAt = (await waitLine(/^screen Portrait/, 10000, resultIndex)).index;
  // 縦画面の対戦設定で Open を OFF（CPU の手札が裏向き）、Elemental を ON（属性マス）にして始める
  await click(...Object.values(uiRect('Portrait', 'SettingsButton', portraitAt)));
  const ps = (await waitLine(/^screen Setup 360x640/, 10000)).index;
  await sleep(300);
  await click(...Object.values(uiRect('Setup', 'Rule_Open', ps)));
  await sleep(150);
  await click(...Object.values(uiRect('Setup', 'Rule_Elemental', ps)));
  await sleep(300);
  await shot('x_setup_portrait');
  await click(...Object.values(uiRect('Setup', 'BeginButton', ps)));
  // 1 手指してから撮る
  const pt = await readTurn();
  await sleep(600);
  const o = [...pt.options].sort((a, b) => b.flips - a.flips)[0];
  await move(pt.hand[o.h].x, pt.hand[o.h].y);
  await mouse('mousePressed', pt.hand[o.h].x, pt.hand[o.h].y);
  await drag(pt.hand[o.h].x, pt.hand[o.h].y, pt.cells[o.c].x, pt.cells[o.c].y, 10);
  await sleep(300);
  await mouse('mouseReleased', pt.cells[o.c].x, pt.cells[o.c].y);
  await waitLine(/^await turn=/, 120000);
  await sleep(500);
  await shot('08_portrait_360x640');

  // 13. 縦画面の手札を拡大（数字の読みやすさの確認。360x640 の実際の画素を 3 倍に拡大）
  {
    let ys = [];
    for (let i = consoleLines.length - 1; i >= 0; i--) {
      const l = consoleLines[i].line;
      const m = l.match(/^hand \d+ (-?\d+) (-?\d+)/);
      if (m) ys.push(+m[2]);
      if (/^await turn=/.test(l)) break;
    }
    const cy = ys.length ? ys.reduce((a, b) => a + b, 0) / ys.length : 560;
    const r2 = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: Math.max(0, cy - 56), width: 360, height: 112, scale: 3 } });
    writeFileSync(join(OUT, '13_card_closeup.png'), Buffer.from(r2.data, 'base64'));
    log('shot 13_card_closeup');
  }

  results.fps = consoleLines.filter((l) => /^fps/.test(l.line)).map((l) => l.line);
} catch (e) {
  if (!e?.done) {
    exitCode = 1;
    console.error('[e2e] FAILED', e.message);
  }
} finally {
  results.errors = consoleLines.filter((l) => /^(CONSOLE|EXCEPTION)/.test(l.line)).map((l) => l.line);
  writeFileSync(join(OUT, 'e2e_log.txt'), consoleLines.map((l) => l.line).join('\n'));
  writeFileSync(join(OUT, 'e2e_console_head.txt'), rawConsole.join('\n'));
  writeFileSync(join(OUT, 'e2e_summary.json'), JSON.stringify(results, null, 2));
  try { await send('Browser.close', {}, undefined); } catch { }
  try { ws?.close(); } catch { }
  await sleep(500);
  try { chrome.kill(); } catch { }
  try { if (server) { server.closeAllConnections?.(); await new Promise((r) => server.close(r)); } } catch { }
  try { rmSync(profile, { recursive: true, force: true }); } catch { }
  log('done', exitCode === 0 ? 'OK' : 'FAILED');
  process.exit(exitCode);
}
