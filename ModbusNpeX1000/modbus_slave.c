// vim: tabstop=4
#include "modbus_slave.h"
#include "modbus_datastore.h"
#if (HAVE_OS)
#include <stdio.h>	// printf
#include <sys/time.h>
#else
#include "Usart1_Lib.c"
#endif
#include <string.h>	// memset

static unsigned char Modbus_Timeout=0;
#if (HAVE_OS)
static uint64_t
time_us(void)
{
	struct timeval	tv;
	gettimeofday(&tv, 0);
	return tv.tv_sec * 1000000UL + tv.tv_usec;
}
static uint64_t		Modbus_T0 = 0;
#	define Clear_Modbus_Timeout_Counter() do { Modbus_T0 = time_us(); } while (0)
#	define	TIMEOUT_US	(1 * 1000 * 1000)
#else
#	define Clear_Modbus_Timeout_Counter() TIM_SetCounter(TIM1,0)
#endif


/** \file Modbus slave implementation. */

//=============================================================================
typedef enum {
	FUNCTION_READ_COILS = 1,
	FUNCTION_READ_DISCRETE_INPUTS = 2,
	FUNCTION_READ_HOLDING_REGISTERS = 3,
	FUNCTION_READ_INPUT_REGISTERS = 4,
	FUNCTION_WRITE_SINGLE_COIL = 5,
	FUNCTION_WRITE_SINGLE_HOLDING_REGISTER = 6,
	FUNCTION_WRITE_MULTIPLE_COILS = 15,
	FUNCTION_WRITE_MULTIPLE_REGISTERS = 16,
} FUNCTION_T;


//=============================================================================
// TODO: increase limit this when crashes!
// Read buffer.
static uint8_t _read_buffer[64];
static int _read_size = -1; // crash when ModbusSlave_Init hasn't been called.
// TODO: increase limit this when crashes!
// Write buffer.
static uint8_t _write_buffer[64];

//=============================================================================
static void Bytes_Of_UInt16(uint8_t* dst, int offset, const uint16_t value)
{
    dst[offset + 0] = (uint8_t)(value >> 8);
    dst[offset + 1] = (uint8_t)(value & 0xFF);
}

//=============================================================================
static uint16_t UInt16_Of_Bytes(const uint8_t* src, int offset)
{
    return (uint16_t)(((src[offset] & 0xFF) << 8) | (src[offset+1] & 0xFF));
}

/* Table of CRC values for high–order uint8_t */ 
static const uint8_t _auchCRCHi[] = {
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
    0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
    0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40
} ;

