# Report_NiMo — CircuitSketch full-project audit

**Product:** CircuitSketch (offline ESP32/Arduino simulator, C#/.NET 10 + WPF)
**Tree:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32` @ `f103d4f` (Alpha 25)
**Date:** 17 October 2026
**Requested by:** user · **Author:** NiMo (OpenCode)

---

## How this audit was done

| Step | Result |
|---|---|
| `dotnet build app/App.csproj --artifacts-path .artefatos/nimo` | **0 warnings, 0 errors** (4 min) |
| Full test suite `CircuitSketch.exe --teste` | **6681 passed, 10 failed** (log: `.artefatos/nimo/saida-teste.txt`) |
| Deep static audit — `motor/` (interpreter, peripherals, radio, TCC) | done |
| Deep static audit — `app/` (WPF, editor, netlist, save/load, autotests) | done |
| Deep static audit — security surface (TCC, HTTP, pipe, scripts, zip) | done |
| Docs-vs-code cross-check (README, PLANO-ALPHAS, agent reports) | done |
| Critical findings re-verified line-by-line by the lead before publishing | yes |

**Caveat on timing:** the test suite ran on this machine *while* three parallel audit
processes were hammering the CPU/disk. The **3 wall-clock performance failures**
below are suspect because of that; the 7 functional failures use the virtual clock
and are not affected by CPU load. One audit report (`RELATORIO-AUDITORIA.md`, 16 Oct)
was used as a map — every claim taken from it was re-verified or re-tested here.

Severity: **P0** = host compromise / whole-process crash · **P1** = wrong vs. real
hardware or a broken feature a student will hit · **P2** = incomplete or annoying but
fenced · **P3** = debt / docs.

---

## Executive summary

The project is in strong shape: clean build, **6681 green tests**, and most of the
failures recorded in the previous audit (slide-switch, MPU6050 drone/nível, IR remote,
rovers, HX711 project, ventilador-L298N) **now pass** — see §8. What remains clusters
in five places:

1. **7 real functional failures** in the suite: the three stepper *driver* modules
   (A4988 / DRV8825 / ULN2003) don't turn their motors; `fechadura-teclado-servo` is
   broken; `sono-profundo-botao` doesn't sleep; ESP-NOW sensor→panel button is dead;
   demo `esp09-dado` doesn't do what it promises (§1).
2. **One new, verified security hole**: a `.sktch` file can write arbitrary files
   outside `%TEMP%` via crafted native-tab names, and opening a project **auto-runs
   it** by default — the shortest attack path in a classroom share (§2.1).
3. **Host trust**: student C runs in-process as RWX x86-64 with no W^X; the named pipe
   accepts a sketch path from any same-user process and auto-runs it; sketch HTTP
   defaults to using the school PC's internet (§2.2–2.4).
4. **A cluster of real UI/state bugs**: leaked 25 fps timers on every closed window,
   paste that silently loses wire bends/board mapping, undo that leaves the object bar
   acting on ghost pieces (§4).
5. **Docs drifted far from the code**: catalog is 161 pieces / 34 boards / 54 demos /
   6681 tests — README still says 50 / 6 / 6 / 1180, contradicts itself on Upload, and
   lists BLE and `configTime` as missing when both exist (§6).

---

## 1. Test-suite failures — 6681 ✔ / 10 ✘ (run today)

### 1.1 Functional (virtual clock — treat as real regressions, P1)

| # | Test | Symptom |
|---|---|---|
| 1 | `bateria: a4988 funciona no circuito` | axis **0 → 0°** — motor never moves |
| 2 | `bateria: drv8825 funciona no circuito` | axis **29 → 29°** — motor never moves |
| 3 | `bateria: uln2003 funciona no circuito` | axis **0 → 4°** — barely twitches |
| 4 | `fechadura-teclado-servo` | correct password judged wrong; servo stays 0°; green LED 0.00 |
| 5 | `sono-profundo-botao` | ESP32 never enters deep sleep (`monitor: ligou 1 vez(es)`) |
| 6 | `esp-now: o botao do sensor acende o LED do painel` | forward path dead — *release* and *return* path pass |
| 7 | `esp09-dado` demo | runs clean but doesn't do what its description promises |

Note `stepper-motor` and `motor-28byj48` pass in the same block — the failure is in the
**driver modules** (A4988/DRV8825/ULN2003), not the stepping logic. For #6, the
asymmetry (panel→sensor works, sensor→panel doesn't) points at the send/queue path of
the originating board, not the ESP-NOW model itself.

### 1.2 Wall-clock performance (suspect — re-run on an idle machine, P2)

| Test | Measured | Claim |
|---|---|---|
| `refazer a rede cabe num quadro` | 7.57 ms | < 1 frame |
| `desempenho: o quadro custa menos de 1 ms` | **3.30 ms** (82.6% of a core @25 fps) | README Marco 14 claims 0.01 ms on the *same* ~1746-node scene |
| `velocidade: o executor passa dos 16 MHz` | 15.0 MHz (0.9× ATmega328P) | ≥ 16 MHz |

The frame test being **330× off** the README's recorded number is too large for CPU
contention alone — this is either a **performance regression since Marco 14** or a
misrecorded baseline. Worth one dedicated run on an idle machine before deciding.

---

## 2. Security — host exposure (classroom PC)

### 2.1 Path traversal via native tab names + auto-run = shortest attack path (P0/P1) — NEW, verified

`motor/Nativo/ModuloNativo.cs` (~line 193) writes the project's C tabs to disk with
**no validation** of the tab name:

```csharp
string destino = Path.Combine(pastaTemporaria, f.Nome.Replace('/', Path.DirectorySeparatorChar));
Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
File.WriteAllText(destino, f.Texto ?? "", new UTF8Encoding(false));
```

The only tab-name validation in the codebase (`NomeDeArquivoVale`,
`app/MainWindow.xaml.cs:691-707`) runs **only in the UI** (create/rename tab) — it does
not run when a `.sktch` is loaded (`Sktch.AbrirCompleto` → `Circuito.Arquivos` keys are
never checked). A tab named `..\..\..\..\Users\<user>\AppData\Roaming\...\Startup\algo.h`
escapes `%TEMP%` with attacker-chosen content. And opening a project **auto-runs it by
default** (`MainWindow.xaml.cs:4079`, `RodarAoAbrirProjeto` defaults `true`), and the
named pipe (§2.3) can open a project without any UI at all.

**Fix (small):** validate `f.Nome` (invalid chars, no `..`, no `\`) and prefix-check
`Path.GetFullPath(destino)` inside `pastaTemporaria` — copy the exact design already
done right in `app/Acervo.cs:484-510` (zip-slip guard). Also validate `Arquivos` keys
at load time in `Sktch.AbrirCompleto`.

### 2.2 Student C runs in-process, RWX, no sandbox (P1)

`ModuloNativo.cs:239-244` compiles with TinyCC to memory and runs on **real Windows
threads** (`:504-512`). The code+data buffer is allocated `PAGE_READWRITE` and made
executable by libtcc itself using stock `PAGE_EXECUTE_READWRITE` — **no `VirtualProtect`
anywhere in the C# code, and `nativo/tcc/fonte/circuitsketch-rodata.patch` touches
sections, not page protection. No W^X.**

Mitigations that *do* exist and are real: `-nostdinc -nostdlib`, `#undef _WIN32/_WIN64`,
`system()` stubbed to -1 (`nativo/runtime/cs_texto.c:696`), `fopen` redirected to the
in-memory SD card, VEH trampoline turning bad pointers into Guru Meditation, fixed
`cs_host` import table. What's missing: any barrier against shellcode — 30 lines of
headerless C find `kernel32!CreateProcessW` via PEB walk. Honest options, in cost order:
(a) a "restricted mode" preference that disables native mode on shared machines;
(b) W^X (split code into `PAGE_EXECUTE_READ`, data into `PAGE_READWRITE` — tcc gives the
section offsets); (c) run native C in a sandboxed child process (correct but expensive).

### 2.3 Named pipe: any same-user process → load + auto-run (P1)

`app/App.xaml.cs:621-669`: `NamedPipeServerStream("CircuitSketch_Canal")` with default
ACL, payload is **one line, unchecked path**, delivered to `CarregarSketch`, which
auto-runs (§2.1). Two additional defects in the same code:

- `catch { return; }` — the comment says it's for shutdown, but it swallows **any**
  transient error and **permanently kills** single-instance forwarding; second launches
  then hang 2 s and silently do nothing.
- No `PipeSecurity`; no check that the path is an existing `.sktch`.

**Fix:** `PipeSecurity` restricting to the current user, validate path exists and ends
with `.sktch`, and **don't auto-run** projects that arrive via the pipe.

### 2.4 Sketch HTTP uses the school PC's internet, on by default (P1)

`motor/Rede.cs:63`: `public bool UsarInternet { get; set; } = true;` and the UI assumes
true (`MainWindow.xaml.cs:2983`). A sketch can reach any `http/https` host (including
`127.0.0.1` — SSRF against localhost services — and the school LAN), and `setInsecure()`
disables TLS validation (`InterpretadorRede.cs:965`, `Rede.cs:399-406`). Off by default
would match the "offline simulator" pitch; a per-sketch destination list in the network
panel would match the classroom.

**What's genuinely good here:** sketch *servers* bind loopback-only (`Rede.cs:1353`,
`NativoRede.cs:92`, `Mqtt.cs:667`, `PortaBluetooth.cs:58`) — nothing is exposed to the
LAN; `AllowAutoRedirect = false` on both HTTP handlers.

