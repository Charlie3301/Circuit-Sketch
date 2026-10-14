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
	File:		alac_dec.c

	Modificado para o PlayerDAP: e o ALACDecoder.cpp da Apple passado de C++
	para C. A classe ALACDecoder virou a struct alac_dec_t; Init, Decode,
	FillElement e DataStreamElement viraram alac_dec_iniciar,
	alac_dec_decodificar e as duas funcoes static do fim. O cookie e lido
	byte a byte (big-endian), sem o EndianPortable. O algoritmo e o mesmo.
*/

#include <stdlib.h>
#include <string.h>

#include "alac_dec.h"
#include "ALACAudioTypes.h"
#include "dplib.h"
#include "aglib.h"
#include "matrixlib.h"
#include "ALACBitUtilities.h"

#define ALAC_VERSAO 0		/* o kALACVersion do ALACAudioTypes.h */

static int32_t FillElement( BitBuffer * bits );
static int32_t DataStreamElement( BitBuffer * bits );
static void Zero16( int16_t * buffer, uint32_t numItems, uint32_t stride );
static void Zero24( uint8_t * buffer, uint32_t numItems, uint32_t stride );
static void Zero32( int32_t * buffer, uint32_t numItems, uint32_t stride );

static uint32_t be32( const uint8_t * p ) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }

int32_t alac_dec_iniciar( alac_dec_t * d, const uint8_t * cookie, uint32_t tamanho )
{
	memset( d, 0, sizeof(*d) );
	if ( cookie == NULL || tamanho < 24 )
		return kALAC_ParamError;

	/* Por razoes historicas o cookie pode vir embrulhado nas caixas 'frma' e
	   'alac' (ALACMagicCookieDescription.txt): pula as duas, se houver. */
	if ( tamanho >= 12 + 24 && memcmp( cookie + 4, "frma", 4 ) == 0 )
	{
		cookie += 12;
		tamanho -= 12;
	}
	if ( tamanho >= 12 + 24 && memcmp( cookie + 4, "alac", 4 ) == 0 )
	{
		cookie += 12;
		tamanho -= 12;
	}
	if ( tamanho < 24 )
		return kALAC_ParamError;

	d->config.frameLength       = be32( cookie + 0 );
	d->config.compatibleVersion = cookie[4];
	d->config.bitDepth          = cookie[5];
	d->config.pb                = cookie[6];
	d->config.mb                = cookie[7];
	d->config.kb                = cookie[8];
	d->config.numChannels       = cookie[9];
	d->config.maxRun            = (uint16_t)( cookie[10] << 8 | cookie[11] );
	d->config.maxFrameBytes     = be32( cookie + 12 );
	d->config.avgBitRate        = be32( cookie + 16 );
	d->config.sampleRate        = be32( cookie + 20 );

	if ( d->config.compatibleVersion > ALAC_VERSAO )
		return kALAC_ParamError;
	if ( d->config.frameLength == 0 || d->config.frameLength > 65536 || d->config.numChannels == 0 )
		return kALAC_ParamError;
	if ( d->config.bitDepth != 16 && d->config.bitDepth != 20 && d->config.bitDepth != 24 && d->config.bitDepth != 32 )
		return kALAC_ParamError;

	d->mixBufferU = (int32_t *) calloc( d->config.frameLength * sizeof(int32_t), 1 );
	d->mixBufferV = (int32_t *) calloc( d->config.frameLength * sizeof(int32_t), 1 );
	/* o "corrector" do preditor dinamico; o shiftBuffer usa a mesma memoria
	   (frameLength * 2 valores de 16 bits cabem em frameLength de 32) */
	d->predictor = (int32_t *) calloc( d->config.frameLength * sizeof(int32_t), 1 );
	d->shiftBuffer = (uint16_t *) d->predictor;

	if ( d->mixBufferU == NULL || d->mixBufferV == NULL || d->predictor == NULL )
	{
		alac_dec_soltar( d );
		return kALAC_MemFullError;
	}
	return ALAC_noErr;
}

