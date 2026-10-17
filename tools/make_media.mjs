// プレイ動画（MP4）と GIF を作る（DESIGN §14-2。Phase 8a）。
// 素材は `node tools/webgl_e2e.mjs --scenario media ...` が撮った連番の JPEG と media.json（区間の印と、ゲームのログの時刻）。
//   node tools/make_media.mjs --in Logs/media8a --mp4 Logs/media8a/edgewise_play.mp4 --gif docs/media/edgewise.gif
// MP4（30〜60 秒）: タイトルの扇 → 対戦の後半（SAME / PLUS → COMBO）→ 報酬の NEW! → 巡り終えた帯 → 星図の鍵が外れる。
// GIF（10 秒前後・幅 640px・5MB 以下）: 同じ流れを短く切ったもの。5MB を超えたら fps と色数を下げて作り直す。
// ffmpeg は PATH にあるものを使う。
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, statSync, mkdirSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, arr) => (v.startsWith('--') ? [...a, [v.slice(2), arr[i + 1]]] : a), []));
const IN = resolve(args.in ?? 'Logs/media8a');
const MP4 = resolve(args.mp4 ?? join(IN, 'edgewise_play.mp4'));
const GIF = resolve(args.gif ?? 'docs/media/edgewise.gif');
const GIF_LIMIT = 5 * 1024 * 1024;

const media = JSON.parse(readFileSync(join(IN, 'media.json'), 'utf8'));
const frames = media.frames;
const markAt = (name) => {
  const m = media.marks.find((x) => x.name === name);
  if (!m) throw new Error('no mark ' + name);
  return m.t;
};
const logAt = (re, after = 0) => {
  const l = media.consoleTimes.find((x) => re.test(x.line) && x.t >= after);
  if (!l) throw new Error('no log ' + re);
  return l.t;
};

const combo = media.combo;
const tTitle = markAt('title');
const tRewardShown = logAt(/^reward shown/);
const tReveal = logAt(/^reward reveal \S+ new/, tRewardShown);
const tCleared = logAt(/^cleared shown/, tReveal);
const tUnlockDone = logAt(/^map unlock library done/, tCleared);
const tMapEnd = markAt('map_end');

/** 区間 [a, b]（ms）。 */
const seg = (a, b, label) => ({ a, b, label });

// MP4: 約 40 秒
const mp4Segments = [
  seg(tTitle, tTitle + 6000, 'title'),
  seg(combo.rule - 11000, combo.combo + 4000, 'match-combo'),
  seg(tRewardShown - 300, tReveal + 3500, 'reward'),
  seg(tCleared - 300, tMapEnd, 'cleared-map'),
];
// GIF: 約 11 秒
const gifSegments = [
  seg(tTitle + 1000, tTitle + 3000, 'title'),
  seg(combo.rule - 1200, combo.combo + 2300, 'combo'),
  seg(tReveal - 1300, tReveal + 1700, 'reward'),
  seg(tUnlockDone - 1400, tUnlockDone + 500, 'map'),
];

function concatList(segments, path) {
  const lines = ['ffconcat version 1.0'];
  let total = 0;
  for (const s of segments) {
    const fs = frames.filter((f) => f.t >= s.a && f.t <= s.b);
    if (fs.length < 2) throw new Error('too few frames in ' + s.label);
    for (let i = 0; i < fs.length; i++) {
      const next = i + 1 < fs.length ? fs[i + 1].t : fs[i].t + 33;
      const d = Math.min(0.2, Math.max(0.005, (next - fs[i].t) / 1000));
      total += d;
      lines.push(`file 'frames/${fs[i].file}'`, `duration ${d.toFixed(4)}`);
    }
    console.log(`[media] ${s.label}: ${fs.length} frames, ${((s.b - s.a) / 1000).toFixed(1)} s`);
  }
  // concat demuxer は最後の duration を使わないので、最後のファイルをもう一度書く
  lines.push(lines[lines.length - 2]);
  writeFileSync(path, lines.join('\n') + '\n');
  return total;
}

function ffmpeg(argv) {
  const r = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...argv], { stdio: 'inherit' });
  if (r.status !== 0) throw new Error('ffmpeg failed: ' + argv.join(' '));
}

const mp4List = join(IN, 'mp4.ffconcat');
const mp4Len = concatList(mp4Segments, mp4List);
mkdirSync(dirname(MP4), { recursive: true });
ffmpeg(['-f', 'concat', '-safe', '0', '-i', mp4List, '-vf', 'fps=30,scale=1280:720:flags=lanczos,format=yuv420p', '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-movflags', '+faststart', MP4]);
console.log(`[media] MP4 ${MP4} ${mp4Len.toFixed(1)} s, ${(statSync(MP4).size / 1048576).toFixed(2)} MB`);

const gifList = join(IN, 'gif.ffconcat');
const gifLen = concatList(gifSegments, gifList);
mkdirSync(dirname(GIF), { recursive: true });
for (const [fps, colors] of [[15, 160], [12, 128], [10, 96], [8, 64]]) {
  const vf = `fps=${fps},scale=640:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=${colors}:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`;
  ffmpeg(['-f', 'concat', '-safe', '0', '-i', gifList, '-vf', vf, '-loop', '0', GIF]);
  const size = statSync(GIF).size;
  console.log(`[media] GIF ${fps} fps / ${colors} colors: ${(size / 1048576).toFixed(2)} MB, ${gifLen.toFixed(1)} s`);
  if (size <= GIF_LIMIT) break;
}
if (statSync(GIF).size > GIF_LIMIT) throw new Error('GIF is larger than 5 MB');

// 代表のスクリーンショット（README 用。JPEG・1280×720）を docs/media へ:
//   title.jpg（タイトルの扇）/ combo.jpg（COMBO の文字が出ている瞬間。連番から選ぶ）/ reward_new.jpg（NEW!）/ map_unlocked.jpg（星図の鍵が外れた後）
const STILLS = resolve(args.stills ?? dirname(GIF));
const nearest = (t) => frames.reduce((best, f) => (Math.abs(f.t - t) < Math.abs(best.t - t) ? f : best), frames[0]);
const comboFrame = nearest(combo.combo + Number(args['combo-offset'] ?? 550));
const stills = [
  [join(IN, 'title.jpg'), 'title.jpg'],
  [join(IN, 'frames', comboFrame.file), 'combo.jpg'],
  [join(IN, 'reward_new.png'), 'reward_new.jpg'],
  [join(IN, 'map_unlocked.jpg'), 'map_unlocked.jpg'],
];
for (const [src, name] of stills) {
  const dst = join(STILLS, name);
  ffmpeg(['-i', src, '-q:v', '3', dst]);
  console.log(`[media] still ${name}: ${(statSync(dst).size / 1024).toFixed(0)} KB`);
}