### 2.5 Library downloads — the best-defended surface (no action)

`app/BibliotecasOnline.cs:234-252`: HTTPS-only, host exactly `downloads.arduino.cc`,
default port, no userinfo, size cap, **SHA-256 vs the index**, and the final
post-redirect URL re-checked with redirects disabled. `app/Acervo.cs:484-510`: zip-slip
guard + 200 MB uncompressed cap, with an autotest that tries `../../fora.h`. Residual:
no cert/SPKI pinning — acceptable on a school network; revisit only if the index origin
ever changes.

### 2.6 Scripts / installer — low risk (minor items)

- No `Invoke-Expression` anywhere; no `shell=True` in the Python tools; `netsh` /
  `powershell -Command` calls use fixed strings (no injection).
- `instalar.ps1` only copies `plataforma\` into the sketchbook + writes `caminho.txt` —
  no registry, no elevation, no execution-policy change. The installer
  (`ferramentas/instalador/Instalador.cs`) is per-user HKCU with an anti-`..`
  uninstaller list.
- **Functional bug:** `plataforma/tools/ponte.ps1:105` `Start-Process -FilePath $exe
  -ArgumentList @($Sketch)` doesn't re-quote — a sketch path with spaces breaks.
  Use `ProcessStartInfo.ArgumentList` (the pattern already used correctly in
  `app/PlacaDeVerdade.cs:351-366`).
- **Privacy (P3):** real Wi-Fi **SSIDs** via `netsh` appear in the UI; every byte the
  sketch sends to Serial is mirrored to `%APPDATA%\CircuitSketch\serial.txt`
  (`app/MonitorIde.cs`) — a sketch can fill the disk; `%TEMP%\circuitsketch-porta.log`
  writes raw protocol lines. All low impact; truncation/off-by-default would be hygiene.

---

## 3. Engine (`motor/`) bugs — verified in code

| # | Where | Defect |
|---|---|---|
| 3.1 | `RadioDoComputador.cs:280-282` | **The 8-second timeout can never fire.** `StandardOutput.ReadToEnd()` runs *before* `WaitForExit(8000)`; a hung `netsh` holding stdout blocks forever — exactly what the comment above says it prevents. `StandardError` is redirected but never read (classic pipe-deadlock). Fix: `ReadToEndAsync()` / read after wait. |
| 3.2 | `Fluxo.cs:148-153` (`find()`) | Naive matcher: on mismatch it only retries a 1-char prefix. `find("aab")` over `"aaab"` returns **false** though the pattern is present. Real Arduino `SearchString` keeps state (KMP-like). |
| 3.3 | `CartaoSd.cs:511-519` vs `app/MainWindow.xaml.cs:5272-5277` | `Escrever()` appends to a `StringBuilder` **without** the lock the reader (`Copia()`) uses — and the UI snapshots cards **before** joining the sketch thread. Concurrent `Append`/`ToString` → torn text or exception. |
| 3.4 | `Nativo/NativoAudio.cs:168-173` | 32-bit I2S slot reduced to `a[2]\|a[3]<<8` — the **low** 16 bits; left-aligned 32-bit PCM (the ESP-IDF convention) would come out near-silence. Should be `((a[0]\|a[1]<<8\|a[2]<<16\|a[3]<<24)) >> 16`. |
| 3.5 | `Mqtt.cs:264-277, 585` | Packet parsing does no bounds check before `GetString(dados, i, n)`; the reader loop's `catch { }` turns any malformed packet into a silent disconnect — user sees "connection lost", never "invalid packet". Length continuation bit also ignored. |
| 3.6 | `InterpretadorRede.cs:469-473` | `ESP.getFlashChipRealSize()` returns **4 MB for every board**, so the common `getFlashChipSize() == getFlashChipRealSize()` LittleFS guard always passes even on 2/8 MB profiles. |
| 3.7 | `Nativo/NativoRede.cs:37-45` | Native WiFi hardcodes /24 mask, `.1` gateway, RSSI −48 — a sketch computing "same subnet?" gets wrong answers off /24. |
| 3.8 | Static mutable state | `RadioDoComputador.Redes()` returns the **internal** cached list (UI mutation corrupts the 20 s cache); `Tcc.cs` statics (`dll`, `sujo`, jump page); `NativoAudio.geracaoDeAudio`; `CpuNativa.ciclosPorVolta`. |

Also counted: **91** `throw … "ainda nao e suportado"` sites and **42** empty/silent
`catch`es in `motor/` — the throws are *good* (honest refusal with line number, per the
project's own doctrine); the notable silent catches are 3.5 and `Rede.cs:826`
(`CancelarMqtt` returns `true` even when the cancel failed).

---

## 4. App (WPF) bugs and glitches — verified in code

| # | Where | Defect |
|---|---|---|
| 4.1 | `MainWindow.xaml.cs:229-243`, `AoFechar` | **Neither `DispatcherTimer` is ever stopped.** `relogioTela` (25 fps) and `salvador` (30 s) keep ticking after close — every closed window keeps repainting forever. The 5-minute suite creates 70+ windows; combined with 4.2 this burns CPU for the whole run and **may itself explain the §1.2 perf failures**. Correct pattern already exists in `JanelaAnalisador.cs:144`. |
| 4.2 | `MainWindow.xaml.cs:245` | `Tema.Mudou += AplicarTema` on a **static** event, never `-=` — every `MainWindow` ever created stays rooted. (`JanelaConfiguracoes.cs:96` does it right.) |
| 4.3 | `EditorCircuito.cs:5275-5278` | Paste #2+ maps board ownership to the **first** paste: `copiadas` is refreshed but `idsOriginaisCopiados` isn't, so a LED pasted twice obeys the wrong board. |
| 4.4 | `EditorCircuito.cs:5166-5193` | Copy/paste **drops wire geometry**: `Dobras` (your hand-routed corners), `CaminhoDoWokwi`, and `Lado="baixo"` (bottom-side traces) aren't in the copied tuple — everything reverts to a plain curve, silently. |
| 4.5 | `MenuDaLigacao.cs:122,186` + `EditorCircuito.cs:329` | Deleting a wire doesn't invalidate `fiosSelecionados` (**indices**) — color/style then applies to a different wire. Same index-invalidation class as 4.3. |
| 4.6 | `MainWindow.xaml.cs:1135-1144` | Undo/redo reloads the circuit but never refreshes the object bar or `RecarregarChips()` — the bar's lambdas keep mutating the **old** piece object; clicks appear to work and do nothing. `aplicandoHistorico = true` is also not in `finally`: one exception silently disables undo recording for the session. |
| 4.7 | `EditorCircuito.cs:6657-6666, 2869-2907, 3359-3363` | 25 fps path still allocates a fresh `RadialGradientBrush` + stops **per lit LED per frame**, and 2 `Path`s + unfrozen brushes per wire per redraw. `Pincel()` silently paints unknown colors (from an imported file) **lime green** with no diagnostic. |
| 4.8 | `Sktch.cs` (line ~123) | The signature separator contains a literal **NUL byte** (verified: exactly 1 in the file) — git/ripgrep treat `Sktch.cs` as binary; diffs are unreviewable. Any separator that can't occur in JSON works. |
| 4.9 | `Sktch.cs:92-96` | Legacy-`.json` detection is `texto.Contains("\"sktch\"")` — a legacy circuit whose *content* mentions "sktch" is parsed as a new-format file, fails-open to an **empty circuit, silently**. Corrupt legacy JSON likewise yields empty + no warning (`Circuito.DeJson` swallows by design; `AbrirProjetoDe` doesn't report). |
| 4.10 | `MainWindow.xaml.cs:4087-4090` | `CarregarCircuitoDe` (the `--circuito` CLI path) has **no try/catch** around `Sktch.AbrirCompleto`, which throws on corrupt files — its sibling `AbrirProjetoDe` wraps it properly. |
| 4.11 | `App.xaml.cs:79-83` | Capture mode moves live menu items into a throwaway menu and never restores them — harmless only because capture exits. |
| 4.12 | `EditorCircuito.cs:4042-4074` | `LigarFio` allows duplicate wires (A→B and B→A both exist until manual "otimizar"); duplicate piece ids are only rejected on JSON load, not on programmatic construction — netlist build then throws `InvalidOperationException` (`Circuito.cs:854-856`). |
| 4.13 | `MainWindow.xaml.cs:3264-3265` | `catch (FormatException) { }` drops a corrupt base64 "saved by sketch" file **without telling the user their file vanished**. |

---

## 5. Incomplete things (real pending work)

1. **Stepper driver modules** — A4988/DRV8825/ULN2003 fail their battery tests today (§1.1). Highest-priority functional item.
2. **`fechadura-teclado-servo` regression** — a 12-second flagship project is broken (§1.1).
3. **Deep sleep** — `sono-profunden-botao` never sleeps (§1.1).
4. **ESP-NOW sensor→panel button** — one direction dead (§1.1).
5. **Languages (Alpha 25): 12 promised, 1 exists** — `motor/idiomas/` has only `en.json`, `"completo": false`, **9 of 43 phrases (21%)**, and `python ferramentas/idiomas.py --conferir` **exits 1** with the same 8 phrases out of catalog reported weeks ago in `coordenacao/chatgpt/RELATORIO-003.md`. Because `Idioma.cs:47-56` only offers complete catalogs, English is effectively invisible.
6. **`http.getStream()`** — still refused (`InterpretadorRede.cs:667-670`, "use getString()").
7. **ArduinoJson / `deserializeJson`** — still refused, and an autotest *asserts* the refusal.
8. **ISR timing** — edge ISRs run after the whole call (e.g. all 8 `shiftOut` edges, then the ISR sees final state) — bit-bang protocols sampling in the ISR break. Documented, still true.
9. **Debugger** — no step-over, no value editing, breakpoints don't track line inserts.
10. **`rover-4wd-reles`** generator still wires bare KS2E coils GPIO→5V (~70 mA, beyond Uno limits) because `ferramentas/projetos.py:683-686` still claims the 1-channel relay module doesn't exist — **it does** (`modulo-rele-1` in `pecas.json`, with SVG, behavior, tests).
11. **Alpha 26** (multiplatform/Avalonia, `nucleo` library, per-OS TCC/Bluetooth) — nothing started, by design.
12. **CircuitPython** — `LINGUAGENS.md` calls it top priority ("front-end exists, vocabulary missing"); zero occurrences in `motor/`.
13. **`ponte.ps1` space-in-path bug** (§2.6) and the **frame-time regression question** (§1.2).

---

## 6. Documentation vs. code

### (A) README/plan contradict the code

| Claim | Reality (verified) |
|---|---|
| README:22-36 "Upload (→) cannot be used… decision: use Verify" | Same README, Marco 18 (:914-992) documents Upload **working** with implemented pluggable discovery (`tools/porta.ps1`). Self-contradiction. |
| README:251 "50 pieces… 468 pins" | `app/assets/pecas.json`: **161 pieces, 1598 pins**; 335 SVGs |
| README:994 "six boards" | `plataforma/boards.txt`: **34 boards** |
| README:1059 "one demo per board" (6 listed) | `app/assets/demos/`: **54 demos** |
| README:114 "#define MAX(a,b) still unsupported" | `motor/Preprocessador.cs:68-121` — function-like **and** variadic macros exist |
| README:2449 "1180 tests" | **6681** (today's run) |
| PLANO-ALPHAS:13 "38 projects" | `projetos/roteiros.json`: **46** |

### (B) Still listed as pending, but **already done** (update the docs)

- **BLE** — README:2344 says "not simulated"; `motor/Ble.cs`, `InterpretadorBle*.cs`,
  ArduinoBLE surface and demo `esp32c3-ble-lampada` all exist (many methods still
  throw individually — "partial" is accurate, "missing" is not).
- **`configTime` / `getLocalTime`** — README:2349 says missing; implemented in
  `InterpretadorTempo.cs` / `Interpretador.cs:2556,3050`.
- **1-channel relay module** — PLANO + `projetos.py` say absent; it's in the catalogue
  with SVG, behavior, energy model and dedicated autotests.
- **Stereo audio (PCM5102A/MAX98357A) + AAC/WMA/APE** and **MicroPython
  class/try/with/lambda/Timer/RTC/SPI** — all landed; PLANO's "next" list never updated.
- Old failure lists in `PLANO-ALPHAS`/agent reports (drone, IR repeat, slide-switch,
  rovers…) — fixed in `c42866a`/`5cdc721` and **confirmed passing today** (§8).

### (C) Genuine product decisions that remain bench traps

- **Flame/gas sensors are active-LOW** (`Comportamentos.cs:128-131`) modeling generic
  LM393 boards; the Joy-IT KY-026 kit in the drawer is HIGH-on-flame. The autotest was
  aligned to the simulator, so the suite stays green while real kits disagree.
- **HX711 formula keeps a ~200 g offset** (`Interpretador.cs:3719`: `84213/420 ≈ 200.5`);
  only removed by `tare()` on an empty cell. The project test passes today, but the
  constant is a standing trap for sketches that skip tare.

---

## 7. Test-debt / autotest weaknesses

- `Autoteste.cs:16200-16207` — `Conferir("constantes de biblioteca existem", true)`
  asserts the literal `true`; only an exception can fail it. Constants silently becoming
  0 would stay green.
- `App.xaml.cs:391` — exit code is `relatorio.Contains("FALHA") ? 1 : 0` instead of the
  existing `falhou` counter: any detail text containing "FALHA" flips CI; a reformatted
  failure could report success.
- **7 dead test helpers** (grep-verified no callers): `TesteHex`, `TesteJanelaDeBibliotecas`,
  `TesteAtualizarPainelDeRede`, `NumeroDiscado`, `AtualizarEnergiaAgora`,
  `PassosDoDriverParaTeste`, `CanalDoControleParaTeste`.
- The leaked-window problem (§4.1/4.2) means the suite's own runtime pollutes
  wall-clock perf measurements — fix those two before trusting §1.2 numbers.

---

## 8. Fixed since the 16 Oct audit (confirmed passing today)

Worth recording so nobody re-fixes them: **slide-switch SPDT** (all three cases now
`OK`, including the motor-on-common test), **MPU6050 projects** (`drone-quadricoptero`
and `nivel-mpu6050` both `OK`), **IR remote pin-3 burn** (`controle-ir-leds` `OK`),
**RC car / rovers / ventilador-l298n / balanca-hx711-lcd / bluetooth / servidor-web**
projects all `OK`, and the **ESP-NOW release** path (only the forward button remains
broken). The recorded "11 of 38 projects failed" snapshot is **stale** — this tree is
at 44/46 (the two failures being `fechadura-teclado-servo` and `sono-profundo-botao`).

---

## 9. Suggested order of work

1. **Path-traversal check on native tab names** at load + in `ModuloNativo.Compilar`
   (copy `Acervo.cs`'s prefix-check design) — small, closes the shortest attack path.
2. **Don't auto-run pipe-delivered projects**; add `PipeSecurity` + `.sktch` existence check.
3. **`UsarInternet` default `false`** for school images.
4. **Stop the two timers + unsubscribe `Tema.Mudou` in `AoFechar`** — 3 lines, fixes the
   suite's own CPU pollution and a real user-facing leak.
5. Fix the **stepper driver trio**, **fechadura**, **deep sleep**, **ESP-NOW forward
   path**, **esp09-dado** (§1.1) — the visible red list.
6. Re-run the 3 perf tests on an idle machine; if the frame is still ~3 ms vs the
   documented 0.01 ms, bisect since Marco 14.
7. `RadioDoComputer` ReadToEnd-order fix; `CartaoSd` lock coverage; `find()` matcher;
   I2S 32-bit sample; MQTT bounds check.
8. Paste fixes (`Dobras`/`Lado`, board mapping), undo object-bar refresh, wire-index
   invalidation.
9. Regenerate `rover-4wd-reles` with `modulo-rele-1`; regenerate the i18n catalog
   (`idiomas.py`) so `--conferir` goes green.
10. Doc pass: Upload section, catalog/board/demo/test counts, remove BLE/`configTime`
    from "missing", refresh PLANO-ALPHAS.

---

## 10. What's genuinely well-built (so it stays that way)

- Deterministic virtual clock with per-chip cycle costs and real `millis`/`micros`
  wraparound; per-chip watchdogs instead of one generic limit.
- `InvariantCulture` throughout the engine — immune to pt-BR Windows.
- The zip/HTTP supply chain (host allow-list + SHA-256 + zip-slip guard + tests) is
  better defended than most commercial tools.
- Sketch servers loopback-only; nothing listens on the LAN.
- Error doctrine: refusal messages carry file:line and *explain the physics*
  ("no AVR o SPI esta desligado, o byte nunca termina de sair").
- `.sktch` is plain JSON with line-array code — diffable, versioned, refuses the future.
- Autotest harness converts exceptions into failures (learned from real hangs), and
  the mouse adapters are exercised directly, not a copy.
- `BitmapCacheOption.OnLoad` + `Freeze()` in `Miniaturas`, `ArgumentList` instead of
  shell strings, union-find netlist indexed once per rebuild — the right patterns are
  already in the codebase; the fixes above are mostly applying them consistently.

---

*Findings marked "verified" were read line-by-line; the 10 suite failures and all
green/red project results are from today's full run (`.artefatos/nimo/saida-teste.txt`).
No code was changed by this audit.*
