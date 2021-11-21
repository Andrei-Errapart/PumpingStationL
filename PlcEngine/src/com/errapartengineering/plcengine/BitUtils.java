package com.errapartengineering.plcengine;

/**
 * 1. Conversion of bytes to/from strings. 
 * 2. CRC algorithms.
 * 
 * @author Andrei
 */
public abstract class BitUtils {
    public static String String_Of_Bytes(byte[] src, int offset, int size)
    {
        StringBuilder sb = new StringBuilder();
        
        for (int i=0; i<size; ++i)
        {
            int sx = src[i] & 0xFF;
            if (i>0)
            {
                sb.append(' ');
            }
            sb.append(_hex.charAt(sx >> 4));
            sb.append(_hex.charAt(sx & 0x0F));
        }
        return sb.toString();
    }

    private final static String _hex = "0123456789ABCDEF";

    public static void Bytes_Of_UInt16_BE(byte[] dst, int offset, int ushort)
    {
        dst[offset + 0] = (byte)(ushort >> 8);
        dst[offset + 1] = (byte)(ushort & 0xFF);
    }

    public static int UInt16_Of_Bytes_BE(byte[] src, int offset)
    {
        return ((src[offset] & 0xFF) << 8) | (src[offset+1] & 0xFF);
    }
    
    public static int Int16_Of_Bytes_BE(byte[] src, int offset)
    {
        return	  (src[offset+0] << 8)
                | (src[offset+1] & 0xFF);
    }
    
    public static void Bytes_Of_Int32_BE(byte[] dst, int offset, int value)
    {
        dst[offset + 0] = (byte)(value >> 24);
        dst[offset + 1] = (byte)(value >> 16);
        dst[offset + 2] = (byte)(value >> 8);
        dst[offset + 3] = (byte)(value);
    }

    public static int Int32_Of_Bytes_BE(byte[] src, int offset)
    {
        return ((src[offset+0] & 0xFF) << 24)
                | ((src[offset+1] & 0xFF) << 16)
                | ((src[offset+2] & 0xFF) << 8)
                | (src[offset+3] & 0xFF);
    }
    
    public static void Bytes_Of_Int64_BE(byte[] dst, int offset, long value)
    {
        dst[offset + 0] = (byte)(value >> 56);
        dst[offset + 1] = (byte)(value >> 48);
        dst[offset + 2] = (byte)(value >> 40);
        dst[offset + 3] = (byte)(value >> 32);
        dst[offset + 4] = (byte)(value >> 24);
        dst[offset + 5] = (byte)(value >> 16);
        dst[offset + 6] = (byte)(value >> 8);
        dst[offset + 7] = (byte)(value);
    }

    public static long Int64_Of_Bytes_BE(byte[] src, int offset)
    {
        int high = Int32_Of_Bytes_BE(src, offset);
        int low = Int32_Of_Bytes_BE(src, offset+4);
        long r = (((long)high) << 32) | (low & 0xFFFFFFFFL);
        return r;
    }
    

    /**
     * CRC-8 C-2 algorithm. Best suited for up to 14 bytes of data.
     * Polynomial: 0x12F (0x97 as used in the code).
     * Hamming distance: 4, 8-119 bits, 2, 120-256 bits.
     * @return 
     */
    public static byte crc8c2_update(byte crc, byte value)
    {
        int x = (crc & 0xFF) ^ (value & 0xFF);
        for (int i = 0; i < 8; ++i)
        {
                    x = (x >> 1) ^ ((x & 1) * 0x97);
        }

        return (byte)x;
    }
    
    /** Apply crc8c2_update on a slice of an array.
     * @param Data Data to calculate CRC of.
     * @param Offset Starting offset.
     * @param Size Size of data, in bytes.
     * @return CRC.
     */
    public static byte crc8c2_of(byte[] Data, int Offset, int Size)
    {
        byte crc = 0;
        for (int i=0; i<Size; ++i)
        {
            crc = crc8c2_update(crc, Data[Offset+i]);
        }
        return crc;
    }
}
