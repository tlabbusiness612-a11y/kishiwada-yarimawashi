using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // data/kishiwada.json（PLATEAU 岸和田市の建物・道路を tools/extract-plateau.ps1 で取り出したもの）
    // JSON は x=東・z=南。Unity では x=東・z=北なので、読み込み時に z の符号を反転する。
    [Serializable] class PlateauJson { public string source; public float[] origin; public PBuilding[] buildings; public PRoad[] roads; }
    [Serializable] class PBuilding { public float h; public float g; public string u; public int s; public string n; public float[] p; }
    [Serializable] class PRoad { public string f; public float[] p; }

    public sealed class Building
    {
        public Vector2[] poly;   // Unity の xz
        public float height;     // 実測高さ(m)
        public float ground;     // 標高(m)
        public int usage;        // PLATEAU の用途コード（411 住宅, 412 共同住宅, 401 業務 ...）
        public int storeys;      // 地上階数（不明は 0）
        public string name;
    }
    public sealed class Road { public Vector2[] poly; public string function; }

    public sealed class PlateauData
    {
        public readonly List<Building> buildings = new List<Building>();
        public readonly List<Road> roads = new List<Road>();

        public static PlateauData Load()
        {
            var ta = Resources.Load<TextAsset>("kishiwada");
            if (ta == null) throw new Exception("Resources/kishiwada.json が見つかりません");
            var j = JsonUtility.FromJson<PlateauJson>(ta.text);
            var d = new PlateauData();
            foreach (var b in j.buildings)
            {
                if (b.p == null || b.p.Length < 6) continue;
                int.TryParse(b.u, out int u);
                d.buildings.Add(new Building { poly = ToPoly(b.p), height = b.h, ground = b.g, usage = u, storeys = b.s > 0 && b.s < 100 ? b.s : 0, name = b.n });
            }
            foreach (var r in j.roads)
            {
                if (r.p == null || r.p.Length < 6) continue;
                d.roads.Add(new Road { poly = ToPoly(r.p), function = r.f });
            }
            return d;
        }

        static Vector2[] ToPoly(float[] p)
        {
            var v = new Vector2[p.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector2(p[i * 2], -p[i * 2 + 1]);
            return v;
        }
    }
}
