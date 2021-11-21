package com.errapartengineering.plcmaster;

import java.net.Socket;
import java.util.LinkedList;
import java.util.Queue;
import java.io.File;

import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Environment;
import android.os.IBinder;
import android.preference.PreferenceManager;
import android.telephony.TelephonyManager;
import android.util.Log;

import com.google.protobuf.ByteString;
import com.errapartengineering.plcengine.*;

/**
 * Mediator between the PlcContext and the Android GUI.
 * 
 * @author Andrei
 */
public class PlcMasterService extends Service {

	public final com.errapartengineering.plcengine.IEngine plcEngine = new PlcEngine();
	public com.errapartengineering.plcengine.Context plcContext = null;
	
	/**
	 * Those queries from the client that are to be executed in the service
	 * thread. Synchronize on the collection before operations!
	 */
	public byte[] plcPopRowIfAny() {
		return plcEngine.popRowIfAny();
	}

	/** Is it time to restart the service? */
	public boolean plcShouldRestartService()
	{
		return plcEngine.shouldRestart();
	}
	
	public void plcSetSignal(String signal_name, int value) {
		PlcCommunication.MessageToPlc.Builder msg_builder = PlcCommunication.MessageToPlc.newBuilder();
		PlcCommunication.SignalAndValue.Builder msg_sv_builder = PlcCommunication.SignalAndValue.newBuilder();
		msg_sv_builder.setName(signal_name);
		msg_sv_builder.setValue(value);
		msg_builder.setId(1);
		msg_builder.addSetSignals(msg_sv_builder.build());
		PlcCommunication.MessageToPlc msg = msg_builder.build();
		plcEngine.handleMessage(msg);
	}

	public void plcStopService()
	{
		Log.d("PlcMasterService", "plcStopService: stopping service....");
		if (plcEngine.isAlive())
		{
			plcEngine.stop();
			this.stopSelf();
		}
		Log.d("PlcMasterService", "plcStopService: ... stopped.");
	}
	
	/** onStartCommand is not available on Android 1.6. That's why onStart is used instead.
	 */
	@Override
	public void onStart(Intent intent, int startId) {
		Log.d("PlcMasterService", "onStart intent:" + intent.toString() + " startId:" + startId + ".");
		try
		{
			this.plcEngine.start(this.plcContext);
		}
		catch (Exception ex)
		{
			Log.d("PlcMasterService", "Error:" + ex.getMessage() + ".");
		}
		handleCommand(intent);
	}

	public class LocalBinder extends android.os.Binder {
		PlcMasterService getService() {
			// Return this instance of PlcMasterService so clients can call public methods
			return PlcMasterService.this;
		}
	}

	@Override
	public IBinder onBind(Intent intent) {
		Log.d("PlcMasterService", "onBind:" + intent.toString() + ".");
		return new LocalBinder();
	}

	// This is called only once, the first time the onStart is called.
	@Override
	public void onCreate() {
		Log.d("PlcMasterService", "onCreate.");
		try {
		} catch (Exception ex) {
			Log.e("PlcMasterService", "onCreate: Error:" + ex + ": " + ex.getMessage());
		}
	}

	public void onDestroy()
	{
		Log.d("PlcMasterService", "onDestroy: stopping service....");
		if (plcEngine.isAlive())
		{
			plcEngine.stop();
			this.stopSelf();
		}
		Log.d("PlcMasterService", "onDestroy: ... stopped.");
	}

	public void handleCommand(Intent arg) {
		Log.d("PlcMasterService", "handleCommand: command=" + arg.toString());
	}
}
