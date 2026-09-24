<#
  PLATEAU（国土交通省 3D都市モデル）岸和田市 CityGML から、カンカン場周辺の
  建物（LOD0/LOD1 の平面形と高さ）と道路（LOD1 の面・LOD0 の中心線）を取り出し、
  ゲーム用のローカル座標（m、x=東・z=南）の JSON にする。

  使い方:
    powershell -ExecutionPolicy Bypass -File tools/extract-plateau.ps1 -Zip <27202_..._citygml_1_op.zip> -Out data/kishiwada.json

  データ出典: 国土交通省 Project PLATEAU「3D都市モデル（Project PLATEAU）岸和田市（2024年度）」
  https://www.geospatial.jp/ckan/dataset/plateau-27202-kishiwadashi-2024
#>
param(
  [Parameter(Mandatory = $true)][string]$Zip,
  [string]$Out = 'data/kishiwada.json',
  [double]$Lat0 = 34.4655,
  [double]$Lon0 = 135.3725,
  [double]$Radius = 650,
  [string[]]$Meshes = @('51355259', '51355269', '51355279', '51355350', '51355360', '51355370')
)

Add-Type -ReferencedAssemblies System.IO.Compression, System.IO.Compression.FileSystem, System.Xml -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public static class PlateauExtract {
  static double lat0, lon0, mLat, mLon, radius;
  static readonly CultureInfo IC = CultureInfo.InvariantCulture;

  public static string Run(string zipPath, string[] meshes, double la0, double lo0, double rad) {
    lat0 = la0; lon0 = lo0; radius = rad;
    double p = la0 * Math.PI / 180.0;
    mLat = 111132.92 - 559.82 * Math.Cos(2 * p) + 1.175 * Math.Cos(4 * p);
    mLon = 111412.84 * Math.Cos(p) - 93.5 * Math.Cos(3 * p);
    var b = new List<string>(); var r = new List<string>(); var l = new List<string>();
    using (var z = ZipFile.OpenRead(zipPath)) {
      foreach (var m in meshes) {
        foreach (var kind in new[] { "bldg", "tran" }) {
          var e = z.GetEntry("udx/" + kind + "/" + m + "_" + kind + "_6697_op.gml");
          if (e == null) continue;
          using (var s = e.Open())
          using (var x = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore })) {
            if (kind == "bldg") Bldg(x, b); else Tran(x, r, l);
          }
        }
      }
    }
    return "{\"source\":\"国土交通省 Project PLATEAU 岸和田市（2024年度）CityGML\",\"origin\":[" + F(lat0, 6) + "," + F(lon0, 6) +
      "],\"buildings\":[" + string.Join(",", b) + "],\"roads\":[" + string.Join(",", r) + "],\"lines\":[" + string.Join(",", l) + "]}";
  }

  static string F(double v, int d) { return Math.Round(v, d).ToString(IC); }

  // posList "lat lon h ..." -> local x,z (+ h)
  static List<double[]> Pts(string s) {
    var t = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    var o = new List<double[]>();
    for (int i = 0; i + 2 < t.Length; i += 3) {
      double la = double.Parse(t[i], IC), lo = double.Parse(t[i + 1], IC), h = double.Parse(t[i + 2], IC);
      o.Add(new[] { (lo - lon0) * mLon, -(la - lat0) * mLat, h });
    }
    if (o.Count > 1) { var a = o[0]; var c = o[o.Count - 1]; if (Math.Abs(a[0] - c[0]) < 1e-6 && Math.Abs(a[1] - c[1]) < 1e-6) o.RemoveAt(o.Count - 1); }
    return o;
  }
  static string Arr(List<double[]> p) {
    var sb = new StringBuilder("[");
    for (int i = 0; i < p.Count; i++) { if (i > 0) sb.Append(','); sb.Append(F(p[i][0], 1)).Append(',').Append(F(p[i][1], 1)); }
    return sb.Append(']').ToString();
  }
  static bool Near(List<double[]> p) {
    double cx = 0, cz = 0; foreach (var q in p) { cx += q[0]; cz += q[1]; }
    cx /= p.Count; cz /= p.Count;
    return cx * cx + cz * cz < radius * radius;
  }
  static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

  static void Bldg(XmlReader x, List<string> outp) {
    while (x.Read()) {
      if (x.NodeType != XmlNodeType.Element || x.LocalName != "Building") continue;
      using (var s = x.ReadSubtree()) {
        double h = -1; string usage = "", name = ""; int st = -1; string foot = null;
        var solid = new List<string>();
        string ctx = ""; int ctxDepth = -1; bool skip = false;
        while (skip || s.Read()) {
          skip = false;
          if (s.NodeType == XmlNodeType.EndElement && s.Depth == ctxDepth) { ctx = ""; ctxDepth = -1; continue; }
          if (s.NodeType != XmlNodeType.Element) continue;
          switch (s.LocalName) {
            case "lod0RoofEdge": case "lod0FootPrint": if (!s.IsEmptyElement) { ctx = "lod0"; ctxDepth = s.Depth; } break;
            case "lod1Solid": if (!s.IsEmptyElement) { ctx = "lod1"; ctxDepth = s.Depth; } break;
            case "measuredHeight": h = double.Parse(s.ReadElementContentAsString(), IC); skip = true; break;
            case "usage": if (s.NamespaceURI.Contains("building")) { usage = s.ReadElementContentAsString(); skip = true; } break;
            case "storeysAboveGround": st = (int)double.Parse(s.ReadElementContentAsString(), IC); skip = true; break;
            case "name": if (s.NamespaceURI.Contains("gml")) { name = s.ReadElementContentAsString(); skip = true; } break;
            case "posList": {
              var t = s.ReadElementContentAsString(); skip = true;
              if (ctx == "lod0" && foot == null) foot = t; else if (ctx == "lod1") solid.Add(t);
              break;
            }
          }
        }
        List<double[]> poly = null; double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var t in solid) foreach (var q in Pts(t)) { zmin = Math.Min(zmin, q[2]); zmax = Math.Max(zmax, q[2]); }
        if (foot != null) poly = Pts(foot);
        else {
          foreach (var t in solid) {
            var q = Pts(t); bool flat = true;
            foreach (var v in q) if (Math.Abs(v[2] - zmin) > 0.01) { flat = false; break; }
            if (flat && q.Count >= 3) { poly = q; break; }
          }
        }
        if (poly == null || poly.Count < 3 || !Near(poly)) continue;
        if (h <= 0 && zmax > zmin) h = zmax - zmin;
        if (h <= 0) h = 6;
        var sb = new StringBuilder("{\"h\":").Append(F(h, 1));
        if (zmin < double.MaxValue) sb.Append(",\"g\":").Append(F(zmin, 1));
        if (usage != "") sb.Append(",\"u\":\"").Append(Esc(usage)).Append('"');
        if (st > 0) sb.Append(",\"s\":").Append(st);
        if (name != "") sb.Append(",\"n\":\"").Append(Esc(name)).Append('"');
        sb.Append(",\"p\":").Append(Arr(poly)).Append('}');
        outp.Add(sb.ToString());
      }
    }
  }

  static void Tran(XmlReader x, List<string> polys, List<string> lines) {
    while (x.Read()) {
      if (x.NodeType != XmlNodeType.Element || x.LocalName != "Road") continue;
      using (var s = x.ReadSubtree()) {
        string fn = ""; string ctx = ""; int ctxDepth = -1; bool interior = false; int intDepth = -1; bool skip = false;
        var rings = new List<string>(); var ls = new List<string>();
        while (skip || s.Read()) {
          skip = false;
          if (s.NodeType == XmlNodeType.EndElement) {
            if (s.Depth == ctxDepth) { ctx = ""; ctxDepth = -1; }
            if (s.Depth == intDepth) { interior = false; intDepth = -1; }
            continue;
          }
          if (s.NodeType != XmlNodeType.Element) continue;
          switch (s.LocalName) {
            case "lod0Network": if (!s.IsEmptyElement) { ctx = "lod0"; ctxDepth = s.Depth; } break;
            case "lod1MultiSurface": if (!s.IsEmptyElement) { ctx = "lod1"; ctxDepth = s.Depth; } break;
            case "interior": if (!s.IsEmptyElement) { interior = true; intDepth = s.Depth; } break;
            case "function": if (s.NamespaceURI.Contains("transportation")) { fn = s.ReadElementContentAsString(); skip = true; } break;
            case "posList": {
              var t = s.ReadElementContentAsString(); skip = true;
              if (ctx == "lod1" && !interior) rings.Add(t); else if (ctx == "lod0") ls.Add(t);
              break;
            }
          }
        }
        foreach (var t in rings) { var q = Pts(t); if (q.Count >= 3 && Near(q)) polys.Add("{\"f\":\"" + Esc(fn) + "\",\"p\":" + Arr(q) + "}"); }
        foreach (var t in ls) { var q = Pts(t); if (q.Count >= 2 && Near(q)) lines.Add(Arr(q)); }
      }
    }
  }
}
'@

$outPath = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }
New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
$json = [PlateauExtract]::Run((Resolve-Path $Zip).Path, $Meshes, $Lat0, $Lon0, $Radius)
[IO.File]::WriteAllText($outPath, $json, (New-Object Text.UTF8Encoding $false))
"{0}  {1:N0} bytes" -f $outPath, (Get-Item $outPath).Length
