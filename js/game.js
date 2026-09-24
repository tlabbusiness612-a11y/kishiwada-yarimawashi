// ゲーム本体：綱先（群衆）が道なりに地車を曳き、前梃子で向きを変える「やりまわし」
import * as THREE from 'three';
import { Sky } from 'three/addons/objects/Sky.js';
import { clamp, wrapAngle, makeTextures, personGeos } from './util.js';
import { buildDanjiri } from './danjiri.js';
import { buildTown } from './town.js';
import { SEG, COURSE, project, pointAt } from './course.js';
import { Narimono } from './audio.js';

const $ = (id) => document.getElementById(id);
const HAPPI = [{ name: '紺', c: 0x1f2e5a }, { name: '朱', c: 0xb8321f }, { name: '萌黄', c: 0x5d7a2a }, { name: '江戸紫', c: 0x5b3a6e }, { name: '焦茶', c: 0x5a3a22 }];
export const TUNE = { grip: 4.2, slide: 0.4, rope: 0.03, ropeT: 0.06, maeYaw: 1.25, maeBrake: 2.1, tmax: 4.2 };
const VMAX = 8.4, ROPE_L = 34, K_T = 1.9, K_C = 2.6, BODY_R = 1.2, BODY_OFF = [-1.35, 0, 1.35], NR = 36;
const BEST_KEY = 'kishiwada-yarimawashi-best-v2';

