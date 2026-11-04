"""Copia o cJSON (o componente json do ESP-IDF, um submodulo) para o core nativo.

Uso: python copiar_cjson.py <pasta do cJSON>

O cJSON e de Dave Gamble e colaboradores (licenca MIT).
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    shutil.copyfile(os.path.join(sys.argv[1], 'cJSON.h'), os.path.join(REPO, 'nativo', 'idf', 'comum', 'cJSON.h'))
    idf = os.path.join(REPO, 'nativo', 'arduino', 'src', 'arduino-esp32', 'idf')
    shutil.copyfile(os.path.join(sys.argv[1], 'cJSON.c'), os.path.join(idf, 'cJSON.c'))
    shutil.copyfile(os.path.join(sys.argv[1], 'LICENSE'), os.path.join(idf, 'LICENSE-cJSON'))


if __name__ == '__main__':
    main()
