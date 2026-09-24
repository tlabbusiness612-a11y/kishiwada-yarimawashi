// 共通ユーティリティ：乱数・ジオメトリ結合・キャンバステクスチャ・人物ジオメトリ
import * as THREE from 'three';

export const rng = (seed) => {
  let s = (seed >>> 0) || 0x9e3779b9;
  return () => { s ^= s << 13; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
};
export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const lerp = (a, b, t) => a + (b - a) * t;
export const wrapAngle = (a) => { while (a > Math.PI) a -= Math.PI * 2; while (a < -Math.PI) a += Math.PI * 2; return a; };

// ---------- ジオメトリ結合 ----------
const _m = new THREE.Matrix4(), _q = new THREE.Quaternion(), _p = new THREE.Vector3(), _s = new THREE.Vector3(), _e = new THREE.Euler(0, 0, 0, 'YXZ');
const UNIT_BOX = new THREE.BoxGeometry(1, 1, 1).toNonIndexed();

export function mergeGeos(list) {
  let n = 0;
  for (const g of list) n += g.attributes.position.count;
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), uv = new Float32Array(n * 2), col = new Float32Array(n * 3);
  let o = 0;
  for (const g of list) {
    pos.set(g.attributes.position.array, o * 3); nor.set(g.attributes.normal.array, o * 3);
    uv.set(g.attributes.uv.array, o * 2); col.set(g.attributes.color.array, o * 3);
    o += g.attributes.position.count;
  }
  const m = new THREE.BufferGeometry();
  m.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  m.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  m.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  m.setAttribute('color', new THREE.BufferAttribute(col, 3));
  m.computeBoundingSphere();
  return m;
}

export class Batch {
  constructor() { this.parts = {}; this.parent = null; }
  within(matrix, fn) { const prev = this.parent; this.parent = matrix; fn(); this.parent = prev; }
  add(key, geo, matrix, color) {
    const g = geo.index ? geo.toNonIndexed() : geo.clone();
    if (matrix) g.applyMatrix4(matrix);
    if (this.parent) g.applyMatrix4(this.parent);
    const n = g.attributes.position.count;
    if (!g.attributes.normal) g.computeVertexNormals();
    if (!g.attributes.uv) g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(n * 2), 2));
    const c = new THREE.Color(color === undefined ? 0xffffff : color), a = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { a[i * 3] = c.r; a[i * 3 + 1] = c.g; a[i * 3 + 2] = c.b; }
    g.setAttribute('color', new THREE.BufferAttribute(a, 3));
    (this.parts[key] || (this.parts[key] = [])).push(g);
    return g;
  }
  box(key, w, h, d, x, y, z, color, ry, rx, rz) {
    _e.set(rx || 0, ry || 0, rz || 0);
    _m.compose(_p.set(x, y, z), _q.setFromEuler(_e), _s.set(w, h, d));
    return this.add(key, UNIT_BOX, _m, color);
  }
  quad(key, w, h, x, y, z, ry, color, u0, v0, u1, v1) {
    const g = new THREE.PlaneGeometry(w, h), uv = g.attributes.uv;
    uv.setXY(0, u0, v1); uv.setXY(1, u1, v1); uv.setXY(2, u0, v0); uv.setXY(3, u1, v0);
    _e.set(0, ry || 0, 0);
    _m.compose(_p.set(x, y, z), _q.setFromEuler(_e), _s.set(1, 1, 1));
    return this.add(key, g, _m, color);
  }
  geo(key, geo, x, y, z, color, ry, rx, rz, sx, sy, sz) {
    _e.set(rx || 0, ry || 0, rz || 0);
    _m.compose(_p.set(x, y, z), _q.setFromEuler(_e), _s.set(sx || 1, sy || sx || 1, sz || sx || 1));
    return this.add(key, geo, _m, color);
  }
  rod(key, a, b, r, color, seg) {
    const d = new THREE.Vector3().subVectors(b, a), len = d.length();
    const g = new THREE.CylinderGeometry(r, r, len, seg || 6, 1, false); g.translate(0, len / 2, 0);
    _q.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
    _m.compose(a, _q, _s.set(1, 1, 1));
    return this.add(key, g, _m, color);
  }
  // 三角形の配列（[x,y,z]×3 の並び）を直接追加。uv は任意
  tris(key, pos, uv, color) {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    if (uv) g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.computeVertexNormals();
    return this.add(key, g, null, color);
  }
  build(materials, opts = {}) {
    const group = new THREE.Group();
    for (const key in this.parts) {
      const mesh = new THREE.Mesh(mergeGeos(this.parts[key]), materials[key.split('|')[0]]);
      mesh.castShadow = !!opts.cast; mesh.receiveShadow = !!opts.receive; mesh.name = key;
      group.add(mesh);
    }
    this.parts = {};
    return group;
  }
}
export function matrixAt(x, y, z, ry, s) {
  return new THREE.Matrix4().compose(new THREE.Vector3(x, y, z), new THREE.Quaternion().setFromEuler(new THREE.Euler(0, ry || 0, 0)), new THREE.Vector3(s || 1, s || 1, s || 1));
}

