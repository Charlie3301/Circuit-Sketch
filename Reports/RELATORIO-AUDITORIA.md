# CircuitSketch — audit report

**Product:** CircuitSketch (offline ESP32 / Arduino simulator)  
**Tree:** `C:\Users\seraf\OneDrive\Desktop\SimuladorESP32`  
**Date:** 16 October 2026  
**Scope:** static review of `app/`, `motor/`, `ferramentas/`, `plataforma/`, `coordenacao/`, and known test artefacts. This is not a full suite run.

## How this was done

- Read architecture and roadmap (`README.md`, `coordenacao/PLANO-ALPHAS.md`).
- Searched for stubs, “ainda nao e suportado”, empty catches, process/network/file I/O, zip extraction, native TCC.
- Cross-checked agent reports (`coordenacao/chatgpt/RELATORIO-00{3,4}.md`, `coordenacao/antigravity/RELATORIO-002.md`) and `coordenacao/resultados-projetos.json` (13 Oct 2026).
- Read the actual implementations behind those failures (energy, sensors, HX711, MPU, L298N, ESP-NOW, native C, HTTP, libraries).

**Not done on purpose:** the full `--teste` suite (~5 minutes, opens real windows) and `Projetos_Testados`. Those collide with a CircuitSketch instance the user may already have open. Findings marked “observed in code” vs “observed in recorded tests” are labelled as such.

Severity:

| Tag | Meaning |
|---|---|
| **P0** | Host compromise, data leaving the machine without a clear classroom-safe default, or crash of the whole process |
| **P1** | Simulator disagrees with the board in a way that will fail a real lab circuit or a recorded project test |
| **P2** | Incomplete language/API, stale docs, or a glitch that is annoying but fenced with an error message |
| **P3** | Roadmap / platform debt (Windows-only, i18n, installer) |

---

## Executive summary

CircuitSketch is a large, deliberate simulator: tree-walk interpreter, optional TinyCC native C, DC energy solver, burn checker, WPF bench, Arduino IDE hook. A lot of the “dangerous” surfaces already have tests (zip path escape, SHA-256 of Arduino library zips, Guru Meditation on native faults).

The remaining problems cluster in four places:

1. **Hardware fidelity bugs** that the 38-project battery already caught (MPU sketches print nothing, SPDT slide switch, IR “burn”, HX711 offset, ESP-NOW release, RC servo vs ESC, L298N PWM).
2. **Host trust:** native C runs as x64 in-process; HTTP from a sketch can use the PC’s internet (on by default); a named pipe accepts a sketch path from any local process.
3. **Subset interpreters** (C++/Python/Rust/Pascal/MicroPython) that refuse whole language features instead of running them — correct for honesty, incomplete for “drop any GitHub sketch”.
4. **Docs and roadmap lag the code** (`README` still says BLE and `configTime` are missing; `PLANO-ALPHAS` still says the 1-channel relay is not in the catalogue).

Recorded project battery on this tree: **11 of 38 failed** on 13 Oct 2026 (`coordenacao/resultados-projetos.json`). An Antigravity worktree later claimed 46/46 after generating extra Alpha 24 projects; that result is **not** the snapshot in this folder.

---

## 1. Bugs and hardware mismatches (P1)

### 1.1 Slide switch is not an SPDT (code + failing test)

Wokwi / real module: pin **2 is common**; left closes 1–2, right closes 2–3.

Energy already models that:

```247:249:app/EditorMotores.cs
                case "slide-switch":
                    yield return new Energia.Contato(peca.Id, "2", peca.Giro > 0.5 ? "3" : "1", 0.3);
                    break;
```

The per-frame input path then treats pins 1, 2 and 3 as three independent digitals driven by `Giro > 0.5`, so the common pin never inherits GND / 3V3 from the contacts:

```162:162:app/Comportamentos.cs
        ["slide-switch"] = new() { Digitais = ["1", "2", "3"], Chave = true, Rotulo = "Ligada" },
```

```5989:5998:app/EditorCircuito.cs
        foreach (string pino in comp.Digitais)
        {
            ...
            bool detectou = peca.Giro > comp.Limiar;
            maq.NivelExterno[gpio] = comp.DigitalInvertido
                ? (detectou ? 0 : 1)
                : (detectou ? 1 : 0);
        }
```

`Comportamento_EntradasESensores` expects 0 / 1 / 0 for left–right–left with 1=GND and 3=3V3 (`app/AutotesteComportamentoEntradas.cs` lines 106–116). ChatGPT recorded that block as **70 passed, 12 failed**, including all three slide-switch cases (`coordenacao/chatgpt/RELATORIO-004.md`). The electrical contact path and the GPIO path disagree.

