package com.errapartengineering.valvetest;

import java.io.File;
import java.net.Socket;
import java.net.SocketException;
import java.util.*;

import com.errapartengineering.plcengine.FileUtils;
import com.errapartengineering.plcengine.IODevice;
import com.errapartengineering.plcengine.IOSignal;
import com.errapartengineering.plcengine.ModbusMaster;

import android.app.Service;
import android.content.Intent;
import android.os.IBinder;
import android.util.Log;

public class ValveTestService extends Service {
	public final static int NUMBER_OF_VALVES = 4;
	public final static long MONITOR_TIME_MS = 15 * 60 * 1000; // Time for monitoring the valve...
	
	public final static int VALVEPOSITION_INACTIVE = 0;
	public final static int VALVEPOSITION_ACTIVE = 1;

	public final static File configurationFile = FileUtils.getFileOnExternalStorage("setup2.xml");
	public final static File versionFile = FileUtils.getFileOnExternalStorage("setup.txt");
	public final static File logFile = FileUtils.getFileOnExternalStorage("valvetest.txt");

	public com.errapartengineering.plcengine.Context plcContext = null;

	private Thread _thread_plc = null;
	private boolean _thread_stop = false;
	
	// UpdateMessage fields.
	private int _loopCount = 0;
	private int _valvePosition = 0;
	private Date _testStartTime = new Date();
	private int _successCount = 0;
	private int _failureCount = 0;
	private boolean[] _valvesOk = new boolean[] { true, true, true, true, true, true, true, true };

	private ValveControl[] _valves = new ValveControl[NUMBER_OF_VALVES];
	private Queue<UpdateMessage> _updateMessages = new LinkedList<UpdateMessage>();
	
	/** onStartCommand is not available on Android 1.6. That's why onStart is used instead.
	 */
	@Override
	public void onStart(Intent intent, int startId) {
		Log.d("ValveTestService", "onStart intent:" + intent.toString() + " startId:" + startId + ".");
		try
		{
			// 1. Load the plcContext.
			plcContext = new com.errapartengineering.plcengine.Context("NoUID",  configurationFile, versionFile, "127.0.0.1:1503", "127.0.0.1:1502");

			// relay M1
			// M1:  NO, U1.DI03=1
			// M4:  NO, U1.DI02=1
			
			// relay M2
			// M2:  NC, U1.DI1=1
			// M3:  NC, U1.DI0=1
			
			// relay M3
			// M5:  NO, U3.DI00=1
			// M8:  NO, U3.DI14=1
			
			// relay M4
			// M6:  NC, U3.DI08=1
			// M7:  NC, U3.DI01=1
			
			List<IOSignal> signals = plcContext.Signals;
			
			// 2. Get the signals...
			for (int i=0; i<_valves.length; ++i)
			{				
				_valves[i] = new ValveControl();
				_valves[i].controlName = "M" + Integer.toString(i+1);
				_valves[i].control = IOSignal.findSignalByName(signals, _valves[i].controlName);
			}
			
			// relay M1 = valves M1,M4
			_valves[0].feedback1Name = "M1";
			_valves[0].feedback1 = IOSignal.findSignalByName(signals, "U1.DI3");
			_valves[0].feedback2Name = "M4";
			_valves[0].feedback2 = IOSignal.findSignalByName(signals, "U1.DI2");
			
			// relay M2 = valves M2,M3
			_valves[1].feedback1Name = "M2";
			_valves[1].feedback1 = IOSignal.findSignalByName(signals, "U1.DI1");
			_valves[1].feedback2Name = "M3";
			_valves[1].feedback2 = IOSignal.findSignalByName(signals, "U1.DI0");
			
			// relay M3 = valves M5,M8
			_valves[2].feedback1Name = "M5";
			_valves[2].feedback1 = IOSignal.findSignalByName(signals, "U3.DI0");
			_valves[2].feedback2Name = "M8";
			_valves[2].feedback2 = IOSignal.findSignalByName(signals, "U1.DI14");

			// relay M4 = valves M6,M7
			_valves[3].feedback1Name = "M6";
			_valves[3].feedback1 = IOSignal.findSignalByName(signals, "U3.DI8");
			_valves[3].feedback2Name = "M7";
			_valves[3].feedback2 = IOSignal.findSignalByName(signals, "U3.DI1");

			// 3. Start the thread.
			_thread_plc = new Thread(new Runnable() { public void run() { plc_thread(); } });
			_thread_plc.start();
		}
		catch (Exception ex)
		{
			Log.d("ValveTestService", "Error:" + ex.getMessage() + ".");
		}
		handleCommand(intent);
	}