static const uint8_t _auchCRCLo[] = { 
    0x00, 0xC0, 0xC1, 0x01, 0xC3, 0x03, 0x02, 0xC2, 0xC6, 0x06, 0x07, 0xC7, 0x05, 0xC5, 0xC4,
    0x04, 0xCC, 0x0C, 0x0D, 0xCD, 0x0F, 0xCF, 0xCE, 0x0E, 0x0A, 0xCA, 0xCB, 0x0B, 0xC9, 0x09,
    0x08, 0xC8, 0xD8, 0x18, 0x19, 0xD9, 0x1B, 0xDB, 0xDA, 0x1A, 0x1E, 0xDE, 0xDF, 0x1F, 0xDD,
    0x1D, 0x1C, 0xDC, 0x14, 0xD4, 0xD5, 0x15, 0xD7, 0x17, 0x16, 0xD6, 0xD2, 0x12, 0x13, 0xD3,
    0x11, 0xD1, 0xD0, 0x10, 0xF0, 0x30, 0x31, 0xF1, 0x33, 0xF3, 0xF2, 0x32, 0x36, 0xF6, 0xF7,
    0x37, 0xF5, 0x35, 0x34, 0xF4, 0x3C, 0xFC, 0xFD, 0x3D, 0xFF, 0x3F, 0x3E, 0xFE, 0xFA, 0x3A,
    0x3B, 0xFB, 0x39, 0xF9, 0xF8, 0x38, 0x28, 0xE8, 0xE9, 0x29, 0xEB, 0x2B, 0x2A, 0xEA, 0xEE,
    0x2E, 0x2F, 0xEF, 0x2D, 0xED, 0xEC, 0x2C, 0xE4, 0x24, 0x25, 0xE5, 0x27, 0xE7, 0xE6, 0x26,
    0x22, 0xE2, 0xE3, 0x23, 0xE1, 0x21, 0x20, 0xE0, 0xA0, 0x60, 0x61, 0xA1, 0x63, 0xA3, 0xA2,
    0x62, 0x66, 0xA6, 0xA7, 0x67, 0xA5, 0x65, 0x64, 0xA4, 0x6C, 0xAC, 0xAD, 0x6D, 0xAF, 0x6F,
    0x6E, 0xAE, 0xAA, 0x6A, 0x6B, 0xAB, 0x69, 0xA9, 0xA8, 0x68, 0x78, 0xB8, 0xB9, 0x79, 0xBB,
    0x7B, 0x7A, 0xBA, 0xBE, 0x7E, 0x7F, 0xBF, 0x7D, 0xBD, 0xBC, 0x7C, 0xB4, 0x74, 0x75, 0xB5,
    0x77, 0xB7, 0xB6, 0x76, 0x72, 0xB2, 0xB3, 0x73, 0xB1, 0x71, 0x70, 0xB0, 0x50, 0x90, 0x91,
    0x51, 0x93, 0x53, 0x52, 0x92, 0x96, 0x56, 0x57, 0x97, 0x55, 0x95, 0x94, 0x54, 0x9C, 0x5C,
    0x5D, 0x9D, 0x5F, 0x9F, 0x9E, 0x5E, 0x5A, 0x9A, 0x9B, 0x5B, 0x99, 0x59, 0x58, 0x98, 0x88,
    0x48, 0x49, 0x89, 0x4B, 0x8B, 0x8A, 0x4A, 0x4E, 0x8E, 0x8F, 0x4F, 0x8D, 0x4D, 0x4C, 0x8C,
    0x44, 0x84, 0x85, 0x45, 0x87, 0x47, 0x46, 0x86, 0x82, 0x42, 0x43, 0x83, 0x41, 0x81, 0x80,
    0x40
};  


//=============================================================================
static int CRC16( const uint8_t* data, int offset, int size )
{ 
	int i;
    uint8_t crc_hi = 0xFF ; /* high uint8_t of CRC initialized  */
    uint8_t crc_lo = 0xFF ; /* low uint8_t of CRC initialized  */

    for (i=0; i<size; ++i)
    { 
        const uint8_t index = (uint8_t)(crc_lo ^ data[offset + i]);   /* calculate the CRC   */
        crc_lo = (uint8_t)(crc_hi ^ _auchCRCHi[index]);
        crc_hi = _auchCRCLo[index];
    } 
    return (crc_lo << 8 | crc_hi) & 0xFFFF;
}

//=============================================================================
static void _Write(const uint8_t* Data, const int Size)
{
	int i;

	for (i=0; i<Size; ++i)
	{
		Uart_PutChar(Data[i]);
	}
}

//=============================================================================
/// Verify packet checksum.
static bool _IsChecksumOK(const uint8_t* buffer, const int offset, const int size)
{
    const int crc_expected = CRC16(buffer, offset, size - 2);
    const int crc_read = UInt16_Of_Bytes(buffer, offset + size - 2);
    return crc_read == crc_expected;
}

//=============================================================================
/// Verify packet checksum.
static void  _AppendChecksum(uint8_t* Data, const int TotalSize)
{
	const int crc = CRC16(Data, 0, TotalSize - 2);
	Bytes_Of_UInt16(Data, TotalSize - 2, (const uint16_t)crc);
}

//=============================================================================
static void _SendErrorReply(const Modbus_DeviceAddress_T DeviceAddress, const unsigned int  FunctionCode, const int ExceptionCode)
{
    // 1. address.
    // 2. error code.
    // 3. exception code.
    // 4. checksum 2 bytes.
	//if(ExceptionCode==(-1)){return;}
    const int size = 5;
    _write_buffer[0] = DeviceAddress;
    _write_buffer[1] = (uint8_t)(FunctionCode | 0x80);
    _write_buffer[2] = ExceptionCode;
    Bytes_Of_UInt16(_write_buffer, size-2, (uint16_t)CRC16(_write_buffer, 0, size-2));
    _Write(_write_buffer, size);
}

