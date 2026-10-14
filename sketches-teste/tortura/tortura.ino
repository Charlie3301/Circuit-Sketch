/*
 * ============================================================
 *  AD174 (Kuongshun) - Controle RC via WiFi
 *  Placa: ESP32 DevKit V1 (comum) no lugar da ESP32-CAM original
 * ============================================================
 *  Carro 4WD do kit AD174 com driver L298N, controlado pelo
 *  navegador em layout paisagem estilo controle RC:
 *    - VELOCIDADE (frente/re)  -> manche vertical, desaceleracao gradual
 *    - DIRECAO (esquerda/dir.) -> slider horizontal autocentralizante
 *
 *  O AD174 e 4WD, mas o L298N tem 2 canais. Ligue em PARES:
 *    OUT1/OUT2 -> os DOIS motores do lado ESQUERDO (em paralelo)
 *    OUT3/OUT4 -> os DOIS motores do lado DIREITO  (em paralelo)
 *
 *  Ligacoes ESP32 V1  ->  L298N:
 *    ENA  -> GPIO 13   (PWM - velocidade lado ESQUERDO)   [tire o jumper do ENA]
 *    IN1  -> GPIO 12
 *    IN2  -> GPIO 14
 *    IN3  -> GPIO 27
 *    IN4  -> GPIO 26
 *    ENB  -> GPIO 25   (PWM - velocidade lado DIREITO)     [tire o jumper do ENB]
 *    GND  -> GND       (OBRIGATORIO: GND comum entre ESP32 e L298N)
 *  Alimentacao dos motores na entrada +12V/VMS do L298N (bateria do carro).
 *  NAO alimente a ESP32 pelos 5V do L298N se a bateria for alta; use USB/fonte propria.
 *
 *  WiFi (modo Access Point - a propria ESP32 cria a rede):
 *    SSID:  AD Carry
 *    Senha: AD174
 *
 *  Como usar:
 *    1. Grave este sketch (placa: "ESP32 Dev Module" / "DOIT ESP32 DEVKIT V1").
 *    2. No celular, conecte-se a rede WiFi "AD Carry" (senha AD174).
 *    3. Abra o navegador em  http://192.168.4.1  e gire o celular na horizontal.
 *
 *  Obs.: se algum lado girar invertido, troque IN1<->IN2 (ou IN3<->IN4)
 *        no codigo ou na fiacao. Para conectar a uma rede existente em vez
 *        de criar a propria, veja o bloco "MODO STA" mais abaixo.
 * ============================================================
 */

#include <WiFi.h>
#include <WebServer.h>

// ---------------- Configuracao WiFi ----------------
const char* AP_SSID     = "AD Carry";
const char* AP_PASSWORD = "AD174";   // minimo 8 caracteres recomendado p/ WPA2

// ---------------- Pinos da Ponte H (L298N) ----------------
const int ENA = 13;   // PWM lado ESQUERDO
const int IN1 = 12;
const int IN2 = 14;
const int IN3 = 27;
const int IN4 = 26;
const int ENB = 25;   // PWM lado DIREITO

// ---------------- Configuracao PWM ----------------
const int PWM_FREQ = 20000;   // 20 kHz (silencioso, fora da faixa audivel)
const int PWM_RES  = 8;       // 8 bits -> 0 a 255
const int PWM_MAX  = 255;

// Canais LEDC (necessario apenas no core ESP32 2.x)
const int CH_ENA = 0;
const int CH_ENB = 1;

// Zona morta: valores abaixo disso sao tratados como parado
const int DEADZONE = 25;

WebServer server(80);

// Guarda o ultimo comando recebido para watchdog de seguranca
unsigned long lastCommand = 0;
const unsigned long SAFETY_TIMEOUT = 800;  // ms sem comando -> para os motores

// Debug: imprime no Serial so quando os valores mudam (evita enxurrada)
const bool DEBUG_SERIAL = true;
int lastDbgSpeed = 999, lastDbgSteer = 999;

// ============================================================
//  PWM - compativel com ESP32 Arduino core 2.x e 3.x
// ============================================================
void pwmSetup() {
#if ESP_ARDUINO_VERSION >= ESP_ARDUINO_VERSION_VAL(3, 0, 0)
  ledcAttach(ENA, PWM_FREQ, PWM_RES);
  ledcAttach(ENB, PWM_FREQ, PWM_RES);
#else
  ledcSetup(CH_ENA, PWM_FREQ, PWM_RES);
  ledcSetup(CH_ENB, PWM_FREQ, PWM_RES);
  ledcAttachPin(ENA, CH_ENA);
  ledcAttachPin(ENB, CH_ENB);
#endif
}

