// Extrai TODAS as pecas dos wokwi-elements para app/assets:
//   <id>.svg      desenho da peca
//   pecas.json    catalogo com tamanho e posicao dos pinos
//
// Os elements sao componentes Lit: o SVG so existe depois de renderizar.
// Montamos um DOM com jsdom, instanciamos cada elemento e serializamos o
// shadow root. pacote.js e o bundle do esbuild (os fontes usam imports
// sem extensao, que o resolvedor ESM do Node recusa).
import { JSDOM } from 'jsdom';
import { writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const dom = new JSDOM('<!doctype html><html><body></body></html>', {
  pretendToBeVisual: true,
  url: 'http://localhost/',
});

// Injeta tudo que o jsdom expoe e que ainda nao existe no Node.
for (const chave of Object.getOwnPropertyNames(dom.window)) {
  if (chave in globalThis) continue;
  try { globalThis[chave] = dom.window[chave]; } catch { /* somente leitura */ }
}
// O Node ja traz Event/CustomEvent proprios. Se ficarem, o EventTarget do
// jsdom recusa eventos criados com eles e qualquer elemento que dispare
// evento no primeiro render quebra.
for (const chave of ['Event', 'CustomEvent', 'EventTarget', 'MessageEvent']) {
  try { if (dom.window[chave]) globalThis[chave] = dom.window[chave]; } catch { }
}
globalThis.window = dom.window;
globalThis.document = dom.window.document;

// O jsdom nao implementa ImageData (API de canvas). O SSD1306 monta uma
// no construtor; um substituto minimo basta para o desenho sair.
globalThis.ImageData ??= class ImageData {
  constructor(a, b, c) {
    if (typeof a === 'number') {
      this.width = a; this.height = b;
      this.data = new Uint8ClampedArray(a * b * 4);
    } else {
      this.data = a; this.width = b; this.height = c ?? 1;
    }
  }
};

await import('./pacote.js');

const serializar = (n) => new dom.window.XMLSerializer().serializeToString(n);

// ---------------------------------------------------------------
// Variantes de estado: cada uma vira um arquivo proprio. Trocar a
// imagem e mais simples e robusto do que mexer no SVG em execucao.
// ---------------------------------------------------------------
const VARIANTES = {
  led: [
    ['-vermelho-off', { color: 'red', value: false }],
    ['-vermelho-on', { color: 'red', value: true }],
    ['-amarelo-off', { color: 'yellow', value: false }],
    ['-amarelo-on', { color: 'yellow', value: true }],
    ['-verde-off', { color: 'green', value: false }],
    ['-verde-on', { color: 'green', value: true }],
    ['-azul-off', { color: 'blue', value: false }],
    ['-azul-on', { color: 'blue', value: true }],
    ['-branco-off', { color: 'white', value: false }],
    ['-branco-on', { color: 'white', value: true }],
  ],
  pushbutton: [
    ['-solto', { color: 'red', pressed: false }],
    ['-pressionado', { color: 'red', pressed: true }],
  ],
};

// Pecas com parte movel: saem tambem cortadas em -corpo e -movel.
const MOVEIS = { potentiometer: 'rotating', servo: 'servo-arm' };

/** Converte "28.2mm" / "40" / "20px" para pixels de tela (96 dpi). */
function paraPixels(valor) {
  if (!valor) return null;
  const n = parseFloat(valor);
  if (Number.isNaN(n)) return null;
  if (valor.includes('mm')) return n * (96 / 25.4);
  if (valor.includes('cm')) return n * (96 / 2.54);
  if (valor.includes('in')) return n * 96;
  return n;
}

/**
 * Le o CSS do componente Lit e copia font-size / fill / font-family para
 * cada <text> sem esses atributos. O CSS do shadow root se perde ao
 * serializar so o <svg>, e sem font-size o texto sai gigante.
 */
function inlinarEstiloDeTexto(el, svg) {
  const estilos = el.constructor?.styles;
  if (!estilos) return;
  const css = (Array.isArray(estilos) ? estilos : [estilos])
    .map((s) => s?.cssText ?? '').join('\n');

  const regra = css.match(/(?:^|\})\s*(?:svg\s+)?text\s*\{([^}]*)\}/);
  if (!regra) return;

  const decls = Object.fromEntries(
    regra[1].split(';').map((d) => d.split(':').map((x) => x.trim()))
      .filter((p) => p.length === 2 && p[0] && p[1]));

  for (const no of svg.querySelectorAll('text')) {
    for (const prop of ['font-size', 'fill', 'font-family', 'letter-spacing']) {
      if (decls[prop] && !no.hasAttribute(prop)) no.setAttribute(prop, decls[prop]);
    }
    absolutizarFonte(no, decls['font-size']);
  }
}

/**
 * font-size relativo ("200%", "1.5em") perde o contexto quando o <svg>
 * sai do shadow root: nao sobra ninguem de quem herdar, e o renderizador
 * cai no padrao de 16px.
 *
 * No arduino-nano isso valia dois glifos de seta com font-size="200%"
 * entre rotulos de 1px. Viravam glifos de 32 unidades numa peca de 17,8
 * de altura, empurravam o limite do desenho 202 px para baixo, e a placa
 * inteira saia da viewport -- so as setas apareciam na tela.
 */
