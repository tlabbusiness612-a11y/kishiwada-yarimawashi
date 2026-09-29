using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kishiwada
{
    // 画面（UI Toolkit をコードで組む）：読み込み・タイトル・走行中・一時停止・結果
    public sealed class Hud
    {
        public static readonly (string name, uint c)[] HAPPI = { ("紺", 0x1f2e5a), ("朱", 0xb8321f), ("萌黄", 0x5d7a2a), ("江戸紫", 0x5b3a6e), ("焦茶", 0x5a3a22) };
        public readonly VisualElement root;
        VisualElement loading, title, hud, result, pause, touch, stamp;
        Label loadingText, teamName, score, place, spd, callMain, callSub, stampWord, stampPts, bang, resGrade, resTown, resTotal, resBest, beatDot;
        VisualElement tempoFill, railFill, rail, resRows, gaugeL, gaugeR, gaugeRear, touchL, touchR, tapRing;
        TextField townField;
        readonly List<Button> swatches = new List<Button>();
        readonly List<(VisualElement el, float s)> stops = new List<(VisualElement, float)>();
        public int colorIndex;
        public Action onStart, onAgain, onTitle, onCamera, onPause, onResume;
        public GameInput input;
        float stampT = -9f, bangT = -9f, calloutUntil;
        readonly Font gothic, mincho;
        readonly bool touchUI;

        static Color C(uint hex, float a = 1f) { var c = KMath.Hex(hex); c.a = a; return c; }

        public Hud(UIDocument doc, bool touchUI)
        {
            this.touchUI = touchUI;
            gothic = Font.CreateDynamicFontFromOSFont(TextPainter.Gothic, 32);
            mincho = Font.CreateDynamicFontFromOSFont(TextPainter.Mincho, 32);
            root = doc.rootVisualElement;
            root.style.unityFontDefinition = FontDefinition.FromFont(gothic);
            root.style.color = Color.white;
            root.pickingMode = PickingMode.Ignore;
            BuildHud(); BuildTitle(); BuildResult(); BuildPause(); BuildLoading();
            ApplySafeArea();
        }

        // ---------- 部品 ----------
        static VisualElement Box(VisualElement parent, string name = null)
        {
            var v = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            parent?.Add(v); return v;
        }
        Label Text(VisualElement parent, string s, int size, bool serif = false, uint color = 0xffffff)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = C(color);
            if (serif) l.style.unityFontDefinition = FontDefinition.FromFont(mincho);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 0; l.style.marginTop = 0; l.style.paddingBottom = 0; l.style.paddingTop = 0;
            parent?.Add(l); return l;
        }
        static void Abs(VisualElement v, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            v.style.position = Position.Absolute;
            if (left.HasValue) v.style.left = left.Value; if (top.HasValue) v.style.top = top.Value;
            if (right.HasValue) v.style.right = right.Value; if (bottom.HasValue) v.style.bottom = bottom.Value;
        }
        static void Fill(VisualElement v) { Abs(v, 0, 0, 0, 0); }
        static void Round(VisualElement v, float r) { v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r; v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r; }
        static void Border(VisualElement v, float w, Color c) { v.style.borderTopWidth = w; v.style.borderBottomWidth = w; v.style.borderLeftWidth = w; v.style.borderRightWidth = w; v.style.borderTopColor = c; v.style.borderBottomColor = c; v.style.borderLeftColor = c; v.style.borderRightColor = c; }
        static void Pad(VisualElement v, float y, float x) { v.style.paddingTop = y; v.style.paddingBottom = y; v.style.paddingLeft = x; v.style.paddingRight = x; }
        Button Btn(VisualElement parent, string s, Action click, uint bg = 0xc23a26, int size = 30)
        {
            var b = new Button(() => click?.Invoke()) { text = s };
            b.style.fontSize = size; b.style.unityFontStyleAndWeight = FontStyle.Bold; b.style.color = Color.white;
            b.style.backgroundColor = C(bg); Border(b, 0, Color.clear); Round(b, 10); Pad(b, 12, 30);
            b.style.marginTop = 8; b.style.marginBottom = 8;
            b.RegisterCallback<PointerEnterEvent>(_ => b.style.backgroundColor = C(bg) * 1.15f);
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = C(bg));
            parent?.Add(b); return b;
        }

        // ---------- 読み込み ----------
        void BuildLoading()
        {
            loading = Box(root, "loading"); Fill(loading);
            loading.style.backgroundColor = C(0x14110e);
            loading.style.alignItems = Align.Center; loading.style.justifyContent = Justify.Center;
            Text(loading, "岸和田やりまわし", 64, true);
            loadingText = Text(loading, "町並みを組み立てています…", 24, false, 0xc9bfae);
            loadingText.style.marginTop = 16;
        }
        public void SetLoading(string s) { if (s == null) loading.style.display = DisplayStyle.None; else { loading.style.display = DisplayStyle.Flex; loadingText.text = s; } }

        // ---------- タイトル ----------
        void BuildTitle()
        {
            title = Box(root, "title"); Fill(title);
            var panel = Box(title); Abs(panel, 0, 0, null, 0);
            panel.style.width = new Length(46, LengthUnit.Percent);
            panel.style.backgroundColor = C(0x0e0b09, 0.72f);
            Pad(panel, 56, 64); panel.style.justifyContent = Justify.Center;
            panel.pickingMode = PickingMode.Position;
            Text(panel, "岸和田やりまわし", 86, true, 0xf6efe2);
            var sub = Text(panel, "PLATEAU の町並みで地車を曳き、角をやりまわす", 22, false, 0xd8cdb8);
            sub.style.marginBottom = 34; sub.style.whiteSpace = WhiteSpace.Normal;
            Text(panel, "町名", 20, false, 0xbfb5a3);
            townField = new TextField { value = "わが町", maxLength = 8 };
            townField.style.fontSize = 30; townField.style.width = 360; townField.style.marginBottom = 18; townField.style.unityFontDefinition = FontDefinition.FromFont(mincho);
            panel.Add(townField);
            // 入力欄：暗い地に白い字
            var input = townField.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = C(0x2a2420, 0.95f); input.style.color = C(0xf6efe2);
                Border(input, 2, C(0xc9bfae, 0.6f)); Round(input, 6); Pad(input, 6, 12);
            }
            Text(panel, "法被の色", 20, false, 0xbfb5a3);
            var row = Box(panel); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 26; row.pickingMode = PickingMode.Position;
            for (int i = 0; i < HAPPI.Length; i++)
            {
                int k = i;
                var b = new Button(() => SelectColor(k)) { text = "" };
                b.style.width = 58; b.style.height = 58; b.style.marginRight = 12; Round(b, 29);
                b.style.backgroundColor = C(HAPPI[i].c);
                b.tooltip = HAPPI[i].name;
                row.Add(b); swatches.Add(b);
            }
            SelectColor(0);
            var start = Btn(panel, "曳き出す", () => onStart?.Invoke(), 0xc23a26, 38);
            start.style.alignSelf = Align.FlexStart; Pad(start, 16, 56);
            var help = Text(panel, touchUI
                ? "真ん中を鳴物の拍に合わせて連打で曳く／左右の縦の帯を押している間、その側の前梃子が効く／「跳」で大工方が跳ぶ"
                : "Space：鳴物の拍に合わせて曳く　← →（Q E）：前梃子　A D：後梃子　J：跳ぶ　C：視点　Esc：一時停止\nゲームパッド：A 曳く　LT RT 前梃子　左スティック 後梃子　Y 跳ぶ　RB 視点", 18, false, 0xbfb5a3);
            help.style.whiteSpace = WhiteSpace.Normal; help.style.marginTop = 20; help.style.unityFontStyleAndWeight = FontStyle.Normal;
        }
        void SelectColor(int k)
        {
            colorIndex = k;
            for (int i = 0; i < swatches.Count; i++) Border(swatches[i], i == k ? 4 : 0, Color.white);
        }
        public string TownName => string.IsNullOrWhiteSpace(townField.value) ? "わが町" : townField.value.Trim();
        public Color HappiColor => C(HAPPI[colorIndex].c);

        // ---------- 走行中 ----------
        void BuildHud()
        {
            hud = Box(root, "hud"); Fill(hud);
            // 左上：町名と点数
            var tl = Box(hud); Abs(tl, 28, 22);
            tl.style.backgroundColor = C(0x0e0b09, 0.55f); Pad(tl, 10, 18); Round(tl, 8);
            teamName = Text(tl, "わが町", 30, true, 0xf6efe2);
            var sr = Box(tl); sr.style.flexDirection = FlexDirection.Row; sr.style.alignItems = Align.FlexEnd;
            score = Text(sr, "0", 40, false, 0xffd98a); Text(sr, " 点", 20, false, 0xd8cdb8).style.marginBottom = 6;
            // 上の真ん中：場所と進み具合
            var tc = Box(hud); Abs(tc, null, 20); tc.style.left = new Length(28, LengthUnit.Percent); tc.style.right = new Length(28, LengthUnit.Percent);
            tc.style.alignItems = Align.Center;
            place = Text(tc, "大阪臨海線", 24, true, 0xf6efe2);
            rail = Box(tc); rail.style.height = 8; rail.style.width = new Length(100, LengthUnit.Percent); rail.style.marginTop = 10;
            rail.style.backgroundColor = C(0xffffff, 0.22f); Round(rail, 4);
            railFill = Box(rail); railFill.style.height = 8; railFill.style.backgroundColor = C(0xffc36b); Round(railFill, 4); railFill.style.width = 0;
            // 右上：視点・一時停止
            var tr = Box(hud); Abs(tr, null, 22, 28); tr.style.flexDirection = FlexDirection.Row; tr.pickingMode = PickingMode.Position;
            var camB = Btn(tr, "視点：追走", () => onCamera?.Invoke(), 0x2a2622, 20); Pad(camB, 8, 16);
            var pb = Btn(tr, "Ⅱ", () => onPause?.Invoke(), 0x2a2622, 20); Pad(pb, 8, 16); pb.style.marginLeft = 10;
            camButton = camB;
            // 左下：速さと鳴物の勢い
            var bl = Box(hud); Abs(bl, 30, null, null, 26);
            bl.style.backgroundColor = C(0x0e0b09, 0.55f); Pad(bl, 12, 20); Round(bl, 8);
            var sp = Box(bl); sp.style.flexDirection = FlexDirection.Row; sp.style.alignItems = Align.FlexEnd;
            spd = Text(sp, "0", 64, false, 0xffffff); Text(sp, " km/h", 22, false, 0xd8cdb8).style.marginBottom = 10;
            var tempoRow = Box(bl); tempoRow.style.flexDirection = FlexDirection.Row; tempoRow.style.alignItems = Align.Center; tempoRow.style.marginTop = 4;
            Text(tempoRow, "鳴物", 18, false, 0xd8cdb8).style.marginRight = 10;
            var tb = Box(tempoRow); tb.style.width = 220; tb.style.height = 12; tb.style.backgroundColor = C(0xffffff, 0.18f); Round(tb, 6);
            tempoFill = Box(tb); tempoFill.style.height = 12; tempoFill.style.backgroundColor = C(0xe0402c); Round(tempoFill, 6);
            beatDot = Text(tempoRow, "●", 22, false, 0xffc36b); beatDot.style.marginLeft = 12;
            // 下の真ん中：梃子の効き具合
            var g = Box(hud); Abs(g, null, null, null, 30); g.style.left = new Length(50, LengthUnit.Percent); g.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            g.style.flexDirection = FlexDirection.Row; g.style.alignItems = Align.FlexEnd;
            gaugeL = Gauge(g, "前梃子 左"); gaugeRear = Gauge(g, "後梃子"); gaugeR = Gauge(g, "前梃子 右");
            // 真ん中：掛け声の案内
            var cc = Box(hud); Abs(cc, 0, null, 0); cc.style.top = new Length(22, LengthUnit.Percent); cc.style.alignItems = Align.Center;
            callMain = Text(cc, "", 56, true, 0xffffff); callSub = Text(cc, "", 24, false, 0xf1e3c4);
            foreach (var l in new[] { callMain, callSub }) { l.style.unityTextOutlineColor = C(0x000000, 0.85f); l.style.unityTextOutlineWidth = 0.18f; }
            // 評価の判
            stamp = Box(hud); Abs(stamp, 0, null, 0); stamp.style.top = new Length(38, LengthUnit.Percent); stamp.style.alignItems = Align.Center;
            stampWord = Text(stamp, "", 120, true, 0xffe2a0); stampPts = Text(stamp, "", 40, false, 0xffffff);
            stampWord.style.unityTextOutlineColor = C(0x5a1a10); stampWord.style.unityTextOutlineWidth = 0.22f;
            stamp.style.opacity = 0;
            bang = Text(hud, "", 96, true, 0xff5a3c); Abs(bang, 0, null, 0); bang.style.top = new Length(52, LengthUnit.Percent);
            bang.style.unityTextAlign = TextAnchor.MiddleCenter; bang.style.opacity = 0; bang.style.unityTextOutlineColor = C(0x000000); bang.style.unityTextOutlineWidth = 0.2f;
            // タッチ操作
            touch = Box(hud); Fill(touch);
            touchL = TouchBar(touch, true); touchR = TouchBar(touch, false);
            var tapZone = Box(touch); Abs(tapZone, null, null, null, 0);
            tapZone.style.left = new Length(18, LengthUnit.Percent); tapZone.style.right = new Length(18, LengthUnit.Percent); tapZone.style.top = new Length(30, LengthUnit.Percent);
            tapZone.pickingMode = PickingMode.Position;
            tapZone.RegisterCallback<PointerDownEvent>(e => { if (input != null) input.touchTaps++; input.usingTouch = true; tapRingT = 0f; });
            tapRing = Box(tapZone); Abs(tapRing, null, null, null, 70); tapRing.style.left = new Length(50, LengthUnit.Percent);
            tapRing.style.width = 160; tapRing.style.height = 160; tapRing.style.marginLeft = -80; Round(tapRing, 80); Border(tapRing, 5, C(0xffffff, 0.6f));
            var tapLabel = Text(tapRing, "曳け", 34, true); tapLabel.style.unityTextAlign = TextAnchor.MiddleCenter; tapLabel.style.flexGrow = 1;
            var jump = Btn(touch, "跳", () => { if (input != null) input.touchJumps++; }, 0x2a2622, 34);
            Abs(jump, null, null, 190, 150); jump.style.width = 100; jump.style.height = 100; Round(jump, 50);
            touch.style.display = touchUI ? DisplayStyle.Flex : DisplayStyle.None;
            hud.style.display = DisplayStyle.None;
        }
        Button camButton;
        float tapRingT = 9f;

        VisualElement Gauge(VisualElement parent, string label)
        {
            var col = Box(parent); col.style.alignItems = Align.Center; col.style.marginLeft = 14; col.style.marginRight = 14;
            var bar = Box(col); bar.style.width = 16; bar.style.height = 70; bar.style.backgroundColor = C(0xffffff, 0.18f); Round(bar, 4);
            bar.style.justifyContent = Justify.FlexEnd;
            var fill = Box(bar); fill.style.width = 16; fill.style.height = 0; fill.style.backgroundColor = C(0xffc36b); Round(fill, 4);
            Text(col, label, 15, false, 0xd8cdb8).style.marginTop = 4;
            return fill;
        }
        VisualElement TouchBar(VisualElement parent, bool left)
        {
            var bar = Box(parent);
            Abs(bar, left ? 0f : (float?)null, 110, left ? (float?)null : 0f, 0);
            bar.style.width = new Length(15, LengthUnit.Percent);
            bar.style.backgroundColor = C(0xffffff, 0.08f);
            bar.style.justifyContent = Justify.Center; bar.style.alignItems = Align.Center;
            bar.pickingMode = PickingMode.Position;
            var l = Text(bar, left ? "前梃子\n左" : "前梃子\n右", 26, true, 0xffffff); l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.opacity = 0.8f;
            var ids = new HashSet<int>();
            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                ids.Add(e.pointerId); bar.CapturePointer(e.pointerId);
                if (input != null) { if (left) input.touchMaeL = true; else input.touchMaeR = true; input.usingTouch = true; }
            });
            void Up(int id)
            {
                ids.Remove(id); if (bar.HasPointerCapture(id)) bar.ReleasePointer(id);
                if (ids.Count == 0 && input != null) { if (left) input.touchMaeL = false; else input.touchMaeR = false; }
            }
            bar.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            bar.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            return bar;
        }

        public void ShowTouch(bool on) => touch.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        public void SetCamera(string name) => camButton.text = "視点：" + name;

        public void BuildRail()
        {
            foreach (var s in stops) s.el.RemoveFromHierarchy();
            stops.Clear();
            (float s, string n)[] list = { (Course.Corners[0].s, "カンカン場"), (Course.Corners[1].s, "紀州街道"), (Course.FinishS, "ゴール") };
            foreach (var (s, n) in list)
            {
                var el = Box(rail); Abs(el, null, -6); el.style.left = new Length(s / Course.FinishS * 100f, LengthUnit.Percent);
                el.style.width = 4; el.style.height = 20; el.style.backgroundColor = C(0xffffff, 0.8f);
                var l = Text(el, n, 15, false, 0xd8cdb8); Abs(l, -40, 22); l.style.width = 84; l.style.unityTextAlign = TextAnchor.UpperCenter;
                stops.Add((el, s));
            }
        }

        public void ShowScreen(string which)
        {
            title.style.display = which == "title" ? DisplayStyle.Flex : DisplayStyle.None;
            hud.style.display = which == "hud" ? DisplayStyle.Flex : DisplayStyle.None;
            result.style.display = which == "result" ? DisplayStyle.Flex : DisplayStyle.None;
            pause.style.display = which == "pause" ? DisplayStyle.Flex : DisplayStyle.None;
            replay.style.display = which == "replay" ? DisplayStyle.Flex : DisplayStyle.None;
            if (which == "pause") hud.style.display = DisplayStyle.Flex;
        }

        public void Callout(string main, string sub, float secs)
        {
            callMain.text = main ?? ""; callSub.text = sub ?? "";
            calloutUntil = Time.unscaledTime + secs;
        }
        public bool CalloutBusy => Time.unscaledTime < calloutUntil;
        public void Guide(string main, string sub) { if (CalloutBusy) return; callMain.text = main ?? ""; callSub.text = sub ?? ""; }
        public void Stamp(string word, int pts) { stampWord.text = word; stampPts.text = "+" + pts; stampT = Time.unscaledTime; }
        public void Bang(string word) { bang.text = word; bangT = Time.unscaledTime; }
        public void Hint(bool left, bool on) { (left ? touchL : touchR).style.backgroundColor = on ? C(0xffc36b, 0.35f) : C(0xffffff, 0.08f); }

        public void UpdateHud(string team, int pts, float s, float speed, float tempo, double beat, float maeL, float maeR, float rear)
        {
            teamName.text = team; score.text = pts.ToString();
            place.text = Scoring.PlaceName(s);
            railFill.style.width = new Length(Mathf.Clamp01(s / Course.FinishS) * 100f, LengthUnit.Percent);
            foreach (var st in stops) st.el.style.backgroundColor = s >= st.s ? C(0xffc36b) : C(0xffffff, 0.8f);
            spd.text = Mathf.RoundToInt(speed * 3.6f).ToString();
            tempoFill.style.width = new Length(tempo * 100f, LengthUnit.Percent);
            float bp = (float)(beat - Math.Floor(beat));
            float pulse = Mathf.Exp(-bp * 6f);
            beatDot.style.scale = new Scale(Vector3.one * (0.8f + pulse * 0.7f));
            beatDot.style.opacity = 0.35f + pulse * 0.65f;
            gaugeL.style.height = new Length(maeL * 100f, LengthUnit.Percent);
            gaugeR.style.height = new Length(maeR * 100f, LengthUnit.Percent);
            gaugeRear.style.height = new Length(Mathf.Abs(rear) * 100f, LengthUnit.Percent);
            // 判・ドーン
            float st2 = Time.unscaledTime - stampT;
            stamp.style.opacity = st2 < 2.3f ? Mathf.Clamp01(Mathf.Min(st2 / 0.12f, (2.3f - st2) / 0.4f)) : 0f;
            stamp.style.scale = new Scale(Vector3.one * (st2 < 0.25f ? Mathf.Lerp(1.8f, 1f, st2 / 0.25f) : 1f));
            float bt = Time.unscaledTime - bangT;
            bang.style.opacity = bt < 0.6f ? 1f - bt / 0.6f : 0f;
            bang.style.scale = new Scale(Vector3.one * (1f + bt * 0.6f));
            tapRingT += Time.unscaledDeltaTime;
            tapRing.style.scale = new Scale(Vector3.one * (1f + Mathf.Exp(-tapRingT * 8f) * 0.25f));
        }

        // ---------- 一時停止・結果 ----------
        void BuildPause()
        {
            pause = Box(root, "pause"); Fill(pause); pause.style.backgroundColor = C(0x000000, 0.5f);
            pause.style.alignItems = Align.Center; pause.style.justifyContent = Justify.Center; pause.pickingMode = PickingMode.Position;
            Text(pause, "一時停止", 60, true);
            Btn(pause, "続ける", () => onResume?.Invoke(), 0xc23a26, 30);
            Btn(pause, "タイトルへ", () => onTitle?.Invoke(), 0x3a342e, 26);
            pause.style.display = DisplayStyle.None;
        }
        void BuildResult()
        {
            result = Box(root, "result"); Fill(result); result.style.backgroundColor = C(0x0e0b09, 0.6f);
            result.style.alignItems = Align.Center; result.style.justifyContent = Justify.Center; result.pickingMode = PickingMode.Position;
            var card = Box(result); card.style.backgroundColor = C(0x1b1612, 0.94f); Pad(card, 30, 46); Round(card, 14); card.style.minWidth = 640;
            card.pickingMode = PickingMode.Position;
            var head = Box(card); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.FlexEnd; head.style.justifyContent = Justify.SpaceBetween;
            resGrade = Text(head, "", 92, true, 0xffe2a0);
            resTown = Text(head, "", 30, true, 0xd8cdb8); resTown.style.marginBottom = 16;
            resRows = Box(card); resRows.style.marginTop = 12; resRows.style.marginBottom = 12;
            var tot = Box(card); tot.style.flexDirection = FlexDirection.Row; tot.style.justifyContent = Justify.SpaceBetween; tot.style.alignItems = Align.FlexEnd;
            Text(tot, "合計", 26, false, 0xd8cdb8);
            resTotal = Text(tot, "0", 64, false, 0xffd98a);
            resBest = Text(card, "", 20, false, 0xbfb5a3); resBest.style.unityTextAlign = TextAnchor.MiddleRight;
            var btns = Box(card); btns.style.flexDirection = FlexDirection.Row; btns.style.justifyContent = Justify.FlexEnd; btns.style.marginTop = 16; btns.pickingMode = PickingMode.Position;
            Btn(btns, "リプレイ", () => onReplay?.Invoke(), 0x2f5d8a, 24).style.marginRight = 14;
            Btn(btns, "もう一度", () => onAgain?.Invoke(), 0xc23a26, 28).style.marginRight = 14;
            Btn(btns, "タイトルへ", () => onTitle?.Invoke(), 0x3a342e, 24);
            result.style.display = DisplayStyle.None;
            // リプレイ中の表示
            replay = Box(root, "replay"); Fill(replay);
            var tag = Text(replay, "リプレイ", 34, true, 0xffffff); Abs(tag, 36, 28);
            tag.style.backgroundColor = C(0xc23a26, 0.85f); Pad(tag, 6, 18); Round(tag, 6);
            var skip = Btn(replay, "とばす", () => onSkipReplay?.Invoke(), 0x2a2622, 22); Abs(skip, null, null, 36, 30);
            replay.style.display = DisplayStyle.None;
        }
        VisualElement replay;
        public Action onReplay, onSkipReplay;
        public void ShowResult(Scoring sc, string town, int best, bool newBest)
        {
            resRows.Clear();
            Scoring.Result top = null;
            foreach (var r in sc.results)
            {
                if (r == null) continue;
                if (top == null || r.pts > top.pts) top = r;
                Row($"{r.name}　入り {r.entry}km/h{(r.hits > 0 ? $"・接触{r.hits}" : "")}{(r.jump ? "・跳" : "")}", r.grade, r.pts.ToString());
            }
            Row($"タイム {sc.time:0.0}秒", "", "+" + sc.timeBonus);
            resTotal.text = sc.score.ToString();
            resGrade.text = top != null ? top.grade : "あかん";
            resTown.text = town;
            resBest.text = newBest ? "自己ベスト更新" : $"自己ベスト {best}";
        }
        void Row(string a, string b, string c)
        {
            var row = Box(resRows); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginBottom = 6;
            var la = Text(row, a, 22, false, 0xf1e8d8); la.style.flexGrow = 1; la.style.unityFontStyleAndWeight = FontStyle.Normal;
            var lb = Text(row, b, 28, true, 0xffc36b); lb.style.width = 130; lb.style.unityTextAlign = TextAnchor.MiddleCenter;
            var lc = Text(row, c, 26, false, 0xffffff); lc.style.width = 90; lc.style.unityTextAlign = TextAnchor.MiddleRight;
        }

        void ApplySafeArea()
        {
            var sa = Screen.safeArea;
            if (sa.width <= 0 || sa.width >= Screen.width - 1) return;
            float kx = 1600f / Screen.width;
            root.style.paddingLeft = sa.xMin * kx; root.style.paddingRight = (Screen.width - sa.xMax) * kx;
        }
    }
}