**Fix direction:** skip `Digitais` for `slide-switch` (and any other true contact) and let `LevarNivel` / energy contacts drive the GPIO, the same way `micro-switch` already does.

### 1.2 Flame / gas polarity vs kit docs (documented choice, still a lab trap)

Sound modules (KY-037 / KY-038) were aligned to Joy-IT: `DigitalInvertido = false` (HIGH above threshold). Flame and gas stay active-LOW (`DigitalInvertido = true`), with a comment that they model generic LM393 boards, not KY-026.

Joy-IT KY-026 is HIGH on flame. A student copying that datasheet will invert every `if (digitalRead)`. The current autotest **expects** the inverted polarity, so the suite will stay green while the kit in the drawer disagrees.

### 1.3 Adafruit MPU6050 projects print nothing (recorded tests)

From `coordenacao/resultados-projetos.json`:

| Project | Symptom |
|---|---|
| `drone-quadricoptero` | Monitor empty; motors 0 rpm / 0 gf |
| `nivel-mpu6050` | Monitor empty; LED stays off when “inclined” |

`mpu.begin()` is implemented (WHO_AM_I vs DS1307 at 0x68). `getEvent(&a,&g,&temp)` is implemented and converts LSB to m/s². The catalogue grandeza for the part is **only `mpu:ax`**. If `begin()` fails (wrong I2C net, library C++ the interpreter cannot parse, or `if (!mpu.begin()) return;`), setup never prints — which matches the empty monitor.

`getEvent` also requires the argument after `&` to be a simple variable (`EventoDoMpu` in `motor/Interpretador.cs`). A member or temporary would silently skip filling the event.

### 1.4 HX711 empty-cell offset (~201 g) (recorded test + formula)

`balanca-hx711-lcd`: LCD showed **701 g** instead of 500 g, then **1451 g** instead of 1250 g (constant +201 g).

```3713:3719:motor/Interpretador.cs
    /// A contagem crua do HX711: o zero da celula mais 420 contagens por
    /// grama ...
    long ContagemDoHx711() => (long)Math.Round(84213 + maq.Leitura("hx711:peso") * 420);
```

`84213 / 420 ≈ 200.5`. That offset is only removed if the sketch `tare()`s on an empty cell. If the roteiro already has mass on the cell when `tare()` runs, or never tares, every reading is high by ~201 g. Either the project wiring/script is wrong (type A) or tare is applied after the simulated mass is already loaded (type B). The number matches the formula, not a random ADC error.

### 1.5 IR remote project “burns” Arduino pin 3 (recorded test)

`controle-ir-leds`: first check fails with  
`Arduino Uno queimou: curto: o pino 3 está em LOW e ligado direto na alimentação`.

IRremote on AVR uses Timer2, which owns **pin 3** (OC2B) even for receive-only sketches. Queima treats “GPIO LOW tied to a supply rail” as a dead short. On a real Uno that pin is a timer output, not a stuck LOW to VCC. Result: the LEDs never light in the test, and a student sees a burn that would not happen on the bench.

Queima itself is honest about being a **netlist heuristic**, not a current solver (`app/Queima.cs` header). This is the false-positive class that heuristic was built to catch — and it still fires on a common library.

### 1.6 ESP-NOW: press works, release does not (recorded test)

`espnow-botao-led`: “recebi: apertado” and LED on; **release leaves the LED on**. First packet is delivered; the second (button up) is not applied. Matches `PLANO-ALPHAS.md`. Likely callback / queue timing in `motor/RadioNaRede.cs` + `Interpretador.cs` (callbacks run after the call, not between pulses — also documented in the README for `shiftOut`).

### 1.7 RC car: ESC follows radio, servo does not (recorded test)

`carrinho-rc-sem-placa`: throttle → 8828 rpm (OK); steering stays at **93°** for left and right. The receiver PWM path that the ESC uses is not the path the servo uses (or the servo is not sampling the same channel). `carrinho-rc-arduino` then reports `sem sinal do radio` even with the receiver on Nano **5V.2**.

Internal copper for `5V` / `5V.2` **is** wired on purpose:

```394:397:app/Catalogo.cs
        // Na placa, os pinos repetidos (5V e 5V.2, ... ) sao a mesma trilha
        // de cobre: o receptor no 5V.2 de um Nano alimentado pelo 5V recebe
        // os mesmos 5 V.
```

