# 岸和田やりまわし

岸和田だんじり祭の「やりまわし」を、スマホのブラウザで遊べる 3D ゲームにしたものです。iPhone の Safari での横向きプレイを想定しています。

大阪臨海線を南へ走り込み、**カンカン場（岸和田港交差点）** で左へやりまわして北町の路地へ。路地を抜けて、**紀州街道** の辻で右へ曲がればゴールです。町並みは国土交通省 PLATEAU の岸和田市 3D 都市モデルにある建物の形と高さから組み立てています。

## 遊び方

| 操作 | iPhone | PC |
| --- | --- | --- |
| 曳け（速くする） | 画面の真ん中を、鳴物の拍に合わせて連打 | Space / ↑ |
| 前梃子（曲がる） | 左右の縦棒を押している間、その側の前輪が止まって曲がる（減速あり） | ← / → |
| 大工方が跳ぶ | 「跳」ボタン（やりまわし中に跳ぶと加点） | J |
| 視点（追走・大工方・桟敷） | 右上のボタン | C |

- 綱先（曳き手の先頭）は道なりに走ります。地車の向きを変えるのは前梃子です。
- 角では手前で前梃子を入れ、向きが揃ったら離します。前梃子を使わずに曲がろうとすると、外へ膨らんで桟敷や家に当たります。
- 採点は角ごとです。入りの速さ、速度をどれだけ保てたか、キレ（向きを変える速さ）、接触の有無、跳の有無で決まります。

## 動かし方

ビルドは不要な静的サイトです。three.js は jsDelivr から読み込みます。

```bash
# Windows（PowerShell）
powershell -ExecutionPolicy Bypass -File tools/serve.ps1
# → http://localhost:8765/
```

ほかの静的サーバー（`npx serve` など）でも動きます。`data/kishiwada.json` を fetch するため、`file://` で直接開くと動きません。GitHub Pages にそのまま置けます。

## ファイル構成

| パス | 内容 |
| --- | --- |
| `index.html` | 画面と操作部品 |
| `js/game.js` | 物理（綱・前梃子・横滑り）、採点、カメラ、入力 |
| `js/danjiri.js` | 岸和田型下地車のモデル（小屋根・大屋根・土呂幕・枡合・見送り・前梃子・後梃子・大工方） |
| `js/town.js` | PLATEAU の建物と道路から町並み、桟敷、人垣、電柱、提灯を組み立てる |
| `js/course.js` | コースの中心線（ローカル座標） |
| `js/audio.js` | 太鼓・鉦・笛・掛け声を Web Audio で合成（録音素材なし） |
| `js/util.js` | テクスチャ生成とジオメトリ結合 |
| `data/kishiwada.json` | PLATEAU から取り出した建物 3,653 棟・道路 1,035 面 |
| `tools/extract-plateau.ps1` | PLATEAU の CityGML から `data/kishiwada.json` を作るスクリプト |
| `tools/map.html` | データを上から見る地図ビューア（コース調整用） |

## 地図データの作り直し

1. G空間情報センターの「[3D都市モデル（Project PLATEAU）岸和田市（2024年度）](https://www.geospatial.jp/ckan/dataset/plateau-27202-kishiwadashi-2024)」から CityGML（`27202_kishiwadashi_city_2024_citygml_1_op.zip`、約 265MB）をダウンロードします。
2. 次のコマンドを実行します。

   ```bash
   powershell -ExecutionPolicy Bypass -File tools/extract-plateau.ps1 -Zip path/to/27202_kishiwadashi_city_2024_citygml_1_op.zip -Out data/kishiwada.json
   ```

原点は北緯 34.4655°・東経 135.3725° で、x が東、z が南（単位 m）です。取り出すのは、原点から 560m 以内の建物（LOD0/LOD1 の平面形と高さ）と道路（LOD1 の面）です。

## 実物との違い

- 建物の平面形と高さは PLATEAU のとおりです。一方、外観（壁の模様・瓦屋根・店先・看板）は用途と高さから当てはめた作り物です。LOD1 には屋根の形の情報がないため、低層の住宅には切妻・寄棟の屋根を推定して載せています。
- コースの中心線は、PLATEAU の道路面と OpenStreetMap の道路線から読み取った目安です。桟敷や人垣の位置は、実際の祭の配置とは異なります。
- 地車は岸和田型の構成に沿って手作りしたモデルで、特定の町の地車を写したものではありません。
- 岸和田城は位置の目安だけを置いた遠景です。
- 岸和田だんじり祭や、各町・関係団体の公式なものではありません。

## ライセンス・出典

- プログラム：MIT License（`LICENSE`）
- 地図データ：国土交通省 Project PLATEAU「3D都市モデル（Project PLATEAU）岸和田市（2024年度）」を加工して作成。PLATEAU のサイトポリシーに従って利用しています。
- 道路名・交差点位置の確認：© OpenStreetMap contributors
- 3D 描画：[three.js](https://threejs.org/)（MIT）
