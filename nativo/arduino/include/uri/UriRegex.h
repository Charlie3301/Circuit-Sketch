/* uri/UriRegex.h -- "server.on(UriRegex("^\/led\/([0-9]+)$"), ...)". */
#ifndef URI_REGEX_H
#define URI_REGEX_H

#include "UriBraces.h"

class UriRegex : public Uri {
public:
    using Uri::Uri;
};

#endif
