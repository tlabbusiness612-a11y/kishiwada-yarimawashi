# 岸和田やりまわし（Unity 版）

ブラウザ版（リポジトリ直下）を Unity 6 で作り直したものです。PC（Windows）と iPhone の両方で動かすため、描画は URP（Universal Render Pipeline）を使っています。

町並み・地車・人・音・質感は、すべてコードで組み立てています。そのため、写真素材や 3D モデルがなくても開いてすぐに動きます。

## 開き方

1. Unity Hub で「Add → Add project from disk」から `unity/` フォルダを選びます。エディタは **6000.6.2f1**（Unity 6.6）です。
2. 初回だけ、メニューの **Kishiwada → セットアップ（材質の雛形とシーン）** を実行します。`Assets/Kishiwada/Scenes/Main.unity` ができます（リポジトリには作成済みのものが入っています）。
3. `Main.unity` を開いて再生します。町並みや人は再生の開始時に組み立てるので、編集中のシーンには地車も町も見えません。

### Mac で開く

1. [Unity Hub](https://unity.com/download) を入れます。「Installs」からエディタ **6000.6.2f1** を入れ、次のモジュールも選びます。
   - **Mac Build Support (Mono)**：Mac で遊ぶ版を作るため
   - **iOS Build Support**：iPhone に入れるため
2. App Store から **Xcode** を入れます（iPhone に入れる場合だけ必要です）。
3. リポジトリを取ってきます。

   ```bash
   git clone https://github.com/tlabbusiness612-a11y/kishiwada-yarimawashi.git
   cd kishiwada-yarimawashi
   git checkout unity-port   # main に取り込むまでは Unity 版はこのブランチにあります
   ```

4. Unity Hub の「Add → Add project from disk」で、取ってきたフォルダの中の `unity/` を選びます（リポジトリ直下ではありません）。
   - 初回は `Library/` の作成とパッケージの取得に数分かかります。
   - 字は Mac のヒラギノで描くので、追加のフォントはいりません。

### iPhone に入れる（Mac）

1. Unity のメニュー **Kishiwada → ビルド → iOS（Xcode プロジェクト）** を選びます。`Build/iOS/` に Xcode プロジェクトができます。
2. `Build/iOS/Unity-iPhone.xcodeproj` を Xcode で開きます。
3. 左の一覧で `Unity-iPhone` を選び、「Signing & Capabilities」の **Team** に自分の Apple ID を選びます（無料の Apple ID でも 7 日間は動きます）。
   - 「Bundle Identifier が使えない」と出たら、`com.tlab.kishiwadayarimawashi` の末尾を変えます。
4. iPhone をケーブルでつなぎ、上の実行先で iPhone を選んで ▶ を押します。
   - iPhone 側で「設定 → 一般 → VPN とデバイス管理」から開発元を信頼し、「デベロッパモード」を入れる必要があることがあります。

## 操作

| 操作 | キーボード | ゲームパッド | iPhone |
| --- | --- | --- | --- |
| 曳く（鳴物の勢いを上げる） | Space（拍に合わせる） | A | 画面の真ん中を拍に合わせて連打 |
| 前梃子（その側の前輪を止めて曲がる） | ← →（Q E） | LT RT（押し込み具合で効きが変わる） | 左右の縦の帯を押す |
| 後梃子（尻を外へ振る） | A D（触らなければ前梃子に合わせて自動） | 左スティック | 自動 |
| 大工方が跳ぶ | J | Y | 「跳」 |
| 視点（追走・大工方・桟敷・空撮） | C | RB | 右上のボタン |
| 一時停止 | Esc | Start | 右上の「Ⅱ」 |

ゴールすると結果画面になります。「リプレイ」を押すと、一つ目の角の手前から見返せます。カメラは、直線では空から、角では桟敷から、路地では追走で映します。

## 動きの仕組み

- **地車**：重さ 4 トンの剛体（PhysX）です。車輪ごとの摩擦は自前で計算しています（木の車輪とアスファルト）。
  - 転がっている間は、横滑りの大きさに応じて摩擦が立ち上がります。車輪が止まると、進んでいる向きの逆へ動摩擦がかかります。縦横の摩擦の合計は、摩擦円で上限を決めています。
  - 前梃子は「その側の前輪のブレーキ」と「前を曲がる側へ押す力」です。後梃子は尻を外へ振る力です。
  - 家や桟敷には、本物の当たり判定でぶつかります。当たった強さが減点と揺れ・音になります。
- **綱と曳き手**：綱 2 本（各 48m、1m おき）を XPBD（位置で解く拘束法）で解いています。1 ステップを 8 回に刻んで解きます。
  - 4 本目から先の点は「曳き手 3 人分」の重さ（210kg）と力を持ちます。道筋の少し先を目指して走ります。
  - 引く力は速さとともに落ちます（筋肉の力と速さの関係）。そのため、まっすぐでは地車は曳き手の速さに揃います。角では、綱が斜めに地車の前を引きます。
  - 綱の端は地車の剛体に、その点の実効質量でつながっています。張力はそのまま地車にかかります。
- **鳴物**：音声スレッドで合成しています。太鼓（膜の倍音）・鉦（非整数倍音）・篠笛（息の音とビブラート）・「そーりゃ」の掛け声（フォルマント合成）・歓声・車輪のごとつき・横滑りの音です。拍の判定は、出力の遅れも差し引いて行います。

数値（重さ・摩擦・梃子の力・曳き手の力など）は、シーンの `Game` オブジェクトのインスペクタで再生中に変えられます。

## ファイル構成

| パス | 内容 |
| --- | --- |
| `Assets/Kishiwada/Scripts/Game/Game.cs` | 組み立てと進行（タイトルのデモ走行 → 用意 → 曳行 → ゴール → 結果）、自動試験 |
| `Scripts/Danjiri/DanjiriBody.cs` | 地車の物理（車輪の摩擦・前梃子・後梃子・衝突） |
| `Scripts/Crowd/RopeSim.cs` | 綱と曳き手の XPBD |
| `Scripts/Danjiri/DanjiriBuilder.cs`・`Carving.cs` | 岸和田型の地車（彫り物は本物の凹凸、組物・垂木・高欄・擬宝珠・鬼板・懸魚） |
| `Scripts/World/TownBuilder.cs` | PLATEAU の建物・道路から町並み（町家・店先・看板・瓦屋根・バルコニー・電柱・提灯・柵・桟敷） |
| `Scripts/People/*` | 骨組みつきの人の形・動き（走り・綱を握る IK・大工方の踊りと跳び・鳴物方）と見物人 |
| `Scripts/Audio/Narimono.cs` | 鳴物と環境音の合成 |
| `Scripts/Game/Hud.cs` | 画面（UI Toolkit） |
| `Scripts/Util/TextureFactory.cs` | 質感の生成（欅・燻し瓦・漆喰・格子・外壁・アスファルトなど、法線つき） |
| `Shaders/KishiwadaLit.shader` | URP の Lit に頂点色（色味・彫りのくぼみの陰）と地面近くの汚れを足したもの |
| `Resources/kishiwada.json` | PLATEAU から取り出した建物・道路（リポジトリ直下の `data/` と同じもの） |

## ビルド

- **Windows**：メニュー **Kishiwada → ビルド → Windows** を選ぶと `Build/Windows/` に書き出します。
- **macOS**：メニュー **Kishiwada → ビルド → macOS** を選ぶと `Build/macOS/KishiwadaYarimawashi.app` に書き出します（Mac Build Support が必要です）。
- **iPhone**：メニュー **Kishiwada → ビルド → iOS（Xcode プロジェクト）** を選ぶと、`Build/iOS/` に Xcode プロジェクトを書き出します。
  - 実機に入れるには、Mac の Xcode でこのプロジェクトを開き、署名（Team）を設定して実行します。
  - iPhone では描画の品質設定が自動で `Mobile` になります。影は近くだけで解像度も下げ、曳き手は 1 点に 1 人、見物人の影なしで描きます。

コマンドラインからも実行できます。

```bash
"C:/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe" -batchmode -projectPath unity -executeMethod Kishiwada.EditorTools.KishiwadaBuild.BuildWindows -quit -logFile build.log
```

## 自動試験

ビルドした実行ファイルに引数を渡すと、自動で曳いて記録を残し、終わると閉じます。

```bash
unity/Build/Windows/KishiwadaYarimawashi.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -kwAuto -kwQuit -kwLog run.csv -kwShots "4:0,30:2,34:3" -kwShotDir shots
```

- `-kwLog`：0.1 秒ごとの位置・速さ・向き・綱の張力・梃子の入力を CSV に書きます。最後に角ごとの採点を書きます。
- `-kwShots`：「秒:視点」の並びです（視点は 0 追走・1 大工方・2 桟敷・3 空撮）。その時刻の画面を PNG で保存します。
- `-kwMobile`：PC でも iPhone と同じ軽い設定で動かします（曳き手の数・質感の解像度など）。描画の品質設定は PC のままです。
- `-kwNoSteer`：梃子を使わずに曳きます。角で外へ膨らんで当たるかを確かめるのに使います。
- `-kwReplay`・`-kwReplayShots "5,11,15"`：ゴール後に自動でリプレイを流し、指定の秒の画面を保存します。

iOS と Windows を続けて書き出すときは、コマンドラインに `-buildTarget Win64`（または `iOS`）を付けてください。付けずに対象を切り替えながら書き出すと、URP の SSAO の資源が抜けて画面が真っ暗になったことがあります。メニューからのビルドでは自動で切り替えます。

## これから作り込むところ

- 人物と地車は、手続きで作った形です。実物の地車の 3D スキャンやモデル、モーションキャプチャの動きに差し替えると、見た目は大きく変わります。人の骨組みは Unity の Humanoid と同じ並びなので、差し替えやすくしてあります。
- 地形は平らです。PLATEAU の地形（DEM）や LOD2 の建物（屋根の形・テクスチャ）を入れると、町並みの正確さが上がります。
- 写真モード・夜の灯入れ曳行（提灯）・複数の町・天候は、まだありません。
- 物理の数値（摩擦・梃子の力・曳き手の力）は、自動運転で角を回れるところまで合わせた段階です。実際に操作しての手触りは、これから詰めます。
