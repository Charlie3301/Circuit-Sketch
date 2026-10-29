/* uri/UriBraces.h -- "server.on(UriBraces("/led/{}"), ...)": o caminho com
 * o "{}", que a rota do simulador entende (o pathArg devolve o pedaco). */
#ifndef URI_BRACES_H
#define URI_BRACES_H

#include "WString.h"

class Uri : public String {
public:
    Uri(const char *uri) : String(uri) {}
    Uri(const String &uri) : String(uri) {}
};
class UriBraces : public Uri {
public:
    using Uri::Uri;
};

#endif
