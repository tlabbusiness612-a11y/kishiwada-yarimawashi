// コース：大阪臨海線を北東から南西へ → カンカン場（岸和田港交差点）で左へやりまわし → 路地を東南東へ → 紀州街道で右へ
// 座標は data/kishiwada.json と同じローカル座標（原点 北緯34.4655° 東経135.3725°、x=東・z=南、単位 m）。
// 各点は PLATEAU の道路面と OpenStreetMap の道路線から読み取った道路中心。
import { clamp } from './util.js';

const P = [
  { x: 41.6, z: -240.0 },  // 臨海線・港緑町あたり（出発）
  { x: -74.8, z: -30.0 },  // カンカン場（岸和田港交差点）
  { x: 78.8, z: 95.2 },    // 紀州街道との辻
  { x: -4.2, z: 196.6 },   // 紀州街道を南西へ
];

export const SEG = [];
{
  let acc = 0;
  for (let i = 0; i < P.length - 1; i++) {
    const a = P[i], b = P[i + 1], len = Math.hypot(b.x - a.x, b.z - a.z);
    const dx = (b.x - a.x) / len, dz = (b.z - a.z) / len;
    SEG.push({ a, b, len, dx, dz, s0: acc, h: Math.atan2(dx, dz) });
    acc += len;
  }
}

export const COURSE = {
  pts: P,
  start: { x: P[0].x, z: P[0].z, h: SEG[0].h },
  corners: [
    { s: SEG[1].s0, x: P[1].x, z: P[1].z, name: 'カンカン場', sub: '岸和田港交差点から路地へ', turn: '左', dir: 1, w: 30 },
    { s: SEG[2].s0, x: P[2].x, z: P[2].z, name: '紀州街道', sub: '路地から紀州街道へ', turn: '右', dir: -1, w: 12 },
  ],
  finishS: SEG[2].s0 + 72,
  // 道幅の目安（中心からの片側、見物人・柵を置く位置）
  halfWidth: [14.5, 5.2, 3.6],
};

export function project(x, z) {
  let best = null;
  for (const s of SEG) {
    const t = clamp((x - s.a.x) * s.dx + (z - s.a.z) * s.dz, 0, s.len);
    const d = Math.hypot(x - (s.a.x + s.dx * t), z - (s.a.z + s.dz * t));
    if (!best || d < best.d - 1e-6) best = { d, s: s.s0 + t, seg: s, t };
  }
  return best;
}
export function pointAt(s) {
  for (const g of SEG) if (s <= g.s0 + g.len || g === SEG[SEG.length - 1]) { const t = s - g.s0; return { x: g.a.x + g.dx * t, z: g.a.z + g.dz * t, seg: g }; }
}
