/* faad_config.h -- PlayerDAP: o que o CMake do faad2 2.11.4 definiria.
 *
 * O perfil: AAC-LC, com o SBR e o PS (o HE-AAC v1 e v2, os do radio e das
 * lojas). Sem o MAIN, o LTP, o LD, o ER e o DRM, que quase nao aparecem em
 * musica: o LC_ONLY_DECODER do common.h tira esses e deixa o SBR/PS.
 */
#ifndef FAAD_CONFIG_H
#define FAAD_CONFIG_H

#define HAVE_INTTYPES_H 1
#define HAVE_STDINT_H 1
#define HAVE_STRING_H 1
#define HAVE_MEMCPY 1
#define PACKAGE_VERSION "2.11.4"
#define LC_ONLY_DECODER

#endif
