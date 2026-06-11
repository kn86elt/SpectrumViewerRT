# 描画パフォーマンス問題 — 原因と対策

## 概要

本ツールは描画が重い傾向があり、またFPSスライダーを下げると
スペクトログラムや波形の時間分解能が低下するという問題がある。
本文書では描画処理のボトルネック分析と、データ収集・描画の分離に関する対策をまとめる。

---

## 現在のアーキテクチャ

```
[デバイススレッド]
  └─ 音声サンプルを生成
       └─ ConcurrentQueue<short[]> _pendingSamples に Enqueue

[UI スレッド (DispatcherTimer — 1/FPS 秒ごとに発火)]
  └─ RenderTimer_Tick() ── すべての処理がここに集中
       ├─ サンプルキューの全デキュー
       ├─ FFT 計算（4096 点）
       ├─ スペクトログラムピクセルシフト（1200 × 620 px）
       ├─ スペクトログラム列の色塗り
       ├─ 波形ピクセルシフト（1200 × 140 px）
       ├─ 波形列の描画
       ├─ スペクトラムアナライザ計算・InvalidateVisual
       ├─ レベルメーター更新・InvalidateVisual
       └─ VFD ステータス更新
```

UI スレッドが重い処理を抱えているため、処理時間がタイマー周期を超えると
UI のクリック・スクロール応答が遅延する。

---

## パフォーマンスボトルネック

### ボトルネック 1：ハン窓の毎フレーム再計算

**該当箇所：** `MainWindow.xaml.cs` 938〜941 行、1038〜1041 行

```csharp
for (int i = 0; i < FftSize; i++)
{
    // ← cos() を 4096 回、フレームごとに毎回計算
    double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
    real[i] = _sampleWindow[start + i] / 32768.0 * hann;
}
```

`FftSize`（4096）は定数のため、窓係数は変化しない。
にもかかわらず毎フレーム 4096 回のコサイン計算を行っている。

---

### ボトルネック 2：FFT 作業配列の毎フレームアロケーション

**該当箇所：** `MainWindow.xaml.cs` 934〜935 行、1035〜1036 行

```csharp
var real = new double[FftSize];       // ← フレームごとに 32KB アロケーション
var imaginary = new double[FftSize];  // ← 同上
```

スペクトログラムとスペクトラムアナライザでそれぞれ発生するため、
1 フレームあたり最大 4 回（ステレオ時）の `double[4096]` アロケーションが起きる。
GC の発生頻度が上がり、GC 停止がフレームドロップの原因となり得る。

---

### ボトルネット 3：スペクトログラムの行単位ピクセルシフト

**該当箇所：** `MainWindow.xaml.cs` 1028〜1033 行

```csharp
for (int y = 0; y < ImageHeight; y++)   // 620 回ループ
{
    int rowStart = y * rowBytes;
    Buffer.BlockCopy(_pixels, rowStart + shiftBytes, _pixels, rowStart, rowBytes - shiftBytes);
    Array.Clear(_pixels, rowStart + rowBytes - shiftBytes, shiftBytes);
}
```

1 フレームあたりのコピー量：
- `columns = 1` のとき：620 行 × 1199 列 × 4 バイト ≈ **2.97 MB** をコピー
- `columns = 5` のとき：620 行 × 1195 列 × 4 バイト ≈ **2.96 MB** をコピー

これが毎フレーム UI スレッドで実行される。

---

### ボトルネット 4：描画オブジェクトの毎フレーム大量生成

**該当箇所：** `SpectrumAnalyzerDisplay.cs` 320〜334 行、`VfdLevelMeter.cs` 251〜259 行

```csharp
// セグメントごと・フレームごとに毎回新規生成
dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 36)), ...);
dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 70)), ...);
dc.DrawRoundedRectangle(new SolidColorBrush(color), ...);
```

バンド数 × 22 行（セグメントスタイル）で、1 フレームあたり数百個の
`SolidColorBrush` オブジェクトが生成・破棄される。

同様に `FormattedText`（ラベル類）、`Pen`（グリッド線）も毎フレーム生成される。

---

### ボトルネット 5：テクスチャ描画の多数の DrawLine 呼び出し

