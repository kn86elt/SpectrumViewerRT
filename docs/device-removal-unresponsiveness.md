# デバイス取り外し時の応答不能問題 — 原因と対策

## 概要

本ツールの実行中にモニタリング対象デバイス（スピーカー・マイク等）を取り外すと、
アプリケーションが応答不能になる事象が確認されている。
本文書では原因の特定と対策の検討結果をまとめる。

---

## 対象コード

| ファイル | 役割 |
|---------|------|
| `WasapiLoopbackCapture.cs` | システム出力のループバックキャプチャ（WASAPI COM API） |
| `AudioCapture.cs` | マイク等の入力デバイスキャプチャ（WaveIn API） |
| `WasapiInterop.cs` | WASAPI COM インターフェース定義 |
| `AudioInterop.cs` | WaveIn P/Invoke 定義 |
| `MainWindow.xaml.cs` | UI スレッドからの `StopCapture()` 呼び出し |

---

## 現在のスレッド構成

```
[UIスレッド (Dispatcher)]
  └─ StopCapture() → _capture.Dispose() → Stop()
       └─ WasapiLoopbackCapture: _thread.Join(1秒) ← UIスレッドがここでブロック
       └─ AudioCapture: waveInStop / waveInReset / waveInClose ← UIスレッドがここでブロック

[WASAPIキャプチャスレッド]
  └─ CaptureLoop() の while ループ
       └─ captureClient.GetNextPacketSize() ← デバイス消失時にブロックの可能性
       └─ captureClient.GetBuffer()         ← デバイス消失時にブロックの可能性

[WaveIn ドライバコールバックスレッド]
  └─ OnWaveIn() → lock(_gate) → waveInAddBuffer() ← デバイス消失時にブロックの可能性
```

---

## 根本原因

### 原因 1：WASAPI COM 呼び出しのブロッキング

**該当箇所：** `WasapiLoopbackCapture.cs` 116〜162 行

```csharp
while (_running)
{
    // デバイス消失時、AUDCLNT_E_DEVICE_INVALIDATED を返す代わりに
    // 無期限ブロックするドライバが存在する
    ThrowIfFailed(captureClient.GetNextPacketSize(out var packetFrames), ...);
    // ...
    ThrowIfFailed(captureClient.GetBuffer(...), ...);
}
```

キャプチャスレッドは `GetNextPacketSize()` / `GetBuffer()` をポーリングしている。
デバイスが取り外されると、これらの COM 呼び出しがエラーを返さずに
無期限にブロックする場合がある（ドライバ・ハードウェアの実装依存）。

`_running = false` をセットしても、COM 呼び出しの内部でスレッドが止まっている場合は
制御が戻らない。

---

### 原因 2：Stop() が UI スレッドをブロックする

**該当箇所：** `WasapiLoopbackCapture.cs` 51〜57 行、`MainWindow.xaml.cs` 1443 行

```csharp
// MainWindow.StopCapture() — UI スレッドから呼ばれる
_capture?.Dispose();  // → Stop() を呼び出す

// WasapiLoopbackCapture.Stop()
_running = false;
_thread.Join(TimeSpan.FromSeconds(1));  // UI スレッドが最大 1 秒ブロック
_thread = null;
// Join タイムアウト後もバックグラウンドスレッドは生き続ける
```

`StopCapture()` → `Dispose()` → `Stop()` の呼び出しチェーンがすべて UI スレッド上で
実行されるため、ワーカースレッドが COM 呼び出しでハングしていると
**UI が最大 1 秒間フリーズ**する。

Join タイムアウト後も背後のスレッドは無効なデバイスへのアクセスを継続する。

---

### 原因 3：デバイス取り外し通知の未実装

**該当箇所：** `WasapiInterop.cs` 72〜74 行

```csharp
// インターフェースは定義されているが、呼び出し箇所が存在しない
int RegisterEndpointNotificationCallback(IntPtr client);
int UnregisterEndpointNotificationCallback(IntPtr client);
```

Windows は `IMMNotificationClient` を通じてデバイス状態変化を通知できるが、
本アプリでは未実装。アプリはデバイス取り外しを検知する手段を持たず、
次のブロッキング COM 呼び出し時に初めて問題が顕在化する。

---

### 原因 4：AudioCapture の WaveIn 系呼び出しが UI スレッドをブロック

**該当箇所：** `AudioCapture.cs` 81〜103 行

```csharp
// Stop() — UI スレッドから呼ばれる（タイムアウト機構なし）
AudioInterop.waveInStop(handle);    // デバイス消失時に無期限ブロックの可能性
AudioInterop.waveInReset(handle);   // 同上
// ...
AudioInterop.waveInClose(handle);   // 同上
```

WaveIn 系 API はすべて同期ブロッキング呼び出しであり、タイムアウト機構がない。

---

### 原因 5：AudioCapture のロック競合・デッドロックリスク

**該当箇所：** `AudioCapture.cs` 88〜103 行 と 149〜153 行

```
[ドライバコールバックスレッド]
OnWaveIn() {
    lock (_gate) {                              // ロック取得
        waveInAddBuffer(...);                   // ← デバイス消失時にブロックの可能性
    }
}

[UI スレッド]
Stop() {
    waveInReset(...);   // ← これがコールバックを誘発する
    lock (_gate) {      // コールバックがロックを保持中なら待機 → デッドロック
        waveInClose(...);
    }
}
```

`waveInReset` の実行でドライバが保留バッファを返却するコールバックを発火させ、
そのコールバックが `_gate` ロックを保持したまま `waveInAddBuffer` でブロックすると、
UI スレッドの `lock (_gate)` 待機と組み合わさり**デッドロック**になる。

