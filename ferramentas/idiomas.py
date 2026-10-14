"""python ferramentas/idiomas.py [--novo CODIGO "NOME" CULTURA] [--conferir]

Os catalogos de traducao do CircuitSketch (Alpha 25), em motor/idiomas/.

O texto original e o portugues do codigo, e ele mesmo e a chave. Este
script:

  - acha toda frase marcada para traducao:
      C#   : Idioma.T("Salvar projeto")  /  Idioma.T("{0} pecas", n)
      XAML : Header="{local:T 'Salvar projeto'}"
  - acrescenta as frases novas a cada catalogo (com "" -- ainda nao
    traduzida), sem tocar no que ja foi traduzido;
  - guarda em "_obsoletos" as traducoes de frases que sairam do codigo (nada
    se perde: se a frase voltar, a traducao volta junto);
  - mostra quanto de cada idioma esta traduzido.

  --novo en "English" en-US : cria o catalogo de um idioma novo.
  --conferir                : so confere (nao grava): sai com erro se uma
                              traducao perdeu ou ganhou um {0}, ou se ha
                              frase no codigo fora de algum catalogo.
"""
import io
import json
import os
import re
import sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOGOS = os.path.join(BASE, 'motor', 'idiomas')
PASTAS = ['app', 'motor']

# Uma string C# comum (com os escapes) logo depois de Idioma.T(
C_SHARP = re.compile(r'Idioma\.T\(\s*"((?:[^"\\\n]|\\.)*)"')
XAML = re.compile(r"\{local:T\s+'((?:[^'\\]|\\.)*)'\s*\}")
MARCA = re.compile(r'\{(\d+)(?:[,:][^}]*)?\}')
ESCAPES = {'n': '\n', 't': '\t', 'r': '\r', '"': '"', '\\': '\\', "'": "'", '0': '\0'}


def desescapar(texto):
    saida, i = [], 0
    while i < len(texto):
        c = texto[i]
        if c != '\\' or i + 1 >= len(texto):
            saida.append(c)
            i += 1
            continue
        proximo = texto[i + 1]
        if proximo == 'u' and i + 5 < len(texto):
            saida.append(chr(int(texto[i + 2:i + 6], 16)))
            i += 6
            continue
        saida.append(ESCAPES.get(proximo, proximo))
        i += 2
    return ''.join(saida)


def frases_do_codigo():
    """Todas as frases marcadas, com onde aparecem (para o relatorio)."""
    achadas = {}
    for pasta in PASTAS:
        for raiz, dirs, arquivos in os.walk(os.path.join(BASE, pasta)):
            dirs[:] = [d for d in dirs if d not in ('bin', 'obj', 'idiomas')]
            for nome in arquivos:
                if not nome.endswith(('.cs', '.xaml')):
                    continue
                caminho = os.path.join(raiz, nome)
                texto = io.open(caminho, encoding='utf-8-sig').read()
                padrao = XAML if nome.endswith('.xaml') else C_SHARP
                for m in padrao.finditer(texto):
                    frase = desescapar(m.group(1))
                    if frase:
                        linha = texto.count('\n', 0, m.start()) + 1
                        achadas.setdefault(frase, f'{os.path.relpath(caminho, BASE)}:{linha}')
    return achadas


def ler(caminho):
    return json.load(io.open(caminho, encoding='utf-8'))


def gravar(caminho, catalogo):
    io.open(caminho, 'w', encoding='utf-8', newline='\n').write(json.dumps(catalogo, ensure_ascii=False, indent=1) + '\n')


def marcas(texto):
    return sorted(set(MARCA.findall(texto)))


def main():
    args = sys.argv[1:]
    if args[:1] == ['--novo']:
        if len(args) != 4:
            sys.exit('uso: --novo CODIGO "NOME" CULTURA')
        codigo, nome, cultura = args[1:]
        caminho = os.path.join(CATALOGOS, codigo + '.json')
        if os.path.exists(caminho):
            sys.exit(f'{codigo}.json ja existe')
        gravar(caminho, {'codigo': codigo, 'nome': nome, 'cultura': cultura, 'textos': {}})
        print(f'criado: motor/idiomas/{codigo}.json')
        args = []

    so_conferir = '--conferir' in args
    frases = frases_do_codigo()
    problemas = []
    print(f'{len(frases)} frases marcadas no codigo')

    for nome in sorted(os.listdir(CATALOGOS)):
        if not nome.endswith('.json'):
            continue
        caminho = os.path.join(CATALOGOS, nome)
        catalogo = ler(caminho)
        textos = catalogo.get('textos', {})
        obsoletos = catalogo.get('_obsoletos', {})

        # Volta do obsoleto o que voltou ao codigo; vai para la o que saiu.
        for frase in list(obsoletos):
            if frase in frases:
                textos[frase] = obsoletos.pop(frase)
        for frase in [f for f in textos if f not in frases]:
            if textos[frase]:
                obsoletos[frase] = textos[frase]
            del textos[frase]
        faltando = [f for f in frases if f not in textos]
        for frase in faltando:
            textos[frase] = ''

        for frase, traducao in textos.items():
            if traducao and marcas(traducao) != marcas(frase):
                problemas.append(f'{nome}: "{frase}" -> "{traducao}": as marcas {{n}} nao batem')
        if so_conferir and faltando:
            problemas.append(f'{nome}: {len(faltando)} frase(s) do codigo fora do catalogo (rode sem --conferir)')

        textos_ordenados = dict(sorted(textos.items(), key=lambda kv: kv[0].casefold()))
        feitas = sum(1 for f in frases if textos.get(f))
        # O catalogo completo aparece na janela de configuracoes; o incompleto, nao.
        completo = bool(frases) and feitas == len(frases) and not any(p.startswith(nome + ':') for p in problemas)
        novo = {'codigo': catalogo['codigo'], 'nome': catalogo['nome'], 'cultura': catalogo.get('cultura', ''),
                'completo': completo, 'textos': textos_ordenados}
        if obsoletos:
            novo['_obsoletos'] = dict(sorted(obsoletos.items(), key=lambda kv: kv[0].casefold()))
        if not so_conferir:
            gravar(caminho, novo)
        porcento = 100 * feitas / len(frases) if frases else 100
        print(f'  {catalogo["codigo"]:6} {catalogo["nome"]:24} {feitas}/{len(frases)} traduzidas ({porcento:.0f}%)'
              + (', COMPLETO' if completo else '') + (f', {len(obsoletos)} obsoleta(s)' if obsoletos else ''))

    for p in problemas:
        print('PROBLEMA: ' + p)
    if problemas:
        sys.exit(1)


if __name__ == '__main__':
    main()
