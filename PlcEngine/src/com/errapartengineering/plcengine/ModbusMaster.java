/*
 * Modbus Master functionality, very simple.
 */
package com.errapartengineering.plcengine;

import java.io.*;

/**
 * ModbusMaster, suitable for any kind of streams.
 * 
 * Very straightforward code.
 * 
 * @author Andrei
 */
public final class ModbusMaster {
    public long TimeoutMs = 1000;
    
    public ModbusMaster(InputStream inStream, OutputStream outStream)
    {
        _in = inStream;
        _out = outStream;
    }

    /// Read input registers.
    public final void ReadInputRegisters(int[] Destination, int DeviceAddress, int RegisterOffset, int RegisterCount) throws Exception
    {
    	_ReadUint16s("ReadInputRegisters", ModbusFunction.READ_INPUT_REGISTERS.Value, Destination, DeviceAddress, RegisterOffset, RegisterCount);
    }

    /// Read holding registers.
    public final void ReadHoldingRegisters(int[] Destination, int DeviceAddress, int RegisterOffset, int RegisterCount) throws Exception
    {
    	_ReadUint16s("ReadHoldingRegisters", ModbusFunction.READ_HOLDING_REGISTERS.Value, Destination, DeviceAddress, RegisterOffset, RegisterCount);
    }
    
    public final void ReadDiscreteInputs(boolean[] Destination, int DeviceAddress, int InputOffset, int InputCount) throws Exception
    {
        _ReadBits("ReadDiscreteInputs", ModbusFunction.READ_DISCRETE_INPUTS.Value, Destination, DeviceAddress, InputOffset, InputCount);
   }
    
    public final void ReadCoils(boolean[] Destination, int DeviceAddress, int InputOffset, int InputCount) throws Exception
    {
        _ReadBits("ReadCoils", ModbusFunction.READ_COILS.Value, Destination, DeviceAddress, InputOffset, InputCount);
    }
    
    public final void WriteSingleCoil(int DeviceAddress, int CoilAddress, boolean Value) throws Exception
    {
        _WriteUint16("WriteSingleCoil", ModbusFunction.WRITE_SINGLE_COIL.Value, DeviceAddress, CoilAddress, Value ? 0xFF00 : 0x0000);
    }

    // Not supported by M-7000 series!
    public final void WriteHoldingRegister(int DeviceAddress, int RegisterAddress, int Value) throws Exception
    {
        _WriteUint16("WriteHoldingRegister", ModbusFunction.WRITE_SINGLE_HOLDING_REGISTER.Value, DeviceAddress, RegisterAddress, Value);
    }

    /// Clear holding register. Only on the M-7000 series!
    public final void ClearHoldingRegister(int DeviceAddress, int RegisterAddress) throws Exception
    {
        _WriteUint16("ClearHoldingRegister", ModbusFunction.WRITE_SINGLE_COIL.Value, DeviceAddress, RegisterAddress + 0x200, 0xFF00);
    }
    
