/* fnmatch.h -- o fnmatch() do POSIX (o da newlib, na placa), que o Windows
 * nao tem: o "*", o "?" e o "[a-z]" dos nomes de arquivo. O UriGlob do
 * WebServer usa ("/arquivos/*.txt"). */
#ifndef CS_FNMATCH_H
#define CS_FNMATCH_H

#define FNM_NOMATCH 1
#define FNM_NOSYS (-1)
#define FNM_NOESCAPE 0x01
#define FNM_PATHNAME 0x02
#define FNM_PERIOD 0x04
#define FNM_LEADING_DIR 0x08
#define FNM_CASEFOLD 0x10
#define FNM_IGNORECASE FNM_CASEFOLD
#define FNM_FILE_NAME FNM_PATHNAME

static inline int cs_fnm_minuscula(int c, int flags)
{
    return (flags & FNM_CASEFOLD) && c >= 'A' && c <= 'Z' ? c - 'A' + 'a' : c;
}

/* O "[...]" que comeca em *p (depois do "["): 1 se c esta nele, 0 se nao,
 * -1 se o colchete nao fecha (entao o "[" e um caractere comum). */
static inline int cs_fnm_classe(const char **p, int c, int flags)
{
    const char *s = *p;
    int negar = (*s == '!' || *s == '^');
    int achou = 0;
    if (negar) s++;
    c = cs_fnm_minuscula(c, flags);
    for (int primeiro = 1; *s && (primeiro || *s != ']'); primeiro = 0) {
        int a = *s++;
        if (a == '\\' && !(flags & FNM_NOESCAPE) && *s) a = *s++;
        int b = a;
        if (*s == '-' && s[1] && s[1] != ']') {
            s++;
            b = *s++;
            if (b == '\\' && !(flags & FNM_NOESCAPE) && *s) b = *s++;
        }
        a = cs_fnm_minuscula(a, flags);
        b = cs_fnm_minuscula(b, flags);
        if (c >= a && c <= b) achou = 1;
    }
    if (*s != ']') return -1;
    *p = s + 1;
    return achou != negar;
}

static inline int fnmatch(const char *pattern, const char *string, int flags)
{
    const char *p = pattern, *s = string;
    const char *estrela = 0, *volta = 0;
    while (*s) {
        int inicio = s == string || ((flags & FNM_PATHNAME) && s[-1] == '/');
        if (*s == '.' && (flags & FNM_PERIOD) && inicio && *p != '.') return FNM_NOMATCH;
        if (*p == '*') {
            while (*p == '*') p++;
            estrela = p;
            volta = s;
            continue;
        }
        if (*p == '?' && !((flags & FNM_PATHNAME) && *s == '/')) {
            p++;
            s++;
            continue;
        }
        if (*p == '[' && !((flags & FNM_PATHNAME) && *s == '/')) {
            const char *q = p + 1;
            int r = cs_fnm_classe(&q, (unsigned char)*s, flags);
            if (r == 1) {
                p = q;
                s++;
                continue;
            }
            if (r == 0) goto falhou;
        }
        {
            int c = (unsigned char)*p;
            if (c == '\\' && !(flags & FNM_NOESCAPE) && p[1]) c = (unsigned char)*++p;
            if (c && cs_fnm_minuscula(c, flags) == cs_fnm_minuscula((unsigned char)*s, flags)) {
                p++;
                s++;
                continue;
            }
        }
        if ((flags & FNM_LEADING_DIR) && !*p && *s == '/') return 0;
    falhou:
        /* Volta ao ultimo "*" e deixa ele engolir mais um caractere (sem passar
         * de uma "/" com FNM_PATHNAME). */
        if (!estrela || ((flags & FNM_PATHNAME) && *volta == '/')) return FNM_NOMATCH;
        p = estrela;
        s = ++volta;
    }
    while (*p == '*') p++;
    return *p ? FNM_NOMATCH : 0;
}

#endif