**該当箇所：** `SpectrumAnalyzerDisplay.cs` 364〜376 行

```csharp
// 新しい Pen を毎フレーム生成して約 207 本の水平線を描画
var horizontalPen = new Pen(new SolidColorBrush(...), 1);
for (double y = 1.5; y < bounds.Height; y += 3)     // 高さ 620px → 約 207 回
    dc.DrawLine(horizontalPen, ...);

// 約 92 本の垂直線
var verticalPen = new Pen(new SolidColorBrush(...), 1);
for (double x = 4.5; x < bounds.Width; x += 13)     // 幅 1200px → 約 92 回
    dc.DrawLine(verticalPen, ...);
```

テクスチャだけで 1 フレームあたり約 **300 回の DrawLine** と
毎フレームの Pen オブジェクト生成が発生する。

---

### ボトルネット 6：DependencyProperty への無条件書き込み

**該当箇所：** `MainWindow.xaml.cs` 846〜854 行

```csharp
// タイマーティックごとに毎回セット（値が変わっていなくても）
LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
LevelMeter.ShowUnlitSegments = ShowUnlitCheck.IsChecked == true;
LevelMeter.GlowEnabled = GlowCheck.IsChecked == true;
LevelMeter.TextureEnabled = TextureCheck.IsChecked == true;
```

これらすべてに `AffectsRender` フラグが設定されているため、
値が変化していなくても毎ティックで `InvalidateVisual()` が発行される。

非点滅スタイル（MeterStyle が 0 or 1）では 4 つのレベル値も毎ティック無条件書き込み。

---

### ボトルネット 7：ホットループ内での UI 要素アクセス

**該当箇所：** `MainWindow.xaml.cs` 1076〜1079 行（620 回ループの内側）

```csharp
private double FrequencyForRow(int y)
{
    // 620 回ループの内側で毎回 UI コントロールの SelectedItem にアクセス
    if (ScaleCombo.SelectedItem as string == "Log")
        return 20.0 * Math.Pow(MaxFrequency / 20.0, normalized);
    ...
}
```

---

### ボトルネット 8：グロー有効時のテキスト 6 倍描画

**該当箇所：** `SpectrumAnalyzerDisplay.cs` 385〜390 行

```csharp
// グロー効果のために 6 方向にオフセットして DrawText を追加発行
foreach (var offset in new[] { (-1,0), (1,0), (0,-1), (0,1), (-1,-1), (1,1) })
    dc.DrawText(glowText, ...);
dc.DrawText(formatted, ...);  // 本体
```

ラベル 1 つあたり計 7 回の `DrawText` 呼び出しになる。

---

## データ/描画の結合問題

### 現状の FPS とデータ分解能の関係

```
_scrollColumnAccumulator += PixelsPerSecond / FPS   // MainWindow.xaml.cs 667 行
_renderTimer.Interval = 1.0 / FPS                   // MainWindow.xaml.cs 687 行
```

タイマー周期 = `1/FPS` 秒
→ その間に蓄積されるサンプル数 ≈ `48000 / FPS`
→ これを 1 回の FFT で処理
→ 1 FFT が代表する時間幅 = `1/FPS` 秒

| FPS | タイマー周期 | 1 ティックのサンプル数 | 描画列数 | 時間分解能 |
|-----|------------|--------------------|---------|---------| 
| 60  | 16ms       | 約 800 サンプル      | 1〜2 列  | 細かい   |
| 30  | 33ms       | 約 1600 サンプル     | 2〜4 列  | 中程度   |
| 10  | 100ms      | 約 4800 サンプル     | 6〜12 列 | 荒い     |

**FPS を下げると時間分解能が落ちる直接的な原因**：
`DrawSpectrumColumns` では 1 回の FFT 結果で `columns` 列分を同一の色で塗りつぶすため、
列数が増えても表現できる情報量は 1 FFT ぶん（= 1 タイマー周期）のまま。

波形（`DrawWaveformColumns`）も同様で、1 ティック分のサンプル全体の min/max を
`columns` 列に一様に描く。FPS が低いほど 1 ティックあたりのサンプル数が増え、
min/max の範囲が広がって波形が「潰れた」表示になる。

---

