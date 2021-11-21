package com.errapartengineering.plcengine;

import android.util.Log;


/**
 * IO device of a given type, a Modbus module.
 * The type is determined at the time of construction.
 * @author Andrei
 */
public class IODevice {
    /** Modbus address, 0 for the built-in NPE. */
    public final int DeviceAddress;
    /** One of DISCRETE_INPUT, COIL, INPUT_REGISTER. */
    public final IOType Type;
    /** Start address of the input/coil/counter. */
    public final int StartAddress;
    /** Number of registers, if any. */
    public final int Count;
    /** Reading period, in milliseconds. */
    public final long ReadingPeriodMs;
    /** No "Read multiple registers" command. Should default to "false". */
    public final boolean NoReadMultiple;
    
    // Last read (or write) time, ms.
    public long LastSyncTimeMs;
    // Was last read successful?
    public boolean IsLastSyncOk;    
    // Data. Discrete inputs and coils are represented as bit fields, counters are stored as is.
    public boolean[] BitData;
    public int[] Data;
    
    public IODevice(int DeviceAddress, IOType Type, int StartAddress, int Count, long ReadingPeriodMs, boolean NoReadMultiple)
    {
        this.DeviceAddress = DeviceAddress;
        this.Type = Type;
        this.StartAddress = StartAddress;
        this.Count = Count;
        this.ReadingPeriodMs = ReadingPeriodMs;
        this.NoReadMultiple = NoReadMultiple;
        this.LastSyncTimeMs = 0;
        this.IsLastSyncOk = false;
        String s_reading_period = ReadingPeriodMs>0L ? (", ReadingPeriodMs=" + ReadingPeriodMs) : "";
        String s_no_read_multiple = ReadingPeriodMs>0L ? (", NoReadMultiple=" + NoReadMultiple) : "";
        Log.d("IODevice", "Addr=" + DeviceAddress + ", Type=" + Type + ", StartRegister=" + StartAddress + ", Count="+Count + s_reading_period + s_no_read_multiple);
    }