void alac_dec_soltar( alac_dec_t * d )
{
	free( d->mixBufferU );
	free( d->mixBufferV );
	free( d->predictor );
	d->mixBufferU = d->mixBufferV = d->predictor = NULL;
	d->shiftBuffer = NULL;
}

/*
	alac_dec_decodificar()
	- the decoded samples are interleaved into the output buffer in the order they arrive in
	  the bitstream
*/
int32_t alac_dec_decodificar( alac_dec_t * d, BitBuffer * bits, uint8_t * sampleBuffer, uint32_t numSamples,
							  uint32_t numChannels, uint32_t * outNumSamples )
{
	BitBuffer			shiftBits;
	uint32_t			bits1, bits2;
	uint8_t				tag;
	uint8_t				elementInstanceTag;
	AGParamRec			agParams;
	uint32_t			channelIndex;
	int16_t				coefsU[32];		// max possible size is 32 although NUMCOEPAIRS is the current limit
	int16_t				coefsV[32];
	uint8_t				numU, numV;
	uint8_t				mixBits;
	int8_t				mixRes;
	uint16_t			unusedHeader;
	uint8_t				escapeFlag;
	uint32_t			chanBits;
	uint8_t				bytesShifted;
	uint32_t			shift;
	uint8_t				modeU, modeV;
	uint32_t			denShiftU, denShiftV;
	uint16_t			pbFactorU, pbFactorV;
	uint16_t			pb;
	int16_t *			out16;
	uint8_t *			out20;
	uint8_t *			out24;
	int32_t *			out32;
	uint8_t				headerByte;
	uint8_t				partialFrame;
	uint32_t			extraBits;
	int32_t				val;
	uint32_t			i, j;
	int32_t				status;

	if ( d == NULL || bits == NULL || sampleBuffer == NULL || outNumSamples == NULL || d->predictor == NULL )
		return kALAC_ParamError;
	if ( numChannels == 0 )
		return kALAC_ParamError;

	memset( &shiftBits, 0, sizeof(shiftBits) );
	d->activeElements = 0;
	channelIndex = 0;

	status = ALAC_noErr;
	*outNumSamples = numSamples;

	while ( status == ALAC_noErr )
	{
		// bail if we ran off the end of the buffer
		if ( bits->cur >= bits->end ) { status = kALAC_ParamError; goto Exit; }

		// copy global decode params for this element
		pb = d->config.pb;

		// read element tag
		tag = BitBufferReadSmall( bits, 3 );
		switch ( tag )
		{
			case ID_SCE:
			case ID_LFE:
			{
				// mono/LFE channel
				elementInstanceTag = BitBufferReadSmall( bits, 4 );
				d->activeElements |= (1u << elementInstanceTag);

				// read the 12 unused header bits
				unusedHeader = (uint16_t) BitBufferRead( bits, 12 );
				if ( unusedHeader != 0 ) { status = kALAC_ParamError; goto Exit; }

				// read the 1-bit "partial frame" flag, 2-bit "shift-off" flag & 1-bit "escape" flag
				headerByte = (uint8_t) BitBufferRead( bits, 4 );

				partialFrame = headerByte >> 3;

				bytesShifted = (headerByte >> 1) & 0x3u;
				if ( bytesShifted == 3 ) { status = kALAC_ParamError; goto Exit; }

				shift = bytesShifted * 8;

				escapeFlag = headerByte & 0x1;

				chanBits = d->config.bitDepth - (bytesShifted * 8);

				// check for partial frame to override requested numSamples
				if ( partialFrame != 0 )
				{
					numSamples  = BitBufferRead( bits, 16 ) << 16;
					numSamples |= BitBufferRead( bits, 16 );
				}
				if ( numSamples > d->config.frameLength ) { status = kALAC_ParamError; goto Exit; }

				if ( escapeFlag == 0 )
				{
					// compressed frame, read rest of parameters
					mixBits	= (uint8_t) BitBufferRead( bits, 8 );
					mixRes	= (int8_t) BitBufferRead( bits, 8 );

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					modeU		= headerByte >> 4;
					denShiftU	= headerByte & 0xfu;

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					pbFactorU	= headerByte >> 5;
					numU		= headerByte & 0x1fu;

					for ( i = 0; i < numU; i++ )
						coefsU[i] = (int16_t) BitBufferRead( bits, 16 );

					// if shift active, skip the the shift buffer but remember where it starts
					if ( bytesShifted != 0 )
					{
						shiftBits = *bits;
						BitBufferAdvance( bits, (bytesShifted * 8) * numSamples );
					}

					// decompress
					set_ag_params( &agParams, d->config.mb, (pb * pbFactorU) / 4, d->config.kb, numSamples, numSamples, d->config.maxRun );
					status = dyn_decomp( &agParams, bits, d->predictor, numSamples, chanBits, &bits1 );
					if ( status != 0 ) goto Exit;

					if ( modeU == 0 )
					{
						unpc_block( d->predictor, d->mixBufferU, numSamples, &coefsU[0], numU, chanBits, denShiftU );
					}
					else
					{
						// the special "numActive == 31" mode can be done in-place
						unpc_block( d->predictor, d->predictor, numSamples, NULL, 31, chanBits, 0 );
						unpc_block( d->predictor, d->mixBufferU, numSamples, &coefsU[0], numU, chanBits, denShiftU );
					}
				}
				else
				{
					// uncompressed frame, copy data into the mix buffer to use common output code
					shift = 32 - chanBits;
					if ( chanBits <= 16 )
					{
						for ( i = 0; i < numSamples; i++ )
						{
							val = (int32_t) BitBufferRead( bits, (uint8_t) chanBits );
							val = (int32_t)((uint32_t)val << shift) >> shift;
							d->mixBufferU[i] = val;
						}
					}
					else
					{
						// BitBufferRead() can't read more than 16 bits at a time so break up the reads
						extraBits = chanBits - 16;
						for ( i = 0; i < numSamples; i++ )
						{
							val = (int32_t) BitBufferRead( bits, 16 );
							val = (int32_t)((uint32_t)val << 16) >> shift;
							d->mixBufferU[i] = val | BitBufferRead( bits, (uint8_t) extraBits );
						}
					}

					mixBits = mixRes = 0;
					bits1 = chanBits * numSamples;
					bytesShifted = 0;
				}

				// now read the shifted values into the shift buffer
				if ( bytesShifted != 0 )
				{
					shift = bytesShifted * 8;

					for ( i = 0; i < numSamples; i++ )
						d->shiftBuffer[i] = (uint16_t) BitBufferRead( &shiftBits, (uint8_t) shift );
				}

				// convert 32-bit integers into output buffer
				switch ( d->config.bitDepth )
				{
					case 16:
						out16 = &((int16_t *)sampleBuffer)[channelIndex];
						for ( i = 0, j = 0; i < numSamples; i++, j += numChannels )
							out16[j] = (int16_t) d->mixBufferU[i];
						break;
					case 20:
						out20 = (uint8_t *)sampleBuffer + (channelIndex * 3);
						copyPredictorTo20( d->mixBufferU, out20, numChannels, numSamples );
						break;
					case 24:
						out24 = (uint8_t *)sampleBuffer + (channelIndex * 3);
						if ( bytesShifted != 0 )
							copyPredictorTo24Shift( d->mixBufferU, d->shiftBuffer, out24, numChannels, numSamples, bytesShifted );
						else
							copyPredictorTo24( d->mixBufferU, out24, numChannels, numSamples );
						break;
					case 32:
						out32 = &((int32_t *)sampleBuffer)[channelIndex];
						if ( bytesShifted != 0 )
							copyPredictorTo32Shift( d->mixBufferU, d->shiftBuffer, out32, numChannels, numSamples, bytesShifted );
						else
							copyPredictorTo32( d->mixBufferU, out32, numChannels, numSamples);
						break;
				}

				channelIndex += 1;
				*outNumSamples = numSamples;
				break;
			}

			case ID_CPE:
			{
				// if decoding this pair would take us over the max channels limit, bail
				if ( (channelIndex + 2) > numChannels )
					goto NoMoreChannels;

				// stereo channel pair
				elementInstanceTag = BitBufferReadSmall( bits, 4 );
				d->activeElements |= (1u << elementInstanceTag);

				// read the 12 unused header bits
				unusedHeader = (uint16_t) BitBufferRead( bits, 12 );
				if ( unusedHeader != 0 ) { status = kALAC_ParamError; goto Exit; }

				// read the 1-bit "partial frame" flag, 2-bit "shift-off" flag & 1-bit "escape" flag
				headerByte = (uint8_t) BitBufferRead( bits, 4 );

				partialFrame = headerByte >> 3;

				bytesShifted = (headerByte >> 1) & 0x3u;
				if ( bytesShifted == 3 ) { status = kALAC_ParamError; goto Exit; }

				shift = bytesShifted * 8;

				escapeFlag = headerByte & 0x1;

				chanBits = d->config.bitDepth - (bytesShifted * 8) + 1;

				// check for partial frame length to override requested numSamples
				if ( partialFrame != 0 )
				{
					numSamples  = BitBufferRead( bits, 16 ) << 16;
					numSamples |= BitBufferRead( bits, 16 );
				}
				if ( numSamples > d->config.frameLength ) { status = kALAC_ParamError; goto Exit; }

				if ( escapeFlag == 0 )
				{
					// compressed frame, read rest of parameters
					mixBits		= (uint8_t) BitBufferRead( bits, 8 );
					mixRes		= (int8_t) BitBufferRead( bits, 8 );

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					modeU		= headerByte >> 4;
					denShiftU	= headerByte & 0xfu;

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					pbFactorU	= headerByte >> 5;
					numU		= headerByte & 0x1fu;
					for ( i = 0; i < numU; i++ )
						coefsU[i] = (int16_t) BitBufferRead( bits, 16 );

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					modeV		= headerByte >> 4;
					denShiftV	= headerByte & 0xfu;

					headerByte	= (uint8_t) BitBufferRead( bits, 8 );
					pbFactorV	= headerByte >> 5;
					numV		= headerByte & 0x1fu;
					for ( i = 0; i < numV; i++ )
						coefsV[i] = (int16_t) BitBufferRead( bits, 16 );

					// if shift active, skip the interleaved shifted values but remember where they start
					if ( bytesShifted != 0 )
					{
						shiftBits = *bits;
						BitBufferAdvance( bits, (bytesShifted * 8) * 2 * numSamples );
					}

					// decompress and run predictor for "left" channel
					set_ag_params( &agParams, d->config.mb, (pb * pbFactorU) / 4, d->config.kb, numSamples, numSamples, d->config.maxRun );
					status = dyn_decomp( &agParams, bits, d->predictor, numSamples, chanBits, &bits1 );
					if ( status != 0 ) goto Exit;

					if ( modeU == 0 )
					{
						unpc_block( d->predictor, d->mixBufferU, numSamples, &coefsU[0], numU, chanBits, denShiftU );
					}
					else
					{
						// the special "numActive == 31" mode can be done in-place
						unpc_block( d->predictor, d->predictor, numSamples, NULL, 31, chanBits, 0 );
						unpc_block( d->predictor, d->mixBufferU, numSamples, &coefsU[0], numU, chanBits, denShiftU );
					}

					// decompress and run predictor for "right" channel
					set_ag_params( &agParams, d->config.mb, (pb * pbFactorV) / 4, d->config.kb, numSamples, numSamples, d->config.maxRun );
					status = dyn_decomp( &agParams, bits, d->predictor, numSamples, chanBits, &bits2 );
					if ( status != 0 ) goto Exit;

					if ( modeV == 0 )
					{
						unpc_block( d->predictor, d->mixBufferV, numSamples, &coefsV[0], numV, chanBits, denShiftV );
					}
					else
					{
						// the special "numActive == 31" mode can be done in-place
						unpc_block( d->predictor, d->predictor, numSamples, NULL, 31, chanBits, 0 );
						unpc_block( d->predictor, d->mixBufferV, numSamples, &coefsV[0], numV, chanBits, denShiftV );
					}
				}
				else
				{
					// uncompressed frame, copy data into the mix buffers to use common output code
					chanBits = d->config.bitDepth;
					shift = 32 - chanBits;
					if ( chanBits <= 16 )
					{
						for ( i = 0; i < numSamples; i++ )
						{
							val = (int32_t) BitBufferRead( bits, (uint8_t) chanBits );
							val = (int32_t)((uint32_t)val << shift) >> shift;
							d->mixBufferU[i] = val;

							val = (int32_t) BitBufferRead( bits, (uint8_t) chanBits );
							val = (int32_t)((uint32_t)val << shift) >> shift;
							d->mixBufferV[i] = val;
						}
					}
					else
					{
						// BitBufferRead() can't read more than 16 bits at a time so break up the reads
						extraBits = chanBits - 16;
						for ( i = 0; i < numSamples; i++ )
						{
							val = (int32_t) BitBufferRead( bits, 16 );
							val = (int32_t)((uint32_t)val << 16) >> shift;
							d->mixBufferU[i] = val | BitBufferRead( bits, (uint8_t)extraBits );

							val = (int32_t) BitBufferRead( bits, 16 );
							val = (int32_t)((uint32_t)val << 16) >> shift;
							d->mixBufferV[i] = val | BitBufferRead( bits, (uint8_t)extraBits );
						}
					}

					bits1 = chanBits * numSamples;
					bits2 = chanBits * numSamples;
					mixBits = mixRes = 0;
					bytesShifted = 0;
				}

				// now read the shifted values into the shift buffer
				if ( bytesShifted != 0 )
				{
					shift = bytesShifted * 8;

					for ( i = 0; i < (numSamples * 2); i += 2 )
					{
						d->shiftBuffer[i + 0] = (uint16_t) BitBufferRead( &shiftBits, (uint8_t) shift );
						d->shiftBuffer[i + 1] = (uint16_t) BitBufferRead( &shiftBits, (uint8_t) shift );
					}
				}

				// un-mix the data and convert to output format
				// - note that mixRes = 0 means just interleave so we use that path for uncompressed frames
				switch ( d->config.bitDepth )
				{
					case 16:
						out16 = &((int16_t *)sampleBuffer)[channelIndex];
						unmix16( d->mixBufferU, d->mixBufferV, out16, numChannels, numSamples, mixBits, mixRes );
						break;
					case 20:
						out20 = (uint8_t *)sampleBuffer + (channelIndex * 3);
						unmix20( d->mixBufferU, d->mixBufferV, out20, numChannels, numSamples, mixBits, mixRes );
						break;
					case 24:
						out24 = (uint8_t *)sampleBuffer + (channelIndex * 3);
						unmix24( d->mixBufferU, d->mixBufferV, out24, numChannels, numSamples,
									mixBits, mixRes, d->shiftBuffer, bytesShifted );
						break;
					case 32:
						out32 = &((int32_t *)sampleBuffer)[channelIndex];
						unmix32( d->mixBufferU, d->mixBufferV, out32, numChannels, numSamples,
									mixBits, mixRes, d->shiftBuffer, bytesShifted );
						break;
				}

				channelIndex += 2;
				*outNumSamples = numSamples;
				break;
			}

			case ID_CCE:
			case ID_PCE:
			{
				// unsupported element, bail
				status = kALAC_ParamError;
				break;
			}

			case ID_DSE:
			{
				// data stream element -- parse but ignore
				status = DataStreamElement( bits );
				break;
			}

			case ID_FIL:
			{
				// fill element -- parse but ignore
				status = FillElement( bits );
				break;
			}

			case ID_END:
			{
				// frame end, all done so byte align the frame and check for overruns
				BitBufferByteAlign( bits, 0 );
				goto Exit;
			}
		}

		// if we've decoded all of our channels, bail
		// - this also protects us if the config does not match the bitstream or crap data bits follow the audio bits
		if ( channelIndex >= numChannels )
			break;
	}

NoMoreChannels:

	// if we get here and haven't decoded all of the requested channels, fill the remaining channels with zeros
	for ( ; channelIndex < numChannels; channelIndex++ )
	{
		switch ( d->config.bitDepth )
		{
			case 16:
				Zero16( &((int16_t *)sampleBuffer)[channelIndex], numSamples, numChannels );
				break;
			case 20:
			case 24:
				Zero24( (uint8_t *)sampleBuffer + (channelIndex * 3), numSamples, numChannels );
				break;
			case 32:
				Zero32( &((int32_t *)sampleBuffer)[channelIndex], numSamples, numChannels );
				break;
		}
	}

Exit:
	(void)bits1;
	(void)bits2;
	return status;
}