## 対策案

### 高速化対策

#### 対策 1：ハン窓のキャッシュ【効果：大、コスト：小】

起動時に窓係数配列を 1 回だけ計算してフィールドに保持し、毎フレームの
コサイン計算を廃止する。

```csharp
// 起動時（または FftSize 変更時）に 1 回だけ計算
private static readonly double[] HannWindow = Enumerable.Range(0, FftSize)
    .Select(i => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1)))
    .ToArray();

// フレームごとの処理
real[i] = _sampleWindow[start + i] / 32768.0 * HannWindow[i];  // 乗算のみ
```

---

#### 対策 2：FFT 作業配列をフィールドに昇格【効果：中、コスト：小】

`double[] _real` / `double[] _imaginary` をインスタンスフィールドとして宣言し、
毎フレームの `new double[4096]` アロケーションを廃止する。

GC 発生頻度の低減と、GC に起因するフレームドロップを解消できる。

---

#### 対策 3：ブラシ・ペンのキャッシュと Freeze【効果：大、コスト：中】

使用する色は限定的（アクティブ色・非アクティブ色・グロー色・グリッド色）なので、
起動時またはカラーテーマ変更時に生成して `.Freeze()` しキャッシュする。

```csharp
// 起動時またはテーマ変更時に生成
_activeBrush = new SolidColorBrush(ActiveColor());
_activeBrush.Freeze();  // Freeze でスレッド間共有可・WPF 内部最適化が有効になる

// フレームごとの描画
dc.DrawRoundedRectangle(_activeBrush, null, rect, 1, 1);  // 再利用
```

`FormattedText` も静的ラベル（周波数ラベル、dB グリッド値）はキャッシュ可能。

---

#### 対策 4：テクスチャのキャッシュビットマップ化【効果：中、コスト：中】

テクスチャはサイズ変更時以外に内容が変わらないため、
`RenderTargetBitmap` に 1 回描いてキャッシュし、
フレームごとに `dc.DrawImage()` の 1 呼び出しに置き換える。

```
現状：DrawLine × 約 300 回 + 毎フレーム Pen 生成
改善後：DrawImage × 1 回
```

---

#### 対策 5：DependencyProperty 変更チェックの追加【効果：中、コスト：小】

カラーテーマ・スタイル等の設定系プロパティは実際に値が変化したときだけセットする。

```csharp
// 変更チェックを追加
int newTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
if (LevelMeter.ColorTheme != newTheme)
    LevelMeter.ColorTheme = newTheme;
```

非点滅スタイルのレベル値も、前フレームと差分がなければ書き込まない。

---

#### 対策 6：ホットループ内の UI 要素アクセスをキャッシュ【効果：小、コスト：小】

`FrequencyForRow` 内の `ScaleCombo.SelectedItem` 参照をループの外に出し、
bool 変数に保持する。

```csharp
// ループ前にキャッシュ
bool logScale = ScaleCombo.SelectedItem as string == "Log";
double maxFreq = MaxFrequency;

for (int y = 0; y < ImageHeight; y++)
{
    double frequency = logScale
        ? 20.0 * Math.Pow(maxFreq / 20.0, normalized)
        : Math.Max(20.0, normalized * maxFreq);
    ...
}
```

---

#### 対策 7：FFT 処理をバックグラウンドスレッドへ移動【効果：大、コスト：大】

専用スレッドまたは `Task` で FFT を実行し、結果（スペクトル強度配列）を
`ConcurrentQueue` 経由で UI スレッドに渡す。

```
現状：
  UI スレッド → FFT → ピクセル生成 → WritePixels → 描画

改善後：
  [計算スレッド] → FFT → スペクトル結果キュー
  [UI スレッド]  → 結果キューから読み出し → WritePixels → 描画
```

UI スレッドの処理時間が大幅に削減され、
FPS を上げても UI 応答性が維持される。

---

### データ/描画分離の対策

#### 対策 A：FFT 処理レートとレンダリングレートの分離【根本解決】

現状の問題は「タイマー周期 = データ処理間隔 = 表示時間分解能」がすべて同一であること。