// ---------- テクスチャ ----------
const cv = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };
function tx(c, { srgb = true, aniso = 8, clampEdge = false } = {}) {
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = clampEdge ? THREE.ClampToEdgeWrapping : THREE.RepeatWrapping;
  t.anisotropy = aniso; if (srgb) t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
// 高さ画像から法線マップを作る
function normalFrom(hc, strength) {
  const w = hc.width, h = hc.height, src = hc.getContext('2d').getImageData(0, 0, w, h).data;
  const [c, g] = cv(w, h), img = g.createImageData(w, h), d = img.data;
  const H = (x, y) => src[(((y + h) % h) * w + ((x + w) % w)) * 4] / 255;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const dx = (H(x + 1, y) - H(x - 1, y)) * strength, dy = (H(x, y + 1) - H(x, y - 1)) * strength;
    const l = Math.hypot(dx, dy, 1), i = (y * w + x) * 4;
    d[i] = (-dx / l * 0.5 + 0.5) * 255; d[i + 1] = (dy / l * 0.5 + 0.5) * 255; d[i + 2] = (1 / l * 0.5 + 0.5) * 255; d[i + 3] = 255;
  }
  g.putImageData(img, 0, 0);
  return tx(c, { srgb: false });
}
const JP = '"Hiragino Sans","Hiragino Kaku Gothic ProN","Yu Gothic","Meiryo",sans-serif';
const JPM = '"Hiragino Mincho ProN","Yu Mincho","MS Mincho",serif';

