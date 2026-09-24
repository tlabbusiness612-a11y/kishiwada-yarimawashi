// 鳴物（太鼓・鉦・笛）と掛け声・歓声を Web Audio で合成。録音素材は使わない。
const FUE = [880, 987.8, 1174.7, 987.8, 880, 784, 659.3, 784, 880, 880, 987.8, 880, 784, 659.3, 587.3, 659.3];

export class Narimono {
  constructor() { this.ctx = null; this.tempo = 0; this.timer = null; this.beats = []; this.lastChant = 0; }

  start() {
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    if (!this.ctx) {
      const c = this.ctx = new AC();
      const comp = c.createDynamicsCompressor(); comp.threshold.value = -14; comp.ratio.value = 4;
      this.master = c.createGain(); this.master.gain.value = 0.85;
      this.master.connect(comp); comp.connect(c.destination);
      const len = c.sampleRate; const buf = c.createBuffer(1, len, c.sampleRate); const d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
      this.noise = buf;
      // 沿道のざわめき
      const src = c.createBufferSource(); src.buffer = buf; src.loop = true;
      const bp = c.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 650; bp.Q.value = 0.5;
      this.crowd = c.createGain(); this.crowd.gain.value = 0;
      src.connect(bp); bp.connect(this.crowd); this.crowd.connect(this.master); src.start();
    }
    if (this.ctx.state !== 'running') this.ctx.resume();
    if (!this.timer) {
      this.next = this.ctx.currentTime + 0.1; this.step = 0;
      this.timer = setInterval(() => this.schedule(), 25);
    }
  }
  stop() {
    clearInterval(this.timer); this.timer = null;
    if (this.ctx) this.crowd.gain.setTargetAtTime(0, this.ctx.currentTime, 0.3);
  }
  get interval() { return 60 / (86 + this.tempo * 116); } // 四分音符の長さ

