package com.errapartengineering.plcengine;

import java.io.*;
import java.util.*; // List<T>, ArrayList<T>

import android.os.Environment;
import android.util.Log;

/** File utilities.
 *
 * @author Andrei
 */
public abstract class FileUtils {
    // ============================================================================================================
    /** Read contents of the file using UTF-8 encoding. Reads the file in one go (and UTF-8 mode) when the file length is non-zero.
     * 
     * @param Filename File to be read.
     * @return Contents as a string.
     * @throws Exception IO or conversion errors.
     */
    public static String ReadFileAsString(java.io.File file) throws java.io.IOException
    {
        int file_length = (int)file.length();
        if (file_length == 0)
        {
            StringBuffer fileData = new StringBuffer();
            BufferedReader reader = new BufferedReader(new FileReader(file), 1024);
            try
            {
	            char[] buf = new char[1024];
	            int numRead=0;
	            while ((numRead=reader.read(buf)) != -1)
	            {
	                String readData = String.valueOf(buf, 0, numRead);
	                fileData.append(readData);
	            }
            }
            finally
            {
	            reader.close();
            }
            return fileData.toString();        }
        else
        {
        	// Read it at once.
	        byte[] b = new byte[file_length];
	        InputStream in = new FileInputStream(file);
	        try
	        {
	            int so_far = 0;
	            while (so_far < file_length)
	            {
	                int this_round = in.read(b, so_far, file_length - so_far);
	                if (this_round < 0)
	                {
	                    // FIXME: what do do?
	                    break;
	                }
	                so_far += this_round;
	            }
	            if (so_far<file_length)
	            {
	            	Log.d("PlcMaster", "FileUtils.ReadFileAsString: expected " + file_length + " bytes, got " + so_far + " bytes.");
	            }
	        }
	        finally 
	        {
	            in.close();
	        }
	        return new String(b, "UTF-8");
        }
    }
    
    // ============================================================================================================
    public static byte[] ReadFileAsBytes(File file) throws Exception
    {
        RandomAccessFile fin = new RandomAccessFile(file, "r");
        try
        {
            int fin_length = (int)fin.length();
            byte[] fin_buffer = new byte[fin_length];
            int this_round = 0;
            for (int so_far=0; so_far<fin_length; so_far += this_round)
            {
                this_round = fin.read(fin_buffer, so_far, fin_length - so_far);
                if (this_round<0)
                {
                    // oops.
                    throw new ApplicationException("Unexpected error when reading file'" + file.getAbsolutePath() + "'.");
                }
            }
            return fin_buffer;
        }
        finally
        {
            fin.close();
        }
    }
    
    // ============================================================================================================
    public static byte[] ReadStreamAsBytes(java.io.InputStream stream) throws Exception
    {
    	// 1. Read the data.
    	final int chunk_size = 256;
    	List<byte[]> input = new ArrayList<byte[]>();
    	int this_round = chunk_size;
    	int total_size = 0;
    	do
    	{
    		byte[] chunk = new byte[chunk_size];
    		this_round = stream.read(chunk);
    		if (this_round>=0)
    		{
    			// Note: we are assuming that only the last one fails.
    			total_size += this_round;
    			input.add(chunk);
    		}
    	} while (this_round>0);
    	
    	// 2. Collect it together.
    	byte[] r = new byte[total_size];
    	int so_far = 0;
    	for (int input_index=0; input_index<input.size(); ++input_index)
    	{
    		byte[] src = input.get(input_index);
    		int n = input_index+1 == input.size() ? (total_size - input_index*chunk_size) : chunk_size;
    		for (int i=0; i<n; ++i, ++so_far)
    		{
    			r[so_far] = src[i];
    		}
    	}
    	return r;
    }
    
    // ============================================================================================================
    /**
     * Write file.
     * Logs success/failure to syslog.
     * Return value: ""=success, otherwise: error message. */
    public static String writeFile(File file, byte[] Contents)
    {
    	String r = "Unknown error";
    	try {
			java.io.FileOutputStream fout = new java.io.FileOutputStream(file);
			try
			{
				fout.write(Contents);
				r = "";
			}
			finally
			{
				fout.close();
			}
    	} catch (Exception ex)
    	{
    		r = ex.getMessage();
    	}
    	if (r.length()>0)
    	{
    		Log.d("PlcMaster", "Cannot write file '" + file.getAbsolutePath() + "': " + r);
    	}
    	else
    	{
    		Log.d("PlcMaster", "Wrote file '" + file.getAbsolutePath() + "' successfully,  " + Contents.length + " bytes in total.");
    	}
		return r;
    }
    
    // ============================================================================================================
    public static File getFileOnExternalStorage(String filename)
    {
    	// TODO: check external storage state.
    	// getExternalStorageState
    	File es_dir = Environment.getExternalStorageDirectory();
    	File external_sd = new File(es_dir, "external_sd");
    	File r = new File(external_sd.exists() ? external_sd : es_dir ,filename);
    	return r;
    }
}
