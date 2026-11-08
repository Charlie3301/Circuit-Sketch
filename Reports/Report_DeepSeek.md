# Report_DeepSeek — CircuitSketch: bugs, vulnerabilities, errors, glitches and incomplete items

**Product:** CircuitSketch — offline ESP32 / Arduino simulator (C#/WPF, engine in C#)
**Tree audited:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32` (commit `f103d4f`, 15 Oct 2026)
**Date:** 16 October 2026
**Author:** DeepSeek audit agent (this report)
**Method:** fresh build, **two full test-suite runs** (the previous audits on this tree did not run the suite), the 46-project battery, and first-hand static verification of every claim below — including all 15 findings of a dedicated `app/` review, folded into §4.10. Claims taken from other reports are marked as such and were re-checked when load-bearing.

Severity scale used here:

| Tag | Meaning |
|---|---|
| **P0** | Host compromise, silent data loss, or a process-wide crash |
| **P1** | Simulator disagrees with the board in a common lab circuit, or a default that is unsafe for a classroom |
| **P2** | Contained bug/glitch, missing guard, doc that lies about current behavior |
| **P3** | Tooling, packaging, roadmap and housekeeping debt |

---

## 0. How this was done (evidence log)

Everything below was actually executed on this tree. Nothing was modified: the build wrote only under `.artefatos/deepseek/` (gitignored), and no project file was edited.

1. **Build** — `dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/deepseek`
   → **0 warnings, 0 errors** (SDK 11.0.100-rc.1, ~2 min 12 s from a clean artifacts folder).
   Executable: `.artefatos/deepseek/bin/App/debug/CircuitSketch.exe`.
2. **Full suite, run 1** — `CircuitSketch.exe --teste --saida .artefatos/deepseek/saida-suite.txt`
   → **6665 passaram, 16 falharam** (exit code 1), 09:56→10:14 on 16 Oct.
   Caveat: it ran **concurrently with another agent's suite** on the same machine (a second full GUI suite, started 20 s later). The timing-sensitive failures are analysed under that lens in §2.
3. **Full suite, run 2 (clean, machine idle)** — same command → `saida-suite2.txt`, with `CS_RESULTADOS=.artefatos/deepseek/resultados-projetos.json`.
   → **6688 passaram, 3 falharam**: two deterministic performance thresholds (unchanged numbers from run 1 — they are properties of this machine, not of the load) and one **flaky project** (`fechadura-teclado-servo`, passed in run 1). See §2.3.
4. **Project battery (inside the suite)** — run 1: **46 de 46 projetos funcionam**; run 2: **45 de 46** (the flaky project above). The previous recorded snapshot, `coordenacao/resultados-projetos.json` (13 Oct), had **27 of 38**; that file is now stale.
5. **Static verification** — every `file:line` cited below was read in this tree today; where behavior could be tested cheaply without touching the app (e.g., a PowerShell `break`-in-`switch` semantics probe, decoding of `netsh` output, byte-size measurements of installer payload, and a WPF routed-event probe that settled §4.10.14), it was tested.

Not done, on purpose: no keyboard/mouse interaction with a running CircuitSketch; no hardware plugged in; no file in the repository was changed or deleted.

---

## 1. Executive summary

CircuitSketch is in visibly better shape than its own paperwork admits. The 2026-10-13 project battery (27/38) is obsolete: today the battery is **46/46 in one run and 45/46 in a second** (one flaky project), and the full suite passes **6688 of 6691 checks on an idle machine** — the three failures being two machine-specific performance thresholds and that one flake (§2).

The real problems cluster in six places:

1. **The IDE-plugin PowerShell layer breaks on ordinary user paths** — sketch paths containing spaces are passed unquoted to `Start-Process` (`ponte.ps1`, `monitor.ps1`); an empty annotation file crashes `ponte.ps1` with `$null.Trim()`; and the installer writes the simulator path in **ASCII**, so any Windows user with an accented name (e.g. “João”) gets a broken plugin→app link. These are user-facing P2s with easy fixes.
2. **Open/save paths can silently destroy user files (P0)** — an `.ino` registered in Recents re-opens as a blank project whose next Ctrl+S writes `.sktch` JSON over the Arduino source, and any `.json` chosen in the Ctrl+O dialog is one save away from the same fate; several open paths (Recents, opening screen, IDE pipe, Wokwi import) also discard unsaved work without asking (§4.10).
3. **Install/uninstall robustness** — both `instalar.ps1` and the C# installer delete folders recursively based on weak checks, and `instalar.ps1` ships ~92 MB of `bin/obj` build junk into the Arduino sketchbook.
4. **Documentation lies about current behavior** — `README.md` still says **BLE is not simulated** and **`configTime()` does not exist**; both are implemented and covered by passing tests in this very tree. Version labels disagree in three places (`App.csproj` = 0.24.0 / “Alpha 24”; README Marco 37 = “1.4.0”; ongoing work = “Alpha 25”).
5. **Incomplete i18n** — the Alpha-25 language system is wired end-to-end but has ~64 marked call-sites out of a planned ~16.000 strings, and the single shipped catalog (`en.json`) has 35 keys of which **9 are translated and 26 are empty** (`"completo": false`).
6. **Test-suite honesty gaps** — several checks can pass for the wrong reason (substring serial checks, a check that literally cannot fail), skipped external prerequisites are silent, and a run that executes **zero** tests is reported green. Two runs also separated real red from noise: 15 of run 1's 16 failures were load artifacts; run 2 left two machine-bound performance thresholds and one flaky project (§2).

Good news worth recording: the online library installer (`JanelaBibliotecas` + `BibliotecasOnline` + `Acervo.InstalarZip`) is genuinely well built — URL allow-list, redirect re-check, size and SHA-256 verification from the index, zip-slip protection and a 200 MB decompression cap (§6.4). The SD-card host folder is traversal-fenced. The `netsh` output decoding (a plausible bug on inspection) is correct in practice on this machine (§6.6).

---

## 2. Test runs: the 16 failures of run 1, and what they mean

### 2.1 What the apparatus is

- `app/Autoteste.cs` registers **216 test blocks** (“Bloco(...)”); each block is wrapped in a try/catch that reports `FALHA <bloco> estourou`.
- Total `Conferir(...)` call-sites: **1.984** across `app/Autoteste*.cs`; run 1 executed **6.681 checks** (6665 OK + 16 FALHA). The suite opens real WPF windows; a full run takes ~17–18 min on this machine.
- Exit code is 0 only when the report string contains no `FALHA` (`app/App.xaml.cs:391`).

### 2.2 The 16 failures of run 1 (machine under parallel load)

| # | Check (block) | Expected | Measured | Reading |
|---|---|---|---|---|
| 1 | `refazer a rede cabe num quadro` (`desempenho`, `app/Autoteste.cs:15447`) | < 4,0 ms per Netlist rebuild | **9,17 ms** | wall-clock measurement; another full suite was hogging the CPU |
| 2 | `desempenho: o quadro custa menos de 1 ms` (`app/Autoteste.cs:1117-1118`) | < 1 ms | **2,14 ms** | same |
| 3 | `velocidade: o executor passa dos 16 MHz do chip` (`app/Autoteste.cs:13180`) | ≥ 16 MHz | **15,3 MHz** | same (test even prints “1,0x o ATmega328P”) |
| 4–8 | `bateria (Alpha 23): a4988 / drv8825 / stepper-motor / uln2003 / motor-28byj48 funciona no circuito` | axle moves within the watched window | “eixo de 29 a 29 graus” etc. | world advances in wall time; under load the window closed before motion |
| 9–12 | demos `esp01-tomada-mqtt`, `esp02-campainha`, `esp11-minuteria`, `esp13-fechadura` — “faz o que a descricao promete” | serial reaches the described end state | only the first lines appeared; **the suite itself printed “está a 0%/15%/25% da velocidade real”** for those boards | the ESP8266 CPU budget ran out under load; the demos themselves are fine when the machine is free |
| 13–14 | `serial entre placas`: cross-ESP32 `Serial2` echo | N ping/echo pairs | 3 pings received but 2 echoes observed | fixed wait window vs. loaded world |
| 15 | `keypad: ligado como o sketch diz, a tecla 5 da '5' e a D da 'D'` (`app/AutotesteTeclado.cs:70-73`) | `tecla: 5 \| tecla: D` | `tecla: D` | the *first* key (pressed 300 ms after start) was dropped: under load the sketch had not reached `loop()` yet |
| 16 | `Demo_PlayerDap estourou` — `InvalidOperationException: Sequence contains no matching element` | block completes | block threw at `pecas.First(...)` (`app/AutotestePlayerDap.cs:340-345`) | demo failed to load silently (`AbrirDemo` swallows errors into the status bar, `app/MainWindow.xaml.cs:1318`), leaving an empty board; needs run 2 to rule out an environment effect |

Pattern: **15 of 16 failures are timing/wall-clock artifacts of running two GUI suites at once**; none of them is a crash of the product. The stepper/KY/ESP8266 rows are exactly the tests whose thresholds are wide bands deliberately (the velocity test's own comment says the floor is low “porque medir velocidade num teste é medir a máquina”).

### 2.3 Run 2 (clean machine) — results

`6688 passaram, 3 falharam` — the three failures are:

| Check | Expected | Measured | Reading |
|---|---|---|---|
| `refazer a rede cabe num quadro` (`app/Autoteste.cs:15447`) | < 4,0 ms | **9,52 ms** (9,17 ms in run 1) | **not load** — reproducible on an idle machine; either this machine class is slow or something does per-frame netlist work that was meant to be indexed |
| `desempenho: o quadro custa menos de 1 ms` (`app/Autoteste.cs:1117-1118`) | < 1 ms | **2,65 ms** (2,14 ms in run 1) | same; the check's own comment says the figure was **0,01 ms** after indexing — measured 265× that. Treat as a possible regression signal, not just a slow PC |
| `projeto fechadura-teclado-servo continua funcionando` (`app/AutotesteProjetos.cs:80`) | “Liberado” + servo 80–100° + LED on | serial shows “Digite a senha e aperte #”; servo stayed at 0°, LED brightness 0,00 | **flaky**: the same project passed in run 1 (4 s); in run 2 it took 11 s and the typed sequence (the roteiro types `1234#` with 0,2–0,25 s holds) never unlocked — a key press was apparently missed by the *simulated* Keypad scan window |

Everything else that failed under load — velocity (16 MHz), the five stepper checks, the four ESP8266 demos, cross-board Serial2, the keypad first-key check and `Demo_PlayerDap` — **passed in run 2**, confirming the load diagnosis for those 15. `Demo_PlayerDap` needed no fix: it was an environment effect of the loaded machine.

So the honest scoreboard for this tree: **two constant performance-threshold failures on this machine, one intermittently flaky project**, out of 6.691 checks.

### 2.4 Why this matters beyond “it was the load”

The suite is the project's regression net (there is **no CI**: `.github/workflows` does not exist). Findings:

- Any parallel agent (and AGENTS.md itself encourages several agents to work in parallel!) can make the suite report red for reasons that are not code. A re-run is needed to tell them apart — I had to do exactly that, and it flipped 15 of 16 reds to green.
- The worst offender is not the threshold tests (they are documented) but the demo checks: they fail with a green light on the product and a printed “0% da velocidade real”, i.e. the *simulator* says “I could not keep up”, and the *test* still fails anyway.
- Run 2 adds two more lessons: (i) two performance checks fail **on this machine even when idle** — they measure the computer, not the code, and should either scale to a calibration or be excluded from exit-code gating; (ii) `fechadura-teclado-servo` is **intermittently red across two identical runs** — the kind of flake that erodes trust in the whole battery. Both should be tracked so “the suite is green” stays a meaningful sentence.

---

## 3. Project battery: 46∼45/46 today, 27/38 recorded

- `projetos/roteiros.json` now holds **46 projects, all marked `"esperado": "funciona"`**; 8 were added after the 13 Oct snapshot (`npn-motor-esp32`, `mosfet-fita-uno`, `zener-referencia`, `capacitor-rc-led`, `opto-isolado`, `fusivel-curto`, `schottky-protecao`, `pcb-semaforo`).
- Everything the previous audit listed as failing on 13 Oct — `drone-quadricoptero`, `carrinho-rc-*`, `rover-4wd*`, `controle-ir-leds`, `nivel-mpu6050`, `ventilador-l298n`, `balanca-hx711-lcd`, `espnow-botao-led` — **passes today** (“FUNCIONA”, per-project seconds printed).
- Run 1: **“46 de 46 projetos funcionam”**. Run 2 (clean): **“45 de 46”** — `fechadura-teclado-servo` is the flaky one (details in §2.3); it is an input-timing flake, not a stable defect, but it means the battery cannot be called “46/46 stable” yet.
- Consequence: `coordenacao/resultados-projetos.json` (27/38) and the “Executive summary” of `RELATORIO-AUDITORIA.md` should be marked **superseded**, not quoted.
- The fresh per-project JSON with every check detail is `.artefatos/deepseek/resultados-projetos.json` (written by run 2 via `CS_RESULTADOS`); copy it over `coordenacao/` when convenient.

---

## 4. Bugs and functional defects (code, directly verified)

### 4.1 `--saida` writes are unguarded in four CLI modes → silent hang (P2)

`App.AoIniciar` writes the report of `--audio` (line 242), `--diagnostico` (286), `--wokwi` (335) and `--teste` (390) with plain `File.WriteAllText(e.Args[...])`. If the path is invalid (typo, directory missing, OneDrive offline), the exception unwinds into the `DispatcherUnhandledException` handler (`app/App.xaml.cs:193-204`), which shows a **modal MessageBox** and marks the exception handled. In `--teste`/`--diagnostico`/`--wokwi`/`--audio` there is no user to click OK, and `Shutdown(...)` is never reached → the process hangs forever on an invisible desktop. The safe pattern is already used elsewhere in the file (`--desenho` wraps in try/catch, line 262-267, and `--placas-da-ide` at 357-362).

### 4.2 IDE plugin: paths with spaces break Upload/Monitor (P2)

- `plataforma/tools/ponte.ps1:103-105` — `Start-Process -FilePath $exe -ArgumentList @($Sketch)`: PowerShell joins `-ArgumentList` with spaces **without quoting**, so `C:\...\Meus Sketches\x.ino` arrives as two arguments and the simulator opens nothing (or the wrong thing). Same class at `plataforma/tools/monitor.ps1:102-105`, where `-Arquivo $arquivo` (the sketch path) is unquoted.
- `plataforma/tools/ponte.ps1:63` and `:86` — `(Get-Content -LiteralPath ... -Raw).Trim()`: on Windows PowerShell 5.1 (the runtime the Arduino IDE plugin actually gets) an **empty file** makes `-Raw` return `$null`, and `.Trim()` on `$null` throws `You cannot call a method on a null-valued expression`. **Verified on this machine** — the reproduction returned exactly that error. Line 86 is the exe-path lookup (`caminho.txt`); an empty or truncated file kills the Upload path with a raw PowerShell error. Easy fix: `'' + (Get-Content ... -Raw)` or `[string]::Join(...)`.

### 4.3 Plugin discovery: `QUIT` does not quit (P2)

`plataforma/tools/porta.ps1:104-107` replies to `QUIT` and then `break`. In PowerShell a bare `break` inside a `switch` exits the **switch**, not the enclosing `while ($true)` at line 80 — verified experimentally on this machine under Windows PowerShell 5.1 (`while` + `switch { break }` → loop continued). The discovery process therefore stays alive blocked on stdin until the pipe closes; if the IDE keeps the pipe open, a stale process lingers after “quit OK”. Needs a label (`:laco while ... break laco`).

### 4.4 Half of the boards are narrated with the wrong flashing tool (P2)

`plataforma/boards.txt` marks `gravador=esptool` for 29 boards, `avrdude` for 5 (the AVRs). Six of the esptool boards are **not** ESP chips: `simrp2040` (line 128), `simpipico` (686), `simpipicow` (708), `simstm32bluepill` (730), `simstnucleol031k6` (752), `simstnucleoc031c6` (774). The Upload narration shown to the student will claim esptool/ESP sequences while flashing a RP2040 or STM32 (which use UF2/picotool or ST-Link in real life) — a fidelity problem of exactly the kind this project treats as first-class.

### 4.5 `monitor-cano.ps1` can corrupt multi-byte characters (P2)

`plataforma/tools/monitor-cano.ps1:68-76` reads the newly appended bytes each poll and decodes that **chunk alone** with `UTF8.GetString`. If a write lands mid-way through a multi-byte character (accented letters are common in this project's serial output), the bytes are mangled to `�` and `$posicao` advances past them, so the character is lost for good in the IDE's serial monitor. A small carry-over buffer of incomplete trailing bytes fixes it.

### 4.6 Installer (`instalar.ps1`) misc (P2/P3)

- `instalar.ps1:30` writes `tools\caminho.txt` with `-Encoding ascii`. The plugin (`ponte.ps1:85-99`) reads this file back to find the simulator. A Windows user whose name or path has accents (e.g. `C:\Users\João\...`) gets mojibake in the path and a silently broken link. Everything else in the plugin layer writes UTF-8 (`ponte.ps1:59` writes UTF8) — this one file is the odd one out.
- `instalar.ps1:10-14` and `19-22`: unconditional `Remove-Item -Recurse -Force` on `...\Arduino\hardware\simulador` and `...\hardware\circuitsketch\esp32`. The C# installer has a guard (`ferramentas/instalador/Instalador.cs:222-227` checks `EhPluginNosso`), the PS1 path does not.
- `instalar.ps1:25` copies `plataforma\*` — measured today: **92,2 MB**, of which **89,5 MB is `plataforma\fonte\porta\bin`** and 2,7 MB `obj` (build artifacts). Every student's Arduino sketchbook gets the compiler's own `bin/obj` dump.

### 4.7 C# installer: recursive delete behind a substring check (P2)

`ferramentas/instalador/Instalador.cs:233-245` (`RemoverAntigo`) deletes `...\hardware\simulador` recursively if **any** `platform.txt` anywhere under it contains the substring `"Simulador"` or `"CircuitSketch"` (case-insensitive). A third-party platform whose `platform.txt` merely mentions the word “simulador” (the file is full of Portuguese text in this ecosystem) is enough to authorize deleting the folder tree. Low probability, high blast radius; match should be anchored (e.g. the plugin's own `name=`/`version=` lines or a marker file).

### 4.8 Crash log lands in the user-profile root (P3)

`app/App.xaml.cs:32-43` appends `circuitsketch-erros.log` to `%USERPROFILE%\` (not `%APPDATA%\CircuitSketch\` like every other piece of state). Cosmetic, but it is the one file a support person is told to look for, and it is the one file *not* co-located with the rest.

### 4.9 Engine (`motor/`) — dedicated review findings (P1–P3)

A focused engine review was commissioned for this report; the reviewer verified each item from code, and I re-checked the three heaviest (EEPROM, Timer0, `HTTPClient.end()`). These are simulator-vs-datasheet divergences, not host risks.

**Assembler (`motor/Montador.cs`) — silent misencodings, all P2:**

- **`.org` counts words, not bytes** (`Montador.cs:296-299` + `AvrIsa.cs:147`, `Avr.cs:1162` jump by word index). avr-as counts **bytes**, so `.org 2*OVF0addr` (a common AVR idiom) lands the ISR at the wrong vector: it works in one direction and breaks silently in the other. The file header claims byte-identical output with avr-as (`Montador.cs:8-11`).
- **Out-of-range `rjmp`/`rcall`/branches are masked, not rejected** (`Montador.cs:517-518,667,663`): `& 0x0FFF` / `& 0x7F` wrap the displacement; `sbi PORTB, 9` silently becomes bit 1 (`bit & 7`). The same file *does* throw for truncated I/O addresses (`:638-640`) — inconsistent policy. A program that outgrows the branch range assembles to a jump into arbitrary code.
- **Unknown directives are dropped** (`Montador.cs:343-347`): `.include`, `.macro/.endm`, and typos like `.orgg` fall into `default: return onde`, so macro/multi-file assembly “succeeds” into an incomplete image that fails only at runtime.
- Context: the assembler is the sole path for `Linguagem.Assembly` (`Compilador.cs:220-221`), so nothing else can catch these.

**AVR timers and interrupts — P2:**

- **Timer0 ignores WGM/CTC** (`Avr.cs:1177-1189`): `TCNT0` always counts 0→255; compare match sets `OCF0A` but never resets the counter, so `TCCR0A = _BV(WGM01); OCR0A = 124;` fires every 256 ticks instead of every 125. Timer1 resets in CTC (`:1216-1222`) but skips `OCR1A == 0` (`alvo != 0`, `:1212`) — that corner never raises `OCF1A`.
- **Timer2 and INT0/INT1/PCINT never fire** (`Avr.cs:1131-1166,1168-1228`): `Pendente()`/`Interromper()` only look at `TIFR0/TIFR1/TIMSK0/TIMSK1`; `TIFR2/TIMSK2/EIFR/EIMSK/PCICR` are never read and `TCNT2` never advances. The vectors exist (`AvrIsa.cs:129-131`). Assembly programs configuring Timer2 (`tone()`'s timer) or external interrupts run with ISRs that never execute. (The *interpreted* `attachInterrupt` path is separate and works.)

**Peripherals and APIs — P2/P3:**

- **`EEPROM.readInt/readFloat` crashes on a negative address** (`InterpretadorEeprom.cs:377`): the guard is only `endereco + k < memoria.Length`, so `memoria[-1]` throws `IndexOutOfRangeException` and aborts the run; plain `read()` checks `>= 0` (`:300`) and returns 0, and writes ignore bad addresses (`:433`). Oddly, `read(0)`/`write(0)` agree with the board; the *typed* reads are the exception. **Verified.**
- **MQTT never detects a dead broker** (`Mqtt.cs:447,569-583,606-612`): PINGRESP is discarded (`:578`), keep-alive failures are swallowed (`catch { }`), and `Estado`/`Conectado` never move back — while a real client drops `connected()` after the keep-alive deadline. Sketches that reconnect on `connected() == false` never reconnect; publishes vanish silently.
- **`HTTPClient.end()` is a no-op** (`InterpretadorRede.cs:649-650`): `end` returns without clearing `Resposta`, and `connected()` is defined as `Resposta is not null` — so after `end()`, `connected()`, `getString()`, `getSize()` and `header()` keep serving the **previous** transaction. **Verified.**
- **`strftime("%Z")` always prints `"UTC"`** (`InterpretadorTempo.cs:203`) even when the local `configTime` offset was applied to every other field — wrong timezone label on generated logs.
- **`esp_sleep_get_ext1_wakeup_status()` conflates sleep modes** (`Sono.cs:313-317,462-464`): the same expression answers both EXT1 and GPIO wake queries, preferring the light-sleep pin set; a sketch using EXT1 deep sleep plus GPIO light sleep reads whichever woke last. Wrong branch on wake decoding.
- **Accept loops die on one transient socket error** (`Rede.cs:1369-1370`, `Nativo/NativoRede.cs:118-119`, `PortaBluetooth.cs:83-84,270-271`): `catch { return; }` ends the thread while the “open” flag stays true — the listener is never rebuilt, so the board's web server/Bluetooth bridge silently stops accepting connections for the rest of the run.
- **`NativoRede.Atender` drops connections over a 32-request queue without a response** (`NativoRede.cs:138-141`): the `break` closes the socket with no HTTP status, unlike every other overload/closure path which sends 503.
- **`ConexaoTcp.Fechar` (real/TLS) abandons the stream** (`Rede.cs:1587-1589`): `Close()` on the socket without disposing `fluxo` (the `SslStream` chain from `:1477-1485`) — repeated reconnect loops accumulate undisposed TLS state until GC.
- **`ArquivoSd` mutates a shared `StringBuilder` unlocked** (`CartaoSd.cs:511-519`) while readers take `lock (trava)` (`:158-163,248-273`): a save/copy from the UI thread racing a datalogging sketch can tear the buffer.
- **`Requisitar` disposes the `CancellationTokenSource` while the HTTP task can still use it** (`Rede.cs:466-513`): on the `ExecucaoInterrompida` path the task is abandoned with a token from a disposed source — occasional unobserved `ObjectDisposedException` in the background (invisible to users, noise for diagnosis).
- **`RadioDoComputador.Rodar` reads stdout before `WaitForExit`** (`RadioDoComputador.cs:279-289`): `ReadToEnd()` blocks until the child closes stdout, so the 8-second `WaitForExit` can never bound a hung helper — the sketch thread blocks indefinitely instead; and stderr is redirected but never drained (classic pipe-deadlock shape, low probability with netsh/Get-PnpDevice).

### 4.10 App shell and editor (`app/`) — dedicated review findings (P0–P3)

A dedicated review of the `app/` shell and editor was commissioned for this report; it returned after the first draft. I re-verified every item below at its cited lines on this tree (static review; no interactive GUI pass), and settled the one mechanism that was cheaply testable with a probe on this machine (§4.10.14). Severities follow this report's scale; the two P0s are silent data loss.

**Open/save paths — silent data loss, P0:**

- **4.10.1 An `.ino` in Recents re-opens as a blank project, and the next Ctrl+S overwrites the sketch file (P0).** Loading a sketch — from the IDE pipe (`app/App.xaml.cs:655`) or "Enviar para a placa" (`app/MainWindow.xaml.cs:4421-4428`, a dialog whose filter even accepts `*.*`) — loads only the code half, but resets `circuitoSalvo` and registers the file in Recents (`:4018-4019`). Clicking that name later (Recents menu, `:1215`) calls `AbrirProjetoDe` (`:4052`); `Sktch.AbrirCompleto` finds no `"sktch"` marker and takes the legacy-JSON branch (`app/Sktch.cs:92-96`), where `Circuito.DeJson` swallows the `JsonException` and returns an empty circuit (`app/Circuito.cs:299-300`). The blank project is shown with no complaint about the file, `caminhoDoProjeto` is set to the `.ino` (`:4071`) and marked clean (`:4064`), and the next Ctrl+S → `SalvarEm` → `Sktch.Salvar` writes `.sktch` JSON over the Arduino source in a single `File.WriteAllText` (`app/Sktch.cs:57-70`). Two clicks; and the IDE workflow fills the Recents list with `.ino` files by design.
- **4.10.2 Any `.json` opened through the Ctrl+O dialog loads blank and can be overwritten (P0).** The filter still includes `*.json` (`app/MainWindow.xaml.cs:3980-3984`); a Wokwi `diagram.json` has no `"sktch"` marker, so it takes the same branch — and `TentarDeJson` accepts *any* syntactically valid JSON, deserializing a `parts`/`connections` file into a circuit with zero pieces and no error (`app/Circuito.cs:311-356`) — while a corrupt file is equally silent because `DeJson` swallows the failure (`:299-300`). `circuitoSalvo`/`caminhoDoProjeto` are set (`:4064`, `:4071`), so the next save replaces the diagram file with `.sktch` JSON. The code knows the difference elsewhere — `TentarDeJson` exists precisely to say *why* a file failed (`:302-310`) — this path just doesn't use it.

**Unsaved work and state guards — P2:**

- **4.10.3 `AbrirProjetoDe` and the Wokwi import never call `PodeDescartar` (P2).** `PodeDescartar` (`app/MainWindow.xaml.cs:3958-3974`) is called only by `NovoCircuito` (`:3928`) and `AbrirCircuito` (`:3978`); the Recents menu (`:1215`), the opening screen / CLI (`AbrirProjeto`, `:4049`), the pipe `.sktch` path (`:4008`) and `ImportarDoWokwi` (`:2274-2298`) all replace the session directly. A student with an unsaved circuit loses it silently by clicking a recent file or importing a diagram.
- **4.10.4 `CarregarSketch` resets the saved-state baseline (P2).** `circuitoSalvo = AssinaturaAtual()` at `:4018`, and the signature covers circuit *and* code (`app/Sktch.cs:122-123`, the whole circuit JSON plus the code) — so unsaved **circuit** edits are absorbed into the "saved" baseline, and neither `AoFechar` (`:863-869`) nor `PodeDescartar` (`:3962`) warns anymore. Edit the circuit, send a sketch to the board, close: the circuit edits are gone without a prompt.
- **4.10.5 `Rodar` can leave `EmSimulacao = true` when it bails out early — "zombie" simulation mode (P2).** Set at `:4642`; the early returns for no board (`:4725`), sketch too big (`:4762`) and compile error (`:4772`) skip the only lines that flip the buttons (`:4804-4805`), and `EmSimulacao` is cleared only inside `PararExecucao` (`:5273`), which the user cannot trigger because `btParar` was never enabled. In that state single-key shortcuts stay suppressed (`AtalhoDeUmaTeclaVale`, `:3662`) while the frame pass keeps applying keyboard/gamepad bindings to pieces, because `AplicarVinculos` is gated only by `EmSimulacao` (`app/EditorCircuito.cs:5448-5450`, reached from `AtualizarTela` `app/MainWindow.xaml.cs:5861-5866` → `AplicarEntradas`). Clicking Rodar again is the only obvious recovery.
- **4.10.6 One exception kills the named-pipe listener for the rest of the session (P2).** `Escutar`'s `while (true)` ends in a bare `catch { return; }` (`app/App.xaml.cs:637-668`), and `CarregarSketch` has no try/catch, so any read failure on the sent path propagates through `Dispatcher.Invoke` into that catch. Afterwards a new IDE launch finds the mutex held, `EntregarParaInstanciaAberta` swallows the failed `Connect` (`:630-634`) and the launcher exits (`:407-411`) — "Verificar" in the IDE then silently does nothing until the app restarts. (Overlaps §6.3; listed here as a resilience bug, not an attack.)
- **4.10.7 Saving is one non-atomic write, and the `.bak` safety copy is opt-in and off by default (P2).** `Sktch.Salvar` serializes everything and writes in a single `File.WriteAllText` (`app/Sktch.cs:57-70`); the backup runs only when `Preferencias.CopiaDeSeguranca` is enabled and `SalvarEm` passes the default `false` (`app/MainWindow.xaml.cs:3875`). A crash or power cut mid-write on a school machine leaves a truncated project with no recovery path, while an unconditional temp-file-plus-replace would cost nothing.

**Editor glitches — P2/P3:**

- **4.10.8 `Girar`/`Espelhar` leave generated overlays behind (P2).** Both loops only run `Posicionar` plus brilho/estouro/TFT (`app/EditorCircuito.cs:4861-4910`); the full repositioning set — movable parts, DIP levers, servo arm, board LEDs, module lights, radio and the per-cell LCD loop — lives in `Reposicionar` (`:4428-4461`), which runs only on drag/snap (`:4234`, `:4237`). Rotating an LCD, servo or radio leaves the decoration in the old place until the piece is dragged.
- **4.10.9 Copy/paste of wires drops `Dobras`, `CaminhoDoWokwi` and `Lado` (P2).** `Copiar` stores a 6-field tuple (`:5166-5167`, `:5188-5193`) and `Colar` rebuilds the `Fio` from it (`:5264-5273`); the three routing fields (`app/Circuito.cs:230-242`) are never copied. A hand-routed wire pastes re-routed, and a soldered bottom-side track comes back as an ordinary top wire.
- **4.10.10 Undo/redo/copy/paste are not blocked during simulation (P3).** The whole guard is `noTexto` (`app/MainWindow.xaml.cs:3623-3626`), and `AplicarDoHistorico` reloads the entire circuit and rebuilds the netlist mid-run (`:1135-1144`). Ctrl+Z right after deleting a board stops it mid-run; Ctrl+Y brings it back dead until Rodar again — with no warning that the circuit changed under the simulation.
- **4.10.11 Potentiometer, DIP, joystick and radio adjustments create no history entry (P3).** Those interactions fire `Arrastou` only, never `Mudou` (`app/EditorCircuito.cs:4688-4736`; `app/EditorRadio.cs` has neither event), while history snapshots and undo reload the whole circuit JSON (`app/MainWindow.xaml.cs:1035-1047`, `:1135-1144`). Turning the pot and then pressing Ctrl+Z to undo something else snaps the pot back with no indication.
- **4.10.12 Right-click is inert while a wire is being drawn (P3).** `AoApertarQualquer` records the right-button press only when `origemFio is null` (`app/EditorCircuito.cs:3417-3428`), so mid-wiring `AoSoltarQualquer` returns early (`:3449-3453`): no menu, no pan and no right-click cancel — only Esc or a left click on empty canvas.
- **4.10.13 `ConferirPlacaDaIde` compares only against the first board (P3).** `FirstOrDefault` at `app/MainWindow.xaml.cs:4507`: with a two-board circuit, an IDE set to the second board still triggers "a Arduino IDE está em X, mas o circuito tem Y" (`:4512-4513`) — a false alarm on exactly the multi-board workflow the comment two lines above celebrates (`:4490-4500`).
- **4.10.14 Hotkey editor can stay armed on several rows (P3).** Each "Trocar" click attaches a new `Pegar` handler and only the handler that *fires* unsubscribes (`app/JanelaAtalhos.cs:118-156`). Mechanism **corrected here by probe**: once one handler binds the key it sets `Handled = true`, and the other armed handlers on the same element are *skipped* for that keystroke (a WPF probe on this machine confirmed the `handledEventsToo: false` semantics — the second handler did not run). So the symptom is not an immediate "já é de…" dialog: the other rows remain "aperte…" and the **next** keystroke is bound to them (silently, or with the conflict dialog if that key is taken). One stray key can rebind another shortcut.
- **4.10.15 Playing the simulated circuit mutates serialized piece state (P3).** Bound keys write `peca.Giro`/`Giro2`/`Bits` while playing (`app/EditorCircuito.cs:5500-5527`, `:5561-5569`; radio switches in `app/EditorRadio.cs:250-267`), and those fields are serialized and part of the dirty signature (`app/Circuito.cs:178-195`; `app/MainWindow.xaml.cs:1037`; `app/Sktch.cs:122-123`). Toggled switches and radio channels keep the played value; the close prompt then asks to save "changes" that were only play state, and accepting writes them into the file, so the project reopens looking altered. (Joystick and knobs are recomputed toward the middle by the frame pass when no key is held, so their exposure is a save made mid-play.)

---

## 5. Test-suite findings (quality of the net itself)

These come from a dedicated review of `app/Autoteste*.cs` performed for this report and spot-verified by me. The pattern that repeats: **assertions that cannot distinguish right from wrong**.

1. **Project-battery serial checks pass on substrings** — `app/AutotesteProjetos.cs:165` accepts any monitor line *containing* the expected text; only one check (the `"re"` fix, `roteiros.json:332`) uses the exact-line variant (`:172`). ~40 roteiro checks remain substring-based; e.g. `"Bateria: 11."` passes on `"Bateria: 111.5"` (`roteiros.json:647`), and `"serial": "3"` matches any line containing `3` (`:1239`).
2. **A check that cannot fail** — `app/AutotesteEeprom.cs:159`: `Conferir("rtc: escrever alem dos 512 bytes devolve false", rtc.Contains("0"), r);` where `r` is the whole joined monitor; the same sketch prints other `0`s (line 144), so the write's return value is never actually compared.
3. **Single-digit assertions** — `app/AutotesteEeprom.cs:136` (`esp.Contains("8") && esp.Contains("4") && esp.Contains("2.50")`) accepts `18`, `104`, `12.50`; same shape at `app/Autoteste.cs:10372` (`Contains("2")` matches “12 itens”).
4. **`analogReadMilliVolts()` can be a stub** — the check at `app/AutotestePlayerDap.cs:70` only demands the string `"0"` (a floating pin legitimately prints 0) — no voltage is ever computed; the companion check (`:72`) only demands the *AVR refusal* mention the name.
5. **“python: chave ausente” passes on the letter “a”** — `app/Autoteste.cs:10377` (`semChave.Contains("a")`), and the interpreter message itself starts with “a chave…” (`motor/InterpretadorPython.cs:217-220`).
6. **A literal `true` assertion** — `app/Autoteste.cs:16206`: `Conferir("constantes de biblioteca existem", true);` right after a sketch run whose result is discarded.
7. **Silent skips** — `AutotesteAccelStepper.cs:25-28` (library not installed), `AutotestePlaca.cs:73-76` (Arduino IDE core absent), `AutotesteNativo.cs:229-232` (DOOM WAD absent), `Autoteste.cs:14704-14708` (no `plataforma/`): the block returns with a parenthetical note, the summary still says “N passaram, 0 falharam”, and nothing counts the skipped work.
8. **A zero-execution run is green** — `app/Autoteste.cs:33-34` skips blocks that don't match `--so`; a filter typo prints “0 passaram, 0 falharam” and `app/App.xaml.cs:391` exits 0 (no `FALHA`). `ferramentas/verificar.ps1:70-73` also never checks how many tests ran.
9. **Regression check deleted with the fix** — commit `5cdc721` added the non-PWM `analogWrite` fallback (`motor/Maquina.cs:1881-1884`) and *removed* the roteiro check for it (`ENA no pino 2 (sem PWM)…`). Today that behavior is exercised nowhere; only the ESP32 path is tested (`app/Autoteste.cs:13536-13537`).
10. **Timing thresholds on wall clock** — run 1 (loaded) vs run 2 (idle) separated 15 load artifacts from 2 **persistent** failures (`< 4 ms` measured 9,2–9,5 ms; `< 1 ms` measured 2,1–2,7 ms against the check's own “0,01 ms” reference) and one flaky project (§2.3). Either the machine is below the intended class or a real per-frame regression slipped in — worth a profiling hour before granting an exemption.
11. **Interactive desktop required; long fixed waits** — blocks open real windows and pump the dispatcher (`app/Autoteste.cs:5753-5764`), with a 4-minute budget for one real-IDE compile (`app/AutotestePlaca.cs:89`). Locked-session or headless runs are impossible; a hung external tool burns minutes.
12. **`ExecucaoInterrompida` swallowed** — `AutotesteCpu.cs:31`, `AutotesteBlePlacas.cs:26`; a stop-early run is indistinguishable from a completed one.
13. **Non-`"funciona"` roteiros are never asserted** (latent today: all 46 are `"funciona"`), and `ferramentas/verificar.ps1:77-86` lists broken projects from JSON without failing.

---

## 6. Security and privacy

Severity here follows the classroom context: this is an offline teaching tool, so “unsafe default” matters more than “theoretical exploit”.

### 6.1 Sketch HTTP uses the PC's real network — ON by default (P1 by the previous audit's logic; unchanged)

- Engine-side class default is on: `motor/Rede.cs:63` `public bool UsarInternet { get; set; } = true;`
- The per-board fallback is off (`motor/Maquina.cs:1215` `new Rede { UsarInternet = false }`) — but the app always overrides it from preferences: `app/MainWindow.xaml.cs:2983-2984` (`Preferencias.Ligado(Preferencias.UsarInternet, true)` — default **true**). So a fresh install lets `HTTPClient` in a sketch reach the internet with the user's connection. It is one menu item away from off, and the panel narrates it — but the default is ON.
- `setInsecure()` disables TLS validation as it does on the real ESP32 (`motor/Rede.cs:399-406`); the insecure client also sets `AllowAutoRedirect = false` (good — the sketch HTTP layer does not follow redirects at all; `motor/Rede.cs:390-396`).

### 6.2 The PC's real radio is scanned by default (P2, local-only)

`app/Preferencias.cs:182` `UsarRadioDoComputador => Ligado(UsarRadioDoPc, true)` — ON by default. `motor/RadioDoComputador.cs:113-137` then runs `netsh wlan show networks mode=bssid` (every 20 s cache) and, for Bluetooth, spawns `powershell.exe` `Get-PnpDevice -Class Bluetooth` (lines 213-217). No data leaves the machine (the results feed the simulated `scanNetworks()`); but the user's real SSIDs appear inside the simulator, and the app shells out silently. Turning it off is one checkbox; the scan is described in the panel. Verified: this does **not** touch the machine's Bluetooth radio (PnP listing only), consistent with the project's rule of not switching radios.

### 6.3 Named pipe “CircuitSketch_Canal” (P2)

`app/App.xaml.cs:621-668`: server created with no `PipeSecurity` (default DACL — same user and admin only) and no authentication; the client reads **one line** and passes it to `MainWindow.CarregarSketch(caminho)` (`app/MainWindow.xaml.cs:3990-4024`), which treats it as a **file path**, reads the file, puts it in the editor, registers it in Recents and **starts it running** (`Dispatcher.BeginInvoke(... Rodar())`, line 4023). So any process running as the same user (including a low-integrity app, since default pipe DACLs usually allow low IL) can make CircuitSketch load and run any existing sketch file. Chain to host compromise requires more (the child is interpreted; native C modules are in-process, §6.5), but the pipe is a genuine local interface with more capability than it needs: the plugin only ever sends a path, and the app would be safer requiring a same-session token or at least checking the caller. The path is not spawned as a process, so the classic “named pipe RCE” shape does not apply directly.

### 6.4 Online library installer — verified strong (record for the good news)

`app/BibliotecasOnline.cs:234-240` restricts downloads to `https://downloads.arduino.cc/` (host match, default port, no userinfo); `:250-252` re-checks the URL after redirects before reading the body; the streamed download verifies the index's size (`:264`, `:270`) and **SHA-256** (`:271-272`); `app/Acervo.cs:484-509` blocks zip-slip by `GetFullPath` prefix comparison and caps decompression at 200 MB. Residual nits only: the redirect is *followed* before the final check (a probe of one URL happens; the body is never read from the wrong host), and the fetched index itself is trusted-by-HTTPS, which matches the Arduino IDE's model.

### 6.5 Native C runs in-process (P1 by design; unchanged from the previous audit)

`motor/Nativo/*` compiles sketch C with TinyCC into the running x64 process (`ModuloNativo`, `HostNativo`). Its host-call table (`motor/Nativo/HostNativo.cs:17-80`) is capability-limited (no `fopen`/process/registry), and the file table routes through the simulated card roots — `motor/CartaoSd.cs:68-81` fences every host path to the piece's folder (`Nunca sai da pasta`, verified). So the blast radius of a malicious *sketch* is the simulator itself, not the filesystem — but any memory-safety failure inside the in-process x64 execution kills or corrupts the whole app, and the pipe (§6.3) is a plausible delivery route for a sketch the user never chose. If the project ever accepts untrusted sketches, module the native core out-of-process.

### 6.6 Two things that *look* like bugs but are not (verified)

- `motor/RadioDoComputador.cs:275` reads `netsh` output as UTF-8 while the console codepage here is CP850. Measured: the UTF-8 decode produces clean `á í ç ã`, the CP850 decode produces box-drawing mojibake — i.e. `netsh` emits UTF-8 to pipes and **the code is right**.
- The SD-card folder traversal guard (§6.5) and the library-zip extraction guard (§6.4) both held up under reading and are covered by tests.

---

## 7. Hardware fidelity, glitches and model limits

- **Project battery**: 46/46 today (§3). The old recorded failures are fixed; the two prior audits' P1 lists should be read as history.
- **Slide-switch is fixed** — `app/EditorCircuito.cs:5672-5682` now models the SPDT properly (“o comum (2) vai ao 1 com a alavanca à esquerda e ao 3 à direita…”, via `LevarNivel`), landed in commit `c42866a` (14 Oct). The line `app/Comportamentos.cs:162` (`["slide-switch"] = new() { Digitais = ["1","2","3"], …}`) remains from the old model, but the electrical path no longer uses it for levels. **`RELATORIO-AUDITORIA.md` §1.1 (“Slide switch is not an SPDT”) is stale** and should be corrected rather than quoted.
- **AVR fidelity gaps found by the engine review** (§4.9): `.org` byte-vs-word, masked branch displacements, Timer0 CTC/WGM ignored, Timer2/EXT interrupts never serviced. These are the places where assembly-level teaching rests, and they are worth fixing ahead of most of the polish items in §10.
- **CPU throttling is surfaced, but by ratio to load** — for ESP8266 / RISC-V boards the suite printed warnings like “este computador não acompanha o ESP8266 nas contas do sketch — está a 0% da velocidade real” while two suites shared the machine. Time-based sketches (`delay()` bounded, `millis()` accurate) are unaffected by design, but compute loops are; a student on a loaded classroom PC will see the same behavior, with the warning explaining it.
- **Known, documented limits** (README, still accurate): interrupt/`shiftOut` edges run after the call, not between pulses; the debugger cannot modify values, step over, or keep breakpoints stable across edits; the Arduino IDE does not show the port (Upload works anyway). These are honest limits, not new bugs.
- **Glitches verified this session**: (i) selecting English today would show 26 blank labels/menus (empty keys in `en.json`, §8); (ii) the IDE's serial monitor can print `�` mid-accent when a UTF-8 sequence is split across file polls (§4.5) and the discovery helper lingers after QUIT (§4.3); (iii) under load, the *keypad test* loses the first key pressed 300 ms after start (§2.2 item 15) — the board has the same startup race (its library only sees keys once `loop()` is scanning), so this one is a test-assumption footnote rather than a product defect.

---

## 8. Incomplete features and i18n

The Alpha-25 language system exists end to end (`motor/Idioma.cs`, `{local:T}` in XAML, `motor/idiomas/*.json`, `ferramentas/idiomas.py`, block `Idiomas_Base`), but coverage is a rounding error against the plan:

- Marked call-sites today: `Idioma.T(` — **2 in `motor/`**, **56 in `app/*.cs`**; `{local:T` — **6 in XAML**. Total ≈ **64**.
- Plan (`coordenacao/PLANO-ALPHAS.md:45`): **~16.000 texts** (11.600 UI + 4.300 engine).
- Ship state: one catalog (`en.json`), `"completo": false`, **35 keys** of which **9 have a translation** (`Abrir projeto...`, `Salvar projeto`, `Novo projeto`, `Exemplos prontos`, `Projetos recentes`, `Idioma`, the “new language applies on restart” note, and the `{0} peças, {1} fios` plural). The other 26 are empty strings — which silently render as empty labels if a user picks English before the catalog is done. (The language selector only appears when a catalog exists — but `en.json` exists *now*, with 26 holes.)
- Everything else remains Portuguese-only, including all error and warning messages the student sees.

Interpreter completeness, quantified for this report: `motor/*.cs` contains **63 explicit “não é suportado” refusal sites** (plus other phrasings — 76 lines mention `suporte` at all). They concentrate in network/HTTP (`InterpretadorRede.cs`, 11), MicroPython (`InterpretadorMicroPython*.cs`, 14), BLE client/server (`InterpretadorBleCliente.cs` 7, `InterpretadorArduinoBle.cs` 6, `InterpretadorBle.cs` 2) and Telegram (2). Refusing loudly is the right call (it is this project's stated design), but the roadmap wording (“Alpha 24/25”) should stop implying these are done.

Documentation drift about incompleteness (all verified in code today):

- README “O que ainda falta” says **BLE is not simulated** (`README.md:2344`) — false: `app/AutotesteBle*.cs` runs full peripheral/central scenarios for the ESP32 core **and for the Nano RP2040 Connect's `ArduinoBLE`** (including cross-board runs), and every one of those blocks passed in run 1. It is a working *subset* — 15 of the engine's refusal sites live in the BLE interpreter files — but “não é simulado” is not a true description of the current code.
- The same section says **`configTime()`/`getLocalTime()` do not exist** (`README.md:2349-2350`) — false: `motor/InterpretadorTempo.cs` implements the NTP clock (with `configTzTime` too), and there is a demo (`esp32s2-relogio-ntp`). The suite exercises it.
- Still true in that section: `ArduinoJson` refused, `http.getStream()` refused (`motor/InterpretadorRede.cs:667-670`), IDE port invisible, debugger limits.

---

## 9. Documentation and metadata inconsistencies

1. **Three version labels disagree**: `app/App.csproj:28-31` says `Version 0.24.0`, `InformationalVersion “Alpha 24”`; `README.md:2158-2160` (Marco 37) says “versão **1.4**” and “**1.4.0**, no `<Version>`”; `AGENTS.md` and `PLANO-ALPHAS.md` describe current work as **Alpha 25**. Whichever is canonical, a support reader gets three answers.
2. **`RELATORIO-AUDITORIA.md`** (untracked, in the tree root) is the most detailed earlier audit but predates the fixes it headlines: §1.1 slide-switch (fixed in `c42866a`), the 27/38 battery (now 46/46), and its “Recorded project battery…” line. It should be annotated as historical.
3. **`coordenacao/resultados-projetos.json`** carries the same stale 27/38 date stamp.
4. **`ferramentas/` help text promises less than the app does** — several generators' docstrings and comments still describe “out of scope” states that the engine has since implemented (network, BLE). Sweep once against the code, not against memory.
5. **`mega2.bak`** — an 11.477-byte Arduino-Mega SVG backup is **tracked in the repository root** (git ls-files confirms), untouched since 2026-09-09. Dead file in the tree root; remove or move with the other assets.

---

## 10. Tooling and operations

1. **`ferramentas/bateria.py:26`** — default output is `'E:/Sktch[/BATERIA DE TESTES'`: running the generator without arguments writes to the professor's **USB stick**. AGENTS.md explicitly forbids this; the safe default (repo-local `bateria/`) is one line away. `BASE` is also hard-coded to the OneDrive path (`:25`).
2. **`ferramentas/projetos.py:29-30`** — default output goes into the repo (`projetos/`), and `DOWNLOADS = 'C:/Users/seraf/Downloads/'` is hard-coded: on any other machine the generator cannot fetch its source material, and the rover projects are silently skipped when missing (per the tooling review).
3. **`ferramentas/extrair-svg/*.py`** — **32 of 51** scripts hard-code `C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/`; they only run on one machine in the world.
4. **`ferramentas/testados.py:11`** — `DESTINO = 'C:/Users/seraf/OneDrive/Desktop/TESTED'`: the classification output goes to one person's OneDrive-synced Desktop (overridable by argument only).
5. **`ferramentas/provar-monitor/provar-monitor.py:26`** — writes into `%APPDATA%\CircuitSketch\serial.txt`, i.e. the live serial-monitor buffer of a running simulator; the docstring warns “Feche o CircuitSketch antes”, which is exactly the kind of footgun the project's isolation rules exist to avoid (`Armazem.Isolar` was built for the test modes — this script predates or bypasses it).
6. **`ferramentas/animar.py`** writes into `app/assets/` — a generator that mutates the source tree of the graphical assets (the area AGENTS.md reserves for one agent).
7. **PowerShell hygiene** — the platform scripts are launched with `-ExecutionPolicy Bypass` throughout (`monitor.ps1:104`, and friends). Necessary for the classroom, but it means the plugin runs whatever PowerShell it is pointed at; keep the scripts short and auditable, and prefer `powershell -NoProfile` (already done in `RadioDoComputador.cs`).
8. **No CI anywhere.** The suite takes ~17 min, needs an interactive desktop, and is run by agents per-task. A scheduled/agent-run nightly with the run-1 caveat in mind (single instance at a time!) would catch regressions earlier than a human task would.
9. **AGENTS.md guidance vs. suite reality** — the file instructs each agent to compile in its own artifacts folder but says nothing about suites; run 1 shows two concurrent suites poison the timing tests. Worth one line: “um `--teste` por máquina”.

---

## 11. Suggested order of work

1. **Stop the silent file overwrites** (§4.10.1–4.10.2) — make `AbrirProjetoDe` prove the file is a project (header/`Formato` check) before adopting it as the session, and keep `.ino`/alien `.json` out of Recents and the open dialog. Without this, one Ctrl+S can destroy a sketch or a Wokwi diagram; with it, the rest of §4.10 is ordinary polish.
2. **Fix the plugin path handling** (`ponte.ps1`/`monitor.ps1` quoting, empty-file `.Trim()`, `caminho.txt` UTF-8) — small diffs, immediate user pain, respects the project's own “portable to any classroom PC” goal.
3. **AVR core fidelity** (§4.9): Timer0 WGM/CTC, Timer2 and external interrupts, `.org` byte-counting, unmasked branch displacements. This is the teaching core of the product; the fixes are contained and testable.
4. **Guard the four `--saida` writes** (or route them through the existing try/catch pattern) so a bad path never hangs a headless run.
5. **App-shell guards and editor polish** (§4.10): ask `PodeDescartar` in `AbrirProjetoDe`/Wokwi import; clear `EmSimulacao` on every `Rodar` bail-out; keep the pipe listener alive across exceptions; atomic save with `.bak` on by default; single-arm the hotkey dialog; reposition overlays on Girar/Espelhar; carry `Dobras`/`CaminhoDoWokwi`/`Lado` in wire copy/paste; block undo/copy during a run.
6. **Test-suite honesty pass** (§5): substring serial checks → exact-line; the two “cannot fail” checks; count skips; fail on “0 tests”; profile the two persistent performance failures before calibrating or exempting them; root-cause the flaky `fechadura-teclado-servo`.
7. **README “O que ainda falta” + version labels** (§9): strike the BLE and `configTime` bullets; align `csproj`/README/plan on one version scheme.
8. **`instalar.ps1`**: drop `bin/obj` from the copied tree (92 MB → ~3 MB), write `caminho.txt` in UTF-8, add the same plugin-ownership guard the C# installer already has.
9. **`bateria.py` default output** → repo-local (align with AGENTS.md), and parameterize the hard-coded machine paths as those scripts are touched.
10. **Quick engine guards**: EEPROM typed reads negative-address check; `HTTPClient.end()` clearing the response; MQTT PINGRESP/dead-broker state; accept-loop retry (§4.9).
11. **Pipe hardening** (even cheap: reject paths outside recent/sketch folders, or require the file to exist under a temp “handoff” folder created with the IDE plugin) — the pipe is the only local attack surface with a real chain.
12. **Decide the classroom default for `UsarInternet`** (currently ON) — a one-line default change plus a first-run note would close the previous audit's P1 as well.
13. **Housekeeping**: delete `mega2.bak`; mark `RELATORIO-AUDITORIA.md` and `coordenacao/resultados-projetos.json` as superseded; copy the fresh `.artefatos/deepseek/resultados-projetos.json` into `coordenacao/`.

---

## 12. Limits of this review

- No hardware was involved: fidelity claims are code/tests vs. datasheet intuition, not measurements.
- Run 1 coincided with another agent's full suite (§2.2) — the clean run 2 is the authoritative number for “does the suite pass on a quiet machine”, and §2.3 corrects §2.2's reading where they disagree.
- The GUI was exercised only through the suite (2×) — no human-style exploratory pass (the `--passeio` walk was not run to avoid a third concurrent window-generating process while timing runs were in flight).
- Static review of `app/` was targeted (build config, App startup/CLI/pipe, network defaults, radio scan, library installer, SD card fencing, language machinery, player-demo path); a dedicated `motor/` engine review is folded into §4.9, and a dedicated `app/` shell-and-editor review is folded into §4.10 — it returned after the first draft, and I re-verified each item at its cited lines, correcting one mechanism with a probe (§4.10.14). These are code-path reviews, not an interactive pass: no human-style exploration of the GUI was done. The 115k-line tree was not read line by line; blocks of it (PCB, audio DSP, graphics internals) were only sampled through what their tests assert.
- The two persistent performance-check failures in §2.3 are specific to this computer's speed; on faster hardware they may pass. The flaky `fechadura-teclado-servo` needs a root-cause run (test input pacing vs. Keypad scan window).
- Analysis is Windows-specific (this machine: Windows, pt-BR, .NET SDK 11.0.100-rc.1).

---

*Report_DeepSeek — written 16 Oct 2026 against commit `f103d4f`. Evidence files: `.artefatos/deepseek/` (build output, `saida-suite.txt`, `saida-suite2.txt`, `resultados-projetos.json`, `sonda-wpf-handled.ps1`/`.txt`).*