//=============================================================================
void _SendSuccessReply(Modbus_DeviceAddress_T DeviceAddress, const int FunctionCode, const int RegisterOffset, const int RegisterCount)
{
    // 1. address.
    // 2. function code.
    // 3. register offset 2 bytes.
    // 4. register count 2 bytes.
    // 4. checksum 2 bytes.
    const int size = 8;
    _write_buffer[0] = DeviceAddress;
    _write_buffer[1] = FunctionCode;
    Bytes_Of_UInt16(_write_buffer, 2, (uint16_t)RegisterOffset);
    Bytes_Of_UInt16(_write_buffer, 4, (uint16_t)RegisterCount);
	_AppendChecksum(_write_buffer, size);
    _Write(_write_buffer, size);
}

//=============================================================================
int _ProcessReceivedData(int processed_sofar)
{
    // 1. Address, valid?
    if (processed_sofar < _read_size)
    {
        const int max_size = _read_size - processed_sofar;
        const uint8_t device_address = _read_buffer[processed_sofar];
		const bool is_data_store_present = Datastore_IsConnected(device_address);

        // Function, valid?
        // 2 = sizeof(address) + sizeof(function)
        // 2 = sizeof(checksum)
        if (max_size>=4)
        {
            uint8_t function_code = _read_buffer[processed_sofar + 1];
            int expected_size = 0;
            switch (function_code)
            {
                case FUNCTION_READ_COILS:
                    expected_size = 8;
                    if (max_size >= expected_size)
                    {
                        if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                        {
                            const int input_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                            const int input_count = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                            const int byte_count = (input_count + 7) / 8;
                            const int reply_size = 5 + byte_count;
							bool any_good = false;
							int dsr = MODBUS_RESULT_SUCCESS;
							int i;
							int bitidx = 0;
							int byteidx = 3;
							_write_buffer[byteidx] = 0;
							for (i=0; i<input_count; ++i)
							{
								bool r = false;
								const Modbus_Result_T	rr = Datastore_ReadCoil(device_address, input_address + i, &r);
								if (rr == MODBUS_RESULT_SUCCESS)
								{
									any_good = true;
									_write_buffer[byteidx] |= r ? (1 << bitidx) : 0;
									++bitidx;
									if (bitidx>=8)
									{
										bitidx = 0;
										++byteidx;
										_write_buffer[byteidx] = 0;
									}
								}
								else if (dsr == MODBUS_RESULT_SUCCESS)
								{
									dsr = rr;
								}
							}
                            if (any_good)
                            {
                                // GOOD!
                                // _SendReply(_read_buffer, processed_sofar, expected_size);
                                _write_buffer[0] = device_address;
                                _write_buffer[1] = function_code;
                                _write_buffer[2] = (uint8_t)(byte_count);
								_AppendChecksum(_write_buffer, reply_size);
                                _Write(_write_buffer, reply_size);
                            }
                            else
                            {
                                _SendErrorReply(device_address, function_code, dsr);
                            }
                        }
                        return processed_sofar + expected_size;
                    }
                    break;
                case FUNCTION_READ_DISCRETE_INPUTS:
                    expected_size = 8;
                    if (max_size >= expected_size)
                    {
                        if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                        {
                            const int input_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                            const int input_count = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                            const int byte_count = (input_count + 7) / 8;
                            const int reply_size = 5 + byte_count;
							bool any_good = false;
							Modbus_Result_T dsr = MODBUS_RESULT_SUCCESS;
							int input_offset;
							memset(_write_buffer, 0, sizeof(_write_buffer));
							for (input_offset=0; input_offset<input_count; ++input_offset)
							{
								bool r = false;
								Modbus_Result_T rr = Datastore_ReadDiscreteInput(device_address, input_address + input_offset, &r);
								if (rr == MODBUS_RESULT_SUCCESS)
								{
									any_good = true;
									if (r)
									{
										const int byte_index = 3 + input_offset/8;
										const int bit_index = input_offset % 8;
										_write_buffer[byte_index] |= (1 << bit_index);
									}
								} else if (dsr == MODBUS_RESULT_SUCCESS)
								{
									dsr = rr;
								}
							}
							if (any_good)
                            {
                                // GOOD!
                                // _SendReply(_read_buffer, processed_sofar, expected_size);
                                _write_buffer[0] = device_address;
                                _write_buffer[1] = function_code;
                                _write_buffer[2] = (uint8_t)(byte_count);
								_AppendChecksum(_write_buffer, reply_size);
                                _Write(_write_buffer, reply_size);
                            }
                            else
                            {
                                _SendErrorReply(device_address, function_code, dsr);
                            }
                        }
                        return processed_sofar + expected_size;
                    }
                    break;
                case FUNCTION_READ_HOLDING_REGISTERS: /** fallthrough! */
                case FUNCTION_READ_INPUT_REGISTERS:
                    expected_size = 8;
                    if (max_size >= expected_size)
                    {
                        if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                        {
                            const int register_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                            const int register_count = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                            const int reply_size = 5 + register_count*2;
							int dsr = MODBUS_RESULT_SUCCESS;
							bool any_good = false;
							int i;
							for (i=0; i<register_count; ++i)
							{
								uint16_t contents = 0;
								Modbus_Result_T rr = function_code==FUNCTION_READ_HOLDING_REGISTERS
												? Datastore_ReadHoldingRegister(device_address, register_address + i, &contents)
												: Datastore_ReadInputRegister(device_address, register_address + i, &contents);
								if (rr == MODBUS_RESULT_SUCCESS)
								{
									any_good = true;
									Bytes_Of_UInt16(_write_buffer, 3 + 2*i, contents);
								}
								else if (dsr == MODBUS_RESULT_SUCCESS)
								{
									dsr = rr;
								}
							}
							if (any_good)
                            {
                                // GOOD!
                                // _SendReply(_read_buffer, processed_sofar, expected_size);
                                _write_buffer[0] = device_address;
                                _write_buffer[1] = function_code;
                                _write_buffer[2] = (uint8_t)(register_count * 2);
								_AppendChecksum(_write_buffer, reply_size);
                                _Write(_write_buffer, reply_size);
                            }
                            else
                            {
                                _SendErrorReply(device_address, function_code, dsr);
                            }
                        }
                        return processed_sofar + expected_size;
                    }
                    break;
					// TODO: not veryfied! :(
                case FUNCTION_WRITE_SINGLE_COIL:
                    expected_size = 8;
                    if (max_size >= expected_size)
                    {
                        if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                        {
                            const int coil_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                            const int value = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                            if (value == 0 || value )
                            {
                                const int dsr = Datastore_WriteSingleCoil(device_address, coil_address, value != 0);
                                if (dsr == 0)
                                {
                                    // GOOD! Reply with a copy!
                                    _Write(_read_buffer + processed_sofar, expected_size);
                                }
                                else
                                {
                                    _SendErrorReply(device_address, function_code, dsr);
                                }
                            }
                            else
                            {
                                _SendErrorReply(device_address, function_code, MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_VALUE);
                            }
                        }
                        return processed_sofar + expected_size;
                    }
                    break;
                case FUNCTION_WRITE_MULTIPLE_COILS:
                    if (max_size >= 10)
                    {
                        const int coil_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                        const int coil_count = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                        const int byte_count = _read_buffer[processed_sofar + 6];
                        expected_size = 9 + byte_count;
                        if (max_size >= expected_size)
                        {
                            if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                            {
								int byteOffset;
								bool any_good = false;
								Modbus_Result_T dsr = MODBUS_RESULT_SUCCESS;
								int coil_offset = 0;
                                for (byteOffset = 0; byteOffset < byte_count; ++byteOffset)
                                {
									const uint8_t b = _read_buffer[processed_sofar + 7 + byteOffset];
									int bitindex;
									for (bitindex=0; bitindex<8; ++bitindex)
									{
										if (coil_offset < coil_count)
										{
											dsr = Datastore_WriteSingleCoil(device_address, coil_address + coil_offset, (b & (1 << bitindex))!=0);
											any_good = any_good || dsr==MODBUS_RESULT_SUCCESS;
										}
										++coil_offset;
									}
								}
								if (any_good)
                                {
                                    // GOOD!
                                    _SendSuccessReply(device_address, function_code, coil_address, coil_count);
                                }
                                else
                                {
                                    _SendErrorReply(device_address, function_code, dsr);
                                }

                            }
                            return processed_sofar + expected_size;
                        }
                    }
                    break;
				case FUNCTION_WRITE_SINGLE_HOLDING_REGISTER:
                    expected_size = 8;
                    if (max_size >= expected_size)
                    {
                        if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
                        {
                            const int register_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
                            const int value = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
                            const int dsr = Datastore_WriteSingleHoldingRegister(device_address, register_address, value);
                            if (dsr == 0)
                            {
                                // GOOD! Reply with a copy!
                                _Write(_read_buffer + processed_sofar, expected_size);
                            }
                            else
                            {
                                _SendErrorReply(device_address, function_code, dsr);
                            }
                        }
                        return processed_sofar + expected_size;
                    }
					break;
				case FUNCTION_WRITE_MULTIPLE_REGISTERS:
					if (max_size >= 8)
					{
						const int register_address = UInt16_Of_Bytes(_read_buffer, processed_sofar + 2);
						const int register_count = UInt16_Of_Bytes(_read_buffer, processed_sofar + 4);
						const int byte_count = _read_buffer[processed_sofar + 6];
                        expected_size = 9 + byte_count;
                        if (max_size >= expected_size)
						{
							if (is_data_store_present && _IsChecksumOK(_read_buffer, processed_sofar, expected_size))
							{
								bool any_good = false;
								int i;
								Modbus_Result_T dsr = MODBUS_RESULT_SUCCESS;
								for (i=0; i<register_count; ++i)
								{
									const int contents = UInt16_Of_Bytes(_read_buffer, processed_sofar + 7 + 2*i);
									Modbus_Result_T rr = Datastore_WriteSingleHoldingRegister(device_address, register_address + i, contents);
									if (rr == MODBUS_RESULT_SUCCESS)
									{
										any_good = true;
									}
									else if (dsr==MODBUS_RESULT_SUCCESS)
									{
										dsr = rr;
									}
								}
								if (any_good)
								{
									int i;
									for (i=0; i<6; ++i)
									{
										_write_buffer[i] = _read_buffer[processed_sofar + i];
									}
									_AppendChecksum(_write_buffer, 8);
									_Write(_write_buffer, 8);
								}
								else
								{
									_SendErrorReply(device_address, function_code, dsr);
								}
							}
							return processed_sofar + expected_size;
						}
					}
					break;
                default:
                    // TODO: what to do?
                    // spoil all the input!
                    return _read_size;
            }
        }
    }

    return processed_sofar;
}


