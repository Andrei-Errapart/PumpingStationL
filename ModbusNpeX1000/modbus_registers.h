#ifndef modbus_registers_h_
#define modbus_registers_h_

#include <stdint.h>
#include <mystdbool.h>

enum {
	// inputs - 8 optoisolation inputs, 16 configurable inputs.
	MODBUS_INPUT_COUNT = 8 + 16,

	// coils
	// relay1, relay2 and 6 optoisolation outputs
	MODBUS_OUTPUT_COUNT = 8,

	// input_registers - 8 analog inputs.
	MODBUS_INPUT_REGISTER_COUNT = 8,
};

typedef struct {
	/// Magic to be set.
	uint32_t	activity_counter;
	/// Unused.
	uint32_t	foo_1;
	/// Digital inputs.
	bool		din[MODBUS_INPUT_COUNT];
	/// Digital outputs.
	bool		dout[MODBUS_OUTPUT_COUNT];
	/// Analog inputs.
	uint16_t	ain[MODBUS_INPUT_REGISTER_COUNT];
} modbus_registers_t;

extern "C" volatile modbus_registers_t*	modbus_registers;

#endif /* modbus_registers_h_ */