// ---------- 描画 ----------
const canvas = $('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
let pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
renderer.setPixelRatio(pixelRatio);
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 0.95;
renderer.shadowMap.enabled = true; renderer.shadowMap.type = THREE.PCFSoftShadowMap;
const scene = new THREE.Scene();
scene.fog = new THREE.Fog(0xd9d4c8, 160, 900);
const camera = new THREE.PerspectiveCamera(58, 1, 0.3, 4000);

// 空と太陽（九月の午後、南西の日差し）
const SUN = new THREE.Vector3().setFromSphericalCoords(1, THREE.MathUtils.degToRad(90 - 34), THREE.MathUtils.degToRad(-128));
const sky = new Sky(); sky.scale.setScalar(3000);
Object.assign(sky.material.uniforms, {});
sky.material.uniforms.turbidity.value = 6; sky.material.uniforms.rayleigh.value = 1.4;
sky.material.uniforms.mieCoefficient.value = 0.006; sky.material.uniforms.mieDirectionalG.value = 0.82;
sky.material.uniforms.sunPosition.value.copy(SUN);
scene.add(sky);
{
  const pm = new THREE.PMREMGenerator(renderer), envScene = new THREE.Scene(), s2 = new Sky();
  s2.scale.setScalar(1000); for (const k in sky.material.uniforms) s2.material.uniforms[k].value = sky.material.uniforms[k].value;
  envScene.add(s2); scene.environment = pm.fromScene(envScene, 0.04).texture; pm.dispose();
}
scene.add(new THREE.HemisphereLight(0xdfe8f2, 0x8c7a62, 1.25));
const sun = new THREE.DirectionalLight(0xffe4bd, 2.6);
const coarse = matchMedia('(pointer: coarse)').matches;
sun.castShadow = true; sun.shadow.mapSize.set(coarse ? 1024 : 2048, coarse ? 1024 : 2048); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03;
Object.assign(sun.shadow.camera, { left: -45, right: 45, top: 45, bottom: -45, near: 1, far: 400 });
scene.add(sun, sun.target);

const tex = makeTextures();
const audio = new Narimono();
let town, segs, grid, CELL;

// 綱と曳き手
const ropeGeo = new THREE.CylinderGeometry(0.045, 0.045, 1, 6, 1, true); ropeGeo.translate(0, 0.5, 0);
const ropeMesh = new THREE.InstancedMesh(ropeGeo, new THREE.MeshStandardMaterial({ color: 0xd9c9a3, roughness: 0.9 }), (NR - 1) * 2);
ropeMesh.frustumCulled = false; ropeMesh.castShadow = true; scene.add(ropeMesh);
const pg = personGeos(), NP = (NR - 3) * 2 + 4;
const lam = (o) => new THREE.MeshLambertMaterial(Object.assign({ vertexColors: true }, o));
const pullT = new THREE.InstancedMesh(pg.torso, lam({}), NP), pullL = new THREE.InstancedMesh(pg.legs, lam({ color: 0xe9e5dc }), NP), pullH = new THREE.InstancedMesh(pg.headBand, lam({}), NP);
for (const m of [pullT, pullL, pullH]) { m.frustumCulled = false; m.castShadow = true; m.instanceMatrix.setUsage(THREE.DynamicDrawUsage); scene.add(m); }

// ---------- 当たり判定（線分） ----------
function pushCircle(x, z, r) {
  let hx = 0, hz = 0, hit = false;
  const gx0 = Math.floor((x - r) / CELL), gx1 = Math.floor((x + r) / CELL), gz0 = Math.floor((z - r) / CELL), gz1 = Math.floor((z + r) / CELL);
  const seen = new Set();
  for (let gx = gx0; gx <= gx1; gx++) for (let gz = gz0; gz <= gz1; gz++) {
    const list = grid.get(gx + ',' + gz); if (!list) continue;
    for (const i of list) {
      if (seen.has(i)) continue; seen.add(i);
      const s = segs[i], ex = s[2] - s[0], ez = s[3] - s[1], l2 = ex * ex + ez * ez || 1;
      const t = clamp(((x - s[0]) * ex + (z - s[1]) * ez) / l2, 0, 1), qx = s[0] + ex * t, qz = s[1] + ez * t;
      const dx = x - qx, dz = z - qz, d2 = dx * dx + dz * dz;
      if (d2 >= r * r || d2 < 1e-12) continue;
      const d = Math.sqrt(d2), pen = r - d, nx = dx / d, nz = dz / d;
      x += nx * pen; z += nz * pen; hx += nx * pen; hz += nz * pen; hit = true;
    }
  }
  return { x, z, hx, hz, hit };
}

// ---------- 状態 ----------
const input = { mL: 0, mR: 0 };
let mode = 'loading', camMode = 0, dj = null, st = null, modeT = 0;
const cfg = { town: 'わが町', color: HAPPI[0].c };
const fwd = (h) => ({ x: Math.sin(h), z: Math.cos(h) });

function reset() {
  const s0 = COURSE.start;
  st = {
    x: s0.x, z: s0.z, h: s0.h, vx: 0, vz: 0, w: 0, pitch: 0, hx: 0, hz: 0, hh: s0.h, hv: 0, trail: [],
    tempo: 0, combo: 0, s: 0, time: 0, clock: 0, score: 0, jumpAt: -9, lastHit: -9, shake: 0,
    cur: null, results: [], mae: [0, 0], timeBonus: 0, stuck: 0, back: 0,
  };
  const f = fwd(s0.h), ax = s0.x + f.x * 2, az = s0.z + f.z * 2;
  for (let d = 0; d <= ROPE_L; d += 0.5) st.trail.push({ x: ax + f.x * d, z: az + f.z * d });
  const hd = st.trail[st.trail.length - 1]; st.hx = hd.x; st.hz = hd.z;
  cam.dx = f.x; cam.dz = f.z; cam.init = false;
}
function trailPoint(s) {
  const tr = st.trail; let a = 0;
  for (let i = tr.length - 1; i > 0; i--) {
    const p = tr[i], q = tr[i - 1], seg = Math.hypot(p.x - q.x, p.z - q.z);
    if (a + seg >= s) { const t = seg > 0 ? (s - a) / seg : 0; return { x: p.x + (q.x - p.x) * t, z: p.z + (q.z - p.z) * t, i }; }
    a += seg;
  }
  return { x: tr[0].x, z: tr[0].z, i: 1 };
}

// 群衆の通り道のうち (x,z) に一番近い点。s は綱先からの道のり
function ropeClosest(x, z) {
  const tr = st.trail; let best = { d: 1e9, s: 0, x: tr[0].x, z: tr[0].z, i: 1 }, acc = 0;
  for (let i = tr.length - 1; i > 0; i--) {
    const p = tr[i], q = tr[i - 1], ex = q.x - p.x, ez = q.z - p.z, l = Math.hypot(ex, ez) || 1e-6;
    const t = clamp(((x - p.x) * ex + (z - p.z) * ez) / (l * l), 0, 1), cx = p.x + ex * t, cz = p.z + ez * t, d = Math.hypot(x - cx, z - cz);
    if (d < best.d) best = { d, s: acc + t * l, x: cx, z: cz, i };
    acc += l; if (acc > ROPE_L + 30) break;
  }
  return best;
}

// ---------- 物理（1/120 秒） ----------
function step(dt) {
  const running = mode === 'run';
  const f = fwd(st.h), left = { x: Math.cos(st.h), z: -Math.sin(st.h) };
  st.tempo = Math.max(0, st.tempo - (0.1 + 0.2 * st.tempo) * dt);
  // 前梃子（左右）。押している間 1 に近づく
  for (let k = 0; k < 2; k++) { const want = running ? (k ? input.mR : input.mL) : mode === 'finish' ? 1 : 0; st.mae[k] += (want - st.mae[k]) * Math.min(1, 14 * dt); }
  const mL = st.mae[0], mR = st.mae[1], mae = Math.max(mL, mR);

  // 綱先は道なりに走る（角は自分たちで先に曲がる）
  if (running || mode === 'finish') {
    const pr = project(st.hx, st.hz), tg = pointAt(pr.s + 10);
    const e = wrapAngle(Math.atan2(tg.x - st.hx, tg.z - st.hz) - st.hh);
    st.hh += clamp(e, -1.1 * dt, 1.1 * dt);
  }
  const vD = Math.hypot(st.vx, st.vz);
  const vT = running ? st.tempo * VMAX : 0;
  st.hv += clamp(vT - st.hv, -4.5 * dt, 3.2 * dt);

  // 綱の張り：結び目から群衆の通り道（綱）までの距離＋そこから綱先までの長さが、綱の長さを超えた分
  const A = { x: st.x + f.x * 2, z: st.z + f.z * 2 };
  const C = ropeClosest(A.x, A.z), stretch = C.d + C.s - ROPE_L;
  const Aim = trailPoint(Math.max(0, C.s - 10));
  let tension = 0;
  if (stretch > 0) { tension = Math.min(TUNE.tmax, K_T * stretch); st.hv -= K_C * Math.min(stretch, 4) * dt; }
  else st.hv = Math.max(st.hv, vD * 1.02);
  if (stretch > 4) st.hv = Math.min(st.hv, vD);
  st.hv = Math.max(0, st.hv);
  st.ropeC = C;
  const hd = fwd(st.hh), hp = pushCircle(st.hx + hd.x * st.hv * dt, st.hz + hd.z * st.hv * dt, 1.2);
  st.hx = hp.x; st.hz = hp.z;
  const tr = st.trail, last = tr[tr.length - 1], prev = tr[tr.length - 2];
  if (Math.hypot(st.hx - prev.x, st.hz - prev.z) > 0.5) tr.push({ x: st.hx, z: st.hz }); else { last.x = st.hx; last.z = st.hz; }
  if (tr.length > 180) tr.splice(0, tr.length - 180);

  // 力：綱の張り・転がり抵抗・前梃子のブレーキ
  let ux = Aim.x - A.x, uz = Aim.z - A.z; const ul = Math.hypot(ux, uz) || 1; ux /= ul; uz /= ul;
  if (st.back > 0) tension = 0; // 押し戻す間は綱を緩める
  st.vx += ux * tension * dt; st.vz += uz * tension * dt;
  const v = Math.hypot(st.vx, st.vz), res = 0.26 + 0.012 * v * v + (mL + mR) * TUNE.maeBrake;
  if (v > 1e-4) { const dv = Math.min(v, res * dt); st.vx -= st.vx / v * dv; st.vz -= st.vz / v * dv; }
  // 横滑り（前梃子で片輪を止めると後ろが振れる）
  const vl = st.vx * left.x + st.vz * left.z, grip = TUNE.grip * (1 - TUNE.slide * mae) * (st.back > 0 || (vD < 1.5 && tension > 2) ? 0.35 : 1) * dt, cut = clamp(vl, -grip, grip);
  st.vx -= left.x * cut; st.vz -= left.z * cut;
  // 向き：綱に引かれる分はわずか。前梃子を入れた側へ大きく回る
  const vf = st.vx * f.x + st.vz * f.z;
  const err = Math.atan2(ux * left.x + uz * left.z, ux * f.x + uz * f.z);
  let wt = TUNE.rope * Math.max(0, vf) * Math.sin(clamp(err, -1.3, 1.3)) * (0.35 + 0.65 * clamp(tension / 1.2, 0, 1));
  wt += (mL - mR) * Math.min(TUNE.maeYaw, 0.15 + Math.abs(vf) / 3.2);
  wt += TUNE.ropeT * tension * Math.sin(clamp(err, -1.4, 1.4));
  if (running && vD < 1.2 && tension > 0.6) wt += clamp(err, -1, 1) * 0.9 * (1 - vD / 1.2); // 止まったら曳き手が向きを直す
  // 家や桟敷に突っかかって動けない時は、後梃子の衆が後ろへ押し戻す
  st.stuck = running && st.back <= 0 && vD < 0.5 && tension > 1.2 ? st.stuck + dt : 0;
  if (st.stuck > 1.6) { st.back = 1.1; st.stuck = 0; if (mode === 'run') callout('押し戻せ！', '後梃子の衆が後ろへ', 1.2); }
  if (st.back > 0) { st.back -= dt; st.vx -= f.x * 2.2 * dt; st.vz -= f.z * 2.2 * dt; wt += clamp(err, -1, 1) * 0.6; }
  st.w += (wt - st.w) * Math.min(1, 3.4 * dt);
  st.h += st.w * dt;
  st.x += st.vx * dt; st.z += st.vz * dt;
  collideBody();
  st.pitch *= Math.exp(-6 * dt);
  if (running) st.time += dt;
  st.clock += dt;
}
function collideBody() {
  const f = fwd(st.h); let impact = 0;
  for (const off of BODY_OFF) {
    const r = pushCircle(st.x + f.x * off, st.z + f.z * off, BODY_R);
    if (!r.hit) continue;
    st.x += r.hx; st.z += r.hz;
    const len = Math.hypot(r.hx, r.hz) || 1, nx = r.hx / len, nz = r.hz / len, vn = st.vx * nx + st.vz * nz;
    if (vn < 0) { st.vx -= nx * vn * 1.12; st.vz -= nz * vn * 1.12; st.vx *= 0.93; st.vz *= 0.93; st.w += off * (f.x * nz - f.z * nx) * vn * 0.12; impact = Math.max(impact, -vn); }
  }
  if (impact > 0.9) onHit(impact);
}

// ---------- 採点 ----------
function onHit(impact) {
  if (st.clock - st.lastHit < 0.6) return;
  st.lastHit = st.clock; st.shake = Math.min(1.2, 0.3 + impact * 0.18); st.pitch = -0.05 * Math.min(1, impact / 4);
  audio.thud(impact / 5);
  if (st.cur) { st.cur.hits++; st.cur.pen += Math.round(40 + impact * 30); } else st.score = Math.max(0, st.score - Math.round(impact * 10));
  showBang(impact > 3.5 ? 'ドーン！' : 'ゴツン');
}
function track(dt) {
  const pr = project(st.x, st.z); st.s = Math.max(st.s, pr.s);
  const v = Math.hypot(st.vx, st.vz);
  COURSE.corners.forEach((cn, k) => {
    if (st.results[k]) return;
    if (!st.cur && pr.s > cn.s - 26 && pr.s < cn.s) st.cur = { k, entry: v, min: v, sum: 0, t: 0, hits: 0, pen: 0, jump: false, h0: SEG[k].h, tA: -1, tB: -1 };
    if (st.cur && st.cur.k === k) {
      const c = st.cur; c.min = Math.min(c.min, v); c.sum += v * dt; c.t += dt;
      const turned = Math.abs(wrapAngle(st.h - c.h0));
      if (c.tA < 0 && turned > 0.26) c.tA = c.t;
      if (c.tB < 0 && turned > 1.2) c.tB = c.t;
      if (pr.s > cn.s + (k ? 14 : 22) && pr.seg === SEG[k + 1]) finishCorner(c);
    }
  });
  if (mode === 'run' && pr.s > COURSE.finishS) finishRun();
}
function finishCorner(c) {
  const entry = c.entry * 3.6, avg = (c.sum / Math.max(c.t, 0.01)) * 3.6, keep = c.min / Math.max(c.entry, 0.5);
  const turnT = c.tA >= 0 && c.tB >= 0 ? c.tB - c.tA : 4, kire = clamp((3.0 - turnT) / 2.1, 0, 1);
  const pts = Math.max(0, Math.round(entry * 7 + avg * 8 + Math.min(1, keep) * 120 + kire * 170 + (c.hits ? 0 : 90) + (c.jump ? 110 : 0) - c.pen));
  const g = pts >= 800 ? '天晴れ' : pts >= 640 ? '見事' : pts >= 460 ? 'ええで' : pts >= 280 ? 'まずまず' : c.hits ? '当てた' : '遅い';
  st.results[c.k] = { name: COURSE.corners[c.k].name, grade: g, pts, entry: Math.round(entry), hits: c.hits, jump: c.jump, turn: turnT };
  st.score += pts; st.cur = null;
  showStamp(g, pts); if (pts >= 460) audio.excite(1);
}
function finishRun() {
  mode = 'finish'; modeT = 0;
  st.timeBonus = Math.max(0, Math.round((95 - st.time) * 6)); st.score += st.timeBonus;
  callout('ゴール', '紀州街道まで曳ききった'); audio.whistle();
}

// ---------- 見た目 ----------
const v3 = new THREE.Vector3(), v3b = new THREE.Vector3(), m4 = new THREE.Matrix4(), q4 = new THREE.Quaternion(), eul = new THREE.Euler(0, 0, 0, 'YXZ'), sc3 = new THREE.Vector3(), UP = new THREE.Vector3(0, 1, 0);
const rp = Array.from({ length: NR }, () => ({ x: 0, z: 0, tx: 0, tz: 1, s: 0 }));
let runPhase = 0;
const setRange = (attr, off, cnt) => { if (attr.addUpdateRange) { attr.clearUpdateRanges(); attr.addUpdateRange(off, cnt); } else { attr.updateRange.offset = off; attr.updateRange.count = cnt; } attr.needsUpdate = true; };
function updateVisuals(dt, t) {
  const f = fwd(st.h), vf = st.vx * f.x + st.vz * f.z, v = Math.hypot(st.vx, st.vz);
  dj.root.position.set(st.x, 0, st.z);
  dj.root.rotation.set(st.pitch, st.h, clamp(st.w * vf * 0.02, -0.08, 0.08), 'YXZ');
  dj.root.updateMatrixWorld(true);
  for (const w of dj.wheels) w.rotation.x += vf * dt / 0.33;
  dj.crewRear.position.y = Math.abs(Math.sin(t * 9)) * 0.05 * Math.min(1, v / 3);
  dj.crewRear.position.x = clamp(-st.w * 0.25, -0.3, 0.3);
  dj.maeteko.forEach((m, i) => { const a = st.mae[i]; m.pivot.rotation.x = 0.1 - a * 0.5; m.crew.position.x = -m.side * a * 0.25; m.crew.position.y = -a * 0.06; });
  const beat = audio.ctx ? (audio.ctx.currentTime / audio.interval) * Math.PI : t * 3;
  dj.daiku.forEach((d, i) => {
    d.arm.rotation.z = Math.sin(t * 7 + i * 1.3) * 0.7 - 0.25; d.arm.rotation.x = -0.2 + Math.sin(t * 3.4 + i) * 0.25;
    const jt = st.clock - st.jumpAt - i * 0.12; let jy = 0;
    if (jt > 0 && jt < 0.65) { const p = jt / 0.65; jy = 3.4 * p * (1 - p); d.legL.rotation.x = d.legR.rotation.x = -0.9 * Math.sin(p * Math.PI); d.armL.rotation.z = 1.2 * Math.sin(p * Math.PI); }
    else { d.legL.rotation.x = d.legR.rotation.x = 0; d.armL.rotation.z = 0; }
    d.group.position.y = d.base.y + jy + Math.abs(Math.sin(beat)) * 0.05;
    d.group.rotation.z = clamp(-st.w * 0.35, -0.4, 0.4); d.group.rotation.y = Math.sin(t * 1.1 + i * 2) * 0.35;
  });

  // 綱の中心線（結び目 → 張りの点 → 群衆の跡 → 綱先）を等間隔に取り直す
  const A = dj.model.localToWorld(v3.set(0, 0.56, 2.2));
  const Tp = st.ropeC || trailPoint(ROPE_L), path = [{ x: A.x, z: A.z }, { x: Tp.x, z: Tp.z }];
  for (let j = Tp.i; j < st.trail.length; j++) path.push(st.trail[j]);
  const cum = [0]; for (let j = 1; j < path.length; j++) cum.push(cum[j - 1] + Math.hypot(path[j].x - path[j - 1].x, path[j].z - path[j - 1].z));
  const total = cum[cum.length - 1] || 1; let j = 1;
  for (let k = 0; k < NR; k++) {
    const s = total * k / (NR - 1); while (j < path.length - 1 && cum[j] < s) j++;
    const seg = cum[j] - cum[j - 1] || 1, u = clamp((s - cum[j - 1]) / seg, 0, 1);
    rp[k].x = path[j - 1].x + (path[j].x - path[j - 1].x) * u; rp[k].z = path[j - 1].z + (path[j].z - path[j - 1].z) * u; rp[k].s = s;
  }
  for (let k = 0; k < NR; k++) { const a = rp[Math.max(0, k - 1)], b = rp[Math.min(NR - 1, k + 1)], l = Math.hypot(b.x - a.x, b.z - a.z) || 1; rp[k].tx = (b.x - a.x) / l; rp[k].tz = (b.z - a.z) / l; }
  runPhase += dt * (4 + st.hv * 1.6);
  let ri = 0, pi = 0;
  const lean = clamp(0.1 + st.hv * 0.04, 0, 0.45), happi = new THREE.Color(cfg.color);
  const anchors = dj.ropeAnchors.map((a) => dj.model.localToWorld(a.clone()));
  for (const sgn of [-1, 1]) {
    const an = anchors[sgn < 0 ? 0 : 1]; let px = an.x, py = an.y, pz = an.z;
    for (let k = 1; k < NR; k++) {
      const p = rp[k], gap = 0.48 + Math.min(1, p.s / 4) * 0.52, nx = p.tz * sgn, nz = -p.tx * sgn;
      const x = p.x + nx * gap, z = p.z + nz * gap, y = 0.55 + Math.min(1, p.s / 2.5) * 0.4;
      v3.set(px, py, pz); v3b.set(x - px, y - py, z - pz); const len = v3b.length();
      q4.setFromUnitVectors(UP, v3b.normalize()); ropeMesh.setMatrixAt(ri++, m4.compose(v3, q4, sc3.set(1, len, 1)));
      px = x; py = y; pz = z;
      if (k >= 3) {
        const off = gap + (k % 2 === 0 ? -0.36 : 0.36), bob = Math.abs(Math.sin(runPhase + k * 0.9 + sgn)) * 0.12 * Math.min(1, st.hv / 2);
        eul.set(lean, Math.atan2(p.tx, p.tz), 0);
        m4.compose(v3.set(p.x + nx * off, bob, p.z + nz * off), q4.setFromEuler(eul), sc3.set(1, 1, 1));
        pullT.setMatrixAt(pi, m4); pullL.setMatrixAt(pi, m4); pullH.setMatrixAt(pi, m4); pullT.setColorAt(pi, happi); pi++;
      }
    }
  }
  const lastP = rp[NR - 1];
  for (let k = 0; k < 4; k++) {
    const off = (k - 1.5) * 0.8, ahead = 0.8 + (k % 2);
    eul.set(lean, Math.atan2(lastP.tx, lastP.tz), 0);
    m4.compose(v3.set(lastP.x + lastP.tz * off + lastP.tx * ahead, Math.abs(Math.sin(runPhase + k)) * 0.12, lastP.z - lastP.tx * off + lastP.tz * ahead), q4.setFromEuler(eul), sc3.set(1, 1, 1));
    pullT.setMatrixAt(pi, m4); pullL.setMatrixAt(pi, m4); pullH.setMatrixAt(pi, m4); pullT.setColorAt(pi, happi); pi++;
  }
  for (const m of [ropeMesh, pullT, pullL, pullH]) m.instanceMatrix.needsUpdate = true;
  pullT.instanceColor.needsUpdate = true;

  // 近くの見物人が跳ねる
  const sp = town.spectators; let lo = 1e9, hi = -1;
  for (let i = 0; i < sp.list.length; i++) {
    const o = sp.list[i], ddx = o.x - st.x, ddz = o.z - st.z, near = ddx * ddx + ddz * ddz < 1100 && mode !== 'title';
    if (!near && !o.act) continue;
    o.act = near;
    eul.set(0, o.ry, 0);
    m4.compose(v3.set(o.x, near ? o.y + Math.abs(Math.sin(t * 8 + o.ph)) * 0.2 : o.y, o.z), q4.setFromEuler(eul), sc3.set(o.sc, o.sc, o.sc));
    for (const m of sp.meshes) m.setMatrixAt(i, m4);
    if (i < lo) lo = i; if (i > hi) hi = i;
  }
  if (hi >= 0) for (const m of sp.meshes) setRange(m.instanceMatrix, lo * 16, (hi - lo + 1) * 16);
  eul.set(0, 0, 0);

  sun.target.position.set(st.x, 0, st.z);
  sun.position.copy(sun.target.position).addScaledVector(SUN, 200);
}

// ---------- カメラ ----------
const cam = { dx: 0, dz: -1, pos: new THREE.Vector3(), look: new THREE.Vector3(), init: false };
const CAM_NAMES = ['追走', '大工方', '桟敷'];
function updateCamera(dt, t) {
  const f = fwd(st.h), v = Math.hypot(st.vx, st.vz);
  let dx = f.x, dz = f.z; if (v > 1.5) { dx = st.vx / v; dz = st.vz / v; }
  const k = 1 - Math.exp(-2.0 * dt);
  cam.dx += (dx - cam.dx) * k; cam.dz += (dz - cam.dz) * k; const l = Math.hypot(cam.dx, cam.dz) || 1; cam.dx /= l; cam.dz /= l;
  const want = new THREE.Vector3(), look = new THREE.Vector3(); let snap = false;
  if (mode === 'title' || mode === 'loading') {
    const a = t * 0.16; want.set(st.x + Math.cos(a) * 11, 3.6, st.z + Math.sin(a) * 11); look.set(st.x, 2.4, st.z);
    const p = pushCircle(want.x, want.z, 0.6); want.x = p.x; want.z = p.z;
  } else if (camMode === 1) {
    dj.model.localToWorld(want.set(0, 6.3, 0.7)); look.set(want.x + f.x * 20, 3.2, want.z + f.z * 20); snap = true;
  } else if (camMode === 2) {
    let best = null; for (const s of town.spots) { const d = Math.hypot(s.x - st.x, s.z - st.z); if (!best || d < best.d) best = { d, s }; }
    if (best.d < 80) { want.set(best.s.x, best.s.y, best.s.z); snap = true; }
    else { want.set(st.x + f.z * 6 + f.x * 15, 2.8, st.z - f.x * 6 + f.z * 15); const p = pushCircle(want.x, want.z, 0.6); want.x = p.x; want.z = p.z; }
    look.set(st.x, 2.2, st.z);
  } else {
    want.set(st.x - cam.dx * 11.5, 7.0, st.z - cam.dz * 11.5);
    const p = pushCircle(want.x, want.z, 0.8); want.x = p.x; want.z = p.z;
    look.set(st.x + f.x * 4, 1.6, st.z + f.z * 4);
  }
  if (!cam.init || snap) { cam.pos.copy(want); cam.look.copy(look); cam.init = true; }
  else { cam.pos.lerp(want, 1 - Math.exp(-6 * dt)); cam.look.lerp(look, 1 - Math.exp(-9 * dt)); }
  camera.position.copy(cam.pos);
  if (st.shake > 0.01) { camera.position.x += (Math.random() - 0.5) * st.shake; camera.position.y += (Math.random() - 0.5) * st.shake; st.shake *= Math.exp(-5 * dt); }
  camera.lookAt(cam.look);
}

// ---------- HUD ----------
const hud = { spd: $('spd'), tempo: $('tempoFill'), rail: $('railFill'), place: $('place'), score: $('score'), callout: $('callout'), stamp: $('stamp'), bang: $('bang') };
let calloutUntil = 0, hudTick = 0;
function callout(main, sub, secs) { hud.callout.innerHTML = main + (sub ? `<small>${sub}</small>` : ''); hud.callout.style.opacity = 1; calloutUntil = performance.now() / 1000 + (secs || 2.2); }
function showStamp(word, pts) { hud.stamp.innerHTML = `<div>${word}<small>+${pts}</small></div>`; hud.stamp.classList.add('show'); setTimeout(() => hud.stamp.classList.remove('show'), 2300); }
function showBang(word) { hud.bang.textContent = word; hud.bang.classList.add('show'); setTimeout(() => hud.bang.classList.remove('show'), 500); }
function placeName(s) {
  const c = COURSE.corners;
  if (Math.abs(s - c[0].s) < 28) return 'カンカン場（岸和田港交差点）';
  if (Math.abs(s - c[1].s) < 14) return '紀州街道の辻';
  if (s < c[0].s) return '大阪臨海線';
  if (s < c[1].s) return '北町の路地';
  return '紀州街道';
}
function updateHud() {
  if (++hudTick % 3) return;
  const v = Math.hypot(st.vx, st.vz);
  hud.spd.textContent = Math.round(v * 3.6);
  hud.tempo.style.width = Math.round(st.tempo * 100) + '%';
  hud.rail.style.width = Math.min(100, st.s / COURSE.finishS * 100).toFixed(1) + '%';
  hud.place.textContent = placeName(st.s); hud.score.textContent = st.score;
  document.querySelectorAll('#rail .stop').forEach((el) => el.classList.toggle('done', st.s >= +el.dataset.s));
  $('mL').classList.toggle('hint', false); $('mR').classList.toggle('hint', false);
  if (mode === 'run' && performance.now() / 1000 > calloutUntil) {
    let msg = '', sub = '';
    for (let k = 0; k < 2; k++) {
      const cn = COURSE.corners[k], d = cn.s - st.s;
      if (!st.results[k] && d > -16 && d < 110) {
        const side = cn.dir > 0 ? '左' : '右';
        if (d > 40) { msg = `${cn.name}まで ${Math.round(d)}m`; sub = st.tempo < 0.6 ? '鳴物を上げろ、曳け！' : cn.sub; }
        else if (d > 18) { msg = `${side}の前梃子、用意`; sub = 'まだ入れるな'; }
        else { msg = `${side}の前梃子！`; sub = '入れて、跳べ'; $(cn.dir > 0 ? 'mL' : 'mR').classList.add('hint'); }
        break;
      }
    }
    if (!msg && st.time < 7) { msg = '曳け！'; sub = '真ん中を鳴物の拍に合わせて連打'; }
    hud.callout.innerHTML = msg + (sub ? `<small>${sub}</small>` : ''); hud.callout.style.opacity = msg ? 1 : 0;
  }
  audio.excite(clamp(v / VMAX, 0, 1) * 0.6 + (st.cur ? 0.4 : 0));
}
function buildRail() {
  const rail = $('rail'); rail.querySelectorAll('.stop').forEach((e) => e.remove());
  [[COURSE.corners[0].s, 'カンカン場'], [COURSE.corners[1].s, '紀州街道'], [COURSE.finishS, 'ゴール']].forEach(([s, name]) => {
    const el = document.createElement('b'); el.className = 'stop'; el.dataset.s = s; el.style.left = (s / COURSE.finishS * 100) + '%'; el.innerHTML = `<span>${name}</span>`; rail.appendChild(el);
  });
}

// ---------- 入力 ----------
function tap() {
  if (mode !== 'run') return;
  const good = audio.offBeat() < 0.18;
  st.tempo = Math.min(1, st.tempo + (good ? 0.19 : 0.11));
  st.combo = good ? st.combo + 1 : 0;
  if (st.combo === 6) callout('ええ調子', '鳴物に乗ってる', 1.2);
  audio.chant();
  const b = $('pullRing'); b.classList.remove('pulse'); void b.offsetWidth; b.classList.add('pulse');
}
function jump() {
  if (mode !== 'run' || st.clock - st.jumpAt < 0.8) return;
  st.jumpAt = st.clock;
  if (st.cur && Math.abs(st.w) > 0.2) st.cur.jump = true;
}
function hold(el, on, off) {
  const ids = new Set();
  el.addEventListener('pointerdown', (e) => { ids.add(e.pointerId); el.setPointerCapture(e.pointerId); el.classList.add('on'); on(); e.preventDefault(); e.stopPropagation(); });
  const up = (e) => { ids.delete(e.pointerId); if (!ids.size) { el.classList.remove('on'); if (off) off(); } };
  el.addEventListener('pointerup', up); el.addEventListener('pointercancel', up);
}
hold($('mL'), () => { input.mL = 1; }, () => { input.mL = 0; });
hold($('mR'), () => { input.mR = 1; }, () => { input.mR = 0; });
hold($('btnJump'), jump);
$('pullZone').addEventListener('pointerdown', (e) => { tap(); e.preventDefault(); });
$('camBtn').addEventListener('click', () => { camMode = (camMode + 1) % 3; cam.init = false; $('camBtn').textContent = CAM_NAMES[camMode]; });
window.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT') return;
  if (!e.repeat && (e.code === 'Space' || e.code === 'ArrowUp')) tap();
  if (e.code === 'ArrowLeft' || e.code === 'KeyA') { input.mL = 1; $('mL').classList.add('on'); }
  if (e.code === 'ArrowRight' || e.code === 'KeyD') { input.mR = 1; $('mR').classList.add('on'); }
  if (e.code === 'KeyJ') jump();
  if (e.code === 'KeyC' && !e.repeat) $('camBtn').click();
  if (e.code.startsWith('Arrow') || e.code === 'Space') e.preventDefault();
});
window.addEventListener('keyup', (e) => {
  if (e.code === 'ArrowLeft' || e.code === 'KeyA') { input.mL = 0; $('mL').classList.remove('on'); }
  if (e.code === 'ArrowRight' || e.code === 'KeyD') { input.mR = 0; $('mR').classList.remove('on'); }
});