---

## タイムライン図

```
時刻  UI スレッド (Dispatcher)        WASAPI ワーカースレッド
----  --------------------------        ----------------------
T0                                      while(_running) ループ中
T1    デバイスが取り外される
T2                                      GetNextPacketSize() ← ここでブロック
T3    ユーザーが Stop をクリック
T4    StopCapture() → Stop() 呼び出し
T5    _running = false をセット
T6    _thread.Join(1秒) ← UI フリーズ開始
T7    ワーカースレッドは依然ブロック中
T8    Join タイムアウト → UI 復帰
T9    （ワーカースレッドはまだ動いている）
```

---

## 対策案

### 対策 A：IMMNotificationClient の実装【根本解決・最優先】

`WasapiInterop.cs` に既に定義されている `RegisterEndpointNotificationCallback` を実際に使用する。

`IMMNotificationClient` インターフェースを実装し、
`OnDeviceStateChanged` / `OnDefaultDeviceChanged` コールバックで
即座に `_running = false` をセットする。
次のポーリングサイクルに入る前にスレッドを安全停止できる。

```
[デバイス取り外し]
  → Windows が OnDeviceStateChanged コールバックを発火
  → _running = false をセット
  → ループが Thread.Sleep(8) 後に条件チェックで正常終了
  → スレッド自然終了
```

---

### 対策 B：WASAPI イベント駆動モードへの切り替え【ブロッキング解消】

`SetEventHandle` + `AudioClientStreamFlagsEventCallback` を使い、
ポーリングをやめてイベント待機に切り替える。

```
現状：
while (_running) {
    GetNextPacketSize();  // ← ブロックの可能性
    Thread.Sleep(8);
}

改善後：
while (_running) {
    WaitForSingleObject(eventHandle, timeout: 20ms);  // タイムアウト付き待機
    if (キャンセル条件) break;
    GetNextPacketSize();  // バッファ準備済みなので即返る
}
```

`WaitForSingleObject` にタイムアウト（20ms 程度）を設定することで、
デバイス消失時もループを確実に抜け出せる。

---

### 対策 C：Stop() を UI スレッドから分離【UIフリーズ解消】

`_capture.Dispose()` を UI スレッド上で同期呼び出しするのをやめる。

```csharp
// 現状（UI スレッドがブロック）
_capture?.Dispose();

// 改善案（Task で非同期化）
var captureToDispose = _capture;
_capture = null;
Task.Run(() => captureToDispose?.Dispose());
```

UI を即座に操作可能な状態に戻し、スレッドのクリーンアップはバックグラウンドで行う。

---

### 対策 D：AUDCLNT_E_DEVICE_INVALIDATED の明示的ハンドリング

デバイス消失時に WASAPI が返すエラーコード `AUDCLNT_E_DEVICE_INVALIDATED`
（`0x88890004`）を「予期される正常終了条件」として扱い、
ユーザーへのエラー表示を抑制しつつ静粛に停止する。

対策 A・B で COM 呼び出しがハングしない経路が確保されていることが前提。

---

### 対策 E：AudioCapture の WaveIn 呼び出し保護【デッドロック解消】

`Stop()` 内の `waveInStop` / `waveInReset` / `waveInClose` を
専用バックグラウンドスレッドまたは `Task.Run` で実行し、UI スレッドをブロックしない。

ロック設計の見直し：
- `_gate` ロック内で WaveIn API を呼び出す構造を解消する
- コールバック終了確認と API 呼び出しを分離し、デッドロードリスクを排除する

---

### 対策 F：WM_DEVICECHANGE の補完的ハンドリング

`HwndSource.AddHook` で `WM_DEVICECHANGE` を受信し、
デバイス取り外しを検知した時点で先手を打って停止処理を開始する。

対策 A の WASAPI 通知と組み合わせることで、
WaveIn デバイスも含めた包括的な取り外し検知が可能になる。

---

## 対策の優先順位

| 優先度 | 対策 | 対象 | 解決する問題 |
|--------|------|------|------------|
| 最高 | **対策 A**：IMMNotificationClient 実装 | WASAPI | デバイス取り外しの事前検知 |
| 高 | **対策 B**：WASAPI イベント駆動モード | WASAPI | COM 呼び出しのブロッキング解消 |
| 高 | **対策 C**：Stop() 非同期化 | 共通 | UI フリーズの解消 |
| 中 | **対策 E**：WaveIn 保護 | WaveIn | デッドロック解消 |
| 中 | **対策 F**：WM_DEVICECHANGE | WaveIn | マイク等の補完的検知 |
| 低 | **対策 D**：エラーコード明示処理 | WASAPI | ユーザー体験の向上 |

---

## 参考：問題箇所一覧

| ファイル | 行 | 問題内容 | 深刻度 |
|---------|-----|---------|--------|
| `WasapiLoopbackCapture.cs` | 116〜162 | ブロッキング COM 呼び出しのポーリングループ | 致命的 |
| `WasapiLoopbackCapture.cs` | 51〜57 | `_thread.Join(1秒)` で UI スレッドブロック | 致命的 |
| `WasapiInterop.cs` | 72〜74 | `RegisterEndpointNotificationCallback` 未使用 | 致命的 |
| `AudioCapture.cs` | 81〜103 | `waveInStop/Reset/Close` を UI スレッドで同期呼び出し | 致命的 |
| `AudioCapture.cs` | 88〜103, 149〜153 | `_gate` ロックとコールバックのデッドロックリスク | 高 |
| `MainWindow.xaml.cs` | 1443 | `_capture.Dispose()` を UI スレッドで同期実行 | 高 |