/*
	FillElement()
	- they're just filler so we don't need 'em
*/
static int32_t FillElement( BitBuffer * bits )
{
	int16_t		count;

	// 4-bit count or (4-bit + 8-bit count) if 4-bit count == 15
	// - plus this weird -1 thing I still don't fully understand
	count = BitBufferReadSmall( bits, 4 );
	if ( count == 15 )
		count += (int16_t) BitBufferReadSmall( bits, 8 ) - 1;

	BitBufferAdvance( bits, count * 8 );

	if ( bits->cur > bits->end )
		return kALAC_ParamError;

	return ALAC_noErr;
}

/*
	DataStreamElement()
	- we don't care about data stream elements so just skip them
*/
static int32_t DataStreamElement( BitBuffer * bits )
{
	int32_t		data_byte_align_flag;
	uint16_t	count;

	// the tag associates this data stream element with a given audio element
	(void) BitBufferReadSmall( bits, 4 );

	data_byte_align_flag = BitBufferReadOne( bits );

	// 8-bit count or (8-bit + 8-bit count) if 8-bit count == 255
	count = BitBufferReadSmall( bits, 8 );
	if ( count == 255 )
		count += BitBufferReadSmall( bits, 8 );

	// the align flag means the bitstream should be byte-aligned before reading the following data bytes
	if ( data_byte_align_flag )
		BitBufferByteAlign( bits, 0 );

	// skip the data bytes
	BitBufferAdvance( bits, count * 8 );

	if ( bits->cur > bits->end )
		return kALAC_ParamError;

	return ALAC_noErr;
}

/*
	ZeroN()
	- helper routines to clear out output channel buffers when decoding fewer channels than requested
*/
static void Zero16( int16_t * buffer, uint32_t numItems, uint32_t stride )
{
	for ( uint32_t index = 0; index < (numItems * stride); index += stride )
		buffer[index] = 0;
}

static void Zero24( uint8_t * buffer, uint32_t numItems, uint32_t stride )
{
	for ( uint32_t index = 0; index < (numItems * stride * 3); index += (stride * 3) )
	{
		buffer[index + 0] = 0;
		buffer[index + 1] = 0;
		buffer[index + 2] = 0;
	}
}

static void Zero32( int32_t * buffer, uint32_t numItems, uint32_t stride )
{
	for ( uint32_t index = 0; index < (numItems * stride); index += stride )
		buffer[index] = 0;
}
