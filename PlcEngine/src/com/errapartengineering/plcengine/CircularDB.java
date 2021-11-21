package com.errapartengineering.plcengine;

import java.util.*;
import java.io.*;
import android.util.*; // Log.d

/**
 * Circular database of a table of bytes.
 * 
 * Usage:
 * open(filename, row_length, row_data)
 * add(row)
 * fetch(destination, start_id, max_num_id)
 * close()
 * 
 * File structure:
 * 1. Header
 * 2. Row ID-s.
 * 3. Rows.
 * 
 * Header structure:
 * 0..3: Magic.
 * 4..7: Version
 * 8..11: Row length, in bytes.
 * 12..15: Number of rows.
 * 16..19: Head ID.
 * 20..23: Tail ID.
 * 24..254: Reserved.
 * 
 * A row can be addressed by an ID. The range of available rows
 * is defined as [TailID ... HeadID), i.e. the Tail is included
 * and the Head is not included.
 * 
 * Optimization opportunities:
 * 1. Cache the last 1..10 written row values.
 * 2. Add function to query the size of the existing database
 *    in order to see whether it can be used with new (possibly smaller) row length.
 * @author Andrei
 */
public class CircularDB {
    /** Database filename. */
    public final String Filename;
    /** Version of the file structure. */
    public final int Version;
    /** Row length, in bytes. Total row length is obtained by adding ROW_OVERHEAD. */
    public final int RowLength;
    /** Number of rows. */
    public final int NumRows;
    /** Total length of the database file in bytes. */
    public final long FileLength;

    /** HeadIndex = HeadID % NumRows */
    public volatile int HeadId;
    /** TailIndex = TailID % NumRows */
    public volatile int TailId;
    
