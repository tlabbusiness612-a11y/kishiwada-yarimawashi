// 岸和田型「下地車」— 前に小屋根、後ろに大屋根。欅の白木、土呂幕・枡合・見送りの彫り物、前梃子・後梃子、綱。
import * as THREE from 'three';
import { Batch, matrixAt } from './util.js';

const V = (x, y, z) => new THREE.Vector3(x, y, z);

// 反りのある切妻屋根。u: -1..1（幅方向）、t: 0..1（前後方向）
function roofParts(w, l, ridgeY, eaveY, flare) {
  const nx = 20, nz = 12;
  const hAt = (u, t) => {
    const a = Math.abs(u), e = Math.abs(2 * t - 1);
    return eaveY + (ridgeY - eaveY) * Math.pow(1 - a, 1.7) + flare * Math.pow(e, 4) * (0.25 + 0.75 * a * a);
  };
  const surf = (off) => {
    const pos = [], uv = [], idx = [];
    for (let j = 0; j <= nz; j++) {
      const t = j / nz, z = -l / 2 + t * l;
      for (let i = 0; i <= nx; i++) { const u = i / nx * 2 - 1; pos.push(u * w / 2, hAt(u, t) + off, z); uv.push(t * l * 0.7, u * w * 0.35); }
    }
    for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) {
      const a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
      if (off >= 0) idx.push(a, c, b, b, c, d); else idx.push(a, b, c, b, d, c);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx); g.computeVertexNormals(); return g;
  };
  // 縁の板（t 固定＝破風、u 固定＝軒先）
  const ribbon = (fixed, val, drop) => {
    const p = [], q = [], id = [], n = fixed === 't' ? nx : nz;
    for (let i = 0; i <= n; i++) {
      let x, y, z;
      if (fixed === 't') { const u = i / n * 2 - 1; x = u * w / 2; z = -l / 2 + val * l; y = hAt(u, val); }
      else { const t = i / n; x = val * w / 2; z = -l / 2 + t * l; y = hAt(val, t); }
      p.push(x, y + 0.05, z, x, y - drop, z); q.push(i / n, 1, i / n, 0);
    }
    for (let i = 0; i < n; i++) { const a = i * 2; id.push(a, a + 1, a + 2, a + 2, a + 1, a + 3); }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(p, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(q, 2));
    g.setIndex(id); g.computeVertexNormals(); return g;
  };
  const gable = () => {
    const s = new THREE.Shape(), n = 14, span = 0.82;
    s.moveTo(-span * w / 2, eaveY - 0.3);
    for (let i = 0; i <= n; i++) { const u = -span + (2 * span) * i / n; s.lineTo(u * w / 2, hAt(u, 0.5) - 0.18); }
    s.lineTo(span * w / 2, eaveY - 0.3);
    const g = new THREE.ShapeGeometry(s), uvs = g.attributes.uv;
    for (let i = 0; i < uvs.count; i++) uvs.setXY(i, uvs.getX(i) * 0.35 + 0.5, uvs.getY(i) * 0.5);
    return g;
  };
  return { top: surf(0), under: surf(-0.11), ribbon, gable, hAt };
}

const HEAD = new THREE.SphereGeometry(0.125, 10, 8);
const HAIR = new THREE.SphereGeometry(0.132, 10, 6, 0, Math.PI * 2, 0, Math.PI * 0.45);
const BAND = new THREE.CylinderGeometry(0.134, 0.134, 0.045, 12, 1, true);