  schedule() {
    const c = this.ctx; if (!c) return;
    while (this.next < c.currentTime + 0.12) {
      this.play(this.step, this.next);
      this.next += this.interval / 2; this.step++;
    }
  }
  play(step, t) {
    const s = step % 8, hi = this.tempo > 0.62;
    if (s % 2 === 0) { this.beats.push(t); if (this.beats.length > 8) this.beats.shift(); }
    if (s === 0 || s === 2 || s === 4 || s === 5 || (hi && (s === 6 || s === 7))) this.taiko(t, s === 0 ? 1 : 0.75);
    if (hi || s % 2 === 1) this.kane(t, s === 3 || s === 7 ? 1 : 0.55);
    if (this.tempo > 0.12 && s % 2 === 0) this.fue(t, FUE[(step / 2) % 16 | 0], this.interval * 0.95);
  }
  env(g, t, a, peak, dec) {
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(peak, t + a);
    g.gain.exponentialRampToValueAtTime(0.0001, t + a + dec);
  }
  taiko(t, v) {
    const c = this.ctx, o = c.createOscillator(), g = c.createGain();
    o.frequency.setValueAtTime(150, t); o.frequency.exponentialRampToValueAtTime(52, t + 0.14);
    this.env(g, t, 0.005, 0.9 * v, 0.34);
    o.connect(g); g.connect(this.master); o.start(t); o.stop(t + 0.45);
    const n = c.createBufferSource(); n.buffer = this.noise;
    const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 500;
    const ng = c.createGain(); this.env(ng, t, 0.002, 0.35 * v, 0.07);
    n.connect(lp); lp.connect(ng); ng.connect(this.master); n.start(t, Math.random() * 0.5); n.stop(t + 0.12);
  }
  kane(t, v) {
    const c = this.ctx, g = c.createGain();
    this.env(g, t, 0.002, 0.16 * v, 0.22 + 0.2 * v);
    g.connect(this.master);
    for (const f of [1650, 2485, 3710]) { const o = c.createOscillator(); o.type = 'sine'; o.frequency.value = f * (0.995 + Math.random() * 0.01); o.connect(g); o.start(t); o.stop(t + 0.5); }
    const n = c.createBufferSource(); n.buffer = this.noise;
    const hp = c.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 5000;
    const ng = c.createGain(); this.env(ng, t, 0.001, 0.08 * v, 0.03);
    n.connect(hp); hp.connect(ng); ng.connect(this.master); n.start(t, Math.random() * 0.5); n.stop(t + 0.06);
  }
  fue(t, f, dur) {
    const c = this.ctx, o = c.createOscillator(), g = c.createGain(), lfo = c.createOscillator(), lg = c.createGain();
    o.type = 'triangle'; o.frequency.setValueAtTime(f * 0.97, t); o.frequency.linearRampToValueAtTime(f, t + 0.04);
    lfo.frequency.value = 5.5; lg.gain.value = f * 0.008; lfo.connect(lg); lg.connect(o.frequency);
    const vol = 0.035 + this.tempo * 0.04;
    g.gain.setValueAtTime(0.0001, t); g.gain.linearRampToValueAtTime(vol, t + 0.03);
    g.gain.setValueAtTime(vol, t + dur * 0.8); g.gain.linearRampToValueAtTime(0.0001, t + dur);
    o.connect(g); g.connect(this.master); o.start(t); lfo.start(t); o.stop(t + dur + 0.02); lfo.stop(t + dur + 0.02);
  }
  // 「そーりゃ」の掛け声（フォルマント合成の群声）
  chant() {
    const c = this.ctx; if (!c) return;
    const t = c.currentTime;
    if (t - this.lastChant < 0.55) return;
    this.lastChant = t;
    const out = c.createGain(); out.gain.value = 0.22; out.connect(this.master);
    const f1 = c.createBiquadFilter(), f2 = c.createBiquadFilter();
    f1.type = f2.type = 'bandpass'; f1.Q.value = 6; f2.Q.value = 8;
    f1.frequency.setValueAtTime(480, t); f1.frequency.setValueAtTime(480, t + 0.3); f1.frequency.linearRampToValueAtTime(780, t + 0.42);
    f2.frequency.setValueAtTime(820, t); f2.frequency.setValueAtTime(820, t + 0.3); f2.frequency.linearRampToValueAtTime(1250, t + 0.42);
    f1.connect(out); f2.connect(out);
    for (let i = 0; i < 5; i++) {
      const o = c.createOscillator(), g = c.createGain(), base = 140 + Math.random() * 90;
      o.type = 'sawtooth'; o.frequency.setValueAtTime(base, t); o.frequency.linearRampToValueAtTime(base * 1.12, t + 0.34); o.frequency.linearRampToValueAtTime(base * 0.85, t + 0.62);
      const d = Math.random() * 0.04;
      g.gain.setValueAtTime(0.0001, t + d); g.gain.linearRampToValueAtTime(0.25, t + d + 0.06);
      g.gain.setValueAtTime(0.22, t + 0.3); g.gain.linearRampToValueAtTime(0.05, t + 0.36); g.gain.linearRampToValueAtTime(0.28, t + 0.42);
      g.gain.linearRampToValueAtTime(0.0001, t + 0.66);
      o.connect(g); g.connect(f1); g.connect(f2); o.start(t + d); o.stop(t + 0.7);
    }
    const n = c.createBufferSource(); n.buffer = this.noise;
    const hp = c.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 4500;
    const ng = c.createGain(); this.env(ng, t, 0.01, 0.12, 0.08);
    n.connect(hp); hp.connect(ng); ng.connect(out); n.start(t); n.stop(t + 0.12);
  }
  thud(power) {
    const c = this.ctx; if (!c) return;
    const t = c.currentTime, o = c.createOscillator(), g = c.createGain();
    o.frequency.setValueAtTime(90, t); o.frequency.exponentialRampToValueAtTime(30, t + 0.3);
    this.env(g, t, 0.005, Math.min(1, 0.4 + power), 0.5);
    o.connect(g); g.connect(this.master); o.start(t); o.stop(t + 0.6);
    const n = c.createBufferSource(); n.buffer = this.noise;
    const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 1400;
    const ng = c.createGain(); this.env(ng, t, 0.003, Math.min(0.8, 0.2 + power * 0.6), 0.35);
    n.connect(lp); lp.connect(ng); ng.connect(this.master); n.start(t); n.stop(t + 0.5);
  }
  whistle() {
    const c = this.ctx; if (!c) return;
    const t = c.currentTime, o = c.createOscillator(), g = c.createGain();
    o.type = 'sine'; o.frequency.setValueAtTime(2300, t);
    for (let i = 0; i < 6; i++) o.frequency.setValueAtTime(i % 2 ? 2100 : 2350, t + i * 0.05);
    g.gain.setValueAtTime(0.0001, t); g.gain.linearRampToValueAtTime(0.12, t + 0.02); g.gain.setValueAtTime(0.12, t + 0.5); g.gain.linearRampToValueAtTime(0.0001, t + 0.6);
    o.connect(g); g.connect(this.master); o.start(t); o.stop(t + 0.65);
  }
  // 沿道の盛り上がり 0..1
  excite(x) { if (this.ctx) this.crowd.gain.setTargetAtTime(0.02 + x * 0.3, this.ctx.currentTime, 0.25); }
  // 叩いた瞬間と拍のずれ（拍の長さに対する割合 0..0.5）
  offBeat() {
    if (!this.ctx || !this.beats.length) return 0.5;
    const t = this.ctx.currentTime, iv = this.interval;
    let best = 1e9;
    for (const b of this.beats) best = Math.min(best, Math.abs(t - b), Math.abs(t - (b + iv)));
    return Math.min(0.5, best / iv);
  }
}
