package android.os;

import java.io.File;

public final class Environment {
	public final static String MEDIA_MOUNTED = "MEDIA_MOUNTED";
	public final static String getExternalStorageState()
	{
		return MEDIA_MOUNTED;
	}
	
	public final static File getExternalStorageDirectory()
	{
		return new File(".");
	}
}