	public class LocalBinder extends android.os.Binder {
		ValveTestService getService() {
			// Return this instance of PlcMasterService so clients can call public methods
			return ValveTestService.this;
		}
	}

	@Override
	public IBinder onBind(Intent intent) {
		Log.d("ValveTestService", "onBind:" + intent.toString() + ".");
		return new LocalBinder();
	}

	// This is called only once, the first time the onStart is called.
	@Override
	public void onCreate() {
		Log.d("ValveTestService", "onCreate.");
		try {
		} catch (Exception ex) {
			Log.e("ValveTestService", "onCreate: Error:" + ex + ": " + ex.getMessage());
		}
	}

	public void onDestroy()
	{
		Log.d("ValveTestService", "onDestroy: stopping service....");
		stop();
		Log.d("PlcMasterService", "onDestroy: ... stopped.");
	}

	public void handleCommand(Intent arg) {
		Log.d("PlcMasterService", "handleCommand: command=" + arg.toString());
	}
	
	// ======================================= UTILITY FUNCTIONS =========================================
	private void _pushUpdateMessage()
	{
		UpdateMessage msg = new UpdateMessage();
		msg.isRunning = !_thread_stop;
		msg.loopNumber = _loopCount;
		msg.valvePosition = _valvePosition;
		msg.startTime = _testStartTime;
		msg.successCount = _successCount;
		msg.failureCount = _failureCount;
		msg.valvesOK = new boolean[_valvesOk.length];
		for (int i=0; i<_valvesOk.length; ++i)
		{
			msg.valvesOK[i] = _valvesOk[i];
		}
		synchronized (_updateMessages)
		{
			_updateMessages.add(msg);
		}
	}
	
	private static void _sleep(int ms) {
		try {
			Thread.sleep(ms);
		} catch (InterruptedException ex) {
			// harmless.
		}
	}
	
	private static Socket _socketByConnectionString(String socketString) throws Exception {
		String[] v = socketString.split(":");
		String ip = v[0];
		int port = Integer.parseInt(v[1]);

		// 2. Connect!
		Socket r = new Socket(ip, port);
		return r;
	}

	ModbusMaster _modbus = null;
	Socket _modbus_client = null;
	
	private boolean _SyncWithModbus()
	{
		int socket_exception_count = 0;
		int fatal_exception_count = 0;
		boolean should_reconnect = false;
		if (!_thread_stop && _modbus!=null)
		{
			socket_exception_count = 0; // some devices could have obtained
										// some meaningful data, have to
										// reconnect in these cases.
			fatal_exception_count = 0;
			if (plcContext.Devices.size()>0)
			{
				for (IODevice dev : plcContext.Devices) {
					try {
						Thread.sleep(10);
						dev.SyncWithModbus(_modbus);
					} catch (SocketException sex) {
						Log.d("ValveTestService", "Fatal error in communicating with device " + dev.DeviceAddress + ": " + sex.getMessage() + ".");
						should_reconnect = true;
						++fatal_exception_count;
					} catch (Exception ex) {
						Log.d("ValveTestService", "Error in communicating with device " + dev.DeviceAddress + ": " + ex.getMessage());
						++socket_exception_count;
					}
				}
				if (socket_exception_count == plcContext.Devices.size())
				{
					Log.d("ValveTestService", "All devices have failed, will fail.");
					should_reconnect = true;
				}
				else if (fatal_exception_count == plcContext.Devices.size())
				{
					Log.d("ValveTestService", "All devices have been disconnected, will reconnect.");
					should_reconnect = true;
				}
				else
				{
					_sleep(100);
					return true;
				}				
			}
		}

		_sleep(100);
		
		return false;
	}
	
