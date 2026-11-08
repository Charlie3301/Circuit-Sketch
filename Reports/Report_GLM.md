# CircuitSketch — audit report (GLM)

**Product:** CircuitSketch (offline ESP32/Arduino simulator, WPF + interpreter + TinyCC native C)
**Tree:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32`
**Date:** 17 October 2026
**Author:** GLM (independent audit; supersedes parts of `RELATORIO-AUDITORIA.md` from 16 Oct)

## How this was done

- **Five parallel subsystem audits** (read-only) covering every non-test file:
  1. `motor/` language core (Lexer, Preprocessador, Parser, Interpretador, Fluxo, TiposC, ...),
  2. `motor/` periphery (Rede, BLE, I2C, Avr, Montador, Nativo/TCC, MicroPython, Radios, TelaGrafica),
  3. `app/` application layer (App.xaml.cs, MainWindow, Janela*, Acervo, BibliotecasOnline, Sktch),
  4. `app/` electrical model (Energia*, Circuito, Queima, Comportamentos, drivers, catálogo),
  5. `plataforma/` + `ferramentas/` + `nativo/` (scripts, Python tools, native C runtime).
- **Marked [V]** when I re-read the cited lines myself; **[A]** when the finding comes from the
  subsystem audit with exact `file:line` quotes (every [A] has code evidence attached).
- **Live checks in this session:** debug build of `app/App.csproj` and one targeted autotest block.
- **Full `--teste` suite and the 38-project battery were NOT run** (they open real windows and
  collide with an open CircuitSketch instance, same reason the previous audit skipped them).

Severity scale (same as the previous audit):

| Tag | Meaning |
|---|---|
| **P0** | Data loss risk, or crash of the whole process (uncatchable), or host compromise |
| **P1** | Simulator disagrees with the board in a way that fails a real lab circuit / recorded test |
| **P2** | Incomplete feature or annoying glitch, fenced with a message, or policy/host exposure default |
| **P3** | Cosmetic / trivial / stale comment |

## Session checks

| Check | Result |
|---|---|
| `dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/glm-auditoria` | **Compiled with success — 0 warnings, 0 errors** |
| `CircuitSketch.exe --teste --so Comportamento_EntradasESensores --saida .artefatos/glm-auditoria/glm-saida-entradas.txt` | **82 passed, 0 failed** (previous audit recorded 70/12 for the same block) |

---

## Executive summary

The tree has moved since the 16 Oct audit: the slide-switch SPDT bug is **fixed** (the test block
that used to fail 70/12 now passes **82/0**), the library download **redirect pinning gap is
closed**, and `modulo-rele-1/2/4` do exist in the catalogue. The build is clean with zero warnings.

What remains clusters in six themes:

1. **Two uncatchable `StackOverflowException` paths** and **unbounded allocations** inside the
   interpreter — these kill the whole WPF process instead of showing the student a written error
   (P0).
2. **A silent-failure chain in the Arduino IDE bridge** (`ponte.ps1` + `instalar.ps1`): sketch
   paths with a space arrive split, missing executables report `exit 0`, the Documents path is
   hard-assumed to be OneDrive-redirected, and `caminho.txt` is written in ASCII — four separate
   ways Verify/Upload quietly does nothing (P1).
3. **Data-loss paths in the app**: an incoming sketch discards unsaved edits and flags the project
   clean, saving is non-atomic with backup off by default, and the IDE pipe listener dies forever
   on a single exception (P1).
4. **Host exposure defaults**: sketch HTTP uses the PC's internet by default (`UsarInternet=true`),
   `setInsecure()` skips TLS validation, the native pipe accepts a path from any same-session
   process, and TinyCC-compiled student C runs as x64 in-process (P1 carried).
5. **Electrical false "all good" results**: an 8-Ω speaker across a 9 V battery draws ~1 A with no
   burn and no warning; relay/stepper coils driven directly from GPIOs bypass the current-based
   burn warnings; dip-switch and potentiometer models diverge from bench behavior (P1/P2).
6. **A set of C-semantics divergences** (`(type)` casts ignore board width, `Serial.find()` misses
   self-overlapping needles, AVR Timer2 interrupts never fire, `parseInt` rejects `+12`) that
   produce the worst kind of student damage: *silently different output* (P1–P3).

Findings count: **3 P0 · 17 P1 · 14 P2 · 18 P3** (plus recorded-but-not-rerun items in §7).

---

## 1. P0 — data loss and process-death paths

### 1.1 `ferramentas/bateria.py` with no argument writes to `E:` (the professor's pendrive) [V]
`ferramentas/bateria.py:25-26`
```python
BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
SAIDA = sys.argv[1] if len(sys.argv) > 1 else 'E:/Sktch[/BATERIA DE TESTES'
```
The docstring (`[pasta-de-saida]`) invites the bare invocation; every bare run creates
`E:\Sktch[\BATERIA DE TESTES` and writes **100+ `.sktch` files** onto `E:\Sktch[\NOT WORKING`'s
drive — the professor's originals. No confirmation, no try/except. `BASE` is also hardwired to
this machine, so the script can never run elsewhere.
**Fix:** default output to `BASE + 'bateria'` (same pattern `projetos.py:29` already uses), derive
`BASE` from `__file__`, and require an explicit path for anything outside the repo.

### 1.2 Library-callback recursion can reach a real `StackOverflowException` — process death [V]
`motor/InterpretadorTexto.cs:766-797` — `ChamarRotina` (the dispatcher used by **every library
event callback**: timers, web server routes, MQTT, BLE, IR) builds the environment and calls
`ExecCorpoDeFuncao` **directly**, with no `chamadasAbertas++` / `PilhaCheia()` / `EstourarPilha`
sequence. All guarded call sites live elsewhere (`motor/Interpretador.cs:3123-3135`,
`motor/InterpretadorClasses.cs:328, 731`): a grep shows **zero** occurrences of
`chamadasAbertas` in `InterpretadorTexto.cs`.
Consequence: a sketch whose callback synchronously re-enters the same callback chain (e.g. a
WebServer route that issues an HTTP request back to its own route, served synchronously —
`InterpretadorRede.cs:847`) recurses without any ceiling on the 64 MB run-thread stack. The
interpreter's normal "a pilha estourou" error never fires; .NET's `StackOverflowException` is
**not catchable** — the whole app dies, unsaved work included.
**Fix:** give `ChamarRotina` the same `chamadasAbertas++ / try / finally { chamadasAbertas--; }`
wrapper that `Chamar` uses (≈6 lines).

### 1.3 `json.dumps` of a self-referencing dict — same uncatchable crash [A]
`motor/InterpretadorMicroPython.cs:593-601` — `ParaJson` recurses with no depth guard:
```csharp
DicionarioPy d => "{" + ... + ParaJson(par.Value) ...
```
Two lines of MicroPython (`d = {}; d["x"] = d; print(json.dumps(d))`) produce an uncatchable
.NET stack overflow instead of the board's `maximum recursion depth exceeded`. `LerJson`
(622-684) has the same hole for deeply nested MQTT payloads.
**Fix:** depth counter in both directions that throws `ErroDeExecucao`.

---

## 2. P1 — bugs that will fail real circuits or real usage

### 2.1 The IDE pipeline silently goes nowhere (chain of four independent holes) [V]

**a) Sketch path with a space breaks the launch.** `plataforma/tools/ponte.ps1:103-105`:
```powershell
$argumentos = @($Sketch)
if ($Peca) { $argumentos += @('--placa', $Peca) }
Start-Process -FilePath $exe -ArgumentList $argumentos -WindowStyle Normal
```
Windows PowerShell 5.1 joins `-ArgumentList` elements with a space and **does not quote them**.
A sketch whose folder contains a space (default sketchbook: `Documents\Arduino\...`) arrives in
`App.xaml.cs` as two arguments; `e.Args[0]` is then a path that does not exist → nothing opens,
no message. **Fix:** `@("`"$Sketch`"")` — the project already does exactly this in
`ferramentas/verificar.ps1:57-58`.

**b) Missing executable reports success.** `ponte.ps1:109-111` prints two stderr lines and `exit
0`. To arduino-cli, exit 0 = tool succeeded: the student sees a green Upload while the simulator
never opens. **Fix:** non-zero exit on that branch.

**c) The installer assumes OneDrive-redirected Documents.** `instalar.ps1:6,10`:
```powershell
$destino = Join-Path $env:USERPROFILE 'OneDrive\Documents\Arduino\hardware\circuitsketch\esp32'
```
If Documents is not redirected there (plain `Documents\`, corporate account, another disk), the
platform installs into a folder the IDE never scans — and still prints success. The repository
already has the right pattern twice (`ferramentas/instalador/Instalador.cs:187-189`,
`instalar-plugin.ps1:47-48` use `[Environment]::GetFolderPath('MyDocuments')`). **Fix:** use it.

**d) `caminho.txt` written as ASCII.** `instalar.ps1:30`
(`Set-Content ... -Encoding ascii`): any accent/cedilla in the path (very common in Brazilian
usernames, e.g. "João") becomes `?` and the recorded executable path stops existing. The plugin
installer already documents UTF-8-with-BOM as the right choice (`instalar-plugin.ps1:114-116`).

Combined with **b)**, on machines with an accented username or non-OneDrive Documents the
Verify/Upload flow silently does nothing. 

### 2.2 The IDE pipe listener dies permanently after one exception [V]
`app/App.xaml.cs:637-668` — the listener loop's outer `catch { return; }` is meant to detect
shutdown, but `Dispatcher.Invoke(() => janela.CarregarSketch(sketch))` runs *inside* the `try`.
`CarregarSketch` has no catch around `File.ReadAllText(arquivo)` (`MainWindow.xaml.cs:4014-4016`):
one IOException (file locked, OneDrive-download placeholder, permission) kills the background
thread's loop, and every later Verify from the IDE is silently dropped for the rest of the
session (the sending side also swallows, `App.xaml.cs:630-634`). **Fix:** catch inside the
invoked callback and `continue`; only treat shutdown as loop exit.

### 2.3 Incoming sketch discards unsaved work and marks the project clean [V]
`app/MainWindow.xaml.cs:4014-4020` — `CarregarSketch` (pipe or double-click path) replaces the
editor text without the `PodeDescartar()` prompt that manual open uses, then sets
```csharp
circuitoSalvo = AssinaturaAtual();   // 4018
```
while **not** updating `caminhoDoProjeto` for a plain `.ino`. Result: the new code is flagged
"saved" while pointing at the *old* project — a subsequent Ctrl+S or the 30 s autosave silently
overwrites the user's previous `.sktch` with the new code, and closing never prompts. **Fix:**
reuse the open-project confirmation flow; leave the dirty flag dirty for `.ino` arrivals.

### 2.4 Saving is non-atomic and the backup is off by default [V / partial A]
`app/Sktch.cs:69`: `File.WriteAllText(caminho, ...)` truncates in place. Under OneDrive the file
is frequently held by the sync engine; one partial write loses the only copy. The recovery copy
exists behind `Preferencias.Ligado(Preferencias.CopiaDeSeguranca, false)`
(`MainWindow.xaml.cs:3875`) — **default off** (a test explicitly turns it on, so the default path
is untested). **Fix:** write `path + ".tmp"` then `File.Replace`; flip the backup default to on
for this app's audience.

### 2.5 `(type)` casts ignore the board's widths [A]
`motor/InterpretadorTexto.cs:235-236` hard-codes 32-bit masks for every cast:
```csharp
"uint32_t" or "unsigned long" or "unsigned int" or "unsigned" => Valor.DeInt((long)n & 0xFFFFFFFF),
```
Assignment honors `ModeloC` (`motor/Interpretador.cs:1116-1117`, comment: "no C, o numero vai
para o tipo da declaracao -- com os bits da placa"), but the cast path does not. On the Uno,
`(unsigned int)-1` prints **4294967295** instead of 65535; `(int)70000` stays 70000; on ESP32
`(long)4000000000` stays 4000000000. `Serial.print((unsigned int)(-1))` style code is everywhere
in AVR tutorials. **Fix:** route integer cast targets through `ModeloC.ParaOTipo` when `EmC`.

### 2.6 `Serial.find()` misses self-overlapping needles [A]
`motor/Fluxo.cs:152`:
```csharp
casados = b == procurado[casados] ? casados + 1 : (b == procurado[0] ? 1 : 0);
```
This is a KMP **without** the fallback function. Verified by hand: `find("aab")` against stream
`aaab` → `1, 2, 1, 0` → returns **false**, where every real Arduino matches. Any needle whose
prefix re-occurs after partial progress fails and burns the buffered bytes — the exact idiom
teaching sketches use to wait for `OK`. **Fix:** proper failure function, or the span search
already present in the same file (`Fluxo.cs:60`).

### 2.7 AVR Timer2 interrupts never fire [V]
`motor/Avr.cs:1130-1143` — `Pendente()` and the vector table wire **only** Timer0/Timer1:
```csharp
bool Pendente() => (dados[t+TIFR1] & dados[t+TIMSK1] & 3) != 0 || (dados[t+TIFR0] & dados[t+TIMSK0] & 3) != 0;
```
Yet the ISA exposes the full Timer2 register map and legal vectors (`AvrIsa.TIFR2 = 0x17`,
`TIMSK2 = 0x70`, `VetorTimer2Ovf = 0x0012`). A Uno sketch with `ISR(TIMER2_OVF_vect)` compiles,
runs, and the interrupt simply never arrives — the silent kind of failure. **Fix:** wire Timer2
in the same tick loop, or reject sketches that enable it with the project's named-error style.

### 2.8 Sketch HTTP uses the PC's internet by default (carried; still true) [V]
`motor/Rede.cs:63`: `public bool UsarInternet { get; set; } = true;` — with bench-to-bench
routing, `HTTPClient`+TCP from a sketch can reach arbitrary internet URLs from a school PC, and
`setInsecure()` installs `RemoteCertificateValidationCallback = (_, _, _, _) => true`
(`Rede.cs:399-403`; real-ESP32 parity, but Windows doesn't collapse it). The menu toggle exists;
**off-by-default would match the "offline simulator" pitch**. Keeping the old audit's policy
finding: the flat internet surface for a classroom default.

### 2.9 TinyCC student C runs as x64 in-process (carried; still true) [carried]
`motor/Nativo/ModuloNativo.cs` compiles student `.c` with `tcc/libtcc.dll` into memory and runs it
on host OS threads (FreeRTOS task = OS thread), with VEH + trampoline + VFS fake disk. The 16 Oct
audit's framing stands: crash/integrity risk for the process, not a remote RCE, but the sandbox is
thinner than a WASM isolate. Not re-audited deeper here.

### 2.10 Unbounded allocations in the interpreter [A]
- `motor/Interpretador.cs:923` `int tamanho = (int)Avaliar(d.Tamanho).Num;` then
  `new Valor[quantos]` (932/944/960/988) with **no cap**: `int arr[99999999]` allocates ~800 MB of
  references; `int arr[3000000000]` wraps the `(int)` cast negative and silently builds an **empty
  array** instead of a compile error.
- `motor/InterpretadorDimensoes.cs:174` `new Valor[Produto(dims)]` — unchecked int product.
- The locals-depth guard charges only literal sizes (`InterpretadorMemoria.cs:415-417`), so
  `int lixo[Serial.available()];` allocates at runtime with nothing counted behind it.
- Contrast: `AvaliarNovo` already caps correctly (`InterpretadorClasses.cs:548-549`).
**Fix:** same ≤1 000 000 cap + negative rejection in the three allocation paths.

### 2.11 Electrical: an 8-Ω speaker on a 9-V battery draws ~1 A and nothing warns [A]
`app/Queima.cs:230-256` (burn rule 8) only reacts to board-ground/supply rails, and battery pins
`+`/`-` are not in the rail lists (`Circuito.cs:903-908`). The safety net
`EditorMotores.EstourosPelaEnergia()` (`EditorMotores.cs:451-561`) has no `alto-falante` case. A
speaker placed directly on the battery draws ~0.95 A (~8.6 W into a 0.5 W coil) and the bench
shows only a healthy "Demais" flow — the classic silent smoke on a real desk. **Fix:** cover
speaker/buzzer in `EstourosPelaEnergia`, or map `+`/`-` to rails in `Mapear`.

### 2.12 Electrical: burn rule 7 is dead for KS2E relay and biaxial stepper coils [A]
`app/Queima.cs:205` scans only `def.Pinos.Where(EhAlimentacao)`, but the relay's pins are
`COIL1/COIL2/P1/P2/...` and the biaxial's `A1+-, A2+-` — neither matches, although
`CorrenteDe` (`Queima.cs:516-517`) declares 80 mA / 400 mA for them, and `AvisoDeMotor`
(`Queima.cs:326-351`) lacks the biaxial case. Pin-hooking these coils to GPIOs (the
step-sequence exercise) produces no "the pin delivers 40 mA" warning. **Fix:** scan a
"consumer pins" list per part (COIL*, coil letters).

### 2.13 Recorded project battery failures (13 Oct snapshot) — not re-run here [recorded]
From `coordenacao/resultados-projetos.json` (previous audit, same tree on 13 Oct): **11 of 38**
projects failed. Notable ones whose root causes I could not close in this pass:
`espnow-botao-led` (press works, release doesn't — second packet lost),
`drone-quadricoptero`/`nivel-mpu6050` (empty serial: `mpu.begin()`/`getEvent` paths),
`controle-ir-leds` (Uno pin 3 false-burn: `Queima` flags Timer2's OC2B pin tied to a rail),
`carrinho-rc-sem-placa` (servo ignores radio channel; ESC follows), `ventilador-l298n`
(low PWM → 0 rpm instead of slow), `rover-4wd-hc-sr04` (duty split not realized on the two L298N
enables). The battery should be re-run on this tree before treating them as fixed — the
slide-switch fix proves the tree moved since 13 Oct.

---

## 3. P2 — incompleteness, model gaps, exposure

1. **`dip-switch-8` imposes fixed levels instead of carrying the other side's** [A].
   `app/EditorCircuito.cs:5772-5786` writes `maq.NivelExterno[g] = ligada ? 0 : 1` on both
   `1a`/`1b`: hooked to 3V3 it still reads LOW, and "off" is a forced HIGH even when the node has
   a pulldown. The sibling switches (`micro-switch`, `slide-switch`) already use the
   transport-the-level `LevarNivel` pattern (`EditorCircuito.cs:5681`).
2. ** potentiometer/slide-pot SIG carries no current** [A]. `app/Energia.cs:861-867` models only
   the VCC→GND 10 kΩ; the SIG tap is not wired, so a load powered from the SIG (a module, LED)
   sits behind a 1e-9 leak and draws 0 A — `analogRead` reads fine because it's control-injected.
   **Fix:** two arms (VCC→SIG→GND of `10k·Giro` / `10k·(1−Giro)`).
3. **LED model is color-blind** [A]. `Energia.cs:674` uses Vf = 2.0 V for every LED/RGB/segments
   (`Queima.cs:52` comments next to the constant: "Vermelho ~1,8; azul ~3,0"). Blue/white LEDs
   appear lit "in range" in cases where the bench barely lights them.
4. **PWM averaging hides current peaks from burn checks** [A]. `app/EnergiaPwm.cs:76-97` averages
   voltages/currents weighted per slice; the "worst slice" reconstruction exists (93-95) for
   *levels* only. An LED on 5% PWM at 5 V peaks ~160 mA for 100 µs but averages ~8 mA → never
   flags.
5. **`Acervo.OQueFalta` enumeration unguarded on the UI thread** [A]. `app/Acervo.cs:650-657`
   walks Arduino library folders with `Directory.GetFiles` and no try/catch (other helpers in the
   file all guard `IOException`/`UnauthorizedAccessException`); one broken junction aborts the
   "missing library" flow with the global exception dialog.
6. **Stop paths join sketch threads up to 1.5 s each on the UI thread** [A].
   `MainWindow.xaml.cs:4863, 5104, 5277` — `thread.Join(1500)` per board; several boards means a
   multi-second freeze during Parar/Energia/Reset.
7. **`JanelaCartao` continuation uses `t.Result` without success check** [A].
   `app/JanelaCartao.cs:197-215` — an enumeration failure becomes `AggregateException` on the UI
   thread; `MainWindowPlaca.cs:261-264` already shows the guarded pattern.
8. **`float('abc')` lets a raw .NET `FormatException` escape** [A].
   `motor/InterpretadorPecasPython.cs:419-422` — `int()` has the guarded `TryParse` message
   (409-414); `float()` doesn't, so the student sees a .NET message instead of Python's
   `ValueError`.
9. **`ConexaoTcp.Bombear` accumulates unbounded receive buffers** [A]. `motor/Rede.cs:1501` — the
   pump appends forever if nobody reads; every other queue in the project has a cap (radio queue
   32, native request 4 MiB, `bytes()` 1 MiB).
10. **Native httpd accepts clients with no `ReceiveTimeout`** [A]. `motor/Nativo/NativoRede.cs:163`
    — byte-by-byte `ReadByte()` on a keep-alive tab or a port scan wedges the handler thread
    forever; the C++ `ServidorWeb` already sets ` cliente.ReceiveTimeout = 5000` (`Rede.cs:1381`).
11. **Native runtime shares `static` time buffers across tasks** [A].
    `nativo/runtime/cs_freertos.c:845, 932`, `cs_bancada.c:30`, `cs_idf.c:145` — `localtime`
    returns one shared `struct tm`; two tasks reading time concurrently corrupt each other's
    formatting (fidelity limitation worth a marker or thread-local storage).
12. **`modulo-rele-1/2/4` narrative is stale in `projetos.py`** [V]. `ferramentas/projetos.py:648-678`
    still builds `rover-4wd-reles` with **bare KS2E relay coils** wired between 5 V and GPIO (the
    ~70 mA over-limit circuit) although the modules exist in `app/assets/pecas.json`
    (`modulo-rele-1/2/4`) and the code comments even say "*como nos modulos*". The generated
    projectnormalizes a circuit the real class kit wouldn't tolerate. Regenerate with modules.
13. **Named pipe has no ACL** [carried, downgraded]: session-local pipe accepts a sketch path from
    any local process (`App.xaml.cs:21-22, 643-645`). Combined with 2.3/2.4 it can also cause the
    silent-overwrite path. Cheap fix: DACL or accepted-extension/path checks.
14. **README contradicts itself about network and BLE** [V]. `README.md:126` still says
    "*rede (WiFi/Bluetooth) esta fora de escopo*" while `README.md:2293+` describes the shipped
    WiFi/HTTP/MQTT/BluetoothSerial work (Marco 40); `README.md:2344` says "BLE não é simulado"
    while `motor/InterpretadorBle*.cs` (3 files) and their autotests exist in the tree. Also
    `ferramentas/idiomas.py --conferir` still reports incomplete languages per RELATORIO-003.

---

## 4. P3 — cosmetics, physics-color details, tooling

| # | Finding | Evidence [A unless noted] |
|---|---|---|
| 1 | `Serial.parseInt`/`parseFloat` reject a leading `+` (`+12` → 0) | `motor/Fluxo.cs:104,122` |
| 2 | Lexer lacks C++14 `'` digit separators and `\uXXXX`/`\U` escape handling | `motor/Lexer.cs:297-341,137-176` |
| 3 | `try/catch/throw/goto` compile to identifiers → misleading "esperava ';'" message | `motor/Parser.cs:84,1171,2104` |
| 4 | `DateTime(y,m,d)` silently clamps month/day ≥ 1, no warning marker | `motor/Interpretador.cs:3703` |
| 5 | `printf` `%g` always exponential, keeps trailing zeros (library printf only) | `motor/InterpretadorTexto.cs:390-393` |
| 6 | `strcpy` into a struct char-field ignores the declared size and morphs the field into text | `motor/InterpretadorTexto.cs:685-694` |
| 7 | `map()` zero-range: C path returns −1 (board-faithful), non-C path throws | `motor/InterpretadorC.cs:356-364` vs `Interpretador.cs:2935` |
| 8 | `strtol` wraps on overflow instead of saturating to LONG_MAX | `motor/InterpretadorTexto.cs:431-441` |
| 9 | Macro arity lenient: `F()` accepted for 1-param; extra args ignored for 0-param macros | `motor/Lexer.cs:844-849` |
| 10 | Preprocessor silent cap: deep `#define` chains stop expanding at 64 without a message | `motor/Lexer.cs:956` |
| 11 | `min`/`max` compare text by `.Num==0` — always returns the first argument | `motor/InterpretadorPecasPython.cs:441-443` |
| 12 | `BLEDevice.discoverService` NRE when the connected device is a phone (`d.Conexao` null) | `motor/InterpretadorArduinoBle.cs:596` |
| 13 | `Requisitar` keeps the whole HTTP body unbounded (time-limited but not size-limited) | `motor/Rede.cs:486-492` |
| 14 | `drawRect` with a negative size draws a bar instead of nothing (contour path) | `motor/TelaGrafica.cs:183-192` |
| 15 | `relogioTela` ticks forever at Render priority even when idle (25 fps CPU cost on weak lab PCs) | `app/MainWindow.xaml.cs:229` |
| 16 | `--menu` capture permanently detaches submenu items from the live window | `app/App.xaml.cs:53-109` |
| 17 | `JanelaAtalhos` "get next key" capture stays armed if the user switches to the mouse | `app/JanelaAtalhos.cs:118-155` |
| 18 | async-void port list / compile handlers unguarded post-await | `app/MainWindowPlaca.cs:118-191` |
| 19 | Predictable temp file names `circuitsketch-demo-*.sktch` / `passeio-*.sktch` in shared TEMP | `app/MainWindow.xaml.cs:1281`, `app/Passeio.cs:760` |
| 20 | MQTT broker binds 1883 per `Rodar()` and is never reaped after a crash (zombie listener) | `app/MainWindow.xaml.cs:4721` |
| 21 | `exibir` bridge log / pipe monitor log grow unboundedly in %TEMP% | `plataforma/tools/porta.ps1`, `monitor-cano.ps1:50-81` |
| 22 | Native NVS open-namespace table survives reboot → `nvs_open` exhausts 16 slots across boots | `nativo/runtime/cs_idf.c:1504-1533` |
| 23 | `idiomas.py` unescapes a truncated `\u` silently into a literal | `ferramentas/idiomas.py:47-52` |
| 24 | `extrair-svg` only loads 6 wokwi elements; everything else prints "FALHOU: sem `<svg>`" | `ferramentas/extrair-svg/entrada.js:1-6`, `extrair.mjs:193` |
| 25 | 51 scripts hardcode `C:/Users/seraf/...` (bateria/testados/provar-monitor/51 SVG tools) | inventory table below |
| 26 | `Baterias`/`platform.txt` comment says "prebuild + postbuild" but only postbuild exists; `{simulador.assinatura}` stays literal for non-AVR boards | `plataforma/platform.txt:8-9,18,77` |
| 27 | `instalar.ps1` comment still cites the removed `enviar.bat`; gerar.ps1 says "Alpha 16"; bateria.py self-identifies "Alpha 18" while generating Alpha-23/24 content | `instalar.ps1:27`, `ferramentas/instalador/gerar.ps1:12`, `bateria.py:3,45` |
| 28 | 92 MB of `fonte/porta/bin+obj` build artifacts installed with the platform on manual install (packaged installer excludes them, the .ps1 doesn't) | `instalar.ps1:25` vs `gerar.ps1:103-106` |
| 29 | Sound sensors (`big/small-sound-sensor`) idle-HIGH→LOW contrast with the rest of the LM393 family — check the kit datasheet before trusting | `app/Comportamentos.cs:135-144` |
| 30 | HX711 raw count is a fixed magic constant (84213 + 420/g) so `scale.read()` without tare always reads the same "perfect" cell | `motor/Interpretador.cs:3719`; calibrated loads pass live (see session checks) |

### Hardcoded machine paths inventory (finding 25) [A]

| File | Fixed to |
|---|---|
| `ferramentas/bateria.py:25` | `C:/Users/seraf/...` + `E:/Sktch[/BATERIA DE TESTES` |
| `ferramentas/projetos.py:30` | machine-relative DOWNLOADS (BASE itself derives from `__file__`) |
| `ferramentas/testados.py:11` | `C:/Users/seraf/...` |
| `ferramentas/provar-monitor.py:25` | `C:/Users/seraf/...` |
| `ferramentas/extrair-svg/*.py` (51 files) | `C:/Users/seraf/...` |
| `bibliotecas/PlayerDAP/src/dap_biblioteca.c:668` | `E:\Musicas` (comment only) |

---

## 5. Things the 16 Oct audit flagged that are now fixed or not reproducible

| Item from 16 Oct | Status on 17 Oct |
|---|---|
| Slide-switch not an SPDT (70/12 in the recorded block) | **Fixed** — explicit case carrying the level with `LevarNivel` (`EditorCircuito.cs:5672-5684`); the block passes **82/0** live [V]. One leftover: the old `Comportamentos.cs:162` table row `Digitais=["1","2","3"]` is now **dead code** that would reintroduce the bug if the explicit case ever goes away [V] |
| Library downloads: redirect gap (`AllowAutoRedirect`) | **Fixed** — `BibliotecasOnline.cs:250-252` re-checks the final URI against the allow-list after the redirect [V] |
| README Ω `#333333` on `#2D2D30` | **Not reproducible** — no `#2D2D30` exists anywhere; `Tema.cs:139` uses `#333333` as a selection background on `#000000`, not text-on-tone [A] |
| 1-channel relay "missing from catalogue" | Confirmed present (`modulo-rele-1/2/4` in `pecas.json`) — but see P2#12 for the stale generator [V] |
| Zip-slip, SHA-256 pinning, `setInsecure`, pipe ACL, in-process native C | Re-verified as **still as described** (good pinning; the gaps are the ones in §2.8/§2.9 and §3#13) |

---

## 6. Incomplete-feature registry (named refusals, counted in the audits)

The project's pattern is to refuse with a line number instead of silently no-op, which the audits
counted as 78+ named throw sites in `motor/` alone. Highlights:

- **C/C++**: ArduinoJson (`deserializeJson`), `http.getStream()`, I2C slave mode, nested
  C++/Python classes inside functions, Adafruit_GFX custom fonts, `try/catch/throw/goto`,
  `std::stoi` out-of-range path (throws a raw .NET message instead of the board's C++
  wording-and-abort — §4 of `Interpretador.cs`).
- **MicroPython**: `yield`, `async/await`, nested `def/class`, `*args/**kwargs`, `urequests`, and
  long tails of `machine.*`/`time.*`/`network.*`/`json.*`/`math.*`/`random.*`/`WLAN.*`.
- **BLE**: implemented for the classic ESP32 central/peripheral surfaces; many methods still
  refuse (`InterpretadorBleCliente.cs` 7 sites, `InterpretadorArduinoBle.cs` 6).
- **Rust/Pascal**: lexers/parsers exist; coverage is not Arduino-like. Expect quick refusals.
- **README**: `Marco 2` still claims macros are object-like only (function macros are
  implemented); `README.md:126` claims network is out of scope (Marco 40 ships it); `README.md:2344`
  claims BLE is missing (files + tests exist).

---

## 7. Suggested order of work

1. **Kill the two process-death paths** (§1.2 `ChamarRotina` guard; §1.3 depth-guard in `ParaJson`)
   — both ≈ 6 lines, both exercised by two-line sketches.
2. **`ponte.ps1`**: quote `$Sketch` inside the array + exit non-zero on missing exe; **`instalar.ps1`**:
   `GetFolderPath('MyDocuments')` + UTF-8 BOM for `caminho.txt` — closes the whole silent-IDE chain.
3. **`CarregarSketch`**: ask/preserve unsaved work, leave the dirty flag dirty for `.ino`; make the
   pipe loop survive exceptions; make save atomic (`File.Replace`) and turn the backup on.
4. **`bateria.py`**: repo-local default output (one line) — removes the E: footgun.
5. **Board semantics**: `(type)` cast widths (§2.5), `Serial.find()` failure function (§2.6),
   Timer2 wiring or rejection (§2.7), allocation caps (§2.10).
6. **Burn checks**: speaker on battery, relay/biaxial coils, maybe PWM peak currents (§2.11/2.12/P2#4).
7. **Policy default**: `UsarInternet = false` for the classroom build (menu can still enable) (§2.8).
8. Re-run the 38-project battery; close the 13 Oct recorded failures one by one (§2.13).
9. Refresh `README.md` (network/BLE/macro/relay-module claims) — all P3, all cheap.

---

## 8. Method limits

- Static review (no fuzzing of TCC, HTTP client, or the pipe protocol); two live artifacts only:
  the clean build and the 82/0 block. Numbers from the recorded battery are from **13 Oct** and
  were **not** re-run (see §2.13).
- Findings marked [A] carry exact line references but I did not personally re-read each one; every
  P0/P1 [A] item was quoted with code in the audit output that produced it.
- Flame/gas polarity (active-LOW vs Joy-IT HIGH-on-detect) remains a documented product decision —
  the kit datasheet conflict flagged by the previous audit still stands as a lab trap, not a bug.
- Where the previous audit and this one disagree (slide-switch, redirect pinning, README theme
  colors, relay module), **this report checked both claims against the current tree** and reports
  the current state only.

---

*GLM — independent audit, 17 October 2026. No files were modified and no scripts were executed
beyond the build and the single autotest block shown in "Session checks". Temporary audit
artifacts: `.artefatos/glm-auditoria/` (build + test output). Please delete after reviewing.*
