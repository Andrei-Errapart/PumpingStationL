package com.errapartengineering.plcengine;

import java.util.*;

import android.util.Log;

/** Format of a row in the circular log database.
 *
 * Format is as follows:
 * 0..7: System.currentTimeMillis().
 * 8..11: Scheme version (automatic).
 * 12...: Signals, bit-packed. Each signal is a tuple(connected,value).
 * All fields are big-endian.
 * @author Andrei
 */
public abstract class CircularRowFormat {
    public final static int TIME_OFFSET = 0;
    public final static int VERSION_OFFSET = 8;
    public final static int SIGNALS_OFFSET = 12;
    
    /// Timestamp + Bits 
    public static int Count_Bytes_Needed(List<IOSignal> signals)
    {
        int bitcount = 0;
        for (IOSignal s : signals)
        {
            bitcount += s.BitLength + 1;
        }
        int bytecount = (bitcount + 7) / 8 + 8 + 4;
        return bytecount;
    }
    
    public static void Encode(byte[] Dst, int offset, List<IOSignal> signals, int Version)
    {
        BitUtils.Bytes_Of_Int64_BE(Dst, offset + TIME_OFFSET, System.currentTimeMillis());
        BitUtils.Bytes_Of_Int32_BE(Dst, offset + VERSION_OFFSET, Version);
        int[] offsets = new int[] { offset + SIGNALS_OFFSET, 7 };

        for (IOSignal s : signals)
        {
            _EncodeBit(Dst, offsets, s.Device==null ? 1 : (s.Device.IsLastSyncOk ? 1 : 0));
            if (s.Type==IOType.COIL || s.Type==IOType.DISCRETE_INPUT)
            {
                _EncodeBit(Dst, offsets, s.getValue());
            }
            else
            {
                int value = s.getValue();
                for (int i=s.BitLength-1; i>=0; --i)
                {
                    _EncodeBit(Dst, offsets, (value>>i) & 1);
                }
            }
        }
    }
    
    public static void RestoreVariables(List<IOSignal> SignalsAndVariables, byte[] Bits)
    {
        int[] Indices = new int[] { SIGNALS_OFFSET, 7};
        for (IOSignal ios : SignalsAndVariables)
        {
            int value = 0;
            /* int is_connected = */ _DecodeBit(Bits, Indices);
            for (int i = 0; i < ios.Type.BitCount; ++i)
            {
                value = (value << 1) | _DecodeBit(Bits, Indices);
            }
            if (ios.IsVariable && ios.IsNonVolatile)
            {
            	Log.d("RestoreVariables", "Restored '" + ios.Name + "' to value '" + value + "'.");
                ios.setValue(value);
            }
        }
    }
    
    public static void decode(boolean[] is_connected, int[] values, byte[] bits, List<IOSignal> signals)
    {
        int[] Indices = new int[] { SIGNALS_OFFSET, 7};
        int signal_index = 0;
        for (IOSignal ios : signals)
        {
            int value = 0;
            is_connected[signal_index] = _DecodeBit(bits, Indices) != 0;
            for (int i = 0; i < ios.Type.BitCount; ++i)
            {
                value = (value << 1) | _DecodeBit(bits, Indices);
            }
            values[signal_index] = value;
            ++signal_index;
        }    	
    }
    
    private static void _EncodeBit(byte[] Dst, int[] offsets, int bit)
    {
        // On first use the byte might contain garbage.
        if (offsets[1] == 7)
        {
            Dst[offsets[0]] = 0;
        }
        
        // Encode bits.
        Dst[offsets[0]] |= bit << offsets[1];
        --offsets[1];
        if (offsets[1] < 0)
        {
            ++offsets[0];
            offsets[1] = 7;
        }
    }
    
    private static int _DecodeBit(byte[] Packet, int[] Indices)
    {
        // Indices[0] = ByteIndex
        // Indices[1] = BitIndex;
        int r = (Packet[Indices[0]] >> Indices[1]) & 1;
        --Indices[1];
        if (Indices[1] < 0)
        {
            Indices[1] = 7;
            ++Indices[0];
        }
        return r;
    }
}