    /** Open the database file.
     * @param DatabaseFile Database file.
     * @param Version Database version.
     * @param RowLength Length of the row, in bytes.
     * @param NumRows Number of rows.
     * @throws Exception 
     */
    public CircularDB(
    		java.io.File DatabaseFile,
    		int Version,
    		int RowLength,
    		int NumRows) throws Exception
    {
        this.Filename = DatabaseFile.getAbsolutePath();
        this.Version = Version;
        this.RowLength = RowLength;
        this.NumRows = NumRows;
        this.FileLength = _HEADER_LENGTH + ((long)NumRows) * ((long)RowLength);
        this._IOBuffer = new byte[_HEADER_LENGTH > (RowLength) ? _HEADER_LENGTH : RowLength];
        
        _file = new java.io.RandomAccessFile(DatabaseFile, "rw");
        if (_file.length() == FileLength)
        {
            // 1. Check the header.
            _file.read(_IOBuffer, 0, _HEADER_LENGTH);
            int file_magic = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _MAGIC_OFFSET);
            int version = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _VERSION_OFFSET);
            int row_length = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _ROW_LENGTH_OFFSET);
            int num_rows = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _NUMBER_OF_ROWS_OFFSET);
            if (version == Version && file_magic==_FILE_MAGIC && row_length==RowLength && num_rows==NumRows)
            {
                int head_id = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _HEAD_ID_OFFSET);
                int tail_id = BitUtils.Int32_Of_Bytes_BE(_IOBuffer, _TAIL_ID_OFFSET);
                if (tail_id>=0 && head_id>=0 && head_id>=tail_id)
                {
                    this.HeadId = head_id;
                    this.TailId = tail_id;
                    
                    // 1. Scan if there are any records past the HeadId (increase TailId when in full circle)...
                    if (HeadId>0)
                    {
                        // t0: time of last row according to HeadId.
                        long t0;
                        byte[] read_buffer = new byte[RowLength];
                        
                        // 1.1. Some of the first rows had timestamps in 1970-s.
                        int new_tail_id = TailId;
                        t0 = _TimeOfId(read_buffer, TailId);
                        long t1 = t0;
                        long t1x = t1;
                        final long t_limit = 1000000000000L;
                        while (t1 < t_limit && new_tail_id < HeadId)
                        {
                            ++new_tail_id;
                            t1x = t1;
                            t1 = _TimeOfId(read_buffer, new_tail_id);
                        }
                        Log.d("CircularDb", "TailId: " + TailId + ", time: " + t0 + " next TailId: " + new_tail_id + ", time: " + t1 + ", time before: " + t1x + ".");
                        
                        // 1.2. Print some of the last timestamps.
                        final int nlast = 10;
                        Log.d("CircularDb", "Last " + nlast + " row headers:");
                        Log.d("CircularDb", "Id      Milliseconds   Timestamp.");
                        long t_last = 0;
                        for (int print_id=HeadId-nlast; print_id<HeadId; ++print_id)
                        {
                            if (print_id >= TailId)
                            {
                                long t = _TimeOfId(read_buffer, print_id);
                                Date dt = new Date(t);
                                Log.d("CircularDb", "" + print_id + "  " + t + "  " + dt + (t<t_last ? "  Skew here!" : ""));
                                t_last = t;
                            }
                        }
                        
                        // 1.3. Did we have some errors in updating the timestamps?
                        t0 = _TimeOfId(read_buffer, HeadId-1);
                        
                        Log.d("CircularDb", "TailId: " + TailId + ", HeadId: " + HeadId + ", head time:" + t0);
                        int new_head_id = HeadId;
                        t1 = t0;
                        do
                        {
                            t0 = t1;
                            t1 = _TimeOfId(read_buffer, new_head_id);
                            ++new_head_id;
                        } while (t1 > t0);
                        --new_head_id;
                        Log.d("CircularDb", "scan stopped at HeadId: " + new_head_id + ", time: " + t0 + ".");
                        
                        if (new_tail_id != TailId || new_head_id!=HeadId)
                        {
                            // Check if we haven't increased the database size to be bigger than the file size.
                            long new_row_count = new_head_id - new_tail_id;
                            if (new_row_count >= NumRows)
                            {
                                new_tail_id = new_head_id - NumRows + 1;
                            }
                            Log.d("CircularDb", "Updating pointers, new TailId: " + new_tail_id + ", new HeadId: " + new_head_id + ".");
                            _WriteTailHead(new_tail_id, new_head_id);
                        }
                    }
                }
                else
                {
                    _RecreateFile();
                }
            }
            else
            {
                _RecreateFile();
            }
        }
        else
        {
            _RecreateFile();
        }
    }

    /** Close the database. */
    public final synchronized void Close()
    {
        RandomAccessFile tmp = _file;
        _file = null;
        if (tmp!=null)
        {
            try {
                tmp.close();
            } catch (Exception ex)
            {
            	Log.d("CircularDB", "close: Error: " + ex.getMessage());
            }
        }
    }
    
    /** Add a new row to the circular database. */
    public final synchronized void Add(byte[] row) throws Exception
    {
        int head_id = this.HeadId;
        int tail_id = this.TailId;
        int head_index = head_id % NumRows;
        int tail_index = tail_id % NumRows;
        
        // 1. Write the data.
        int row_length = row.length<=RowLength ? row.length : RowLength;
        _file.seek(_RowOffsetByIndex(head_index));
        _file.write(row, 0, row_length);
        
        // 2. Update the pointers.
        int next_head_id = head_id + 1;
        int next_head_index = next_head_id % NumRows;
        int next_tail_id = next_head_index==tail_index ? (tail_id + 1) : tail_id;
        _WriteTailHead(next_tail_id, next_head_id);
    }
    
    /**
     * Fetch the rows from the circular database into preallocated buffers.
     * 
     * The maximum number of rows is determined by Dst.size().
     * Every buffer has to be at least the length of the row.

     * @param Dst Destination buffers.
     * @param TailId Lowest Id to be fetched.
     * @param HeadId Lowest Id not to be fetched.
     * @return Number of rows fetched, or: -1=too low TailId,  0=no rows with the id >= TailId.
     * @throws Exception when there is a read error.
     */
    public final synchronized int Fetch(List<byte[]> Dst, int TailId, int HeadId) throws Exception
    {
        int tail_id = this.TailId;
        int head_id = this.HeadId;
        if (TailId>=tail_id)
        {
            int n = HeadId - TailId;
            for (int i=0; i<n; ++i)
            {
                int id = TailId + i;
                if (id < head_id)
                {
                    if (i==0)
                    {
                        _file.seek(_RowOffsetByIndex(TailId % NumRows));
                    }
                    _file.read(Dst.get(i), 0, RowLength);
                }
                else
                {
                    return i;
                }
            }
            return n;
        }
        else
        {
            return -1;
        }
    }
    
    private void _RecreateFile() throws Exception
    {
    	Log.d("CircularDB", "Re-creating the database file '" + Filename + "'.");
        // 1. Write the header.
        _ClearIOBuffer();
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, _MAGIC_OFFSET, _FILE_MAGIC);
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, _VERSION_OFFSET, Version);
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, _ROW_LENGTH_OFFSET, RowLength);
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, _NUMBER_OF_ROWS_OFFSET, NumRows);
        _file.seek(0);
        _file.write(_IOBuffer, 0, _HEADER_LENGTH);

        // 3. Correct the length.
        _file.setLength(FileLength);
    }
    
    /**
     * Retrieve time corresponding to the given Id.
     * @param Buffer Read buffer of length RowLength.
     * @param Id Id of the row. No checks are performed.
     * @return Time, milliseconds, as in System.currentTimeMillis().
     * @throws java.io.IOException 
     */
    private long _TimeOfId(byte[] Buffer, int Id) throws java.io.IOException
    {
        _file.seek(_RowOffsetByIndex(Id % NumRows));
        _file.read(Buffer, 0, RowLength);
        long r = BitUtils.Int64_Of_Bytes_BE(Buffer, CircularRowFormat.TIME_OFFSET);
        return r;
    }

    /**
     * Update HeadId,TailId with the new values and write the header.
     * @param NewTailId
     * @param NewHeadId
     * @throws java.io.IOException 
     */
    private void _WriteTailHead(int NewTailId, int NewHeadId) throws java.io.IOException
    {
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, 0, NewHeadId);
        BitUtils.Bytes_Of_Int32_BE(_IOBuffer, _SIZEOF_INT, NewTailId);
        _file.seek(_HEAD_ID_OFFSET);
        _file.write(_IOBuffer, 0, 2*_SIZEOF_INT);
        this.HeadId = NewHeadId;
        this.TailId = NewTailId;
    }
    
    private long _RowOffsetByIndex(int RowIndex)
    {
        return _HEADER_LENGTH + RowIndex * RowLength;
    }
    
    private void _ClearIOBuffer()
    {
        for (int i=0; i<_IOBuffer.length; ++i)
        {
            _IOBuffer[i] = (byte)0;
        }
    }

    private final static int _FILE_MAGIC = 0x07884E41;
    private final static int _HEADER_LENGTH = 256;
    private final static int _MAGIC_OFFSET = 0;
    private final static int _VERSION_OFFSET = 4;
    private final static int _ROW_LENGTH_OFFSET = 8;
    private final static int _NUMBER_OF_ROWS_OFFSET = 12;
    private final static int _HEAD_ID_OFFSET = 16;
    private final static int _TAIL_ID_OFFSET = 20;
    private final static int _SIZEOF_INT = 4;
    
    private RandomAccessFile _file;
    private final byte[] _IOBuffer;
}
