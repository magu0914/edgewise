// game-icons.net の SVG（白い図形・黒い背景）を、白い図形だけのマスク PNG（グレースケール、既定 512px）に変換する（DESIGN §7-1 パイプライン 1）。
// 新しいパッケージを入れずに済むよう、E2E と同じヘッドレス Chrome（専用の一時プロファイル・ウィンドウなし・GPU なしの CPU ラスタライズ）の
// canvas に描いて書き出す。Chrome は終了時に必ず閉じる（例外時も）。
//
// 使い方:
//   マスク:   node tools/rasterize_icons.mjs --src Assets/Art/Icons/Source --list <author/name を 1 行ずつ> --out Assets/Art/Icons/Masks
//   見比べ用: node tools/rasterize_icons.mjs --src <icons-master> --list <list> --sheet out.png [--cols 10] [--cell 128]
// 出力: <out>/<author>/<name>.png（R=G=B=白い図形の被覆率、A=255）。同じ SVG・同じ Chrome なら同じ PNG になる。
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, rmSync, mkdtempSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, arr) => (v.startsWith('--') ? [...a, [v.slice(2), arr[i + 1]]] : a), []));
const SRC = resolve(args.src ?? 'Assets/Art/Icons/Source');
const LIST = args.list;
const OUT = args.out ? resolve(args.out) : null;
const SHEET = args.sheet ? resolve(args.sheet) : null;
const SIZE = Number(args.size ?? 512);
const COLS = Number(args.cols ?? 10);
const CELL = Number(args.cell ?? 128);
const CHROME = args.chrome ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = Number(args.port ?? 9335);
if (!LIST || (!OUT && !SHEET)) {
  console.error('usage: --src <dir> --list <file> (--out <dir> | --sheet <png>)');
  process.exit(2);
}

const names = readFileSync(LIST, 'utf8').split(/\r?\n/).map((l) => l.replace(/#.*/, '').trim()).filter(Boolean);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[raster]', ...a);

const profile = mkdtempSync(join(tmpdir(), 'edgewise-raster-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
  '--disable-gpu', '--disable-extensions', '--mute-audio', '--hide-scrollbars', 'about:blank',
], { stdio: 'ignore', windowsHide: true });

let ws;
let nextId = 1;
let sessionId;
const pending = new Map();
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
        }
      };
      return;
    } catch {
      await sleep(200);
    }
  }
  throw new Error('could not connect to Chrome');
}

async function evaluate(expression) {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error('page error: ' + JSON.stringify(r.exceptionDetails).slice(0, 500));
  return r.result.value;
}

// ページ側: SVG を黒の上に描き、明るさ（max(R,G,B)）をグレースケールにする。白い図形だけが残る（背景の正方形は黒）。
const PAGE_LIB = `
window.__loadSvg = (text, size) => new Promise((res, rej) => {
  const img = new Image(size, size);
  img.onload = () => res(img);
  img.onerror = () => rej(new Error('svg load failed'));
  img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(text)));
});
window.__mask = async (text, size) => {
  const img = await window.__loadSvg(text, size);
  const c = document.createElement('canvas');
  c.width = size; c.height = size;
  const g = c.getContext('2d', { willReadFrequently: true });
  g.fillStyle = '#000'; g.fillRect(0, 0, size, size);
  g.drawImage(img, 0, 0, size, size);
  const d = g.getImageData(0, 0, size, size);
  for (let i = 0; i < d.data.length; i += 4) {
    const v = Math.max(d.data[i], d.data[i + 1], d.data[i + 2]);
    d.data[i] = v; d.data[i + 1] = v; d.data[i + 2] = v; d.data[i + 3] = 255;
  }
  g.putImageData(d, 0, 0);
  return c.toDataURL('image/png').split(',')[1];
};
window.__sheet = async (items, cols, cell) => {
  const label = 18;
  const rows = Math.ceil(items.length / cols);
  const c = document.createElement('canvas');
  c.width = cols * cell; c.height = rows * (cell + label);
  const g = c.getContext('2d');
  g.fillStyle = '#10131f'; g.fillRect(0, 0, c.width, c.height);
  for (let i = 0; i < items.length; i++) {
    const x = (i % cols) * cell, y = Math.floor(i / cols) * (cell + label);
    try {
      const img = await window.__loadSvg(items[i].text, cell);
      g.drawImage(img, x + 2, y + 2, cell - 4, cell - 4);
    } catch (e) { }
    g.fillStyle = '#ffd27a'; g.font = '11px sans-serif';
    g.fillText(items[i].name.slice(0, 22), x + 3, y + cell + 13);
  }
  return c.toDataURL('image/png').split(',')[1];
};
1`;

let exitCode = 0;
try {
  await connect();
  const { targetId } = await send('Target.createTarget', { url: 'about:blank' }, undefined);
  ({ sessionId } = await send('Target.attachToTarget', { targetId, flatten: true }, undefined));
  await send('Runtime.enable');
  await evaluate(PAGE_LIB);

  if (SHEET) {
    const items = [];
    for (const n of names) {
      const p = join(SRC, n + '.svg');
      if (!existsSync(p)) { log('missing', n); continue; }
      items.push({ name: n.split('/').pop(), text: readFileSync(p, 'utf8') });
    }
    const b64 = await evaluate(`window.__sheet(${JSON.stringify(items)}, ${COLS}, ${CELL})`);
    mkdirSync(dirname(SHEET), { recursive: true });
    writeFileSync(SHEET, Buffer.from(b64, 'base64'));
    log('sheet', SHEET, items.length, 'icons');
  } else {
    let n = 0;
    for (const name of names) {
      const p = join(SRC, name + '.svg');
      if (!existsSync(p)) throw new Error('missing svg: ' + p);
      const b64 = await evaluate(`window.__mask(${JSON.stringify(readFileSync(p, 'utf8'))}, ${SIZE})`);
      const outPath = join(OUT, name + '.png');
      mkdirSync(dirname(outPath), { recursive: true });
      writeFileSync(outPath, Buffer.from(b64, 'base64'));
      n++;
    }
    log('masks', n, '->', OUT);
  }
} catch (e) {
  exitCode = 1;
  console.error('[raster] FAILED', e.message);
} finally {
  try { await send('Browser.close', {}, undefined); } catch { }
  try { ws?.close(); } catch { }
  await sleep(300);
  try { chrome.kill(); } catch { }
  await sleep(200);
  try { rmSync(profile, { recursive: true, force: true }); } catch { }
  process.exit(exitCode);
}
