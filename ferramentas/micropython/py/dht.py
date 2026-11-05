# A API do dht.py do MicroPython. Na bancada o DHT e um aparelho: o protocolo de 40 bits nao passa
# pelo pino, entao a medida vem direto dele, com os mesmos erros de quem nao recebe resposta.
import errno
import _cs


class DHTBase:
    def __init__(self, pin):
        self.pin = pin
        self._t = 0
        self._h = 0

    def measure(self):
        r = _cs.dht(self.pin, self._tipo)
        if r is None:
            raise OSError(errno.ETIMEDOUT)
        self._t, self._h = r


class DHT11(DHTBase):
    _tipo = 11

    def humidity(self):
        return int(self._h)

    def temperature(self):
        return int(self._t)


class DHT22(DHTBase):
    _tipo = 22

    def humidity(self):
        return self._h

    def temperature(self):
        return self._t
