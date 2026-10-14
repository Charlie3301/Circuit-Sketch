"""Separa cópias dos projetos conforme os resultados do autoteste.

python ferramentas/testados.py <resultados.json> [<destino>] [--origem <pasta>]
"""
import argparse
from datetime import datetime
import json
from pathlib import Path


DESTINO = 'C:/Users/seraf/OneDrive/Desktop/TESTED'


def falhas(resultado):
    if resultado.get('erro') is not None:
        return [f"não rodou: {resultado['erro']}"]
    return [f"{c['nome']}: {c['visto']}"
            for c in resultado['conferencias'] if c['ok'] is False]


def cabecalho(resultado, data):
    funcionou = resultado['funcionou'] is True
    estado = 'FUNCIONA' if funcionou else 'NÃO FUNCIONA'
    linhas = [f'TESTADO NO SIMULADOR em {data}: {estado}.', '']
    if funcionou:
        linhas += ['O que foi conferido:']
        linhas += [f"  - {c['nome']}" for c in resultado['conferencias']]
        linhas += ['']
    else:
        linhas += ['O que falhou:']
        linhas += [f'  - {falha}' for falha in falhas(resultado)]
        linhas += ['']
        passaram = [c for c in resultado['conferencias'] if c['ok'] is True]
        if passaram:
            linhas += ['O que funcionou:']
            linhas += [f"  - {c['nome']}" for c in passaram]
            linhas += ['']
    return linhas


def separar(resultados, destino, origem=None):
    origem = origem if origem is not None else resultados.parent / 'projetos'
    dados = json.loads(resultados.read_text(encoding='utf-8'))
    data = datetime.fromisoformat(dados['data']).strftime('%d/%m/%Y')
    pastas = {True: destino / 'WORKING', False: destino / 'NOT WORKING'}
    # Valide e leia antes de gravar, para nunca sobrescrever os originais.
    copias = []
    for nome, resultado in dados['projetos'].items():
        if not nome or nome in ('.', '..') or any(c in nome for c in '/\\:'):
            raise ValueError(f'Nome de projeto inválido: {nome!r}')
        arquivo = nome + '.sktch'
        fonte = origem / arquivo
        funcionou = resultado['funcionou'] is True
        escolhida = pastas[funcionou] / arquivo
        antiga = pastas[not funcionou] / arquivo
        if fonte.resolve() in (escolhida.resolve(), antiga.resolve()):
            raise ValueError(f'O destino coincide com a origem de {nome}.')
        projeto = json.loads(fonte.read_text(encoding='utf-8'))
        projeto['descricao'] = cabecalho(resultado, data) + projeto['descricao']
        conteudo = json.dumps(projeto, ensure_ascii=False, indent=1)
        copias.append((nome, resultado, escolhida, antiga, conteudo))

    for pasta in pastas.values():
        pasta.mkdir(parents=True, exist_ok=True)
    contagem = {True: 0, False: 0}
    reprovados = []
    for nome, resultado, escolhida, antiga, conteudo in copias:
        with escolhida.open('w', encoding='utf-8', newline='\n') as arquivo:
            arquivo.write(conteudo)
        if antiga.exists():
            antiga.unlink()
        funcionou = resultado['funcionou'] is True
        contagem[funcionou] += 1
        if not funcionou:
            motivos = falhas(resultado)
            reprovados.append((nome, motivos[0] if motivos else 'sem falha informada'))

    print(f"WORKING: {contagem[True]}")
    print(f"NOT WORKING: {contagem[False]}")
    if reprovados:
        print('Projetos que não funcionaram:')
        for nome, motivo in reprovados:
            print(f'  - {nome}: {motivo}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('resultados', type=Path)
    parser.add_argument('destino', type=Path, nargs='?', default=Path(DESTINO))
    parser.add_argument('--origem', type=Path,
                        help='Pasta dos .sktch (padrão: projetos/ ao lado do JSON).')
    args = parser.parse_args()
    try:
        separar(args.resultados, args.destino, args.origem)
    except (OSError, ValueError, KeyError, TypeError) as erro:
        parser.exit(1, f'Erro: {erro}\n')


if __name__ == '__main__':
    main()
