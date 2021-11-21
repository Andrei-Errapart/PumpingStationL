package com.errapartengineering.plcmaster;

import java.io.File;

import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.telephony.TelephonyManager;
import android.util.Log;
import android.app.AlertDialog;

public class Utils {
	// =================================================================================================================
	public static void messageBox(android.app.Activity act, String message)
	{
		AlertDialog.Builder b = new AlertDialog.Builder(act);
		b.setMessage(message);
		b.setCancelable(true);
	    b.setNegativeButton(R.string.alert_ok_button_text, new DialogInterface.OnClickListener() {
           public void onClick(DialogInterface dialog, int id) {
                dialog.cancel();
           }
	    });
		AlertDialog alert = b.create();
		alert.show();
	}

	// =================================================================================================================
	/**
	 * Return the device id, one of the following:
	 * 1. "eth0:mac"
	 * 2. "android:android_id"
	 * 3. "imei:imei"
	 * @return
	 */
	public static final String getDeviceId(Context context)
	{
		// See:
		// 1) http://android-developers.blogspot.com/2011/03/identifying-app-installations.html
		// 2) http://stackoverflow.com/questions/1972381/how-to-programmatically-get-the-devices-imei-esn-in-android
		// 3) http://stackoverflow.com/questions/7332931/get-android-ethernet-mac-address-not-wifi-interface
		// 4) http://stackoverflow.com/questions/2785485/is-there-a-unique-android-device-id
		// 5) finally: http://stackoverflow.com/questions/6064510/how-to-get-ip-address-of-the-device/13007325#13007325

		// TODO: shall the order be:
		// a) eth0 MAC, AndroidID, IMEI
		// or
		// b) AndroidID, eth0 MAC, IMEI
		
		// 1. PLC - by eth0 mac.
		String eth0_mac = null;
	    try {
	    	File eth0_file = new java.io.File("/sys/class/net/eth0/address");
	    	// Avoid exceptions.
	    	if (eth0_file.exists())
	    	{
		        eth0_mac = com.errapartengineering.plcengine.FileUtils.ReadFileAsString(eth0_file).toUpperCase();
		        if (eth0_mac!=null && eth0_mac.length()>=17)
		        {
		        	eth0_mac = eth0_mac.substring(0, 17);
			    	Log.d("PlcMasterService:", "eth0 mac: '" + eth0_mac + "'.");
		        }
		        else
		        {
		        	eth0_mac = null;
		        }
	    	}
	    } catch (java.lang.Exception e) {
	    	// skip it.
	    	Log.d("PlcMasterService:", "Cannot get eth0 mac address:" + e.toString());
	    }
	    if (eth0_mac != null)
	    {
	    	return "eth0:" + eth0_mac;
	    }
	    
	    // 2. Other devices - by Android ID.
	    String android_id = android.provider.Settings.Secure.getString(context.getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
	    if (android_id != null)
	    {
	    	return "android:" + android_id;
	    }
	    
	    // 3. Remaining: by IMEI?
	    TelephonyManager tm = (TelephonyManager)context.getSystemService(Context.TELEPHONY_SERVICE);
	    if (tm!=null)
	    {
	    	String imei = tm.getDeviceId();
	    	if (imei!=null)
	    	{
	    		return "imei:" + imei;
	    	}
	    }
	    return "unknown";
	}
	
	// =================================================================================================================
	/** Get CPU usage of the current process, in the range 0...100.
	 * @return
	 */
	public static final double getCpuUsage()
	{
		int pid = android.os.Process.myPid();
		double r = 10.1;
		return r;
	}
	
	/** Get total CPU usage (for all processes), in the range 0...100.
	 * @return
	 */
	public static final double getTotalCpuUsage()
	{
		double r = 22.2;
		return r;
	}
}
