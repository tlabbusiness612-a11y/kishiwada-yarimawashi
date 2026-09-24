// 町並み：PLATEAU（国土交通省 3D都市モデル）岸和田市の建物・道路から組み立てる
import * as THREE from 'three';
import { Batch, rng, clamp, matrixAt, personGeos } from './util.js';
import { SEG, COURSE, project, pointAt } from './course.js';

const W = 0xffffff;

export function buildTown(data, tex) {
  const r = rng(1989), pick = (a) => a[Math.floor(r() * a.length)];
  const b = new Batch(), bf = new Batch();
  let chunk = '0,0';
  const setChunk = (x, z) => { chunk = Math.floor(x / 90) + ',' + Math.floor(z / 90); };
  const K = (m) => m + '|' + chunk;
  const segs = [], spect = [], wires = [];
  const addSeg = (ax, az, bx, bz, kind) => segs.push([ax, az, bx, bz, kind || 'house']);

  // ---------- 建物 ----------
  const area = (p) => { let a = 0; for (let i = 0; i < p.length; i += 2) { const j = (i + 2) % p.length; a += p[i] * p[j + 1] - p[j] * p[i + 1]; } return a / 2; };
  function obb(p) {
    const n = p.length / 2; let best = null;
    for (let i = 0; i < n; i++) {
      const j = (i + 1) % n, ex = p[j * 2] - p[i * 2], ez = p[j * 2 + 1] - p[i * 2 + 1], L = Math.hypot(ex, ez);
      if (L < 0.5) continue;
      const ux = ex / L, uz = ez / L; let a0 = 1e9, a1 = -1e9, c0 = 1e9, c1 = -1e9;
      for (let k = 0; k < n; k++) { const x = p[k * 2], z = p[k * 2 + 1], pu = x * ux + z * uz, pv = -x * uz + z * ux; a0 = Math.min(a0, pu); a1 = Math.max(a1, pu); c0 = Math.min(c0, pv); c1 = Math.max(c1, pv); }
      const A = (a1 - a0) * (c1 - c0);
      if (!best || A < best.A) best = { A, ux, uz, a0, a1, c0, c1 };
    }
    if (!best) return null;
    const cu = (best.a0 + best.a1) / 2, cc = (best.c0 + best.c1) / 2;
    let o = { A: best.A, cx: best.ux * cu - best.uz * cc, cz: best.uz * cu + best.ux * cc, ux: best.ux, uz: best.uz, vx: -best.uz, vz: best.ux, hu: (best.a1 - best.a0) / 2, hv: (best.c1 - best.c0) / 2 };
    if (o.hu < o.hv) o = Object.assign(o, { ux: o.vx, uz: o.vz, vx: -o.uz, vz: o.ux, hu: o.hv, hv: o.hu });
    return o;
  }
  function walls(p, sgn, y0, y1, key, color, cw, ch) {
    const pos = [], uv = []; let per = 0;
    for (let i = 0; i < p.length; i += 2) {
      const j = (i + 2) % p.length, ax = p[i], az = p[i + 1], bx = p[j], bz = p[j + 1], L = Math.hypot(bx - ax, bz - az);
      if (L < 0.05) continue;
      const u0 = per / cw, u1 = (per + L) / cw, v0 = y0 / ch, v1 = y1 / ch; per += L;
      if (sgn < 0) { pos.push(ax, y0, az, bx, y0, bz, bx, y1, bz, ax, y0, az, bx, y1, bz, ax, y1, az); uv.push(u0, v0, u1, v0, u1, v1, u0, v0, u1, v1, u0, v1); }
      else { pos.push(bx, y0, bz, ax, y0, az, ax, y1, az, bx, y0, bz, ax, y1, az, bx, y1, bz); uv.push(u1, v0, u0, v0, u0, v1, u1, v0, u0, v1, u1, v1); }
    }
    b.tris(key, pos, uv, color);
  }
  function flatRoof(p, y, key, color, target) {
    const pts = []; for (let i = 0; i < p.length; i += 2) pts.push(new THREE.Vector2(p[i], p[i + 1]));
    const pos = [], uv = [];
    for (const t of THREE.ShapeUtils.triangulateShape(pts, [])) {
      const [a, c, d] = t.map((k) => pts[k]);
      const up = (c.y - a.y) * (d.x - a.x) - (c.x - a.x) * (d.y - a.y) > 0;
      for (const v of up ? [a, c, d] : [a, d, c]) { pos.push(v.x, y, v.y); uv.push(v.x / 8, v.y / 8); }
    }
    (target || b).tris(key, pos, uv, color);
  }
  function pitched(o, wallH, rise, hip, wallKey, wallColor) {
    const ov = 0.5, L = o.hu + ov, S = o.hv + ov, slope = rise / o.hv, eaveY = wallH - ov * slope, top = wallH + rise;
    const P = (a, c, y) => [o.cx + o.ux * a + o.vx * c, y, o.cz + o.uz * a + o.vz * c];
    const rl = hip ? Math.max(0.2, o.hu - o.hv) : L;
    const pos = [], uv = [];
    const face = (vs, uvs) => { pos.push(...vs.flat()); uv.push(...uvs); };
    for (const s of [1, -1]) {
      const E1 = P(L, s * S, eaveY), E2 = P(-L, s * S, eaveY), R2 = P(-rl, 0, top), R1 = P(rl, 0, top);
      const sl = Math.hypot(S, rise + ov * slope) / 1.8;
      face([E1, E2, R2, E1, R2, R1], [L / 1.8, 0, -L / 1.8, 0, -rl / 1.8, sl, L / 1.8, 0, -rl / 1.8, sl, rl / 1.8, sl]);
      if (hip) { const A1 = P(s * L, S, eaveY), A2 = P(s * L, -S, eaveY), R = P(s * rl, 0, top); face([A1, A2, R], [S / 1.8, 0, -S / 1.8, 0, 0, sl]); }
    }
    b.tris(K('tile'), pos, uv, W);
    b.box(K('tile'), 0.34, 0.24, rl * 2 + 0.3, o.cx, top + 0.06, o.cz, 0xcfcfcf, Math.atan2(o.ux, o.uz));
    if (!hip) {
      const gp = [];
      for (const s of [1, -1]) { const a = P(s * o.hu, o.hv, wallH), c = P(s * o.hu, -o.hv, wallH), t = P(s * o.hu, 0, top); gp.push(...a, ...c, ...t, ...a, ...t, ...c); }
      b.tris(K(wallKey), gp, new Array(gp.length / 3 * 2).fill(0.5), wallColor);
    }
  }
  function lanternPair(x, y, z, nx, nz) {
    for (const k of [-1, 1]) {
      const lx = x + nz * k * 1.1, lz = z - nx * k * 1.1;
      b.geo(K('lantern'), SPH, lx, y, lz, 0xe0402c, 0, 0, 0, 0.19, 0.26, 0.19);
      b.box(K('plain'), 0.2, 0.06, 0.2, lx, y + 0.25, lz, 0x1b1b1b); b.box(K('plain'), 0.2, 0.06, 0.2, lx, y - 0.25, lz, 0x1b1b1b);
    }
  }
  const SPH = new THREE.SphereGeometry(1, 12, 10);
  const HOUSE = [0xefe9dc, 0xe0d8c8, 0xcfc8ba, 0xe9e0d2, 0xc3bcae, 0xd9ccb4, 0xb3ada3, 0xe4ddd5];
  const OFFICE = [0xe2dfd8, 0xcfccc5, 0xbcb8b0, 0xebe7df, 0xd7d0c4];
  const ROOF = [0x8e8b86, 0x9a968f, 0x7f7c78, 0xa39f97];

  for (const bd of data.buildings) {
    const p = bd.p; if (p.length < 6) continue;
    const A = area(p); if (Math.abs(A) < 5) continue;
    const sgn = A > 0 ? 1 : -1;
    let cx = 0, cz = 0; for (let i = 0; i < p.length; i += 2) { cx += p[i]; cz += p[i + 1]; } cx /= p.length / 2; cz /= p.length / 2;
    setChunk(cx, cz);
    const pr = project(cx, cz), segI = pr.d < 45 ? SEG.indexOf(pr.seg) : -1;
    const u = +bd.u || 0, h = Math.max(3, bd.h);
    const tall = h > 11.5 || u === 412 || u === 401 || u === 431 || u === 441 || (u === 402 && h > 8);
    const o = obb(p), rect = o ? Math.abs(A) / (4 * o.hu * o.hv) : 0;
    const machiya = !tall && h < 10 && (segI === 2 || (segI === 1 && r() < 0.3));
    const canPitch = o && !tall && h < 11.5 && [0, 411, 413, 415, 461, 454, 402].includes(u) && rect > 0.72 && o.hv > 1.6 && o.hu < 18;
    const rise = canPitch ? Math.min(2.6, o.hv * 0.42) : 0;
    const wallH = canPitch ? Math.max(2.8, h - rise) : h;
    const wallKey = machiya ? 'plaster' : tall ? 'office' : 'house';
    const wallCol = machiya ? W : tall ? pick(OFFICE) : pick(HOUSE);
    walls(p, sgn, 0, wallH, K(wallKey), wallCol, tall ? 3.6 : machiya ? 4 : 4, tall ? 3.2 : machiya ? 2.6 : 3);
    if (canPitch) pitched(o, wallH, rise, !machiya && r() < 0.45, wallKey, wallCol);
    else { flatRoof(p, wallH, K('plain'), pick(ROOF)); if (tall) walls(p, sgn, wallH, wallH + 0.6, K('plain'), 0xd9d6cf, 4, 1); }

    // 道に面した壁：店先・格子・看板・庇
    if (segI >= 0) {
      const hw = COURSE.halfWidth[segI];
      for (let i = 0; i < p.length; i += 2) {
        const j = (i + 2) % p.length, ax = p[i], az = p[i + 1], bx = p[j], bz = p[j + 1], L = Math.hypot(bx - ax, bz - az);
        if (L < 2.2) continue;
        const nx = sgn * (bz - az) / L, nz = -sgn * (bx - ax) / L, mx = (ax + bx) / 2, mz = (az + bz) / 2;
        const q = project(mx, mz); if (q.d > hw + 9) continue;
        const toC = pointAt(q.s), dx = toC.x - mx, dz = toC.z - mz, dl = Math.hypot(dx, dz) || 1;
        if ((nx * dx + nz * dz) / dl < 0.55) continue;
        const ry = Math.atan2(nx, nz), fx = mx + nx * 0.06, fz = mz + nz * 0.06;
        if (machiya) {
          b.quad(K('lattice'), L, 3.0, fx, 1.5, fz, ry, W, 0, 0, Math.max(1, Math.round(L / 2)), 1);
          b.box(K('tile'), L + 0.1, 0.1, 1.1, mx + nx * 0.5, 3.18, mz + nz * 0.5, W, ry, 0.3);
          b.box(K('plain'), L, 0.14, 0.2, mx + nx * 0.1, 3.0, mz + nz * 0.1, 0x3a2717, ry);
          if (r() < 0.6) lanternPair(mx + nx * 0.95, 2.6, mz + nz * 0.95, nx, nz);
        } else if (wallH > 4.5) {
          const v = Math.floor(r() * 4), cu = (v % 2) * 0.5, cvv = v < 2 ? 0.5 : 0;
          b.quad(K('shop'), L - 0.2, 3.0, fx, 1.5, fz, ry, W, cu, cvv, cu + 0.5, cvv + 0.5);
          b.box(K('plain'), L, 0.07, 1.2, mx + nx * 0.6, 3.1, mz + nz * 0.6, pick([0xb33a2a, 0x2f5d8a, 0x3f7a4a, 0xd9b44a, 0x8a3f6a, 0x6b6b6b]), ry, 0.22);
          if (r() < 0.75) {
            const sg = Math.floor(r() * tex.signCount), sc = sg % 4, sr = Math.floor(sg / 4), sw = Math.min(L - 0.6, 3.6);
            b.box(K('plain'), sw + 0.2, 1.0, 0.12, mx + nx * 0.1, 3.8, mz + nz * 0.1, 0x2a2a2a, ry);
            b.quad(K('signs'), sw, 0.9, mx + nx * 0.17, 3.8, mz + nz * 0.17, ry, W, sc / 4, 1 - (sr + 1) / 8, (sc + 1) / 4, 1 - sr / 8);
          }
          if (r() < 0.3) lanternPair(mx + nx * 1.3, 2.7, mz + nz * 1.3, nx, nz);
        }
      }
    }
    // 当たり判定（コース近くの建物だけ）
    if (pr.d < 40) for (let i = 0; i < p.length; i += 2) { const j = (i + 2) % p.length; addSeg(p[i], p[i + 1], p[j], p[j + 1], 'house'); }
  }

  // ---------- 地面・道路 ----------
  {
    const g = new THREE.PlaneGeometry(3000, 3000); g.rotateX(-Math.PI / 2);
    const uv = g.attributes.uv; for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * 1500, uv.getY(i) * 1500);
    bf.add('walk', g, null, 0xc9c2b4);
  }
  for (const rd of data.roads) {
    if (rd.p.length < 6) continue;
    if (rd.f === '1') { // 阪神高速湾岸線（高架）
      setChunk(rd.p[0], rd.p[1]);
      flatRoof(rd.p, 11, K('plain'), 0x9d9d98);
      let cx = 0, cz = 0; for (let i = 0; i < rd.p.length; i += 2) { cx += rd.p[i]; cz += rd.p[i + 1]; } cx /= rd.p.length / 2; cz /= rd.p.length / 2;
      b.box(K('plain'), 2.2, 10.6, 2.2, cx, 5.3, cz, 0xa8a8a2);
      continue;
    }
    flatRoof(rd.p, 0.03, 'asphalt', W, bf);
  }
  // 臨海線の区画線と横断歩道
  {
    const s0 = SEG[0];
    for (let t = 0; t < s0.len - 34; t += 1) {
      const x = s0.a.x + s0.dx * t, z = s0.a.z + s0.dz * t, nx = s0.dz, nz = -s0.dx;
      bf.box('plain', 0.18, 0.01, 0.9, x + nx * 0.2, 0.05, z + nz * 0.2, 0xe8c64a, s0.h); bf.box('plain', 0.18, 0.01, 0.9, x - nx * 0.2, 0.05, z - nz * 0.2, 0xe8c64a, s0.h);
      if (t % 8 < 5) for (const k of [-6.5, 6.5]) bf.box('plain', 0.15, 0.01, 1, x + nx * k, 0.05, z + nz * k, 0xeeeeee, s0.h);
    }
    const zebra = (cx, cz, h, len) => { const nx = Math.cos(h), nz = -Math.sin(h); for (let k = -len / 2; k < len / 2; k += 1) bf.box('plain', 0.5, 0.012, 3.4, cx + nx * k, 0.052, cz + nz * k, 0xeeeeee, h); };
    const J = SEG[1].a, s1 = SEG[1];
    zebra(J.x - SEG[0].dx * 26, J.z - SEG[0].dz * 26, SEG[0].h, 26);
    zebra(J.x + s1.dx * 19, J.z + s1.dz * 19, s1.h, 9);
  }

  // ---------- 観覧席・柵・見物人 ----------
  const s0 = SEG[0], s1 = SEG[1], s2 = SEG[2], J = s1.a, Kc = s2.a;
  const left = (s) => ({ x: s.dz, z: -s.dx });
  const L0 = left(s0), L1 = left(s1), L2 = left(s2);
  const addP = (a, s, t, n, u) => ({ x: a.x + s.dx * t + n.x * u, z: a.z + s.dz * t + n.z * u });
  // 柵（ロープと赤い杭）＋ 後ろに見物人、当たり判定つき
  function fence(pts, faceSign, rows = 2) {
    for (let i = 0; i < pts.length - 1; i++) {
      const a = pts[i], c = pts[i + 1], L = Math.hypot(c.x - a.x, c.z - a.z); if (L < 0.01) continue;
      const dx = (c.x - a.x) / L, dz = (c.z - a.z) / L, nx = -dz * faceSign, nz = dx * faceSign, ry = Math.atan2(dx, dz);
      setChunk(a.x, a.z);
      b.box(K('plain'), 0.07, 0.07, L, (a.x + c.x) / 2, 1.0, (a.z + c.z) / 2, 0xf2efe6, ry);
      for (let t = 0; t < L; t += 2.4) b.box(K('plain'), 0.1, 1.1, 0.1, a.x + dx * t, 0.55, a.z + dz * t, 0xc8261c);
      for (let row = 0; row < rows; row++) for (let t = r() * 0.6; t < L; t += 0.62 + r() * 0.5) {
        const back = 0.7 + row * 0.72 + r() * 0.2;
        spect.push({ x: a.x + dx * t - nx * back, z: a.z + dz * t - nz * back, ry: Math.atan2(nx, nz) + (r() - 0.5) * 0.5, y: 0 });
      }
      addSeg(a.x, a.z, c.x, c.z, 'crowd');
    }
  }
  // 桟敷（ひな壇）。front から depth 方向へ高くなる。face は道を向く向き
  function stand(a, c, tiers) {
    const L = Math.hypot(c.x - a.x, c.z - a.z), dx = (c.x - a.x) / L, dz = (c.z - a.z) / L;
    const bx = -dz, bz = dx; // 奥へ（a→c の右手）
    const ry = Math.atan2(dx, dz); setChunk(a.x, a.z);
    for (let k = 0; k < tiers; k++) {
      const off = 1 + k * 2, top = 0.55 * (k + 1), mx = (a.x + c.x) / 2 + bx * off, mz = (a.z + c.z) / 2 + bz * off;
      b.box(K('plain'), 2, top, L, mx, top / 2, mz, k % 2 ? 0x707c89 : 0x5e6977, ry);
      for (let t = 0.5; t < L - 0.3; t += 0.6 + r() * 0.2) if (r() < 0.9) spect.push({ x: a.x + dx * t + bx * (off + 0.2), z: a.z + dz * t + bz * (off + 0.2), ry: Math.atan2(-bx, -bz), y: top - 0.42 });
    }
    const back = 2 * tiers + 0.2;
    b.box(K('plain'), 0.3, 4, L, (a.x + c.x) / 2 + bx * back, 2, (a.z + c.z) / 2 + bz * back, 0x46525f, ry);
    b.quad(K('kohaku'), L, 1.0, (a.x + c.x) / 2 - bx * 0.04, 0.5, (a.z + c.z) / 2 - bz * 0.04, Math.atan2(-bx, -bz), W, 0, 0, L / 1.6, 1);
    addSeg(a.x, a.z, c.x, c.z, 'stand');
  }
  // 臨海線：両側の柵
  const hw0 = COURSE.halfWidth[0];
  fence([addP(s0.a, s0, -10, L0, hw0), addP(s0.a, s0, s0.len - 52, L0, hw0)], 1);
  fence([addP(s0.a, s0, s0.len + 22, L0, -hw0), addP(s0.a, s0, -10, L0, -hw0)], 1);
  // 山側の桟敷（曲がる内側）と海側の桟敷（外側・正面）
  stand(addP(s0.a, s0, s0.len - 16, L0, hw0 + 0.5), addP(s0.a, s0, s0.len - 52, L0, hw0 + 0.5), 3);
  stand(addP(J, s0, 22, L0, -hw0), addP(J, s0, 22, L0, 9), 5);
  // 角の内側・外側をつなぐ人垣
  const hw1 = COURSE.halfWidth[1];
  fence([addP(s0.a, s0, s0.len - 16, L0, hw0 + 0.5), addP(J, s1, 20, L1, hw1)], 1);
  fence([addP(J, s0, 22, L0, 9), addP(J, s1, 14, L1, -hw1 - 3.5), addP(J, s1, 28, L1, -hw1)], -1, 3);
  // 路地・紀州街道：建物の切れ目は人垣でふさぐ（走査）
  const cast = (ox, oz, dx, dz, lim) => {
    let best = lim + 1;
    for (const s of segs) {
      const ex = s[2] - s[0], ez = s[3] - s[1], den = dx * ez - dz * ex; if (Math.abs(den) < 1e-9) continue;
      const wx = s[0] - ox, wz = s[1] - oz, t = (wx * ez - wz * ex) / den, u = (wx * dz - wz * dx) / den;
      if (t > 0 && t < best && u >= 0 && u <= 1) best = t;
    }
    return best;
  };
  function corridor(seg, n, hw, fromL, fromR, to) {
    for (const sd of [1, -1]) {
      const from = sd > 0 ? fromL : fromR;
      let run = [];
      const flush = () => { if (run.length > 1) fence(run, sd > 0 ? 1 : -1, 1); run = []; };
      for (let t = from; t <= to; t += 1) {
        const c = { x: seg.a.x + seg.dx * t, z: seg.a.z + seg.dz * t }, d = cast(c.x, c.z, n.x * sd, n.z * sd, hw + 2.5);
        if (d > hw) run.push({ x: c.x + n.x * sd * hw, z: c.z + n.z * sd * hw });
        else {
          flush();
          if (r() < 0.85) { const back = d - 0.45 - r() * 0.35; spect.push({ x: c.x + n.x * sd * back, z: c.z + n.z * sd * back, ry: Math.atan2(-n.x * sd, -n.z * sd) + (r() - 0.5) * 0.6, y: 0 }); }
        }
      }
      flush();
    }
  }
  corridor(s1, L1, hw1, 20, 28, s1.len - 7);
  corridor(s2, L2, COURSE.halfWidth[2], 6, 6, s2.len);
  // 紀州街道の辻：まっすぐ・左の道は人垣でふさぐ
  const hw2 = COURSE.halfWidth[2];
  fence([addP(s1.a, s1, s1.len + 6, L1, -hw1 - 1), addP(s1.a, s1, s1.len + 6, L1, hw1 + 1)], -1, 3);
  fence([addP(Kc, s2, -6, L2, -hw2 - 3), addP(Kc, s2, -6, L2, hw2 + 3)], 1, 3);
  const end = pointAt(COURSE.finishS + 18), e2 = end.seg;
  fence([{ x: end.x - L2.x * (hw2 + 1), z: end.z - L2.z * (hw2 + 1) }, { x: end.x + L2.x * (hw2 + 1), z: end.z + L2.z * (hw2 + 1) }], -1, 3);

  // ---------- 信号・電柱・電線・提灯 ----------
  const addWire = (a, c, sag) => { let p = a; for (let i = 1; i <= 8; i++) { const t = i / 8; const q = [a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t - Math.sin(t * Math.PI) * sag, a[2] + (c[2] - a[2]) * t]; wires.push(p, q); p = q; } };
  function signal(x, z, h) {
    setChunk(x, z); const ax = Math.sin(h), az = Math.cos(h);
    b.rod(K('plain'), new THREE.Vector3(x, 0, z), new THREE.Vector3(x, 6.2, z), 0.12, 0x8d9096);
    b.rod(K('plain'), new THREE.Vector3(x, 5.9, z), new THREE.Vector3(x + ax * 6, 6.0, z + az * 6), 0.07, 0x8d9096);
    const sx = x + ax * 5.2, sz = z + az * 5.2;
    b.box(K('plain'), 1.25, 0.42, 0.3, sx, 6.3, sz, 0x3f4a44, h + Math.PI / 2);
    [0x2bbf6a, 0x333333, 0x333333].forEach((c, i) => b.geo(K('lantern'), SPH, sx + az * (i - 1) * 0.38, 6.3, sz - ax * (i - 1) * 0.38, c, 0, 0, 0, 0.13, 0.13, 0.13));
  }
  { const a = addP(s0.a, s0, s0.len - 30, L0, -hw0 + 0.5); signal(a.x, a.z, Math.atan2(L0.x, L0.z)); }
  { const a = addP(s0.a, s0, s0.len - 54, L0, hw0 - 0.5); signal(a.x, a.z, Math.atan2(-L0.x, -L0.z)); }
  function poles(seg, n, off, from, to) {
    let prev = null;
    for (let t = from; t <= to; t += 26 + r() * 6) {
      const x = seg.a.x + seg.dx * t + n.x * off, z = seg.a.z + seg.dz * t + n.z * off;
      setChunk(x, z);
      b.rod(K('plain'), new THREE.Vector3(x, 0, z), new THREE.Vector3(x, 10.5, z), 0.16, 0xa9a59c, 10);
      b.box(K('plain'), 1.8, 0.12, 0.12, x, 9.6, z, 0x6d6a64, seg.h); b.box(K('plain'), 1.4, 0.12, 0.12, x, 8.9, z, 0x6d6a64, seg.h);
      if (r() < 0.4) b.box(K('plain'), 0.5, 0.8, 0.5, x + n.x * 0.35, 7.6, z + n.z * 0.35, 0x8a8f94);
      if (prev) for (const [k, y] of [[-0.8, 9.66], [0.8, 9.66], [0, 8.96]]) addWire([prev.x + seg.dz * k, y, prev.z - seg.dx * k], [x + seg.dz * k, y, z - seg.dx * k], 0.35);
      prev = { x, z };
    }
  }
  poles(s1, L1, hw1 - 0.3, 22, s1.len - 10);
  poles(s2, L2, -(hw2 - 0.2), 8, s2.len - 4);
  function lanternLine(seg, n, t, hw) {
    const a = { x: seg.a.x + seg.dx * t + n.x * hw, z: seg.a.z + seg.dz * t + n.z * hw }, c = { x: a.x - n.x * hw * 2, z: a.z - n.z * hw * 2 };
    addWire([a.x, 8.4, a.z], [c.x, 8.4, c.z], 0.5); setChunk(a.x, a.z);
    for (let i = 1; i < 7; i++) { const u = i / 7; b.geo(K('lantern'), SPH, a.x + (c.x - a.x) * u, 8.1 - Math.sin(u * Math.PI) * 0.5, a.z + (c.z - a.z) * u, i % 2 ? 0xe0402c : 0xf2ead8, 0, 0, 0, 0.17, 0.24, 0.17); }
  }
  for (let t = 26; t < s1.len - 10; t += 22) lanternLine(s1, L1, t, hw1 + 0.3);
  for (let t = 12; t < s2.len; t += 20) lanternLine(s2, L2, t, hw2 + 0.5);

  // ---------- 遠景：岸和田城（位置は目安）・和泉山脈 ----------
  {
    const x = -95, z = 660;
    const fr = (rb, rt, hh, y, c) => { const g = new THREE.CylinderGeometry(rt, rb, hh, 4, 1); g.rotateY(Math.PI / 4); bf.geo('plain', g, x, y + hh / 2, z, c); };
    fr(30, 23, 10, 0, 0x8f8a7e);
    let y = 10;
    [[26, 10, 18], [19, 8.5, 13.5], [13, 7.5, 9]].forEach(([s, hh, rs], i) => {
      bf.box('plain', s * 0.62, hh * 0.62, s * 0.5, x, y + hh * 0.31, z, 0xf2f0ea);
      y += hh * 0.62; fr(rs * 0.8, rs * 0.42, 2.6, y - 0.4, 0x3b4150); y += 1.6;
      if (i === 2) { bf.box('plain', 3.2, 1.6, 1.2, x, y + 0.6, z, 0x3b4150); for (const s2x of [-1, 1]) bf.box('plain', 0.4, 0.9, 0.4, x + s2x * 1.6, y + 1.8, z, 0xd4a646); }
    });
  }
  const hills = (() => {
    const pos = [], idx = [], N = 70;
    for (let i = 0; i <= N; i++) {
      const a = -1.3 + (i / N) * 2.8, R = 1400, hh = 70 + Math.sin(i * 0.7) * 25 + Math.sin(i * 0.23) * 70 + r() * 15;
      pos.push(Math.cos(a) * R, -5, Math.sin(a) * R, Math.cos(a) * R, hh, Math.sin(a) * R);
      if (i < N) { const k = i * 2; idx.push(k, k + 1, k + 2, k + 2, k + 1, k + 3); }
    }
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(idx);
    return new THREE.Mesh(g, new THREE.MeshBasicMaterial({ color: 0x9fb0c0, fog: false, side: THREE.DoubleSide }));
  })();

  // ---------- まとめ ----------
  const lam = (o) => new THREE.MeshLambertMaterial(Object.assign({ vertexColors: true }, o));
  const mats = {
    plain: lam({}), tile: lam({ map: tex.tile, side: THREE.DoubleSide }), lattice: lam({ map: tex.lattice }), plaster: lam({ map: tex.plaster }),
    house: lam({ map: tex.house }), office: lam({ map: tex.office }), shop: lam({ map: tex.shop }), signs: lam({ map: tex.signs, emissive: 0x333333 }),
    kohaku: lam({ map: tex.kohaku, side: THREE.DoubleSide }), lantern: lam({ emissive: 0x551a0c }), asphalt: lam({ map: tex.asphalt, polygonOffset: true, polygonOffsetFactor: -1 }),
    walk: lam({ map: tex.walk }),
  };
  const group = new THREE.Group();
  const town = b.build(mats, { cast: true, receive: true }); group.add(town);
  const flat = bf.build(mats, { receive: true }); group.add(flat, hills);
  const wg = new THREE.BufferGeometry(); wg.setAttribute('position', new THREE.Float32BufferAttribute(wires.flat(), 3));
  group.add(new THREE.LineSegments(wg, new THREE.LineBasicMaterial({ color: 0x1d1d1f })));

  // 見物人（インスタンス描画）
  const pg = personGeos(), n = spect.length;
  const cloth = [0x2b3a55, 0xe9e4d8, 0x8c2f2f, 0x3f5f3f, 0x2a2a2a, 0xc9a86a, 0x5b6f8f, 0xf2f2f2, 0x7a4a6a, 0x1f4f6f, 0xd46a2a, 0x1c2a4a, 0xb8321f, 0x5d7a2a];
  const mT = new THREE.InstancedMesh(pg.torso, lam({}), n), mL = new THREE.InstancedMesh(pg.legs, lam({ color: 0x3a4152 }), n), mH = new THREE.InstancedMesh(pg.head, lam({}), n);
  const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), pp = new THREE.Vector3(), sc = new THREE.Vector3(), c = new THREE.Color();
  spect.forEach((o, i) => {
    o.sc = 0.9 + r() * 0.16; o.ph = r() * 6.28;
    m4.compose(pp.set(o.x, o.y, o.z), q.setFromEuler(e.set(0, o.ry, 0)), sc.set(o.sc, o.sc, o.sc));
    mT.setMatrixAt(i, m4); mL.setMatrixAt(i, m4); mH.setMatrixAt(i, m4); mT.setColorAt(i, c.set(pick(cloth)));
  });
  for (const m of [mT, mL, mH]) { m.frustumCulled = false; m.instanceMatrix.setUsage(THREE.DynamicDrawUsage); group.add(m); }

  // 当たり判定の格子
  const CELL = 8, grid = new Map();
  segs.forEach((s, i) => {
    const x0 = Math.floor((Math.min(s[0], s[2]) - 2) / CELL), x1 = Math.floor((Math.max(s[0], s[2]) + 2) / CELL);
    const z0 = Math.floor((Math.min(s[1], s[3]) - 2) / CELL), z1 = Math.floor((Math.max(s[1], s[3]) + 2) / CELL);
    for (let gx = x0; gx <= x1; gx++) for (let gz = z0; gz <= z1; gz++) { const k = gx + ',' + gz; (grid.get(k) || grid.set(k, []).get(k)).push(i); }
  });
  const spots = [
    { x: J.x + s0.dx * 30 - L0.x * 20, y: 7.5, z: J.z + s0.dz * 30 - L0.z * 20 },
    { x: Kc.x + s1.dx * 8 + L1.x * 1, y: 8.5, z: Kc.z + s1.dz * 8 + L1.z * 1 },
  ];
  return { group, segs, grid, CELL, spectators: { list: spect, meshes: [mT, mL, mH] }, spots };
}