function absolutizarFonte(no, base) {
  const valor = no.getAttribute('font-size');
  if (!valor) return;

  const m = valor.trim().match(/^(-?[\d.]+)(%|em|rem)$/);
  if (!m) return;

  const n = parseFloat(m[1]);
  const b = parseFloat(base ?? '');
  if (!isFinite(n) || !isFinite(b)) return;

  const fator = m[2] === '%' ? n / 100 : n;
  no.setAttribute('font-size', `${+(b * fator).toFixed(4)}px`);
}

/** Escolhe o <svg> real: alguns elementos tem icones ocultos antes dele. */
function acharSvg(el) {
  const visiveis = [...(el.shadowRoot?.querySelectorAll('svg') ?? [])]
    .filter((s) => !/visibility:\s*hidden|display:\s*none/
      .test(s.getAttribute('style') ?? ''));
  return visiveis
    .map((s) => ({ s, n: serializar(s).length }))
    .sort((a, b) => b.n - a.n)[0]?.s;
}

/** Corta a peca em "-corpo" (sem a parte movel) e "-movel" (so ela). */
function gerarMetades(svg, id, arquivo, destino) {
  const corpo = svg.cloneNode(true);
  const alvoCorpo = corpo.querySelector(`#${id}`);
  if (!alvoCorpo) return false;
  alvoCorpo.remove();
  writeFileSync(join(destino, `${arquivo}-corpo.svg`), serializar(corpo), 'utf8');

  const movel = svg.cloneNode(true);
  const alvo = movel.querySelector(`#${id}`);
  if (!alvo) return false;
  for (const filho of [...movel.children]) {
    if (filho.tagName !== 'defs' && filho !== alvo && !filho.contains(alvo)) filho.remove();
  }
  writeFileSync(join(destino, `${arquivo}-movel.svg`), serializar(movel), 'utf8');
  return true;
}

// ---------------------------------------------------------------
const destino = process.argv[2] ?? './saida';
mkdirSync(destino, { recursive: true });

// Descobre os elementos pelo nome do arquivo: led-element.js -> wokwi-led
const pasta = './node_modules/@wokwi/elements/dist/esm';
const ids = readdirSync(pasta)
  .filter((f) => f.endsWith('-element.js') && !f.includes('.spec.'))
  .map((f) => f.replace('-element.js', ''))
  .sort();

const catalogo = [];
let ok = 0, falhou = 0;

for (const id of ids) {
  const tag = `wokwi-${id}`;
  try {
    const el = document.createElement(tag);
    if (!el.shadowRoot && !el.updateComplete) {
      // customElements nao registrou: elemento desconhecido
      document.body.appendChild(el);
    } else {
      document.body.appendChild(el);
    }
    if (el.updateComplete) await el.updateComplete;
    await new Promise((r) => setTimeout(r, 20));

    const svg = acharSvg(el);
    if (!svg) { console.log(`FALHOU  ${id}: sem <svg>`); falhou++; continue; }

    inlinarEstiloDeTexto(el, svg);

    const larg = paraPixels(svg.getAttribute('width'));
    const alt = paraPixels(svg.getAttribute('height'));

    // pinInfo: nome e posicao de cada pino, no mesmo espaco de pixels
    // que width/height. E o que permite ligar fio em pino.
    let pinos = [];
    try {
      pinos = (el.pinInfo ?? []).map((p) => ({
        nome: p.name, x: p.x, y: p.y,
        sinais: (p.signals ?? []).map((s) => s.type ?? s.signal ?? '').filter(Boolean),
        descricao: p.description ?? '',
      }));
    } catch { /* elemento sem pinInfo */ }

    const variantes = VARIANTES[id];
    if (variantes) {
      for (const [sufixo, props] of variantes) {
        const v = document.createElement(tag);
        for (const [k, val] of Object.entries(props)) v[k] = val;
        document.body.appendChild(v);
        if (v.updateComplete) await v.updateComplete;
        const svgV = acharSvg(v);
        if (svgV) {
          inlinarEstiloDeTexto(v, svgV);
          writeFileSync(join(destino, `${id}${sufixo}.svg`), serializar(svgV), 'utf8');
        }
        v.remove();
      }
    }

    let texto = serializar(svg);
    if (!texto.includes('xmlns=')) {
      texto = texto.replace('<svg', '<svg xmlns="http://www.w3.org/2000/svg"');
    }
    writeFileSync(join(destino, `${id}.svg`), texto, 'utf8');

    let temMetades = false;
    if (MOVEIS[id]) temMetades = gerarMetades(svg, MOVEIS[id], id, destino);

    catalogo.push({
      id,
      largura: Math.round(larg ?? 40),
      altura: Math.round(alt ?? 40),
      pinos,
      variantes: variantes ? variantes.map(([s]) => id + s) : [],
      metades: temMetades,
    });

    el.remove();
    ok++;
  } catch (e) {
    console.log(`FALHOU  ${id}: ${e.message.split('\n')[0]}`);
    falhou++;
  }
}

writeFileSync(join(destino, 'pecas.json'),
  JSON.stringify(catalogo, null, 2), 'utf8');

console.log(`\n${ok} pecas extraidas, ${falhou} falharam`);
console.log(`catalogo: ${catalogo.length} entradas, ` +
  `${catalogo.reduce((n, p) => n + p.pinos.length, 0)} pinos no total`);
