#ifndef modbus_datastore_h_
#define modbus_datastore_h_

#include <stdint.h>		// uint8_t, etc.
#include "mystdbool.h"	// bool, true, false.

#if defined(__cplusplus)
extern "C" {
#endif /* __cplusplus */

typedef uint8_t		Modbus_DeviceAddress_T;

/** 0=success, everything else is failure. */
typedef int			Modbus_Result_T;
#define MODBUS_RESULT_SUCCESS							(0)
#define MODBUS_RESULT_EXCEPTION_ILLEGAL_FUNCTION		(1)
#define MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_ADDRESS	(2)
#define MODBUS_RESULT_EXCEPTION_ILLEGAL_DATA_VALUE		(2)

/** Return true iff there is a device at the given address. */
extern bool Datastore_IsConnected(const Modbus_DeviceAddress_T Address);

/** Read coils (back). */
extern Modbus_Result_T Datastore_ReadCoil(const Modbus_DeviceAddress_T Address, int CoilAddress, bool* Contents);

/** Read single discrete inputs. */
extern Modbus_Result_T Datastore_ReadDiscreteInput(const Modbus_DeviceAddress_T Address, int InputAddress, bool* Content);

/** Read single holding register. */
extern Modbus_Result_T Datastore_ReadHoldingRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, uint16_t* Contents);

/** Read single input registers */
extern Modbus_Result_T Datastore_ReadInputRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, uint16_t* Contents);

/** Write single coil. */
extern Modbus_Result_T Datastore_WriteSingleCoil(const Modbus_DeviceAddress_T Address, int CoilAddress, bool Value);

/** Write single holding register. */
extern Modbus_Result_T Datastore_WriteSingleHoldingRegister(const Modbus_DeviceAddress_T Address, int RegisterAddress, int Value);


#if defined(__cplusplus)
}
#endif /* __cplusplus */

#endif /* modbus_datastore_h_ */
