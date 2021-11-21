/** \file Sample datastore. */
#include <stdio.h>
//#include "assert.h"
#include <stdint.h>
#include <mystdbool.h>
#include <modbus_datastore.h>
#include <modbus_registers.h>

#define	ADDR	1



extern "C" {

volatile modbus_registers_t*	modbus_registers = 0;

//=============================================================================
bool Datastore_IsConnected(const Modbus_DeviceAddress_T Address)
{
	return Address==ADDR && modbus_registers!=0;
}


//=============================================================================
Modbus_Result_T Datastore_ReadCoil(const Modbus_DeviceAddress_T Address, int CoilAddress, bool* result)
{
	if (CoilAddress>=0 && CoilAddress<MODBUS_OUTPUT_COUNT) {
		++ modbus_registers->activity_counter;
		*result = modbus_registers->dout[CoilAddress];
		return MODBUS_RESULT_SUCCESS;
	} else {
		return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
	}
}

//=============================================================================
Modbus_Result_T Datastore_ReadDiscreteInput(const Modbus_DeviceAddress_T Address, int InputAddress, bool* Content)
{
	if (InputAddress>=0 && InputAddress<MODBUS_INPUT_COUNT) {
		++ modbus_registers->activity_counter;
		*Content = modbus_registers->din[InputAddress];
		return MODBUS_RESULT_SUCCESS;		
	} else {
		return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
	}
}

//=============================================================================
Modbus_Result_T Datastore_ReadHoldingRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, uint16_t* Contents)
{
	if (RegisterAddress>=0 && RegisterAddress<MODBUS_INPUT_REGISTER_COUNT) {
		++ modbus_registers->activity_counter;
		*Contents = modbus_registers->ain[RegisterAddress];
		return MODBUS_RESULT_SUCCESS;
	} else {
		return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
	}
}

//=============================================================================
Modbus_Result_T Datastore_ReadInputRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, uint16_t* Contents)
{
	if (RegisterAddress>=0 && RegisterAddress<MODBUS_INPUT_REGISTER_COUNT) {
		++ modbus_registers->activity_counter;
		*Contents = modbus_registers->ain[RegisterAddress];
		return MODBUS_RESULT_SUCCESS;
	} else {
		return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
	}
}

//=============================================================================
Modbus_Result_T Datastore_WriteSingleCoil(const Modbus_DeviceAddress_T Address, int CoilAddress, bool Value)
{
	if (CoilAddress>=0 && CoilAddress<MODBUS_OUTPUT_COUNT) {
		++ modbus_registers->activity_counter;
		modbus_registers->dout[CoilAddress] = Value;
		return MODBUS_RESULT_SUCCESS;
	} else {
		return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
	}
}

//=============================================================================
Modbus_Result_T Datastore_WriteSingleHoldingRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, int Value)
{
	return MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS;
}

} // extern "C"