    public void SyncWithModbus(ModbusMaster mm) throws Exception
    {
    	final long t1 = System.currentTimeMillis();
    	final boolean time_to_read = ReadingPeriodMs==0 || LastSyncTimeMs==0 || (LastSyncTimeMs + ReadingPeriodMs < t1);
    	boolean time_for_holding_register = time_to_read;
        switch (Type)
        {
            case DISCRETE_INPUT:
            	if (time_to_read)
            	{
	                IsLastSyncOk = false;
	                if (BitData==null)
	                {
	                    BitData = new boolean[Count];
	                }
	                mm.ReadDiscreteInputs(BitData, DeviceAddress, StartAddress, Count);
	                LastSyncTimeMs = System.currentTimeMillis();
	                IsLastSyncOk = true;
            	}
                break;
            case COIL:
                IsLastSyncOk = false;
                if (BitData==null)
                {
                    boolean[] bit_data = new boolean[Count];
                    _ReadCoilBits(bit_data, mm);
                    BitData = bit_data;
                    _BitDataToBeWritten = new boolean[Count];
                    for (int i=0; i<Count; ++i)
                    {
                        _BitDataToBeWritten[i] = BitData[i];
                    }
                    LastSyncTimeMs = System.currentTimeMillis();
                    IsLastSyncOk = true;
                }
                else
                {
                    boolean is_dirty = false;
                    int coils_to_write = 0; // for Modbus module.
                    // Update our view of the world.
                    _ReadCoilBits(BitData, mm);
                    
                    for (int i=0; i<Count; ++i)
                    {
                        if (BitData[i] != _BitDataToBeWritten[i])
                        {
                            is_dirty = true;
                        }
                        if (_BitDataToBeWritten[i])
                        {
                            coils_to_write |= (1 << i);
                        }
                    }
                    if (is_dirty)
                    {
                        // modbus module.
                        mm.WriteMultipleCoils(DeviceAddress, StartAddress, coils_to_write, Count);
                    }
                    // By this time, it should be OK.
                    for (int i=0; i<Count; ++i)
                    {
                        BitData[i] = _BitDataToBeWritten[i];
                    }

                    LastSyncTimeMs = System.currentTimeMillis();
                    IsLastSyncOk = true;
                }
                break;
            case INPUT_REGISTER:
            	if (time_to_read)
            	{
	                IsLastSyncOk = false;
	                if (Data==null)
	                {
	                    Data = new int[Count];
	                }
	                mm.ReadInputRegisters(Data, DeviceAddress, StartAddress, Count);
	                LastSyncTimeMs = System.currentTimeMillis();
	                IsLastSyncOk = true;
            	}
                break;
            case HOLDING_REGISTER:
                if (Data==null)
                {
                    Data = new int[Count];
                    _DataToBeWritten = new int[Count];
                    for (int i=0; i<Count; ++i)
                    {
                        _DataToBeWritten[i] = -1;
                    }
                }
            	for (int RegisterOffset=0; RegisterOffset<Count; ++RegisterOffset)
            	{
            		time_for_holding_register = time_for_holding_register || _DataToBeWritten[RegisterOffset]>=0;
            	}
            	if (time_for_holding_register)
            	{
                    IsLastSyncOk = false;
	                if (NoReadMultiple)
	                {
	                	int[] xdata = new int[1];
	                	// one separate for each register.
	                	for (int RegisterOffset=0; RegisterOffset<Count; ++RegisterOffset)
	                	{
	                    	// Normal mode.
		                    int value = _DataToBeWritten[RegisterOffset];
		                    if (value<0)
		                    {
		    	                mm.ReadHoldingRegisters(xdata, DeviceAddress, StartAddress + RegisterOffset, 1);
		    	                Data[RegisterOffset] = xdata[0];
		                    }
		                    else
		                    {
		                    	// FIXME: ICPDAS special?
	                            mm.WriteHoldingRegister(DeviceAddress, StartAddress + RegisterOffset, value);
		                        _DataToBeWritten[RegisterOffset] = -1;
		                        Data[RegisterOffset] = value;
		                    }
	                	}
	                }
	                else
	                {
	                	// Normal mode.
		                mm.ReadHoldingRegisters(Data, DeviceAddress, StartAddress, Count);
		                for (int i=0; i<Count; ++i)
		                {
		                    int value = _DataToBeWritten[i];
		                    if (value >= 0)
		                    {
		                        if (value == 0)
		                        {
		                            // ICPDAS special.
		                            mm.ClearHoldingRegister(DeviceAddress, StartAddress + i);
		                        }
		                        else
		                        {
		                            mm.WriteHoldingRegister(DeviceAddress, StartAddress + i, value);
		                        }
		                        _DataToBeWritten[i] = -1;
		                    }
		                }
	                }
	                LastSyncTimeMs = System.currentTimeMillis();
	                IsLastSyncOk = true;
            	}
                break;
            case REGISTER32:
            	// Not supported by any device.
            	break;
        }
    }
    
    /** Write one holding register or coil.
     * NB! Nothing is written until the first round.
     */
    public void Write(int Address, int Value)
    {
        switch (Type)
        {
            case COIL:
                if (_BitDataToBeWritten!=null)
                {
                    _BitDataToBeWritten[Address - StartAddress] = Value!=0;
                }
                break;
            case HOLDING_REGISTER:
                if (_DataToBeWritten!=null)
                {
                    _DataToBeWritten[Address - StartAddress] = Value;
                }
                break;
            case DISCRETE_INPUT:
            	break;
            case INPUT_REGISTER:
            	break;
            case REGISTER32:
            	// not supported by any device.
            	break;
        }
    }
    
    /** Read coils data from the given modbus device.
     * 
     * @param bit_data
     * @param mm
     * @throws Exception 
     */
    private void _ReadCoilBits(boolean[] bit_data, ModbusMaster mm) throws Exception
    {
        mm.ReadCoils(bit_data, DeviceAddress, StartAddress, Count);
    }
    
    // Is written coil when differs from BitData.
    private boolean[] _BitDataToBeWritten;
    // Is written to holding register when >= 0.
    private int[] _DataToBeWritten;
}