```
【現状】
デバイス → サンプルキュー → [UI タイマー: FFT + 描画]
                                 ↑ FPS が全てを制御

【改善後】
デバイス → サンプルキュー → [計算スレッド: FFT → スペクトル列バッファ]  ← 固定レート（例: 50Hz）
                                        ↓
                           [UI タイマー: バッファ読み出し + 描画]  ← 任意 FPS
```

計算スレッドを一定間隔（例: 20ms = 50回/秒）で動作させ、
FFT 結果を「スペクトル列リングバッファ」に追記する。

UI タイマーはこのバッファから「画面に表示すべき列数」を読み出して描画するだけにする。

---

#### 対策 B：スペクトログラムのスクロール方式変更【対策 A の補完】

現状は全ピクセルバッファを毎フレームシフトしている。

対策 A と組み合わせた場合、「リングバッファ上のどの位置から表示するか」
というオフセット管理に変更することで、
物理的なメモリコピー（約 3MB/フレーム）を廃止できる。

```
現状：毎フレーム Buffer.BlockCopy で全行をシフト（約 3MB/フレーム）
改善後：バッファ先頭オフセットを更新するだけ（O(1)）
```

描画時は `WritePixels` を 2 回（折り返し部分）呼ぶか、
ビットマップのオフセット描画を利用する。

---

#### 対策 C：波形の時間分解能の独立化

波形の min/max サンプリングも計算スレッドで固定レートで行い、
「列ごとの min/max 値」をリングバッファに格納する。

UI タイマーはバッファから列データを読み出すだけになり、
FPS に依存しない時間分解能が得られる。

---

## 対策の優先順位と期待効果

| 優先度 | 対策 | 区分 | 期待効果 | 実装コスト |
|--------|------|------|---------|----------|
| 最高 | **対策 1**：ハン窓キャッシュ | 高速化 | 毎フレーム 4096 cos() 廃止 | 小 |
| 最高 | **対策 2**：FFT 配列フィールド化 | 高速化 | GC 停止低減 | 小 |
| 高 | **対策 3**：ブラシ・ペンキャッシュ | 高速化 | オブジェクト生成大幅削減 | 中 |
| 高 | **対策 5**：DP 変更チェック | 高速化 | 不要な InvalidateVisual 廃止 | 小 |
| 高 | **対策 A**：FFT/描画レート分離 | 分離 | FPS 依存の分解能低下を解消 | 大 |
| 中 | **対策 4**：テクスチャキャッシュ | 高速化 | DrawLine × 300 を DrawImage × 1 に | 中 |
| 中 | **対策 7**：FFT バックグラウンド化 | 高速化 | UI スレッドの処理負荷を大幅削減 | 大 |
| 中 | **対策 B**：スクロール方式変更 | 分離 | 毎フレーム 3MB コピーを廃止 | 大 |
| 低 | **対策 6**：ループ外キャッシュ | 高速化 | UI アクセス × 620 回を廃止 | 小 |

---

## 参考：問題箇所一覧

| ファイル | 行 | 問題内容 |
|---------|-----|---------|
| `MainWindow.xaml.cs` | 938〜941, 1038〜1041 | ハン窓の毎フレーム再計算（cos × 4096） |
| `MainWindow.xaml.cs` | 934〜935, 1035〜1036 | FFT 配列の毎フレームアロケーション |
| `MainWindow.xaml.cs` | 1028〜1033 | スペクトログラムの行単位ピクセルシフト（約 3MB/フレーム） |
| `MainWindow.xaml.cs` | 846〜854 | DP への毎ティック無条件書き込み |
| `MainWindow.xaml.cs` | 1048〜1076 | ホットループ内での UI 要素参照・文字列比較 |
| `SpectrumAnalyzerDisplay.cs` | 320〜334 | セグメントごとに SolidColorBrush を新規生成 |
| `SpectrumAnalyzerDisplay.cs` | 364〜376 | テクスチャ描画で DrawLine × 約 300 回・毎回 Pen 生成 |
| `SpectrumAnalyzerDisplay.cs` | 385〜390 | グロー有効時にテキスト 1 つあたり DrawText × 7 回 |
| `VfdLevelMeter.cs` | 251〜259 | DrawRectangle ごとに SolidColorBrush を新規生成 |
