// WebGL 版の E2E: ヘッドレス Chrome（専用の一時プロファイル・ウィンドウなし）を CDP で操作して 1 局を最後まで遊び、
// スクリーンショットを Logs/shots/3a/ に保存する。Unity 側は URL の ?log で操作用のログ（[EW] ...）を console に出す。
//
// 使い方（先に Builds/WebGL を HTTP で配信しておく）:
//   node tools/webgl_e2e.mjs --url http://127.0.0.1:8765/ --out Logs/shots/3a [--seed 7] [--slow 4]
// 終了時に Chrome を必ず終了する（例外時も）。
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, arr) => (v.startsWith('--') ? [...a, [v.slice(2), arr[i + 1]]] : a), []));
const BASE = args.url ?? 'http://127.0.0.1:8765/';
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

async function playMatch({ wantPreview, wantFlip, wantRule, wantPivot, wantKeyboard, wantMenu, wantHint, wantCombo, alwaysSP }) {
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

let exitCode = 0;
try {
  await connect();
  const { targetId } = await send('Target.createTarget', { url: 'about:blank' }, undefined);
  ({ sessionId } = await send('Target.attachToTarget', { targetId, flatten: true }, undefined));
  await send('Runtime.enable');
  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
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
  exitCode = 1;
  console.error('[e2e] FAILED', e.message);
} finally {
  results.errors = consoleLines.filter((l) => /^(CONSOLE|EXCEPTION)/.test(l.line)).map((l) => l.line);
  writeFileSync(join(OUT, 'e2e_log.txt'), consoleLines.map((l) => l.line).join('\n'));
  writeFileSync(join(OUT, 'e2e_console_head.txt'), rawConsole.join('\n'));
  writeFileSync(join(OUT, 'e2e_summary.json'), JSON.stringify(results, null, 2));
  try { await send('Browser.close', {}, undefined); } catch { }
  try { ws?.close(); } catch { }
  await sleep(500);
  try { chrome.kill(); } catch { }
  try { rmSync(profile, { recursive: true, force: true }); } catch { }
  log('done', exitCode === 0 ? 'OK' : 'FAILED');
  process.exit(exitCode);
}