// 法被姿の人物（鳴物方・梃子方）
function person(b, x, y, z, ry, happi, pose, legs = 0x1f2638) {
  const lean = pose === 'lean' ? 0.5 : pose === 'hold' ? 0.25 : 0;
  b.within(matrixAt(x, y, z, ry), () => {
    if (pose === 'sit') {
      b.box('cloth', 0.17, 0.16, 0.5, -0.11, 0.08, 0.2, legs); b.box('cloth', 0.17, 0.16, 0.5, 0.11, 0.08, 0.2, legs);
      b.box('cloth', 0.44, 0.58, 0.27, 0, 0.45, 0, happi);
      b.box('cloth', 0.11, 0.45, 0.13, -0.29, 0.5, 0.15, happi, 0, -0.9); b.box('cloth', 0.11, 0.45, 0.13, 0.29, 0.5, 0.15, happi, 0, -0.9);
      b.geo('skin', HEAD, 0, 0.88, 0, 0xe0b48f); b.geo('cloth', HAIR, 0, 0.9, -0.01, 0x1c1714); b.geo('cloth', BAND, 0, 0.93, 0, 0xf4f1ea);
      return;
    }
    const hip = 0.84, c = Math.cos(lean), s = Math.sin(lean), stride = lean > 0 ? 0.35 : 0;
    b.box('cloth', 0.17, 0.84, 0.2, -0.11, 0.42, -stride * 0.5, legs, 0, stride);
    b.box('cloth', 0.17, 0.84, 0.2, 0.11, 0.42, stride * 0.3, legs, 0, -stride * 0.6);
    b.box('cloth', 0.44, 0.6, 0.27, 0, hip + 0.3 * c, 0.3 * s, happi, 0, lean);
    b.box('cloth', 0.11, 0.55, 0.13, -0.29, hip + 0.3 * c, 0.3 * s + 0.12, happi, 0, lean - 0.9);
    b.box('cloth', 0.11, 0.55, 0.13, 0.29, hip + 0.3 * c, 0.3 * s + 0.12, happi, 0, lean - 0.9);
    b.geo('skin', HEAD, 0, hip + 0.72 * c, 0.72 * s, 0xe0b48f);
    b.geo('cloth', HAIR, 0, hip + 0.74 * c, 0.72 * s - 0.01, 0x1c1714);
    b.geo('cloth', BAND, 0, hip + 0.76 * c, 0.72 * s, 0xf4f1ea);
  });
}

// 大工方：屋根の上で団扇を振り、跳ぶ
function makeDaiku(happi) {
  const g = new THREE.Group();
  const mat = (c, r = 0.85) => new THREE.MeshStandardMaterial({ color: c, roughness: r });
  const mHappi = mat(happi), mLeg = mat(0xf1ede4), mSkin = mat(0xe0b48f, 0.6), mHair = mat(0x1c1714), mTabi = mat(0x1b1b1b);
  const add = (geo, m, x, y, z, parent = g) => { const o = new THREE.Mesh(geo, m); o.position.set(x, y, z); o.castShadow = true; parent.add(o); return o; };
  const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
  const legL = add(box(0.17, 0.82, 0.2), mLeg, -0.11, 0.45, 0), legR = add(box(0.17, 0.82, 0.2), mLeg, 0.11, 0.45, 0);
  add(box(0.17, 0.08, 0.28), mTabi, -0.11, 0.04, 0.04); add(box(0.17, 0.08, 0.28), mTabi, 0.11, 0.04, 0.04);
  const body = add(box(0.44, 0.6, 0.27), mHappi, 0, 1.15, 0);
  add(HEAD, mSkin, 0, 1.58, 0); add(HAIR, mHair, 0, 1.595, -0.01); add(BAND, mLeg, 0, 1.62, 0);
  const arm = new THREE.Group(); arm.position.set(0.3, 1.38, 0); g.add(arm);
  add(box(0.11, 0.55, 0.13), mHappi, 0, 0.24, 0, arm);
  const fan = add(new THREE.CircleGeometry(0.19, 20), new THREE.MeshStandardMaterial({ color: 0xf4efe4, side: THREE.DoubleSide, roughness: 0.7 }), 0, 0.68, 0, arm);
  add(new THREE.RingGeometry(0.15, 0.19, 20), new THREE.MeshStandardMaterial({ color: 0xc23a26, side: THREE.DoubleSide }), 0, 0.68, 0.002, arm);
  const armL = add(box(0.11, 0.55, 0.13), mHappi, -0.3, 1.1, 0.05); armL.rotation.x = -0.4;
  return { group: g, arm, legL, legR, body, armL, fan };
}