    // Write up to 32 coils.
    public final void WriteMultipleCoils(int DeviceAddress, int CoilAddress, int Coils, int CoilCount) throws Exception
    {
        byte FunctionCode = ModbusFunction.WRITE_MULTIPLE_COILS.Value;
        String FunctionName = "WriteMultipleCoils";
        
        // 1. Write the lovely query :)
        int coil_bytes = (CoilCount + 7) / 8;
        int total_bytes = 9 + coil_bytes;
        int tmp_coils = Coils;
        _out_buf[0] = (byte)DeviceAddress;
        _out_buf[1] = FunctionCode;
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 2, CoilAddress);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 4, CoilCount);
        _out_buf[6] = (byte)coil_bytes;
        for (int i=0; i<coil_bytes; ++i)
        {
            _out_buf[7 + i] = (byte)(tmp_coils & 0xFF);
            tmp_coils >>= 8;
        }
        int in_crc1 = ModbusFormat.CRC16(_out_buf, 0, total_bytes-2);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, total_bytes-2, in_crc1);
        _PurgeInput();
        _out.write(_out_buf, 0, total_bytes);
        _out.flush();
        
        // 2. Read it.
        int expected = 8;
        _ReadInputFailIfIncorrect(FunctionName, expected, DeviceAddress, FunctionCode, -1);

        // 3. Was the reply Ok?
        int read_coil_address = BitUtils.UInt16_Of_Bytes_BE(_in_buf, 2);
        if (read_coil_address != CoilAddress)
        {
            throw new ApplicationException(FunctionName + ": Invalid coil address in reply, expected " + CoilAddress + ", got " + read_coil_address + ".");
        }
        
        int read_coil_count = BitUtils.UInt16_Of_Bytes_BE(_in_buf, 4);
        if (read_coil_count != CoilCount)
        {
            throw new ApplicationException(FunctionName + ": Invalid coil count in reply, expected " + CoilCount + ", got " + read_coil_count + ".");
        }
    }
    
    private void _ReadBits(String FunctionName, byte FunctionCode, boolean[] Destination, int DeviceAddress, int InputOffset, int InputCount) throws Exception
    {
        // 1. Write the lovely query :)
        _out_buf[0] = (byte)DeviceAddress;
        _out_buf[1] = FunctionCode;
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 2, InputOffset);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 4, InputCount);
        int in_crc1 = ModbusFormat.CRC16(_out_buf, 0, 6);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 6, in_crc1);
        _PurgeInput();
        _out.write(_out_buf, 0, 8);
        _out.flush();

        // 2. Read response, if any...
        int expected_payload = (InputCount + 7) / 8;
        int expected = 5 + expected_payload;
        _ReadInputFailIfIncorrect(FunctionName, expected, DeviceAddress, FunctionCode, expected_payload);
        
        // 3. Transfer to the output buffers.
        int in_offset = 3;
        int byte_mask = 1;
        for (int i=0; i<InputCount; ++i)
        {
            Destination[i] = ((_in_buf[in_offset] & 0xFF) & byte_mask) != 0;
            byte_mask <<= 1;
            if (byte_mask  > 0xFF)
            {
                byte_mask = 1;
                ++in_offset;
            }
        }
    }

    /// Read input/holding registers.
    public final void _ReadUint16s(String FunctionName, byte FunctionCode, int[] Destination, int DeviceAddress, int RegisterOffset, int RegisterCount) throws Exception
    {
        // 1. Write the lovely query :)
        _out_buf[0] = (byte)DeviceAddress;
        _out_buf[1] = FunctionCode;
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 2, RegisterOffset);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 4, RegisterCount);
        int in_crc1 = ModbusFormat.CRC16(_out_buf, 0, 6);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 6, in_crc1);
        _PurgeInput();
        _out.write(_out_buf, 0, 8);
        _out.flush();
        
        // 2. Read response, if any...
        int expected_payload = 2*RegisterCount;
        int expected = 5 + expected_payload;
        _ReadInputFailIfIncorrect(FunctionName, expected, DeviceAddress, FunctionCode, expected_payload);
      
        // 3. Transfer to the output buffers.
        for (int i=0; i<RegisterCount; ++i)
        {
            Destination[i] = BitUtils.UInt16_Of_Bytes_BE(_in_buf,  3 + 2*i);
        }
    }
    
    private void _WriteUint16(String FunctionName, byte FunctionCode, int DeviceAddress, int RegisterAddress, int Value) throws Exception
    {
        // 1. Write the lovely query.
        _out_buf[0] = (byte)DeviceAddress;
        _out_buf[1] = FunctionCode;
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 2, RegisterAddress);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 4, Value);
        int in_crc1 = ModbusFormat.CRC16(_out_buf, 0, 6);
        BitUtils.Bytes_Of_UInt16_BE(_out_buf, 6, in_crc1);
        _PurgeInput();
        _out.write(_out_buf, 0, 8);
        _out.flush();
        
        // 2. Read response, if any...
        int expected = 8;
        _ReadInputFailIfIncorrect(FunctionName, expected, DeviceAddress, FunctionCode, -1);

        // 3. Check it!
        int read_coil_address = BitUtils.UInt16_Of_Bytes_BE(_in_buf, 2);
        if (read_coil_address != RegisterAddress)
        {
            throw new ApplicationException(FunctionName + ": Invalid coil address in reply, expected " + RegisterAddress + ", got " + read_coil_address + ".");
        }
        
        int read_value = BitUtils.UInt16_Of_Bytes_BE(_in_buf, 4);
        if (read_value != Value)
        {
            throw new ApplicationException(FunctionName + ": Invalid data in reply, expected " + Value + ", got " + read_value + ".");
        }
    }


    private void _ReadInputFailIfIncorrect(String FunctionName, int ExpectedCount, int DeviceAddress, byte FunctionCode, int ExpectedPayload) throws Exception
    {
        int so_far = 0;
        long startTime = System.currentTimeMillis();
        long elapsedTime;
        do {
            int this_round = _in.available();
            if (this_round>0)
            {
                this_round = _in.read(_in_buf, so_far, ExpectedCount - so_far);
                if (this_round>0)
                {
                    so_far += this_round;
                    
                    // Can we check errors??? :)
                }
            }
            elapsedTime = System.currentTimeMillis() - startTime;
            if (so_far < ExpectedCount) {
                Thread.sleep(1);
            }
        } while (elapsedTime < TimeoutMs && so_far<ExpectedCount);

        // 3. Did we got what we wanted?
        if (so_far<ExpectedCount) {
            throw new ApplicationException(FunctionName + ": timeout reading data, expected " + ExpectedCount + " bytes, got " +  BitUtils.String_Of_Bytes(_in_buf, 0, so_far) + ", query:" + BitUtils.String_Of_Bytes(_out_buf,  0,  8) + ".");
        }

        if (_in_buf[0]!=DeviceAddress) {
            throw new ApplicationException(FunctionName + ": Invalid device address in reply, expected " + DeviceAddress + ", got " + _in_buf[0] + ".");
        }

        if (_in_buf[1]!=FunctionCode) {
            throw new ApplicationException(FunctionName + ": Invalid function code in reply, expected " + FunctionCode + ", got " + _in_buf[1] + ".");
        }

        if (ExpectedPayload>=0 && _in_buf[2]!=ExpectedPayload) {
            throw new ApplicationException(FunctionName + ": Invalid payload size in reply, expected " + ExpectedPayload + ", got " + _in_buf[2] + ".");
        }
        
        int expected_crc = ModbusFormat.CRC16(_in_buf, 0, ExpectedCount-2);
        int read_crc = BitUtils.UInt16_Of_Bytes_BE(_in_buf, ExpectedCount-2);
        if (expected_crc!=read_crc) {
            throw new ApplicationException(FunctionName + ": Invalid checksum in reply, expected " + expected_crc + ", got " + read_crc + ".");
        }
    }
    
    /// Flush the input queue.
    private void _PurgeInput() throws Exception
    {
        int expected = _in.available();
        if (expected > 0)
        {
            long startTime = System.currentTimeMillis();
            long elapsedTime;
            int so_far = 0;
            do
            {
                int this_round = _in.read(_in_buf, 0, expected - so_far);
                if (this_round>0)
                {
                    so_far += this_round;
                }
                elapsedTime = System.currentTimeMillis() - startTime;
            } while (elapsedTime < TimeoutMs && so_far<expected);
        }
    }
    
    private InputStream _in;
    private OutputStream _out;
    private byte[] _out_buf = new byte[300];
    private byte[] _in_buf = new byte[300];
}