export function makeTextures() {
  const r = rng(7), out = {};

  { // 欅の木目
    const [c, g] = cv(512, 512);
    g.fillStyle = '#946038'; g.fillRect(0, 0, 512, 512);
    for (let i = 0; i < 220; i++) {
      const y0 = r() * 512, a = 0.04 + r() * 0.12;
      g.strokeStyle = r() < 0.62 ? `rgba(62,32,12,${a})` : `rgba(206,150,96,${a})`;
      g.lineWidth = 0.6 + r() * 2.2; g.beginPath();
      for (let x = 0; x <= 512; x += 8) g.lineTo(x, y0 + Math.sin(x * 0.02 + i) * 5 + Math.sin(x * 0.09 + i * 3) * 1.4);
      g.stroke();
    }
    out.wood = tx(c);
  }
  { // 彫り物：高さ画像（雲・波・人物の塊）→ 色と法線
    const [hc, hg] = cv(512, 256);
    hg.fillStyle = '#3a3a3a'; hg.fillRect(0, 0, 512, 256);
    const blob = (x, y, rad, v) => { const gr = hg.createRadialGradient(x, y, 0, x, y, rad); gr.addColorStop(0, `rgba(${v},${v},${v},1)`); gr.addColorStop(1, 'rgba(58,58,58,0)'); hg.fillStyle = gr; hg.beginPath(); hg.arc(x, y, rad, 0, 7); hg.fill(); };
    for (let i = 0; i < 90; i++) blob(r() * 512, r() * 256, 8 + r() * 30, 150 + r() * 105);
    hg.lineCap = 'round';
    for (let i = 0; i < 80; i++) { const x = r() * 512, y = r() * 256, rad = 5 + r() * 20; hg.strokeStyle = `rgba(10,10,10,${0.5 + r() * 0.4})`; hg.lineWidth = 1.5 + r() * 2; hg.beginPath(); hg.arc(x, y, rad, r() * 6, r() * 6 + 3.5); hg.stroke(); }
    hg.strokeStyle = '#111'; hg.lineWidth = 8; hg.strokeRect(4, 4, 504, 248);
    out.carveN = normalFrom(hc, 4.0);
    const [c, g] = cv(512, 256), src = hg.getImageData(0, 0, 512, 256).data, img = g.createImageData(512, 256);
    for (let i = 0; i < src.length; i += 4) { const v = src[i] / 255; img.data[i] = 70 + v * 120; img.data[i + 1] = 40 + v * 78; img.data[i + 2] = 20 + v * 44; img.data[i + 3] = 255; }
    g.putImageData(img, 0, 0);
    out.carve = tx(c);
  }
  { // 燻し瓦（1リピート＝2m）
    const [c, g] = cv(128, 128);
    g.fillStyle = '#3f464f'; g.fillRect(0, 0, 128, 128);
    for (let y = 0; y < 128; y += 16) {
      for (let x = 0; x < 128; x += 16) { const gr = g.createLinearGradient(x, 0, x + 16, 0); gr.addColorStop(0, '#2a2f36'); gr.addColorStop(0.5, '#646c77'); gr.addColorStop(1, '#2a2f36'); g.fillStyle = gr; g.fillRect(x, y, 16, 14); }
      g.fillStyle = 'rgba(12,14,18,.85)'; g.fillRect(0, y + 13, 128, 3);
    }
    out.tile = tx(c);
  }
  { // 町家1階：格子（1リピート＝幅2m×高さ3m）
    const [c, g] = cv(128, 192);
    g.fillStyle = '#17100a'; g.fillRect(0, 0, 128, 192);
    for (let x = 0; x < 128; x += 8) { g.fillStyle = '#5e3c22'; g.fillRect(x + 1, 22, 5, 164); g.fillStyle = 'rgba(255,220,170,.14)'; g.fillRect(x + 1, 22, 1, 164); }
    g.fillStyle = '#3b2515'; g.fillRect(0, 0, 128, 22); g.fillRect(0, 184, 128, 8);
    out.lattice = tx(c);
  }
  { // 町家2階：漆喰と虫籠窓（1リピート＝幅4m×高さ2.6m）
    const [c, g] = cv(256, 168);
    g.fillStyle = '#e6dcc6'; g.fillRect(0, 0, 256, 168);
    for (let i = 0; i < 40; i++) { g.fillStyle = `rgba(120,100,70,${r() * 0.05})`; g.fillRect(r() * 256, r() * 168, 30 + r() * 60, 10 + r() * 40); }
    g.fillStyle = '#2b2520'; g.beginPath(); g.roundRect(70, 56, 116, 56, 14); g.fill();
    g.fillStyle = '#d8ceb6'; for (let x = 76; x < 184; x += 12) g.fillRect(x, 56, 6, 56);
    g.fillStyle = '#4b3524'; g.fillRect(0, 158, 256, 10);
    out.plaster = tx(c);
  }
  { // 住宅の外壁（1リピート＝幅4m×1階3m、白っぽくして頂点色で着色）
    const [c, g] = cv(128, 96);
    g.fillStyle = '#f1efe9'; g.fillRect(0, 0, 128, 96);
    for (let y = 0; y < 96; y += 4) { g.fillStyle = 'rgba(0,0,0,.035)'; g.fillRect(0, y, 128, 1); }
    g.fillStyle = '#7d8790'; g.fillRect(36, 24, 56, 40);
    g.fillStyle = '#c9d6df'; g.fillRect(40, 28, 23, 32); g.fillRect(65, 28, 23, 32);
    g.fillStyle = 'rgba(255,255,255,.35)'; g.fillRect(40, 28, 8, 32);
    g.fillStyle = '#8e8a84'; g.fillRect(32, 64, 64, 4);
    out.house = tx(c);
  }
  { // ビル・マンションの外壁（1リピート＝幅3.6m×1階3.2m）
    const [c, g] = cv(128, 112);
    g.fillStyle = '#ebe8e2'; g.fillRect(0, 0, 128, 112);
    g.fillStyle = '#44525e'; g.fillRect(12, 26, 104, 58);
    g.fillStyle = 'rgba(210,228,240,.3)'; g.fillRect(12, 26, 44, 58);
    g.fillStyle = '#dcd8d0'; g.fillRect(62, 26, 4, 58); g.fillRect(6, 84, 116, 7);
    out.office = tx(c);
  }
  { // 店先 2×2（1コマ＝幅4m×高さ3m）
    const [c, g] = cv(512, 384);
    const cell = (i, j, fn) => { g.save(); g.translate(i * 256, j * 192); fn(); g.restore(); };
    cell(0, 0, () => { g.fillStyle = '#6a6259'; g.fillRect(0, 0, 256, 192); g.fillStyle = '#2b3a44'; g.fillRect(10, 30, 236, 150); for (let k = 0; k < 14; k++) { g.fillStyle = `hsl(${r() * 360},45%,${45 + r() * 25}%)`; g.fillRect(20 + r() * 200, 110 + r() * 50, 14 + r() * 20, 10 + r() * 20); } g.fillStyle = 'rgba(220,235,245,.22)'; g.fillRect(10, 30, 90, 150); g.fillStyle = '#ddd'; g.fillRect(126, 30, 4, 150); });
    cell(1, 0, () => { g.fillStyle = '#9ba1a4'; g.fillRect(0, 0, 256, 192); for (let y = 20; y < 192; y += 6) { g.fillStyle = 'rgba(0,0,0,.18)'; g.fillRect(0, y, 256, 2); } g.fillStyle = '#5a5f63'; g.fillRect(0, 0, 256, 20); });
    cell(0, 1, () => { g.fillStyle = '#3a2a1e'; g.fillRect(0, 0, 256, 192); g.fillStyle = '#1d2a4a'; g.fillRect(40, 20, 176, 70); g.fillStyle = '#f3ead8'; g.font = `bold 34px ${JPM}`; g.textAlign = 'center'; g.fillText('御食事処', 128, 68); g.strokeStyle = '#3a2a1e'; g.lineWidth = 3; for (let x = 84; x < 216; x += 44) { g.beginPath(); g.moveTo(x, 40); g.lineTo(x, 90); g.stroke(); } g.fillStyle = '#e8c890'; g.fillRect(40, 100, 176, 92); });
    cell(1, 1, () => { g.fillStyle = '#b8a98f'; g.fillRect(0, 0, 256, 192); for (let y = 0; y < 192; y += 12) for (let x = (y / 12) % 2 * 12; x < 256; x += 24) { g.fillStyle = 'rgba(0,0,0,.06)'; g.fillRect(x, y, 22, 10); } g.fillStyle = '#4b3a2c'; g.fillRect(150, 50, 70, 142); g.fillStyle = '#2d3b44'; g.fillRect(30, 60, 90, 70); });
    out.shop = tx(c, { clampEdge: true });
  }
  { // 看板（4列×8行、1枚 256×64）
    const [c, g] = cv(1024, 512);
    const words = ['たこ焼', '喫茶', '鮮魚', '呉服', '酒店', '薬局', '理容', '食堂', '青果', 'お好み焼', '精肉', '寝具', '時計・眼鏡', '和菓子', '印刷', '文具', '履物', '洋品', '仏壇', '写真館', 'かしわ', '豆腐', '銭湯', '居酒屋', '中華そば', '金物', '畳', 'クリーニング', '書店', '整骨院', '不動産', '祭用品'];
    const bgs = [['#f3ead8', '#1a2238'], ['#b62c20', '#fff'], ['#1d3b6e', '#fff'], ['#e9c14a', '#2a1a0a'], ['#2d6a3e', '#fff'], ['#fff', '#b62c20']];
    words.forEach((w, i) => {
      const x = (i % 4) * 256, y = Math.floor(i / 4) * 64, s = bgs[i % bgs.length];
      g.fillStyle = s[0]; g.fillRect(x + 2, y + 2, 252, 60); g.strokeStyle = 'rgba(0,0,0,.35)'; g.lineWidth = 3; g.strokeRect(x + 4, y + 4, 248, 56);
      g.fillStyle = s[1]; g.textAlign = 'center'; g.textBaseline = 'middle'; g.font = `bold ${w.length > 5 ? 30 : 38}px ${JP}`; g.fillText(w, x + 128, y + 34);
    });
    out.signs = tx(c, { clampEdge: true }); out.signCount = words.length;
  }
  { // アスファルト（1リピート＝8m）
    const [c, g] = cv(256, 256);
    g.fillStyle = '#56575a'; g.fillRect(0, 0, 256, 256);
    for (let i = 0; i < 6000; i++) { const v = 60 + r() * 55; g.fillStyle = `rgba(${v},${v},${v + 4},.5)`; g.fillRect(r() * 256, r() * 256, 1.5, 1.5); }
    for (let i = 0; i < 8; i++) { g.fillStyle = 'rgba(25,25,28,.12)'; g.beginPath(); g.ellipse(r() * 256, r() * 256, 20 + r() * 40, 8 + r() * 20, r() * 3, 0, 7); g.fill(); }
    out.asphalt = tx(c, { aniso: 16 });
  }
  { // 歩道・地面（1リピート＝2m）
    const [c, g] = cv(64, 64);
    g.fillStyle = '#a39d92'; g.fillRect(0, 0, 64, 64);
    g.strokeStyle = '#857f75'; g.lineWidth = 2; g.strokeRect(1, 1, 31, 31); g.strokeRect(33, 1, 30, 31); g.strokeRect(1, 33, 31, 30); g.strokeRect(33, 33, 30, 30);
    out.walk = tx(c);
  }
  { // 紅白幕
    const [c, g] = cv(64, 8);
    for (let i = 0; i < 4; i++) { g.fillStyle = i % 2 ? '#f4f1ea' : '#c8261c'; g.fillRect(i * 16, 0, 16, 8); }
    out.kohaku = tx(c);
  }
  return out;
}

