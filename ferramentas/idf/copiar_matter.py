"""Monta nativo/matter: os cabecalhos e os endpoints da biblioteca Matter do core
sobre o modelo de dados do simulador (ferramentas/idf/matter).

O SDK de verdade (o connectedhomeip e o esp-matter) tem milhares de arquivos, e
o protocolo dele nao roda no simulador. Ficam de verdade os cabecalhos e os
endpoints do core (MatterOnOffLight e os outros); o esp_matter.h daqui da os
numeros dos clusters e o modelo de dados, e cs_matter.cpp faz o ArduinoMatter e
o resto, com a bancada (motor/Matter.cs) no papel do aplicativo.

Uso: python copiar_matter.py <pasta do arduino-esp32>

A biblioteca do core e Apache 2.0.
"""
import os
import shutil
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
DESTINO = os.path.join(REPO, 'nativo', 'matter')

# O que depende do servidor do CHIP de verdade: o simulador tem o seu (cs_matter.cpp).
FORA = {'Matter.cpp', 'MatterIdentity.cpp', 'MatterWiFiStackWrap.cpp', 'MatterMemWrap.cpp'}
# Os caminhos do SDK que a biblioteca inclui: tudo mora no esp_matter.h daqui.
DO_SDK = ['esp_matter_attribute.h', 'esp_matter_cluster.h', 'esp_matter_core.h', 'data_model_provider/esp_matter_data_model_provider.h',
          'app/AttributeAccessInterface.h', 'app/AttributeAccessInterfaceRegistry.h', 'app/AttributeValueDecoder.h', 'app/AttributeValueEncoder.h',
          'app/clusters/occupancy-sensor-server/occupancy-hal.h', 'app/clusters/occupancy-sensor-server/occupancy-sensor-server.h',
          'app/clusters/temperature-control-server/supported-temperature-levels-manager.h', 'app/reporting/reporting.h',
          'app/util/attribute-storage-null-handling.h', 'lib/support/Span.h', 'lib/core/CHIPError.h', 'platform/CHIPDeviceLayer.h',
          'app/clusters/boolean-state-server/boolean-state-cluster.h', 'app-common/zap-generated/cluster-objects.h',
          'app-common/zap-generated/cluster-enums.h', 'app/ConcreteAttributePath.h', 'lib/support/CodeUtils.h',
          'platform/CHIPDeviceConfig.h', 'app/server/Server.h', 'app/clusters/window-covering-server/window-covering-server.h',
          'app/clusters/closure-control-server/closure-control-cluster-objects.h', 'setup_payload/SetupPayload.h', 'platform/DeviceInstanceInfoProvider.h',
          'platform/CommissionableDataProvider.h', 'crypto/CHIPCryptoPAL.h']


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    lib = os.path.join(sys.argv[1], 'libraries', 'Matter', 'src')
    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    inc = os.path.join(DESTINO, 'include')
    src = os.path.join(DESTINO, 'src')
    os.makedirs(os.path.join(inc, 'MatterEndpoints'))
    os.makedirs(os.path.join(src, 'MatterEndpoints'))
    for pasta, _, arquivos in os.walk(lib):
        rel = os.path.relpath(pasta, lib)
        for a in arquivos:
            if a.endswith('.h'):
                shutil.copyfile(os.path.join(pasta, a), os.path.join(inc, rel, a))
            elif a.endswith('.cpp') and a not in FORA:
                shutil.copyfile(os.path.join(pasta, a), os.path.join(src, rel, a))
    shutil.copyfile(os.path.join(AQUI, 'matter', 'esp_matter.h'), os.path.join(inc, 'esp_matter.h'))
    shutil.copyfile(os.path.join(AQUI, 'matter', 'cs_matter.cpp'), os.path.join(src, 'cs_matter.cpp'))
    for caminho in DO_SDK:
        alvo = os.path.join(inc, *caminho.split('/'))
        os.makedirs(os.path.dirname(alvo), exist_ok=True)
        with open(alvo, 'w', newline='\n') as f:
            f.write('#pragma once\n#include <esp_matter.h>\n')
    print('Matter: pronto')


if __name__ == '__main__':
    main()