//=============================================================================
void ModbusSlave_Init(void)
{
	_read_size = 0;
#if ( HAVE_OS)
	Modbus_T0 = time_us();
#else
	USART1_Init(115200);
#endif
}


//=============================================================================
void Uart_CharReceived(const uint8_t ch)
{
	int processed_sofar = 0;
	int next_processed_sofar = 0;
	int remaining;

#if (HAVE_OS)
	const uint64_t	t1 = time_us();
	const uint64_t	dt = t1 - Modbus_T0;
	Modbus_Timeout = Modbus_T0!=0 && dt>TIMEOUT_US ? 1 : 0;
#endif

	// 1. Read timeout should result in discarding previous input.
	if (Modbus_Timeout==1)
	{
		Clear_Modbus_Timeout_Counter();
		_read_size = 0;
		Modbus_Timeout=0;
	}

	// Data alread in place, only have to increase the counter.
	Clear_Modbus_Timeout_Counter();

	
	_read_buffer[_read_size] = ch;
	++_read_size;

	// 2. Process remaining stuff.
	do
	{
		processed_sofar = next_processed_sofar;
		next_processed_sofar = _ProcessReceivedData(processed_sofar);
	} while (processed_sofar != next_processed_sofar);
	processed_sofar = next_processed_sofar;
	remaining = _read_size - processed_sofar;
	if (remaining > 0 && processed_sofar>0)
	{
		int i;
		// Data has to remain at the beginning, simple & stupid.
		// src src_offset dst dst_offset length
		for (i=0; i<remaining; ++i)
		{
			_read_buffer[i] = _read_buffer[processed_sofar + i];
		}
	}
	_read_size = remaining;
	if (_read_size >= sizeof(_read_buffer))
	{
		// OVERFLOW! Revert to zero.
		_read_size = 0;
	}
}

