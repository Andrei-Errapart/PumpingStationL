#ifndef modbus_slave_h_
#define modbus_slave_h_

/** \file MODBUS slave.
*
* The user should:
* 1. Implement functions as described in the header file "modbus_datastore.h".
* 2. Interface the modbus slave to the UART by implementing Uart_PutChar function and calling modbus slave function Uart_CharReceived whenever a character is received.
* 3. Initialize the library by calling ModbusSlave_Init().
*/
#include <stdint.h>	// uint8_t, etc.

#if defined(__cplusplus)
extern "C" {
#endif /* __cplusplus */

/** Initialize the library. */
extern void ModbusSlave_Init(void);

/** Implemented in the USART library. */
extern void Uart_PutChar(const uint8_t ch);
/** Call this in the USART library whenever a character is received. */
extern void Uart_CharReceived(const uint8_t ch);

#if defined(__cplusplus)
}
#endif /* __cplusplus */


#endif /* modbus_slave_h_ */