	// ======================================= WORKER THREAD =========================================
	public void plc_thread()
	{
		// 1. CONNECT
		try {
			// Connect!
			_modbus_client = _socketByConnectionString(plcContext.modbus);
			_modbus = new ModbusMaster(_modbus_client.getInputStream(), _modbus_client.getOutputStream());
			Log.d("ValveTestService", "Connected to modbus proxy at IP:" + _modbus_client.getRemoteSocketAddress().toString() + ".");
		} catch (Exception ex) {
			_modbus = null;
			_modbus_client = null;
			Log.e("ValveTestService", "Cannot connect to modbus proxy:" + ex.toString());
			return;
		}
		
		// 2. MAIN LOOP
		boolean modbus_ok = true;
		while (!_thread_stop && modbus_ok)
		{
			++this._loopCount;
			for (_valvePosition=0; _valvePosition<2 && !_thread_stop && modbus_ok; ++_valvePosition)
			{
				// 1. Set the signal.
				for (int vc_index=0; vc_index<this.NUMBER_OF_VALVES && !_thread_stop && modbus_ok; ++vc_index)
				{
					ValveControl vc = _valves[vc_index];
					vc.control.setValue(_valvePosition);
				}
				_testStartTime = new Date();
				_pushUpdateMessage();
				modbus_ok = _SyncWithModbus();				
				if (!modbus_ok)
				{
					break;
				}
				
				// 2. Wait...
				long t0 = System.currentTimeMillis();
				long t1;
				do
				{
					modbus_ok = _SyncWithModbus();
					t1 = System.currentTimeMillis();
					_sleep(100);
				} while (t1-t0 < MONITOR_TIME_MS && modbus_ok);

				// 3. Check!
				int expected_value = _valvePosition == VALVEPOSITION_INACTIVE ? 1 : 0;
				for (int vc_index=0; vc_index<NUMBER_OF_VALVES; ++vc_index)
				{
					ValveControl vc = _valves[vc_index];
					boolean ok1 = vc.feedback1.getValue() == expected_value;
					_valvesOk[2*vc_index + 0] = ok1;
					if (ok1) {
						++_successCount;
					} else {
						++_failureCount;
					}

					boolean ok2 = vc.feedback2.getValue() == expected_value;
					_valvesOk[2*vc_index + 1] = ok2;
					if (ok2) {
						++_successCount;
					} else {
						++_failureCount;
					}
				}
				
				// 4. Send the word out.
				_pushUpdateMessage();
				
				try
				{
					Date dt = new Date();
					java.io.FileWriter fout = new java.io.FileWriter(logFile, true);
					fout.write(dt.toString() + " #" + Integer.toString(_loopCount) + ": " + (_valvePosition==0 ? "Default " : "Activated ") + Integer.toString(_successCount) + " successes, " + Integer.toString(_failureCount) + " failures. Fails= ");
					for (int i=0; i<_valvesOk.length; ++i)
					{
						if (!_valvesOk[i])
						{
							fout.write("M" + Integer.toString(i+1) + " ");
						}
					}
					fout.write("\r\n");
					fout.close();
				}
				catch (Exception ex)
				{
					// pass.
				}
			}			
		}
		if (!modbus_ok)
		{
			Log.d("ValveTestService", "Testing finished due to modbus error.");
		}
		
		// 3. FINALE
		_pushUpdateMessage();		
		try {
			if (_modbus_client!=null)
			{
				_modbus_client.close();
			}
		} catch (Exception ex) {
			Log.e("ValveTestService", "Error in modbus_client.close: " + ex.toString());
		}
		Log.d("ValveTestService", "Service: Finished!");
	}	

	// ======================================= PUBLIC INTERFACE =========================================
	public void stop()
	{
		_thread_stop = true;
	}
	
	public UpdateMessage popUpdateMessage()
	{
		UpdateMessage r = null;
		synchronized (_updateMessages)
		{
			if (!_updateMessages.isEmpty())
			{
				r = _updateMessages.remove();
			}
		}
		return r;
	}
}