// ---------- 画面 ----------
const sw = $('swatches');
HAPPI.forEach((h, i) => {
  const b = document.createElement('button'); b.type = 'button'; b.style.background = '#' + h.c.toString(16).padStart(6, '0');
  b.setAttribute('aria-label', h.name); b.setAttribute('aria-pressed', i === 0 ? 'true' : 'false');
  b.addEventListener('click', () => { cfg.color = h.c; sw.querySelectorAll('button').forEach((x) => x.setAttribute('aria-pressed', x === b ? 'true' : 'false')); rebuild(); });
  sw.appendChild(b);
});
function rebuild() { if (dj) scene.remove(dj.root); dj = buildDanjiri(tex, cfg.color); dj.setTown(cfg.town); scene.add(dj.root); }
$('townInput').addEventListener('input', (e) => { cfg.town = e.target.value.trim() || 'わが町'; if (dj) dj.setTown(cfg.town); });
function start() {
  audio.start();
  $('title').hidden = true; $('result').hidden = true; $('hud').hidden = false; $('teamName').textContent = cfg.town;
  reset(); buildRail(); mode = 'count'; modeT = 0; camMode = 0; $('camBtn').textContent = CAM_NAMES[0];
  input.mL = input.mR = 0; callout('よーい', cfg.town + '、曳き出し', 1.6);
}
$('startBtn').addEventListener('click', start);
$('againBtn').addEventListener('click', start);
$('titleBtn').addEventListener('click', () => { $('result').hidden = true; $('title').hidden = false; mode = 'title'; reset(); audio.stop(); });
function showResult() {
  mode = 'result'; audio.stop(); $('hud').hidden = true; $('result').hidden = false;
  const rows = st.results.map((r) => `<div><span>${r.name}　入り ${r.entry}km/h${r.hits ? `・接触${r.hits}` : ''}${r.jump ? '・跳' : ''}</span><em>${r.grade}</em><b>${r.pts}</b></div>`);
  rows.push(`<div><span>タイム ${st.time.toFixed(1)}秒</span><em></em><b>+${st.timeBonus}</b></div>`);
  $('resRows').innerHTML = rows.join(''); $('resTotal').textContent = st.score;
  const top = st.results.reduce((a, r) => (r && r.pts > a.pts ? r : a), { pts: -1, grade: 'あかん' });
  $('resGrade').innerHTML = `${top.grade}<span>${cfg.town}</span>`;
  let best = 0; try { best = +localStorage.getItem(BEST_KEY) || 0; if (st.score > best) localStorage.setItem(BEST_KEY, String(st.score)); } catch (e) { /* 保存できない環境 */ }
  $('resBest').textContent = st.score > best ? '自己ベスト更新' : `自己ベスト ${best}`;
}