So “5V.2 is a different net” is probably **not** the cause anymore. Remaining suspects: pulse capture on that GPIO, receiver model, or the Nano sketch never seeing `pulseIn`.

### 1.8 Rovers (recorded tests)

- `rover-4wd`: “re” check saw `monitor: parado | frente` — substring **“re” inside “frente”** (type A, already noted in `TAREFA-002.md`). Motors also did not reverse for reverse (type B).
- `rover-4wd-hc-sr04`: PWM 200 on one enable vs full on the other did not split speed as expected (`PLANO-ALPHAS`). L298N enable PWM is modelled (`DriversDeMotor.ConducaoDe`); if both channels still match, the sketch or the enable wiring in the generated project is wrong, or PWM is not reaching ENB in `EnergiaPwm`.
- `rover-4wd-reles`: generator still uses bare **KS2E** coils from GPIO to 5 V because it thinks the 1-channel module does not exist (`ferramentas/projetos.py` ~684). The catalogue **does** have `modulo-rele-1`. Coil current ~70 mA vs Uno 40 mA, and “forward is reversed”, follow from that outdated circuit, not from the new module.

### 1.9 Fan + L298N (`ventilador-l298n`)

Pot low → **0 rpm**; pot high → 4453 rpm. Low PWM is treated as off rather than slow. Possible causes: enable jumper vs PWM threshold, or analogWrite not reaching ENA at small duty.

### 1.10 Interrupt timing vs real silicon (documented)

`attachInterrupt` now exists (README “not simulated” is stale). Remaining glitch, still true in README:

> Edge ISRs run **after** the call that produced the edges, not between pulses. A `shiftOut` fires eight edges, then the ISR sees the **final** pin state.

That will break bit-bang protocols that sample inside the ISR.

---

## 2. Security and host exposure

Classroom context: the threat is a student sketch, a downloaded `.zip` library, or another user on the same PC — not a remote anonymous attacker. Defaults still matter.

### 2.1 Native C is in-process x64 (P0 if untrusted C is in scope)

`motor/Nativo/ModuloNativo.cs` compiles student `.c` with TinyCC (`libtcc.dll`) to **memory**, then runs it on host threads (FreeRTOS task = OS thread). Mitigations that are real:

- `-nostdinc -nostdlib`, `#undef _WIN32` / `_WIN64` so libraries do not take the Windows path.
- File I/O is a VFS over the simulated SD / flash (`NativoArquivos.cs`), not `CreateFile` on the PC.
- SEH / exception trampoline aims to turn bad pointers into “Guru Meditation” instead of killing WPF.
- Host calls go through a fixed `cs_host` table (`HostNativo.cs`).

What remains: this is still **native code in the CircuitSketch process**. A determined `#include` of a leaked Windows header, inline syscall, or TCC codegen bug is a process-level sandbox escape. Wokwi’s WASM story is stronger here. For a closed lab where every `.c` is the student’s own, this is a crash/integrity risk more than remote RCE.

TCC also has a known sticky-global state; a failed compile **reloads the DLL** (`motor/Nativo/Tcc.cs`). That is a reliability fix, not a security one.

### 2.2 Sketch HTTP uses the computer’s network (P1, default on)

```63:63:motor/Rede.cs
    public bool UsarInternet { get; set; } = true;
```

After bench-to-bench routing, `HTTPClient` on the ESP32 becomes a real `System.Net.Http.HttpClient` to arbitrary `http`/`https` URLs (`Rede.Requisitar`). There is a menu toggle (“Rede usa a internet do computador”) and `setInsecure()` **disables TLS certificate checks** (`ClienteInseguro`).

Impact in a school: a sketch can exfiltrate files the student pasted into a payload, hit intranet URLs, or ignore HTTPS authenticity. Off by default would match the “offline simulator” pitch in `README.md`.

### 2.3 Online libraries: good pinning, redirect gap (P2)

`BibliotecasOnline.ConferirUrl` allows only `https://downloads.arduino.cc/` (default port, no userinfo), checks size and **SHA-256**, and re-checks the **final** URL after the response. Zip extract in `Acervo.Extrair` rejects paths that leave the destination and caps uncompressed size at 200 MB. Autotest installs a `../../fora.h` zip and expects refusal (`app/AutotesteCpp.cs`).

Gap: `JanelaBibliotecas` builds a default `HttpClient` **with redirects enabled**. Intermediate hosts on a 30x chain still see the request; only the last URL is pinned. `RELATORIO-003.md` already flags `AllowAutoRedirect = false` as the caller’s job. `Rede` does disable redirects; the library window does not.

