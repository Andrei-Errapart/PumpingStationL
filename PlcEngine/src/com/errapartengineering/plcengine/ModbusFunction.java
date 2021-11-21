package com.errapartengineering.plcengine;

import java.util.*;
// import java.util.

/**
 *
 * @author Andrei
 */
public enum ModbusFunction {
    READ_COILS((byte)1),
    READ_DISCRETE_INPUTS((byte)2),
    READ_HOLDING_REGISTERS((byte)3),
    READ_INPUT_REGISTERS((byte)4),
    WRITE_SINGLE_COIL((byte)5),
    WRITE_SINGLE_HOLDING_REGISTER((byte)6),
    WRITE_MULTIPLE_COILS((byte)15);

    public byte Value;

    ModbusFunction(byte value)
    {
        this.Value = value;
    }
    
    public static ModbusFunction ofByte(byte b)
    {
        return _map.get(b);
    }
    private static final Map<Byte, ModbusFunction> _map;
    
    static {
        _map = new HashMap<Byte, ModbusFunction>();
        for (ModbusFunction mf : ModbusFunction.values()) {
            _map.put(mf.Value, mf);
        }
    }
}
