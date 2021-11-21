package com.errapartengineering.plcengine;

/**
 * Signal exported to the outside world.
 * 
 * Also provides interface to get/set values.
 * In case of bit values, 0=false, 1=true.
 * 
 * Signals can be used as variables when specifying all id-s as zeroes.
 * @author Andrei
 */
public class IOSignal {
    /** Name of the signal, if any present. */
    public final String Name;
    /** Identifier. */
    public final int Id;
    /** Discrete input, coil, etc. */
    public final IOType Type;
    /** Device itself, if any. */
    public IODevice Device = null;
    /** IO register index in the device. */
    public final int IOIndex;
    /** Length, in bits. */
    public final int BitLength;
    /** Is it a variable? */
    public final boolean IsVariable;
    /** Description string, if any. */
    public final String Description;
    /** Shall the write to the database check be skipped for this variable? */
    public final boolean SkipWriteCheck;
    /** Is it non-volatile? Non-volatile variables are reloaded on start-up. */
    public final boolean IsNonVolatile;

    /** Internal storage for variables. */
    private int _Value = 0;
    
    public IOSignal (String Name, int Id, IOType Type, int IOIndex, String Description, boolean SkipWriteCheck, boolean IsNonVolatile)
    {
        this.Name = Name;
        this.Id = Id;
        this.Type = Type;
        this.IOIndex = IOIndex;
        this.BitLength = Type.BitCount;
        this.IsVariable = IOIndex<0 || Id<0;
        this.Description = Description;
        this.SkipWriteCheck = SkipWriteCheck;
        this.IsNonVolatile = IsNonVolatile;
    }
    
    /** 
     * Get value. Call Device.SyncWithModbus to refresh value.
     * 
     * Discrete inputs and coils: 0=false, 1=true.
     * Input registers and holding registers: 16 bits of data.
     * @return Value of the signal.
     */
    public final int getValue()
    {
        if (Device == null)
        {
            return _Value;
        }
        else
        {
            // When there has no connection been made, the Device.BitData or Device.Data might not be set.
        	if (Type.BitCount==1)
        	{
                return Device.BitData==null
                        ? 0
                        : (Device.BitData[IOIndex - Device.StartAddress] ? 1 : 0);
        	}
        	else
        	{
                return Device.Data==null
                        ? 0
                        : Device.Data[IOIndex - Device.StartAddress];
        	}
        }
    }
    
    /** Set the signal in the device. Call Device.SyncWithModbus to actually write it.
     * Note: StartupGroup is NOT used.
     * 
     * @param Value Value to be set.
     */
    public final void setValue(int Value)
    {
        if (Device == null)
        {
            _Value = Value;
        }
        else
        {
            Device.Write(IOIndex, Value);
        }
    }

    public static final IOSignal findSignalByName(java.util.List<IOSignal> signals, String name) throws ApplicationException
    {
		for (IOSignal s : signals)
		{
			if (s.Name.equals(name))
			{
				return s;
			}
		}
		throw new ApplicationException("IOSignal: Signal '" + name + "' not found.");
    }
    
    @Override
    public String toString()
    {
        if (Name.length()>0)
        {
            return "Signal " + Name + " (id:" + Id + ")";
        }
        else
        {
            return "Signal (id:" + Id + ")";
        }
    }
}