export function buildDanjiri(tex, happi) {
  const root = new THREE.Group(), model = new THREE.Group();
  root.add(model); model.scale.setScalar(0.92);
  const std = (o) => new THREE.MeshStandardMaterial(Object.assign({ vertexColors: true, roughness: 0.62, metalness: 0 }, o));
  const mats = {
    wood: std({ map: tex.wood }),
    carve: std({ map: tex.carve, normalMap: tex.carveN, normalScale: new THREE.Vector2(1.4, 1.4), roughness: 0.7 }),
    roof: std({ map: tex.wood, side: THREE.DoubleSide, roughness: 0.5 }),
    gold: new THREE.MeshStandardMaterial({ color: 0xd8ac4c, metalness: 0.85, roughness: 0.32 }),
    plain: std({ roughness: 0.8 }),
    cloth: std({ roughness: 0.92 }),
    skin: std({ roughness: 0.6 }),
  };
  const W = 0xffffff, DARK = 0x9a8878, AGED = 0xd2c0ac;
  const b = new Batch();

  // 台・地覆・土呂幕
  b.box('wood', 1.95, 0.36, 3.9, 0, 0.54, 0, DARK);
  b.box('wood', 2.14, 0.26, 0.34, 0, 0.56, 1.99, DARK); b.box('wood', 2.14, 0.26, 0.34, 0, 0.56, -1.99, DARK);
  b.box('carve', 0.1, 0.58, 3.4, -1.02, 1.02, 0, W); b.box('carve', 0.1, 0.58, 3.4, 1.02, 1.02, 0, W);
  b.box('carve', 1.95, 0.58, 0.1, 0, 1.02, 1.9, W); b.box('carve', 1.95, 0.58, 0.1, 0, 1.02, -1.9, W);
  for (const sx of [-1, 1]) b.box('wood', 0.14, 0.12, 3.5, sx * 1.02, 0.7, 0, DARK);
  // 縁と高欄（擬宝珠つき）
  b.box('wood', 2.46, 0.1, 4.12, 0, 1.35, 0, AGED);
  for (const sx of [-1, 1]) {
    b.box('wood', 0.08, 0.07, 3.98, sx * 1.17, 1.71, 0, AGED); b.box('wood', 0.05, 0.04, 3.98, sx * 1.17, 1.52, 0, AGED);
    for (let z = -1.95; z <= 1.96; z += 0.39) b.box('wood', 0.07, 0.36, 0.07, sx * 1.17, 1.54, z, AGED);
    for (const sz of [-1, 1]) { b.geo('gold', new THREE.SphereGeometry(0.065, 10, 8), sx * 1.17, 1.8, sz * 1.98, W); b.geo('gold', new THREE.ConeGeometry(0.03, 0.08, 8), sx * 1.17, 1.88, sz * 1.98, W); }
  }
  b.box('wood', 2.36, 0.07, 0.08, 0, 1.71, 2.03, AGED); b.box('wood', 2.36, 0.07, 0.08, 0, 1.71, -2.03, AGED);

  // 前の間：柱・虹梁・枡合・太鼓
  for (const sx of [-1, 1]) for (const z of [0.62, 1.82]) b.box('wood', 0.15, 1.3, 0.15, sx * 0.82, 2.0, z, W);
  b.box('wood', 1.8, 0.14, 0.16, 0, 2.3, 1.86, AGED);
  b.box('carve', 1.78, 0.3, 0.1, 0, 2.52, 1.86, W);
  b.box('carve', 0.1, 0.3, 1.3, -0.86, 2.52, 1.22, W); b.box('carve', 0.1, 0.3, 1.3, 0.86, 2.52, 1.22, W);
  const drum = new THREE.CylinderGeometry(0.3, 0.3, 0.44, 24); drum.rotateX(Math.PI / 2);
  b.geo('plain', drum, 0, 1.78, 1.2, 0x7d2a17);
  b.geo('plain', new THREE.CircleGeometry(0.29, 24), 0, 1.78, 1.43, 0xe8dcc0);
  b.geo('plain', new THREE.CircleGeometry(0.29, 24), 0, 1.78, 0.97, 0xe8dcc0, Math.PI);
  for (let k = 0; k < 10; k++) { const a = k / 10 * Math.PI * 2; b.geo('gold', new THREE.SphereGeometry(0.018, 6, 4), Math.cos(a) * 0.3, 1.78 + Math.sin(a) * 0.3, 1.43, W); }

  // 後ろの間：柱・枡合・見送り
  for (const sx of [-1, 1]) for (const z of [-1.86, 0.56]) b.box('wood', 0.17, 1.78, 0.17, sx * 0.9, 2.24, z, W);
  b.box('carve', 0.1, 0.34, 2.5, -0.95, 2.95, -0.65, W); b.box('carve', 0.1, 0.34, 2.5, 0.95, 2.95, -0.65, W);
  b.box('carve', 1.9, 0.34, 0.1, 0, 2.95, 0.6, W); b.box('carve', 1.9, 0.34, 0.1, 0, 2.95, -1.9, W);
  b.box('wood', 1.9, 0.12, 0.14, 0, 2.72, 0.6, AGED);
  b.box('carve', 1.62, 1.28, 0.1, 0, 2.05, -1.88, W);
  b.box('plain', 1.5, 1.1, 2.0, 0, 1.95, -0.75, 0x2b1d13);
  b.box('carve', 0.28, 0.24, 2.7, -1.07, 3.02, -0.5, AGED); b.box('carve', 0.28, 0.24, 2.7, 1.07, 3.02, -0.5, AGED);
  b.box('carve', 0.22, 0.2, 1.3, -0.93, 2.57, 1.32, AGED); b.box('carve', 0.22, 0.2, 1.3, 0.93, 2.57, 1.32, AGED);

  // 屋根（上面・裏面・破風・軒先・垂木）
  const big = roofParts(2.95, 3.15, 3.98, 3.1, 0.36), bz = -0.52;
  const small = roofParts(2.35, 1.75, 3.1, 2.62, 0.24), sz = 1.36;
  for (const [R, zc, L, Wd, drop] of [[big, bz, 3.15, 2.95, 0.26], [small, sz, 1.75, 2.35, 0.2]]) {
    b.geo('roof', R.top, 0, 0, zc, AGED); b.geo('roof', R.under, 0, 0, zc, 0xa89684);
    for (const t of [0, 1]) b.geo('wood', R.ribbon('t', t, drop), 0, 0, zc, DARK);
    for (const u of [-1, 1]) b.geo('wood', R.ribbon('u', u, 0.14), 0, 0, zc, DARK);
    for (const u of [-1, 1]) for (let z = -L / 2 + 0.18; z < L / 2 - 0.1; z += 0.17) {
      const t = (z + L / 2) / L, y = R.hAt(u * 0.86, t) - 0.13;
      b.box('wood', 0.42, 0.05, 0.05, u * (Wd / 2 - 0.3), y, zc + z, AGED, 0, 0, u * 0.35);
    }
  }
  b.geo('carve', big.gable(), 0, 0, bz + 3.15 / 2 - 0.34, W);
  b.geo('carve', big.gable(), 0, 0, bz - 3.15 / 2 + 0.34, W, Math.PI);
  b.geo('carve', small.gable(), 0, 0, sz + 1.75 / 2 - 0.26, W);
  b.box('wood', 0.22, 0.26, 3.1, 0, 4.09, bz, DARK); b.box('wood', 0.3, 0.06, 3.1, 0, 4.23, bz, DARK);
  b.box('wood', 0.17, 0.2, 1.7, 0, 3.19, sz, DARK);
  // 鬼板（上に反る板）・懸魚・飾り金具
  const oni = new THREE.Shape(); oni.moveTo(-0.34, 0); oni.lineTo(0.34, 0); oni.quadraticCurveTo(0.44, 0.5, 0.3, 0.7); oni.quadraticCurveTo(0, 0.56, -0.3, 0.7); oni.quadraticCurveTo(-0.44, 0.5, -0.34, 0);
  const oniG = new THREE.ExtrudeGeometry(oni, { depth: 0.12, bevelEnabled: true, bevelSize: 0.02, bevelThickness: 0.02, bevelSegments: 1 });
  const uvs = oniG.attributes.uv; for (let i = 0; i < uvs.count; i++) uvs.setXY(i, uvs.getX(i) * 0.4 + 0.5, uvs.getY(i) * 0.6 + 0.1);
  for (const s of [-1, 1]) {
    b.geo('carve', oniG, 0, 4.0, bz + s * 1.52 - 0.06, W);
    b.box('carve', 0.3, 0.38, 0.06, 0, 3.55, bz + s * 1.58, W);
    b.box('gold', 0.22, 0.12, 0.22, 0, 4.74, bz + s * 1.52, W);
    for (const sx of [-1, 1]) b.box('gold', 0.14, 0.1, 0.2, sx * 1.46, 3.5, bz + s * 1.56, W);
  }
  b.geo('carve', oniG, 0, 3.12, sz + 0.8, W, 0, 0, 0, 0.7, 0.7, 0.7);
  b.box('gold', 0.14, 0.1, 0.14, 0, 3.62, sz + 0.86, W);

  // 綱の結び目・後梃子
  for (const sx of [-1, 1]) b.geo('plain', new THREE.TorusGeometry(0.1, 0.035, 8, 14), sx * 0.52, 0.56, 2.18, 0xd9c9a3);
  for (const sx of [-1, 1]) b.rod('wood', V(sx * 0.55, 0.8, -1.95), V(sx * 1.05, 1.08, -5.1), 0.075, DARK, 8);

  // 鳴物方
  person(b, -1.0, 1.4, 0.2, -Math.PI / 2, happi, 'sit');
  person(b, 1.0, 1.4, -0.4, Math.PI / 2, happi, 'sit');
  person(b, 0, 1.4, 0.55, Math.PI, happi, 'sit');
  const body = b.build(mats, { cast: true, receive: true });
  model.add(body);

  // 後梃子の人たち（揺らすので別グループ）
  const rear = new Batch();
  for (const sx of [-1, 1]) for (let k = 0; k < 3; k++) person(rear, sx * (0.62 + 0.5 * (k + 1) / 3 + 0.34), 0, -2.7 - k * 0.8, 0, happi, 'lean');
  const crewRear = rear.build(mats, { cast: true });
  model.add(crewRear);

  // 前梃子：左右に一本ずつ。押すと先が前輪に食い込む
  const maeteko = [];
  for (const sx of [-1, 1]) {
    const pivot = new THREE.Group(); pivot.position.set(sx * 1.55, 1.05, 2.95); model.add(pivot);
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.07, 2.3, 10), mats.wood.clone());
    pole.material.vertexColors = false; pole.material.color.set(0x8f6a4c);
    pole.geometry.rotateX(Math.PI / 2); pole.geometry.translate(0, 0, -1.1); pole.castShadow = true; pivot.add(pole);
    pivot.rotation.order = 'YXZ'; pivot.rotation.set(0.1, sx * 0.4, 0);
    const crew = new Batch();
    person(crew, sx * 1.8, 0, 2.95, -sx * 0.25, happi, 'hold');
    person(crew, sx * 1.35, 0, 3.4, -sx * 0.15, happi, 'hold');
    const crewG = crew.build(mats, { cast: true });
    model.add(crewG);
    maeteko.push({ pivot, crew: crewG, side: sx, amt: 0 });
  }

  // 車輪
  const wheels = [];
  const wGeo = new THREE.CylinderGeometry(0.36, 0.36, 0.26, 24); wGeo.rotateZ(Math.PI / 2);
  const wMat = new THREE.MeshStandardMaterial({ map: tex.wood, color: 0x8a7462, roughness: 0.75 });
  const hub = new THREE.CylinderGeometry(0.1, 0.1, 0.3, 10); hub.rotateZ(Math.PI / 2);
  for (const sx of [-1, 1]) for (const z of [-1.3, 1.3]) {
    const w = new THREE.Mesh(wGeo, wMat); w.position.set(sx * 0.86, 0.36, z); w.castShadow = true;
    w.add(new THREE.Mesh(hub, mats.gold)); model.add(w); wheels.push(w);
  }

  // 町名の札
  const plate = document.createElement('canvas'); plate.width = 256; plate.height = 96;
  const plateTex = new THREE.CanvasTexture(plate); plateTex.colorSpace = THREE.SRGBColorSpace;
  const plateMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.9, 0.34), new THREE.MeshStandardMaterial({ map: plateTex, roughness: 0.8 }));
  plateMesh.position.set(0, 1.45, 2.08); model.add(plateMesh);
  function setTown(name) {
    const g = plate.getContext('2d');
    g.fillStyle = '#f4efe2'; g.fillRect(0, 0, 256, 96); g.strokeStyle = '#1b1b1b'; g.lineWidth = 6; g.strokeRect(4, 4, 248, 88);
    g.fillStyle = '#141414'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.font = 'bold 56px "Hiragino Mincho ProN","Yu Mincho",serif';
    g.fillText(name, 128, 52, 232); plateTex.needsUpdate = true;
  }

  const d1 = makeDaiku(happi), d2 = makeDaiku(happi);
  d1.group.position.set(0, 4.26, bz + 1.1); d1.base = d1.group.position.clone();
  d2.group.position.set(0, 3.29, sz + 0.35); d2.base = d2.group.position.clone();
  model.add(d1.group, d2.group);

  return { root, model, wheels, daiku: [d1, d2], crewRear, maeteko, setTown, ropeAnchors: [V(-0.52, 0.56, 2.2), V(0.52, 0.56, 2.2)] };
}