// ---------- ループ ----------
function resize() {
  const w = window.innerWidth, h = window.innerHeight;
  renderer.setSize(w, h, false); camera.aspect = w / h; camera.fov = w < h ? 70 : 56; camera.updateProjectionMatrix();
}
window.addEventListener('resize', resize);
document.addEventListener('visibilitychange', () => { if (!audio.ctx) return; if (document.hidden) audio.ctx.suspend(); else if (mode !== 'title' && mode !== 'result') audio.ctx.resume(); });

let lastT = performance.now(), accum = 0, perfT = 0, perfN = 0;
function advance(dt, t) {
  audio.tempo = st.tempo;
  if (mode === 'count' || mode === 'run' || mode === 'finish') {
    modeT += dt;
    if (mode === 'count' && modeT > 1.8) { mode = 'run'; audio.whistle(); callout('曳き出し！', '真ん中を鳴物に合わせて連打', 2.4); }
    accum += dt; while (accum >= 1 / 120) { step(1 / 120); accum -= 1 / 120; }
    track(dt);
    if (mode === 'finish' && modeT > 3.2) showResult();
  }
  updateVisuals(dt, t); updateCamera(dt, t);
  if (mode === 'count' || mode === 'run' || mode === 'finish') updateHud();
  if (performance.now() / 1000 > calloutUntil && mode !== 'run') hud.callout.style.opacity = 0;
}
function frame(now) {
  requestAnimationFrame(frame);
  const dt = Math.min(0.05, (now - lastT) / 1000); lastT = now;
  advance(dt, now / 1000);
  renderer.render(scene, camera);
  perfT += dt; perfN++;
  if (perfT > 2.5) { if (perfT / perfN > 0.024 && pixelRatio > 1.01) { pixelRatio = Math.max(1, pixelRatio - 0.25); renderer.setPixelRatio(pixelRatio); resize(); } perfT = 0; perfN = 0; }
}

async function boot() {
  const res = await fetch('data/kishiwada.json');
  if (!res.ok) throw new Error('data/kishiwada.json を読み込めません (' + res.status + ')');
  const data = await res.json();
  town = buildTown(data, tex); scene.add(town.group);
  segs = town.segs; grid = town.grid; CELL = town.CELL;
  rebuild(); reset(); resize(); mode = 'title';
  $('loading').hidden = true;
  requestAnimationFrame(frame);
}
boot().catch((e) => { $('loading').textContent = '読み込みに失敗しました：' + e.message; console.error(e); });

window.DJ_DEBUG = {
  get st() { return st; }, input, TUNE, get mode() { return mode; }, start, tap, jump, get segs() { return segs; }, COURSE, SEG,
  sim(sec, fn) { for (let i = 0; i < sec * 30; i++) { if (fn) fn(i / 30); advance(1 / 30, performance.now() / 1000 + i / 30); } renderer.render(scene, camera); },
};