Installing a user-picked `.zip` from disk is unrestricted besides path/size — expected, but that zip’s C++ can later run in the interpreter or TCC.

### 2.4 Named pipe loads any path the sender writes (P2)

Second instance sends the sketch path to `CircuitSketch_Canal`; the first instance calls `CarregarSketch` (`app/App.xaml.cs`). No auth, no ACL. On a shared lab PC another local process can push a path into the open window. Low sophistication, local-only.

### 2.5 Radio of the PC (`netsh` / PowerShell) (P2)

`RadioDoComputador` runs `netsh` and `Get-PnpDevice` to list real SSIDs. Can be disabled in settings and is forced off in Autoteste. When on, the app learns the room’s Wi-Fi names (privacy) and spawns OS processes. Arguments are fixed (no sketch-controlled shell). `AGENTS.md` forbids agents from turning Bluetooth on; the product itself may still query it.

### 2.6 What looks scary and is actually contained

| Topic | Status |
|---|---|
| Zip-slip | Guard + unit test |
| Arduino library download | Host allow-list + SHA-256 |
| SD / `fopen` in native mode | Simulated card, not host disk |
| `.sktch` load | `System.Text.Json` (not BinaryFormatter) |
| Empty `catch { }` | Mostly UI `Owner` / `Close` / kill-timeout; `EntregarParaInstanciaAberta` swallows IPC errors |

---

## 3. Incomplete language and APIs (P2)

The interpreter **refuses** unsupported calls with a line number. That is better than silent no-ops (the old `attachInterrupt` bug). It still means a random GitHub sketch will die early.

### C / Arduino (examples of remaining throws)

- Many `Serial.*`, `String.*`, `SD.*`, `WiFi.*`, `http.*`, `server.*`, `client.*` methods
- `http.getStream()` (workaround: `getString()`)
- `ArduinoJson` / `deserializeJson` (still refused; listed in README)
- I2C **slave** mode (`InterpretadorI2c.cs`)
- Nested C++ / Python classes inside functions
- TFT custom Adafruit_GFX fonts (`FreeSans…`)
- Macro functions **are** in `Preprocessador.Macro` now; README Marco 2 still says only object-like `#define`

### MicroPython (`ParserPython` / `InterpretadorMicroPython*`)

Supported enough for Alpha 17 demos: `class` at module level, `try/except`, `with`, `lambda` (claimed in `JanelaAbertura`), Timer/RTC/SPI subset.

Still refused: `yield`, `async`/`await`, nested `class`/`def`, `*args` / `**kwargs`, `urequests`, and a long tail of `machine.*`, `time.*`, `network.*`, `json.*`, `math.*`, `random.*`, `WLAN.*`.

### BLE

Code and `AutotesteBle*.cs` exist (`InterpretadorBle.cs`, `InterpretadorArduinoBle.cs`, `InterpretadorBleCliente.cs`). `README.md` “O que ainda falta” still says **BLE is not simulated**. Treat README as stale; treat unimplemented methods in those files as the real surface (many `ainda nao e suportado` throws).

### Other languages

Lexer/parser files exist for **Rust** and **Pascal**. They are not at Arduino/MicroPython coverage. Expect “not supported” quickly.

### Debugger (README, still accurate)

Cannot change values, cannot step over functions, breakpoints do not track line inserts above them.

---

## 4. Electrical model limits (P2)

`Energia` is a **lumped DC** (and PWM-averaged) resistor/source network with extra passes for H-bridges, regulators, and USB-less boards. Capacitors get a time step via `EnergiaTransitoria`; it is not SPICE.

`Queima` is a **connectivity heuristic** (shorts, LED without resistor, motor on a GPIO). It will both miss some real damage and flag some legal timer/IR pins (see 1.5).

AVR core documents what it does **not** model: pin rise time, noise, analog current (`motor/Avr.cs`). Cycle counts for the ISA are taken seriously; analog physics is not.

PWM on GPIO is averaged over the period (`Energia.Calcular` → `EnergiaPwm`). Fast digital protocols that depend on the exact high/low waveform of a motor enable will look like a DC voltage.

---

## 5. Incomplete product / roadmap (P3)