// ---------- 人物（インスタンス用） ----------
export function personGeos() {
  const b = new Batch();
  b.box('t', 0.44, 0.6, 0.27, 0, 1.13, 0, 0xffffff);
  b.box('t', 0.5, 0.2, 0.3, 0, 1.36, 0, 0xffffff);
  b.box('t', 0.12, 0.56, 0.14, -0.3, 1.1, 0.06, 0xdddddd, 0, -0.35, 0.12);
  b.box('t', 0.12, 0.56, 0.14, 0.3, 1.1, 0.06, 0xdddddd, 0, -0.35, -0.12);
  b.box('t', 0.46, 0.08, 0.29, 0, 0.86, 0, 0x777777);
  b.box('l', 0.17, 0.8, 0.2, -0.11, 0.42, 0, 0xffffff); b.box('l', 0.17, 0.8, 0.2, 0.11, 0.42, 0, 0xffffff);
  b.box('l', 0.17, 0.08, 0.28, -0.11, 0.04, 0.04, 0x222222); b.box('l', 0.17, 0.08, 0.28, 0.11, 0.04, 0.04, 0x222222);
  b.geo('h', new THREE.SphereGeometry(0.125, 8, 6), 0, 1.56, 0, 0xe0b48f);
  b.geo('h', new THREE.SphereGeometry(0.132, 8, 3, 0, Math.PI * 2, 0, Math.PI * 0.45), 0, 1.575, -0.01, 0x1c1714);
  b.geo('hb', new THREE.CylinderGeometry(0.134, 0.134, 0.045, 8, 1, true), 0, 1.6, 0, 0xf4f1ea);
  const p = b.parts;
  return { torso: mergeGeos(p.t), legs: mergeGeos(p.l), head: mergeGeos(p.h), headBand: mergeGeos(p.h.concat(p.hb)) };
}
