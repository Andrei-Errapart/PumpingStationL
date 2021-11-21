package com.errapartengineering.plcengine;

/**
 *
 * @author Andrei
 */
public enum IOType {
    // Bit. Input only.
    DISCRETE_INPUT(0, false, 1),
    // Bit. Read-write.
    COIL(1, true, 1),
    // 16-bit word. Read-only.
    INPUT_REGISTER(2, false, 16),
    // 16-bit word. Read-write.
    HOLDING_REGISTER(3, true, 16),
    // 32-bit register, not standard.
    REGISTER32(4, true, 32);
    
    public byte Value;
    public boolean CanWrite;
    public int BitCount;
    
    IOType(int Value, boolean CanWrite, int BitCount)
    {
        this.Value = (byte)Value;
        this.BitCount = BitCount;
        this.CanWrite = CanWrite;
    }
    
    /** Return IOType according to the string specification.
     * Recognizes "input", "output", "input_register" and "holding_register".
     * @param s String to be analyzed.
     * @return IOType, or null, when not recognized.
     */
    public static IOType OfString(String s)
    {
        if (s==null)
        {
            return null;
        }
        if (s.equalsIgnoreCase("input"))
        {
            return DISCRETE_INPUT;
        }
        if (s.equalsIgnoreCase("output"))
        {
            return COIL;
        }
        if (s.equalsIgnoreCase("input_register"))
        {
            return INPUT_REGISTER;
        }
        if (s.equalsIgnoreCase("holding_register"))
        {
            return HOLDING_REGISTER;
        }
        if (s.equalsIgnoreCase("register32"))
        {
        	return REGISTER32;
        }
        return null;
    }
}