| Item | Evidence |
|---|---|
| Windows-only UI | WPF; `Circuito` uses `System.Windows.Point`. Linux/macOS = Alpha 26 (`PLANO-ALPHAS.md`) |
| Native exception path | Windows SEH; POSIX signals not implemented |
| Bluetooth of the PC | `PortaBluetooth.cs` Winsock `AF_BTH` |
| Arduino IDE Upload / discovery | Original README: Verify (✓) via postbuild; Upload needs a fake serial discovery tool. Later README says Upload works but IDE does not list the port — cause inside the IDE |
| 12 languages | `ferramentas/idiomas.py --conferir` in RELATORIO-003: English **9/33** for that batch; incomplete catalogues are hidden from the settings picker |
| Installer | Explicitly Alpha 26 only (`AGENTS.md`, `PLANO-ALPHAS.md`) |
| 1-channel relay in catalogue | **Present** (`app/assets/pecas.json`, `modulo-rele-1`). `PLANO-ALPHAS.md` and `projetos.py` comments still say it is missing |
| PCM5102A / MAX98357A | Implemented and tested (`AutotesteAudioEstereo`, player DAP). Listed as “next projects” in the Alpha 24 plan |

---

## 6. Glitches and UX traps

- **Single instance mutex** `CircuitSketch_Mutex`: a second Verify from the IDE sends the sketch over the pipe. If the first window is in a dialog or a test, the student thinks Verify did nothing (`catch` in `EntregarParaInstanciaAberta`).
- **Broken wires used to vanish**; `FiosQuebrados()` now warns. Opening old projects with `D16` vs `RX2` still needs that banner.
- **IR NEC:** holding a key used to retrigger every frame; now one code + 0xFFFFFFFF repeat. Old sketches that polled every loop may change behaviour.
- **Two compilations in one folder** break the build (`AGENTS.md`) — agent/process hazard, not a student one.
- **`python ferramentas/bateria.py` without args writes to `E:`** (professor originals). Operational footgun.
- **TCC globals:** a compile error poisons the next compile unless the DLL is reloaded (already coded).

---

## 7. Test debt

| Source | Result | Trust |
|---|---|---|
| `coordenacao/resultados-projetos.json` (13 Oct 2026) | 11 / 38 projects failed | High for this tree |
| `coordenacao/chatgpt/RELATORIO-004.md` | `Comportamento_EntradasESensores`: 70 / 12; slide-switch + flame/sound polarity | Tests for flame/sound were later **rewritten** to match the simulator; slide-switch expectation was **not** |
| `coordenacao/chatgpt/RELATORIO-003.md` | Biblioteca online tests 81/0 with fake HTTP; idiomas conferir failed | High |
| `coordenacao/antigravity/RELATORIO-002.md` | 46/46 in **another worktree** after regenerating `projetos/` | Do not treat as this folder’s status |
| Full `CircuitSketch.exe --teste` | Not re-run here | — |

`Autoteste.cs` is huge (16k+ lines) plus many `Autoteste*.cs` partials. Coverage is a strength. The remaining holes are **fidelity** (does the bench match the datasheet?), not “untested buttons”.

---

## 8. Suggested order of work

1. **Slide switch** — stop driving pins 1/2/3 as fake digitals; reuse the contact path (`P1`, small, test already written).
2. **MPU Adafruit path** — make `drone-quadricoptero` / `nivel-mpu6050` print; if `begin()` fails, show it on the serial bar (`P1`).
3. **HX711 tare vs loaded cell** in `balanca-hx711-lcd` (`P1`).
4. **Queima vs Timer2 / pin 3** on Uno IRremote (`P1`).
5. **ESP-NOW second packet** (`P1`).
6. **RC servo channel** and Nano receiver (`P1`).
7. **Default `UsarInternet = false`** for a school image (`P1` policy).
8. **`HttpClient` in `JanelaBibliotecas`:** `AllowAutoRedirect = false` (`P2`).
9. **Refresh README / PLANO-ALPHAS** so BLE, interrupts, `configTime`, relay module, PCM5102A match the tree (`P3`, cheap).
10. **Regenerate rover-reles** with `modulo-rele-1` instead of GPIO-driven KS2E (`P2`).

---

## 9. Method limits

This report is a **code and artefact audit**. It does not claim every Autoteste block is green today, and it does not fuzz TCC or the HTTP client. Numbers for project failures are from 13 Oct 2026 unless noted. Polarities of flame/gas are a product decision, not an uncaught exception.

If you want a follow-up, the highest-signal experiment is:

```text
dotnet build app/App.csproj -nologo -v q --artifacts-path .artefatos/auditoria
.artefatos\auditoria\bin\App\debug\CircuitSketch.exe --teste --so Comportamento_EntradasESensores --saida coordenacao/auditoria-entradas.txt
.artefatos\auditoria\bin\App\debug\CircuitSketch.exe --teste --so Projetos_Testados --saida coordenacao/auditoria-projetos.txt
```

(Do not run those while a student window is open.)