void pwmWriteA(int duty) {
#if ESP_ARDUINO_VERSION >= ESP_ARDUINO_VERSION_VAL(3, 0, 0)
  ledcWrite(ENA, duty);
#else
  ledcWrite(CH_ENA, duty);
#endif
}

void pwmWriteB(int duty) {
#if ESP_ARDUINO_VERSION >= ESP_ARDUINO_VERSION_VAL(3, 0, 0)
  ledcWrite(ENB, duty);
#else
  ledcWrite(CH_ENB, duty);
#endif
}

// ============================================================
//  Controle dos motores
//  motor A/B: velocidade de -255 (re) a +255 (frente)
// ============================================================
void driveMotorA(int v) {          // motor esquerdo
  if (v > 0) { digitalWrite(IN1, HIGH); digitalWrite(IN2, LOW); }
  else if (v < 0) { digitalWrite(IN1, LOW); digitalWrite(IN2, HIGH); }
  else { digitalWrite(IN1, LOW); digitalWrite(IN2, LOW); }   // freio/parado
  pwmWriteA(abs(v));
}

void driveMotorB(int v) {          // motor direito
  if (v > 0) { digitalWrite(IN3, HIGH); digitalWrite(IN4, LOW); }
  else if (v < 0) { digitalWrite(IN3, LOW); digitalWrite(IN4, HIGH); }
  else { digitalWrite(IN3, LOW); digitalWrite(IN4, LOW); }
  pwmWriteB(abs(v));
}

void stopMotors() {
  driveMotorA(0);
  driveMotorB(0);
}

// Mistura velocidade + direcao (differential drive) e aciona os motores
void applyControl(int speed, int steer) {
  // speed e steer chegam de -255 a +255
  int left  = speed + steer;
  int right = speed - steer;

  // Limita a faixa valida
  left  = constrain(left,  -PWM_MAX, PWM_MAX);
  right = constrain(right, -PWM_MAX, PWM_MAX);

  // Zona morta -> evita zumbido do motor parado
  if (abs(left)  < DEADZONE) left  = 0;
  if (abs(right) < DEADZONE) right = 0;

  driveMotorA(left);
  driveMotorB(right);

  // ---- Debug no Serial (so quando os inputs mudam) ----
  if (DEBUG_SERIAL && (speed != lastDbgSpeed || steer != lastDbgSteer)) {
    lastDbgSpeed = speed;
    lastDbgSteer = steer;
    Serial.printf(
      "[INPUT] Velocidade: %4d (%-5s) | Direcao: %4d (%-9s)  ->  Motor Esq: %4d | Motor Dir: %4d\n",
      speed, speed > 0 ? "FRENTE" : (speed < 0 ? "RE" : "PARADO"),
      steer, steer > 0 ? "DIREITA" : (steer < 0 ? "ESQUERDA" : "CENTRO"),
      left, right);
  }
}

