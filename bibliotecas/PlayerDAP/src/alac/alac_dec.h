/*
 * Copyright (c) 2011 Apple Inc. All rights reserved.
 *
 * @APPLE_APACHE_LICENSE_HEADER_START@
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * @APPLE_APACHE_LICENSE_HEADER_END@
 */

/*
	File:		alac_dec.h

	Modificado para o PlayerDAP: e o ALACDecoder.h da Apple em C -- a classe
	ALACDecoder virou a struct alac_dec_t e as funcoes alac_dec_*. O
	algoritmo e o mesmo.
*/

#ifndef ALAC_DEC_H
#define ALAC_DEC_H

#include <stdint.h>

struct BitBuffer;

/* A configuracao do "magic cookie" (o ALACSpecificConfig, 24 bytes). */
typedef struct {
	uint32_t	frameLength;
	uint8_t		compatibleVersion;
	uint8_t		bitDepth;
	uint8_t		pb;
	uint8_t		mb;
	uint8_t		kb;
	uint8_t		numChannels;
	uint16_t	maxRun;
	uint32_t	maxFrameBytes;
	uint32_t	avgBitRate;
	uint32_t	sampleRate;
} alac_config_t;

typedef struct {
	alac_config_t	config;
	uint16_t		activeElements;
	int32_t *		mixBufferU;
	int32_t *		mixBufferV;
	int32_t *		predictor;
	uint16_t *		shiftBuffer;	/* a mesma memoria do predictor */
} alac_dec_t;

/* Le o cookie (com ou sem as caixas 'frma' e 'alac' na frente). 0 = certo. */
int32_t	alac_dec_iniciar( alac_dec_t * d, const uint8_t * cookie, uint32_t tamanho );

/* Um pacote: as amostras saem intercaladas, com bitDepth bits cada (16 em
 * int16_t, 20 e 24 em 3 bytes, 32 em int32_t). 0 = certo. */
int32_t	alac_dec_decodificar( alac_dec_t * d, struct BitBuffer * bits, uint8_t * sampleBuffer,
							  uint32_t numSamples, uint32_t numChannels, uint32_t * outNumSamples );

void	alac_dec_soltar( alac_dec_t * d );

#endif