// ============================================================
//  Pagina web (HTML + CSS + JS embutidos)
// ============================================================
const char PAGE_HTML[] PROGMEM = R"HTML(
<!DOCTYPE html>
<html lang="pt-br">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
<title>AD Carry - Controle</title>
<style>
  :root {
    --bg: #0e1116;
    --panel: #171c24;
    --accent: #ffb400;
    --accent2: #35d0a5;
    --text: #e8edf2;
    --muted: #8a97a6;
    --track: #262d38;
  }
  * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
  html, body {
    margin: 0; height: 100%;
    background: radial-gradient(circle at 50% 0%, #1a2029, var(--bg));
    color: var(--text);
    font-family: -apple-system, Segoe UI, Roboto, sans-serif;
    overflow: hidden; touch-action: none;
  }
  /* ---- Layout PAISAGEM estilo controle RC ---- */
  .rc {
    height: 100%; display: flex; flex-direction: row;
    align-items: stretch; gap: 12px; padding: 12px;
  }
  .side {
    flex: 1; background: var(--panel); border-radius: 18px;
    box-shadow: 0 6px 20px rgba(0,0,0,.35);
    display: flex; flex-direction: column; justify-content: center;
    padding: 12px 16px; min-width: 0;
  }
  .center {
    flex: 0 0 32%; display: flex; flex-direction: column;
    justify-content: center; gap: 12px; text-align: center; min-width: 0;
  }
  header h1 {
    margin: 0; font-size: 20px; letter-spacing: 2px;
    color: var(--accent); text-transform: uppercase;
  }
  header p { margin: 2px 0 0; font-size: 11px; color: var(--muted); }
  .ctrl label {
    display: flex; justify-content: space-between; align-items: baseline;
    font-size: 13px; color: var(--muted); margin-bottom: 10px;
    text-transform: uppercase; letter-spacing: 1px;
  }
  .ctrl .val { font-size: 18px; font-weight: 700; color: var(--text); }
  .ctrl .val.speed { color: var(--accent2); }
  .ctrl .val.steer { color: var(--accent); }
  .ctrl .val.ramp  { color: var(--text); }

  input[type=range] {
    -webkit-appearance: none; appearance: none;
    width: 100%; height: 46px; background: transparent; margin: 0;
  }
  /* trilho */
  input[type=range]::-webkit-slider-runnable-track {
    height: 14px; border-radius: 10px;
    background: var(--track);
    border: 1px solid #313a47;
  }
  input[type=range]::-moz-range-track {
    height: 14px; border-radius: 10px; background: var(--track);
    border: 1px solid #313a47;
  }
  /* marca central (sliders horizontais autocentralizantes) */
  .slider-box { position: relative; }
  .slider-box.centered::after {
    content: ""; position: absolute; top: 50%; left: 50%;
    width: 2px; height: 26px; margin-top: -13px; margin-left: -1px;
    background: #4a5666; border-radius: 2px; pointer-events: none;
  }
  /* polegar */
  input[type=range]::-webkit-slider-thumb {
    -webkit-appearance: none; appearance: none;
    width: 40px; height: 40px; margin-top: -14px; border-radius: 50%;
    background: radial-gradient(circle at 35% 30%, #fff, var(--accent));
    border: 3px solid #0e1116;
    box-shadow: 0 3px 8px rgba(0,0,0,.5); cursor: pointer;
  }
  input[type=range]::-moz-range-thumb {
    width: 40px; height: 40px; border-radius: 50%;
    background: var(--accent); border: 3px solid #0e1116;
    box-shadow: 0 3px 8px rgba(0,0,0,.5); cursor: pointer;
  }

  /* ---- Slider VERTICAL de velocidade (frente/re) ---- */
  .speed-area {
    flex: 1; display: flex; flex-direction: column;
    align-items: center; justify-content: center; gap: 6px; min-height: 0;
  }
  .speed-area .cap { font-size: 11px; color: var(--muted); letter-spacing: 1px; }
  .vbox {
    position: relative; width: 46px; height: 210px;
  }
  .vbox input[type=range] {
    position: absolute; top: 50%; left: 50%;
    width: 210px; height: 46px;                 /* vira o comprimento apos girar */
    transform: translate(-50%, -50%) rotate(-90deg);  /* max (255) no topo */
  }
  .vbox input[type=range]::-webkit-slider-thumb {
    background: radial-gradient(circle at 35% 30%, #fff, var(--accent2));
  }
  .vbox input[type=range]::-moz-range-thumb { background: var(--accent2); }
  /* marca central horizontal do slider vertical */
  .vbox::after {
    content: ""; position: absolute; top: 50%; left: 50%;
    width: 26px; height: 2px; margin-left: -13px; margin-top: -1px;
    background: #4a5666; border-radius: 2px; pointer-events: none;
  }

  .status {
    text-align: center; font-size: 12px; color: var(--muted);
  }
  .dot { display:inline-block; width:9px; height:9px; border-radius:50%;
         background:#e5484d; margin-right:6px; vertical-align:middle; }
  .dot.on { background: var(--accent2); }

  .stopbtn {
    margin-top: 4px; padding: 14px; border: none; border-radius: 14px;
    background: #e5484d; color: #fff; font-size: 16px; font-weight: 700;
    letter-spacing: 1px; cursor: pointer;
  }
  .stopbtn:active { transform: scale(.98); }

  /* Aviso para girar o celular (aparece so em retrato) */
  .rotate-hint {
    display: none; position: fixed; inset: 0; z-index: 50;
    background: var(--bg); color: var(--text);
    flex-direction: column; align-items: center; justify-content: center;
    gap: 16px; text-align: center; padding: 30px;
  }
  .rotate-hint .icon { font-size: 54px; animation: spin 2.5s ease-in-out infinite; }
  .rotate-hint p { margin: 0; font-size: 16px; color: var(--muted); }
  @keyframes spin { 0%,60%{transform:rotate(0)} 80%,100%{transform:rotate(-90deg)} }
  @media (orientation: portrait) {
    .rotate-hint { display: flex; }
  }
</style>
</head>
<body>
  <div class="rc">

    <!-- ESQUERDA: manche de velocidade (vertical) -->
    <div class="side">
      <div class="ctrl" style="text-align:center; flex:1; display:flex; flex-direction:column;">
        <label style="justify-content:center; gap:8px">Velocidade <span id="speedVal" class="val speed">0</span></label>
        <div class="speed-area">
          <span class="cap">▲ FRENTE</span>
          <div class="vbox">
            <input type="range" id="speed" min="-255" max="255" value="0">
          </div>
          <span class="cap">▼ RÉ</span>
        </div>
      </div>
    </div>

    <!-- CENTRO: titulo, ajuste e parada -->
    <div class="center">
      <header>
        <h1>AD Carry</h1>
        <p>AD174 · ESP32 V1 · L298N</p>
      </header>

      <div class="ctrl">
        <label style="justify-content:center; gap:8px">Freio ao soltar <span id="rampVal" class="val ramp">0.6 s</span></label>
        <div class="slider-box">
          <input type="range" id="ramp" min="1" max="80" value="6">
        </div>
      </div>

      <button class="stopbtn" id="stop">PARAR</button>

      <div class="status">
        <span id="dot" class="dot"></span><span id="statusText">Conectando...</span>
      </div>
    </div>

    <!-- DIREITA: direcao (horizontal) -->
    <div class="side">
      <div class="ctrl">
        <label style="justify-content:center; gap:8px">Direção <span id="steerVal" class="val steer">0</span></label>
        <div style="display:flex; justify-content:space-between; font-size:11px; color:var(--muted); letter-spacing:1px; margin-bottom:6px;">
          <span>◀ ESQ</span><span>DIR ▶</span>
        </div>
        <div class="slider-box centered">
          <input type="range" id="steer" min="-255" max="255" value="0">
        </div>
      </div>
    </div>

  </div>

  <!-- Aviso para girar o celular -->
  <div class="rotate-hint">
    <div class="icon">📱</div>
    <p>Gire o celular na horizontal<br>para usar o controle</p>
  </div>

<script>
  const speed = document.getElementById('speed');
  const steer = document.getElementById('steer');
  const ramp  = document.getElementById('ramp');
  const speedVal = document.getElementById('speedVal');
  const steerVal = document.getElementById('steerVal');
  const rampVal  = document.getElementById('rampVal');
  const dot = document.getElementById('dot');
  const statusText = document.getElementById('statusText');

  let dirty = true;
  let draggingSpeed = false;    // enquanto o dedo/mouse segura o slider de velocidade
  const MAXV = 255;
  const DECAY_MS = 30;          // periodo da desaceleracao

  function updateLabels() {
    speedVal.textContent = speed.value;
    steerVal.textContent = steer.value;
    dirty = true;
  }
  function updateRampLabel() {
    // valor do slider (1..80) = tempo em decimos de segundo para parar do maximo
    rampVal.textContent = (ramp.value / 10).toFixed(1) + ' s';
  }

  speed.addEventListener('input', updateLabels);
  steer.addEventListener('input', updateLabels);
  ramp.addEventListener('input', updateRampLabel);
  updateRampLabel();

  // ---- Velocidade: enquanto arrasta, sem desaceleracao ----
  ['pointerdown','mousedown','touchstart'].forEach(ev =>
    speed.addEventListener(ev, () => { draggingSpeed = true; }));
  // ao soltar, apenas libera a desaceleracao (nao zera de imediato)
  ['pointerup','pointercancel','pointerleave','touchend','touchcancel','mouseup','mouseleave']
    .forEach(ev => speed.addEventListener(ev, () => { draggingSpeed = false; }));

  // ---- Direcao: autocentraliza no centro imediatamente ----
  function centerSteer() { steer.value = 0; updateLabels(); }
  ['pointerup','pointercancel','pointerleave','touchend','touchcancel','mouseup','mouseleave']
    .forEach(ev => steer.addEventListener(ev, centerSteer));

  // ---- PARAR: emergencia, zera tudo na hora ----
  document.getElementById('stop').addEventListener('click', () => {
    draggingSpeed = false;
    speed.value = 0; steer.value = 0;
    updateLabels();
  });

  // ---- Loop de desaceleracao gradual da velocidade ----
  setInterval(() => {
    if (draggingSpeed) return;              // segurando o slider: mantem o valor
    let v = +speed.value;
    if (v === 0) return;
    // passo por tick = MAXV / (tempoTotal / DECAY_MS); tempoTotal = ramp/10 s
    const totalMs = (ramp.value / 10) * 1000;
    const step = Math.max(1, MAXV / (totalMs / DECAY_MS));
    if (v > 0) v = Math.max(0, v - step);
    else       v = Math.min(0, v + step);
    speed.value = Math.round(v);
    updateLabels();
  }, DECAY_MS);

  // ---- Envio periodico do comando ao ESP32 ----
  async function sendLoop() {
    dirty = false;
    try {
      const r = await fetch('/control?speed=' + speed.value + '&steer=' + steer.value,
                            { cache: 'no-store' });
      if (r.ok) {
        dot.classList.add('on');
        statusText.textContent = 'Conectado';
      } else {
        throw new Error('bad');
      }
    } catch (e) {
      dot.classList.remove('on');
      statusText.textContent = 'Sem conexão';
    }
  }

  // envia a cada 100 ms (mantem o watchdog do ESP32 alimentado)
  setInterval(sendLoop, 100);
</script>
</body>
</html>
)HTML";

// ============================================================
//  Handlers do servidor
// ============================================================
void handleRoot() {
  server.send_P(200, "text/html", PAGE_HTML);
}

void handleControl() {
  int speed = 0, steer = 0;
  if (server.hasArg("speed")) speed = server.arg("speed").toInt();
  if (server.hasArg("steer")) steer = server.arg("steer").toInt();

  speed = constrain(speed, -PWM_MAX, PWM_MAX);
  steer = constrain(steer, -PWM_MAX, PWM_MAX);

  applyControl(speed, steer);
  lastCommand = millis();

  server.send(200, "text/plain", "ok");
}

void handleNotFound() {
  server.send(404, "text/plain", "404");
}

// ============================================================
//  Setup / Loop
// ============================================================
void setup() {
  Serial.begin(115200);

  pinMode(IN1, OUTPUT); pinMode(IN2, OUTPUT);
  pinMode(IN3, OUTPUT); pinMode(IN4, OUTPUT);
  pwmSetup();
  stopMotors();

  // ---------- MODO AP (padrao): ESP32 cria a rede ----------
  WiFi.mode(WIFI_AP);
  // WPA2 exige senha de 8 a 63 caracteres. "AD174" tem 5, entao a rede
  // sobe ABERTA (sem senha). Para rede protegida, use 8+ caracteres
  // (ex.: "AD174car"). O nome continua "AD Carry" nos dois casos.
  bool ok;
  if (strlen(AP_PASSWORD) >= 8) {
    ok = WiFi.softAP(AP_SSID, AP_PASSWORD);
  } else {
    ok = WiFi.softAP(AP_SSID);   // rede aberta (senha curta demais p/ WPA2)
    Serial.println("[AVISO] Senha < 8 caracteres: rede criada SEM senha.");
  }
  IPAddress ip = WiFi.softAPIP();
  Serial.println();
  Serial.print("Rede criada: "); Serial.print(AP_SSID);
  Serial.println(ok ? "" : "  (FALHA ao iniciar AP!)");
  Serial.print("Acesse:       http://"); Serial.println(ip);

  /* ---------- MODO STA (opcional) ----------
     Para conectar a uma rede WiFi existente em vez de criar a propria,
     comente o bloco "MODO AP" acima e descomente este:

  WiFi.mode(WIFI_STA);
  WiFi.begin(AP_SSID, AP_PASSWORD);
  Serial.print("Conectando");
  while (WiFi.status() != WL_CONNECTED) { delay(300); Serial.print("."); }
  Serial.print("\nIP: "); Serial.println(WiFi.localIP());
  -------------------------------------------- */

  server.on("/", handleRoot);
  server.on("/control", handleControl);
  server.onNotFound(handleNotFound);
  server.begin();
  Serial.println("Servidor web iniciado.");
}

void loop() {
  server.handleClient();

  // Watchdog de seguranca: se o navegador parar de enviar comandos
  // (perda de conexao, aba fechada), os motores param sozinhos.
  static bool wdStopped = false;
  if (millis() - lastCommand > SAFETY_TIMEOUT) {
    if (!wdStopped) {
      stopMotors();
      wdStopped = true;
      lastDbgSpeed = lastDbgSteer = 999;   // forca reimpressao no proximo comando
      if (DEBUG_SERIAL) Serial.println("[WATCHDOG] Sem comando -> motores PARADOS.");
    }
  } else {
    wdStopped = false;
  }
}
