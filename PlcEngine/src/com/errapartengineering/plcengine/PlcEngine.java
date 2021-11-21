package com.errapartengineering.plcengine;

import android.os.Environment;
import android.util.Log;
import java.util.*; // List, ArrayList.
import java.net.*;

import com.google.protobuf.ByteString;

/**
 * PLC core.
 * 
 * TODO: the washing sequence is a hack. Any ideas of how to do it better?
 * 
 * @author Andrei
 */
public class PlcEngine implements IEngine {
	public final static int DB_MAX_WRITE_PERIOD_MS = 1 * 60 * 1000; // one minutes
	public final static int DB_NUM_ROWS = (2 * 60 / 1) * 24 * 30 * 3; // approx. three months.
	public final static int DB_VERSION = 1;
	public final static String DB_FILENAME = "PlcMaster.db";
	
	// FIXME: substitute this with proper implementation!
	public final static int WASH_HOUR = 3;
	// FIXME: substitute this with proper implementation!
	public final static long WASH_PERIOD_MS = 3L * 24L * 3600L * 1000L;

	/** Maximum number of rows in a batch. Queries exceeding this will be served only the newer ones. */
    public final static int MAX_BATCH_SIZE = 100;
	public int DB_Row_Length = -1;
	public Context plcContext = null;

	public final static long SERVER_CONNECTION_TIMEOUT_MS = 60 * 1000;
	/** Log database. */
	private CircularDB _DB = null;
	private Thread _thread_plc = null;
	private Thread _server_thread = null;
	private Thread _server_output_thread = null;
	private volatile boolean _thread_stop = false;
	private volatile boolean _reload_configuration = false;
	// Shall the write be triggered this time?
	private volatile boolean _trigger_db_write = false;
	
	// PLC data rows for displaying. Access is synchronized with _rows_for_display.
	private Queue<byte[]> _rows_for_display = new LinkedList<byte[]>();
	/**
	 * Those queries from the client that are to be executed in the service
	 * thread. Synchronize on the collection before operations!
	 */
	public final Queue<PlcCommunication.MessageToPlc> _queries = new LinkedList<PlcCommunication.MessageToPlc>();
	// OOB Id-s are negative, starting from -1.
	public long Last_OOBId_Sent = 0;
    private final PlcCommunication.DatabaseRow.Builder _OOBDatabaseRowBuilder = PlcCommunication.DatabaseRow.newBuilder();
    private final PlcCommunication.DatabaseRow.Builder _BatchSignalsBuilder = PlcCommunication.DatabaseRow.newBuilder();
    private final PlcCommunication.MessageFromPlc.Builder _DatabaseRowEnvelopeBuilder = PlcCommunication.MessageFromPlc.newBuilder();
    /** Buffer of some rows for batch queries. */
    private final List<byte[]> _Batch_Buffer = new ArrayList<byte[]>();
    
    // Last server activity.
    private long _lastServerActivityTimeMs = 0;
    
    /** Washing system. */
    public final static String WASH_SIGNAL = "FJP.WASHING";
    private long _wash_start_time_ms = 0;
    private int _wash_index = -1;
    private static SequenceStep[] _wash_steps = new SequenceStep[] {
    		// 00m 00s
			new SequenceStep(0, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("P01.START_STOP", 0),
					SequenceStep.CommandSetSignal("MV11.OPEN", 0),
					SequenceStep.CommandSetSignal("MV11.CLOSE", 1),
					SequenceStep.CommandSetSignal("MV21.OPEN", 0),
					SequenceStep.CommandSetSignal("MV21.CLOSE", 1),
					}),
    		// 00m 30s
    		new SequenceStep(30000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("M1", 1),
					SequenceStep.CommandSetSignal("M3", 1),
					SequenceStep.CommandSetSignal("M2", 1),
					SequenceStep.CommandSetSignal("M4", 0),
					}),
			// 01m 00s
    		new SequenceStep(1 * 60000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("P5.START_STOP", 1) } ),
    		// 11m 00s
    		new SequenceStep(9 * 60000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("M2", 0),
					SequenceStep.CommandSetSignal("M4", 1),
					}),
    		// 21m 00s
    		new SequenceStep(17 * 60000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("P5.START_STOP", 0),
					SequenceStep.CommandSetSignal("M4", 0),
					}),
    		// 25m 00s
    		new SequenceStep(21 * 60000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal("M1", 1),
					SequenceStep.CommandSetSignal("M3", 1),
					}),
    		// 25m 30s
    		new SequenceStep(21 * 60000 + 30000, new PlcCommunication.MessageToPlc[] {
					SequenceStep.CommandSetSignal(WASH_SIGNAL, 0),
					} ),
    };
    private IOSignal _wash_fjp_washing = null;    
    private final static PlcCommunication.MessageToPlc[] _wash_reset = new PlcCommunication.MessageToPlc[] {
		SequenceStep.CommandSetSignal("P5.START_STOP", 0),
		SequenceStep.CommandSetSignal("M1", 0),
		SequenceStep.CommandSetSignal("M2", 0),
		SequenceStep.CommandSetSignal("M3", 0),
		SequenceStep.CommandSetSignal("M4", 0),
		SequenceStep.CommandSetSignal(WASH_SIGNAL, 0),
    };

	// FIXME: substitute this with proper implementation!
    // Last washing time.
    private long _wash_last_time_ms = System.currentTimeMillis();
    
    /*
Pesutsükkel:
1. 00m00s: Signaal FJP.WASHING <= 1. Selle peale põhiprogramm suleb mahutite klapid ja lülitab puurkaevupumba välja.
2. 00m30s: Klappide 1,4,5,8 sulgemine, 2,3 avamine.
3. 01m00s: Pesupumba käivitamine.
4. 11m00s: Klappide 2,3 sulgemine, klappide 6,7 avamine
5. 21m00s: Pesupumba seiskamine, klappide 6,7 sulgemine.
6. 25m00s: Klappide 1,4,5,8 avamine, signaal FJP.WASHING <= 0. Selle peale põhiprogramm jätkab oma põhitööga...
     */

    private void _resetWash()
    {
    	_wash_last_time_ms = System.currentTimeMillis();
		Log.d("PlcEngine", "Resetting washing valves to the default positions.");
		_wash_index = -1;
		_wash_start_time_ms = 0;
		for (PlcCommunication.MessageToPlc cmd : _wash_reset)
		{
			handleMessage(cmd);
		}
    }
    
	public byte[] popRowIfAny() {
		byte[] r = null;
		synchronized (_rows_for_display) {
			if (!_rows_for_display.isEmpty()) {
				r = _rows_for_display.remove();
			}
		}
		return r;
	}

	/*
	public void setSignal(String signal_name, int value) {
		PlcCommunication.MessageToPlc.Builder msg_builder = PlcCommunication.MessageToPlc
				.newBuilder();
		PlcCommunication.SignalAndValue.Builder msg_sv_builder = PlcCommunication.SignalAndValue
				.newBuilder();
		msg_sv_builder.setName(signal_name);
		msg_sv_builder.setValue(value);
		msg_builder.setId(1);
		msg_builder.addSetSignals(msg_sv_builder.build());
		PlcCommunication.MessageToPlc msg = msg_builder.build();
		synchronized (_queries) {
			_queries.add(msg);
		}
	}
	*/

	// Get the latest and greatest context.
	public Context getContext()
	{
		return plcContext;
	}

	public boolean shouldRestart()
	{
		return  _reload_configuration;
	}
	
	// Start the PLC.
	public void start(Context ctx)
	{
		Log.d("PlcEngine", "start");
		try {
			// 1. Read the configuration resource..
			this.plcContext = ctx;
			DB_Row_Length = CircularRowFormat.Count_Bytes_Needed(plcContext.Signals);
			Log.d("PlcEngine",
					"start: loaded " + plcContext.Signals.size()
							+ " signals, log row length: " + DB_Row_Length
							+ " bytes.");

			// 2. Open the database, if possible.
			if (_DB == null && plcContext.Devices.size()>0) {
				// what do with the circular db.
				// _db = new CircularDB();
				java.io.File db_file = FileUtils.getFileOnExternalStorage(DB_FILENAME);
				Log.d("PlcEngine", "Database filename: " + db_file.getAbsolutePath());
				_DB = new CircularDB(db_file, DB_VERSION, DB_Row_Length, DB_NUM_ROWS);
				Log.d("PlcEngine", "Database opened, total length is " + _DB.FileLength + " bytes.");
				
				// Have to restore the variables into a perfect working order!
				byte[] last_row_buffer = new byte[DB_Row_Length];
				List<byte[]> last_rows = new ArrayList<byte[]>();
				last_rows.add(last_row_buffer);
				if (_DB.HeadId > _DB.TailId && _DB.Fetch(last_rows, _DB.HeadId-1, _DB.HeadId)>0)
				{
					System.out.println("Restoring variables...");
					CircularRowFormat.RestoreVariables(plcContext.Signals, last_row_buffer);
				}
			}

			_wash_fjp_washing = plcContext.SignalByNameOrId(WASH_SIGNAL);
			if (_wash_fjp_washing!=null)
			{
				Log.d("PlcEngine", "Wash signal " + WASH_SIGNAL + " found.");
				_resetWash();
			}
			_wash_start_time_ms = 0;
			_wash_index = -1;
			
			// 3. Start the PLC thread...
			_thread_plc = new Thread(new Runnable() { public void run() { plc_thread(); } });
			_thread_plc.start();

			// 4. Start the thread connecting to the server.
			_server_thread = new Thread(new Runnable() { public void run() { server_input_thread(); } });
			_server_thread.start();
			
			// 5. Start the output thread.
			_server_output_thread = new Thread(new Runnable() { public void run() { server_output_thread(); } });
			_server_output_thread.start();

		} catch (Exception ex) {
			Log.e("PlcEngine", "PlcEngine.onStart: " + ex + ": "
					+ ex.getMessage());
		}
	}

	public void stop()
	{
		// 1. Send the drop connection signal.
		java.io.OutputStream out = _messages_output_stream;
		if (out != null)
		{
			try
			{
		        PlcCommunication.MessageFromPlc.Builder b = PlcCommunication.MessageFromPlc.newBuilder();
		        b.setId(--Last_OOBId_Sent);
		        b.setCommand(PlcCommunication.COMMAND.DROP_CONNECTION);
		        PlcCommunication.MessageFromPlc msg = b.build();
		        this._sendToServer(msg);
		        _sleep(300); // give them a chance to process it.
			}
			catch (Exception ex)
			{
				Log.e("PlcMasterService", "Error when sending DROP_CONNECTION message:" + ex.toString());
			}
			_thread_stop = true;
		}

		// 2. Signal stop to every thread.
		_thread_stop = true;
		try {
			_thread_plc.join();
		} catch (InterruptedException ex) {
			Log.e("PlcEngine", "stop(): Error '" + ex.toString() + "'.");
		}
	}
	
	public boolean isAlive()
	{
		Thread th = this._thread_plc;
		return th!=null && th.isAlive();
	}

	public boolean isConnected()
	{
		long t1 = System.currentTimeMillis();
		long dt = t1 - _lastServerActivityTimeMs;
		return dt>=0 && dt<SERVER_CONNECTION_TIMEOUT_MS;
	}

	public void handleMessage(PlcCommunication.MessageToPlc msg)
	{
		synchronized (_queries) {
			_queries.add(msg);
		}		
	}

	public void onDestroy()
	{
		// 1. Send the drop connection signal.
		java.io.OutputStream out = _messages_output_stream;
		if (out != null)
		{
			_thread_stop = true;
			try
			{
		        PlcCommunication.MessageFromPlc.Builder b = PlcCommunication.MessageFromPlc.newBuilder();
		        b.setId(--Last_OOBId_Sent);
		        b.setCommand(PlcCommunication.COMMAND.DROP_CONNECTION);
		        PlcCommunication.MessageFromPlc msg = b.build();
				msg.writeDelimitedTo(out);
				_sleep(10); // give them a chance to process it.
			}
			catch (Exception ex)
			{
				Log.e("PlcEngine", "Error when sending DROP_CONNECTION message:" + ex.toString());
			}
		}

		// 2. Signal stop to every thread.
		_thread_stop = true;
		try {
			_thread_plc.join();
		} catch (InterruptedException ex) {
			Log.e("PlcEngine", "PlcEngine.onDestroy: Error '"
					+ ex.toString() + "'.");
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

	// Flush the message queue.
	private void _flushMessagesToServer()
	{
		synchronized (_messages_to_server)
		{
			_messages_to_server.clear();
		}
	}

	public void plc_thread()
	{
		byte[] RowBuffer = new byte[DB_Row_Length];
		/*
		byte[] PreviousRowBuffer = new byte[DB_Row_Length];
		*/
		long[] previousRowBuffer = null;
		long PreviousWriteTimeMs = 0;

		Log.d("PlcEngine", "Service started!");

		ModbusMaster modbus = null;
		Socket modbus_client = null;
		while (!_thread_stop)
		{
			_flushMessagesToServer();
			
			// Do we have to reconnect the modbus service?
			if (!_thread_stop && modbus==null)
			{
				try {
					// Connect!
					modbus_client = _socketByConnectionString(plcContext.modbus);
					modbus = new ModbusMaster(modbus_client.getInputStream(), modbus_client.getOutputStream());
					Log.d("PlcEngine", "Connected to modbus proxy at IP:" + modbus_client.getRemoteSocketAddress().toString() + ".");
					_trigger_db_write = true;
				} catch (Exception ex) {
					modbus = null;
					modbus_client = null;
					Log.e("PlcEngine", "Cannot connect to modbus proxy:"
							+ ex.toString());
				}
			}

			int socket_exception_count = 0;
			int fatal_exception_count = 0;
			boolean should_reconnect = false;
			if (!_thread_stop && modbus!=null)
			{
				socket_exception_count = 0; // some devices could have obtained
											// some meaningful data, have to
											// reconnect in these cases.
				fatal_exception_count = 0;
				if (plcContext.Devices.size()>0 && _DB!=null)
				{
					for (IODevice dev : plcContext.Devices) {
						try {
							Thread.sleep(10);
							dev.SyncWithModbus(modbus);
						} catch (SocketException sex) {
							Log.d("PlcEngine", "Fatal error in communicating with device " + dev.DeviceAddress + ": " + sex.getMessage() + ".");
							should_reconnect = true;
							++fatal_exception_count;
						} catch (Exception ex) {
							Log.d("PlcEngine", "Error in communicating with device " + dev.DeviceAddress + ": " + ex.getMessage());
							++socket_exception_count;
						}
					}
					if (socket_exception_count == plcContext.Devices.size())
					{
						Log.d("PlcEngine", "All devices have failed, will reconnect.");
						should_reconnect = true;
					}
					else if (fatal_exception_count == plcContext.Devices.size())
					{
						Log.d("PlcEngine", "All devices have been disconnected, will reconnect.");
						should_reconnect = true;
					}
					else
					{
						// Update the computed signals, needed for logic.
						for (ComputedSignal cs : plcContext.ComputedSignals) {
							cs.Update();
						}
						CircularRowFormat.Encode(RowBuffer, 0, plcContext.Signals, plcContext.version);
						boolean this_row_is_different = _trigger_db_write;
						if (!this_row_is_different)
						{
							if (previousRowBuffer==null)
							{
								this_row_is_different = true;
							}
							else
							{
								for (int i=0; i<plcContext.Signals.size(); ++i)
								{
									IOSignal	s = plcContext.Signals.get(i);
									if (!s.SkipWriteCheck)
									{
										this_row_is_different = this_row_is_different || s.getValue()!=previousRowBuffer[i];
									}
								}
							}
						}
						if (this_row_is_different)
						{
							_trigger_db_write = false; // TODO: fix the race condition.
						}
						long t1 = System.currentTimeMillis();
						boolean max_period_exceeded = PreviousWriteTimeMs != 0 && t1 - PreviousWriteTimeMs > DB_MAX_WRITE_PERIOD_MS;
						if (this_row_is_different || PreviousWriteTimeMs == 0
								|| max_period_exceeded) {
							if (max_period_exceeded) {
								Log.d("PlcEngine", "No change for quite some time, writing database row anyway.");
							}
							try {
								_DB.Add(RowBuffer);
								PlcCommunication.DatabaseRow row = _DatabaseRow_Of_DB(_OOBDatabaseRowBuilder, _DB.HeadId, RowBuffer);
								_DatabaseRowEnvelopeBuilder.setId(--Last_OOBId_Sent);
								_DatabaseRowEnvelopeBuilder.setOOBDatabaseRow(row);
								_sendToServer(_DatabaseRowEnvelopeBuilder.build());
								PreviousWriteTimeMs = t1;
							} catch (Exception ex) {
								Log.d("PlcEngine",
										"Error adding row to the database:"
												+ ex.getMessage());
							}
						}
						if (previousRowBuffer==null)
						{
							previousRowBuffer = new long[plcContext.Signals.size()];
						}
						for (int i=0; i<plcContext.Signals.size(); ++i)
						{
							previousRowBuffer[i] = plcContext.Signals.get(i).getValue();
						}
		
						synchronized (_rows_for_display) {
							if (_rows_for_display.size() > 5) {
								_rows_for_display.remove(0);
							}
							_rows_for_display.add(RowBuffer);
						}
					}
					
					if (should_reconnect)
					{
						Log.d("PlcEngine", "Closing modbus connection.");
						try {
							modbus_client.close();
						}
						catch (Exception ex)
						{
							Log.e("PlcEngine", "Error when closing modbus connection: " + ex.getMessage());
						}
						modbus_client = null;
						modbus = null;
						continue;
					}
				}
			}

			// sleep for approximately 0.1 seconds.
			// for (int i = 0; i < 1 && !_thread_stop; ++i) {
				_sleep(100);
			// }
			
			// Any washing to do?
			if (_wash_fjp_washing != null && _wash_index>=0 && _wash_index<_wash_steps.length && _wash_start_time_ms!=0)
			{
				long wtime_ms = System.currentTimeMillis() - _wash_start_time_ms;
				SequenceStep ss = _wash_steps[_wash_index];
				if (ss.startTimeMs < wtime_ms)
				{
					for (PlcCommunication.MessageToPlc cmd : ss.commands)
					{
						handleMessage(cmd);
					}
					++_wash_index;
					if (_wash_index >= _wash_steps.length)
					{
						_resetWash();
					}
				}
			}

			// FIXME: substitute this with proper implementation!
			// Shall we start washing?
			long wash_delta = System.currentTimeMillis() - _wash_last_time_ms;
			int hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY);
			if (_wash_fjp_washing != null && wash_delta > WASH_PERIOD_MS && hour==WASH_HOUR)
			{
				_wash_last_time_ms = System.currentTimeMillis();
				Log.d("PlcEngine", "Time to wash!");
				
				PlcCommunication.MessageToPlc.Builder msg_builder = PlcCommunication.MessageToPlc.newBuilder();
				PlcCommunication.SignalAndValue.Builder msg_sv_builder = PlcCommunication.SignalAndValue.newBuilder();
				msg_sv_builder.setName(WASH_SIGNAL);
				msg_sv_builder.setValue(1);
				msg_builder.setId(1);
				msg_builder.addSetSignals(msg_sv_builder.build());
				PlcCommunication.MessageToPlc msg = msg_builder.build();
				this.handleMessage(msg);
			}
			
			// Process the queries.
			// Programs might want to be interested if there were any
			// queries.
			PlcCommunication.MessageToPlc qp;
			do {
				synchronized (_queries) {
					qp = _queries.poll();
				}
				if (qp != null && qp.hasId()) {
					// Query: Set signal value.
					int nsignals_to_set = qp.getSetSignalsCount();
					for (int signal_index = 0; signal_index < nsignals_to_set; ++signal_index) {
						PlcCommunication.SignalAndValue sv = qp.getSetSignals(signal_index);
						if (sv.hasName() && sv.hasValue()) {
							String signal_name = sv.getName();
							int signal_value = sv.getValue();
							IOSignal ios = plcContext
									.SignalByNameOrId(signal_name);
							if (ios == null) {
								Log.d("PlcEngine",
										"Query was to set signal "
												+ signal_name
												+ " to "
												+ signal_value
												+ ", but this signal is unknown to us!");
							} else {
								int old_signal_value = ios.getValue();
								Log.d("PlcEngine", signal_name + " := " + signal_value + " (previously:" + old_signal_value + ")");
								ios.setValue(signal_value);
								if (_wash_fjp_washing!=null && signal_name.equals(WASH_SIGNAL))
								{
									if (signal_value>0)
									{
										if (old_signal_value==0 && _wash_index<0)
										{
											Log.d("PlcEngine", "Washing started.");
											_wash_index = 0;
											_wash_start_time_ms = System.currentTimeMillis();
										}
									}
									else
									{
										if (old_signal_value>0 && _wash_index>=0)
										{
											_resetWash();
										}
									}
								}
							}
						}
					}
					
					// Query: request a range of database rows.
					if (qp.hasQueryDatabaseRows())
					{
						if (_DB == null)
						{
							Log.d("PlcEngine", "Database rows queried, but no database file open.");
						}
						else
						{
                            PlcCommunication.IdRange r = qp.getQueryDatabaseRows();
                            // Have we got the right stuff?
                            if (r.hasHeadId() && r.hasTailId())
                            {
                                int tail_id = r.getTailId();
                                int head_id = r.getHeadId();
                                if (head_id - tail_id > MAX_BATCH_SIZE)
                                {
                                    int new_tail_id = head_id - MAX_BATCH_SIZE;
                                    // PlcMaster.LogLine("PlcClientConnection: query for ID-s in the range " + head_id + " ... " + tail_id + " exceeds batch size limit of " + MAX_BATCH_SIZE + ", changing tail to " + new_tail_id + ".");
                                    tail_id = new_tail_id;
                                }
                                else
                                {
                                    // PlcMaster.LogLine("PlcClientConnection: query for ID-s in the range " + head_id + " ... " + tail_id + ".");
                                }
                                
                                _sendRows(tail_id, head_id, qp.getId());
                            }
						}
					}
					if (qp.hasNewRowPlcConfiguration())
					{
						PlcCommunication.RowPlcConfiguration new_cfg = qp.getNewRowPlcConfiguration();
						if (new_cfg.hasConfigurationFile() && new_cfg.hasPreferences())
						{
							// 1. Write out "setup.xml"
							/*
							ByteString cfg_file = new_cfg.getConfigurationFile();
							FileUtils.writeFile(plcContext.configurationFile, cfg_file.toByteArray());
							*/
							Log.d("PlcEngine", "Received new configuration, ignoring it.");
							// TODO: 2. Write new preferences file.
						}
					}
				}
			} while (qp != null);

			// Finally, execute the logic program!
			for (LogicStatement ls : plcContext.LogicProgram) {
				ls.Execute();
			}
				
			// sleep for approximately 1 second before reconnecting.
			for (int i = 0; i < 10 && !_thread_stop; ++i) {
				_sleep(100);
			}
		}

		try {
			CircularDB dbx = _DB;
			if (dbx != null) {
				_DB = null;
				dbx.Close();
			}
		} catch (Exception ex) {
			Log.e("PlcEngine",
					"Error in CirculardDB.Close:" + ex.toString());
		}
		try {
			if (modbus_client!=null)
			{
				modbus_client.close();
			}
		} catch (Exception ex) {
			Log.e("PlcEngine", "Error in modbus_client.close: " + ex.toString());
		}
		Log.d("PlcEngine", "Service: Finished!");
	}

	public void server_input_thread()
	{
    	Socket client = null;
		while (!_thread_stop && !_reload_configuration)
		{
			client = null;
			_messages_output_stream = null;
			_flushMessagesToServer();
			
			// 1. Connect to the control server.
			Log.d("PlcEngine", "Server connection: initiating...");
			try {
				// Connect!
				client = _socketByConnectionString(plcContext.server);
				Log.d("PlcEngine", "Connected to control server at: " + client.getRemoteSocketAddress().toString() + ".");
				_lastServerActivityTimeMs = System.currentTimeMillis();
				_trigger_db_write = true;
			} catch (Exception ex)
			{
				Log.e("PlcEngine", "Cannot connect to control server:" + ex.toString());
				_lastServerActivityTimeMs = 0;
				for (int i=0; i<100 && !_thread_stop; ++i)
				{
					_sleep(100);
				}
				continue;
			}
			
			// 2. Flush the message queue.
			_flushMessagesToServer();
			
			try
			{
				java.io.InputStream input = client.getInputStream();
				_messages_output_stream = client.getOutputStream();

				// 3. Send the greetings.
				byte[] cfg_file = new byte[] { };
				try
				{
					cfg_file = FileUtils.ReadFileAsBytes(plcContext.configurationFile);
				}
				catch (Exception ex)
				{
					Log.d("PlcMaster", "Error reading setup file: " + ex.getMessage());
				}
				byte[] prefs_file = new byte[] { };
				/*
				 * TODO: how to handle preferences?
				try
				{
					prefs_file = FileUtils.ReadFileAsBytes(new java.io.File(this.getFilesDir(), PREFERENCES_FILENAME));
				}
				catch (Exception ex)
				{
					Log.d("PlcMaster", "Error reading preferences file: " + ex.getMessage());
				}
				 */
		        Log.d("PlcEngine", "Sending configuration file (" +  cfg_file.length + " bytes) and preferences (" + prefs_file.length + " bytes) to the control server.");

		        // 1.1. Message needs to be constructed.
		        PlcCommunication.MessageFromPlc.Builder envelope_builder = PlcCommunication.MessageFromPlc.newBuilder();
		        envelope_builder.setId(--Last_OOBId_Sent);

		        // 1.2. OOBConfiguration
		        PlcCommunication.Configuration.Builder configuration_builder = PlcCommunication.Configuration.newBuilder();
		        configuration_builder.setDeviceId(plcContext.plcDeviceId);
		        configuration_builder.setVersion(plcContext.version);
		        configuration_builder.setConfigurationFile(com.google.protobuf.ByteString.copyFrom(cfg_file));
		        configuration_builder.setPreferences(com.google.protobuf.ByteString.copyFrom(prefs_file));
		        envelope_builder.setOOBConfiguration(configuration_builder);
		        
		        // 1.3. OOBDatabaseRange, if any.
		        if (_DB != null)
		        {
			        PlcCommunication.IdRange.Builder idrange_builder = PlcCommunication.IdRange.newBuilder();
			        idrange_builder.setTailId(_DB.TailId);
			        idrange_builder.setHeadId(_DB.HeadId);
			        envelope_builder.setOOBDatabaseRange(idrange_builder);
		        }

		        // 1.4. Send it!
		        PlcCommunication.MessageFromPlc configuration_message = envelope_builder.build();
		        _sendToServer(configuration_message);
				
				// 4. Process input while possible.
				while (!_thread_stop)
				{
	                PlcCommunication.MessageToPlc msg = PlcCommunication.MessageToPlc.parseDelimitedFrom(input);
					_lastServerActivityTimeMs = System.currentTimeMillis();
	                if (msg == null)
	                {
	                    Log.d("PlcEngine", "Control server disconnected, finishing servicing.");
	                    break;
	                }
	                else
	                {
	            		if (msg.hasCommand() && msg.getCommand()==PlcCommunication.COMMAND.RELOAD_CONFIGURATION)
	            		{
	            			Log.d("PlcMaster", "Received command: RELOAD_CONFIGURATION");
	            			_reload_configuration = true;
	            		}
	            		else
	            		{
		            		synchronized (_queries)
		            		{
		            			_queries.add(msg);
		            		}
	            		}
	                }
				}
			} catch (Exception ex)
			{
                Log.d("PlcEngine", "Control server exception:" + ex.toString());
			}
		}
		
		_messages_output_stream = null;
		_lastServerActivityTimeMs = 0;
		try {
			if (client!=null)
			{
				client.close();
			}
		} catch (Exception ex)
		{
			Log.e("PlcEngine", "Error in control server Socket.close: " + ex.toString());
		}
		Log.d("PlcEngine", "Server connection: Finished!");
		_server_thread = null;
	}

	/**
	 * Synchronize on the collection before operations! Produced by the main
	 * thread and server_input_thread. Consumed by server_output_thread
	 **/
	private final Queue<PlcCommunication.MessageFromPlc> _messages_to_server = new LinkedList<PlcCommunication.MessageFromPlc>();
	private volatile java.io.OutputStream _messages_output_stream = null;

	boolean _messages_to_server_overflow = false;
	private void _sendToServer(PlcCommunication.MessageFromPlc msg)
	{
		int nmessages = 0;
		synchronized (_messages_to_server) {
			_messages_to_server.add(msg);
			nmessages = _messages_to_server.size();
		}
		boolean overflow = nmessages > 1000;
		if (overflow && !_messages_to_server_overflow)
		{
			Log.e("PlcEngine", "More than 1000 messages in the message queue to the server!");
		}
		_messages_to_server_overflow = overflow;
	}

	public void server_output_thread() {
		Log.e("PlcEngine", "Server output thread started.");
		while (!_thread_stop) {
			java.io.OutputStream out = _messages_output_stream;
			if (out == null)
			{
				_sleep(500);
			}
			else
			{
				PlcCommunication.MessageFromPlc msg = null;
				synchronized (_messages_to_server) {
					if (!_messages_to_server.isEmpty()) {
						msg = _messages_to_server.remove();
					}
				}
				if (msg == null) {
					// Time to wait!
					_sleep(100);
				} else {
					// Time to spread the news!
					try {
						Log.d("PlcEngine.server_output_thread", "Sending some stuff to the client.");
						msg.writeDelimitedTo(out);
						_lastServerActivityTimeMs = System.currentTimeMillis();
					} catch (Exception ex) {
						Log.e("PlcEngine",
								"Error in control server output thread: "
										+ ex.toString());
					}
				}
			}
		}
		Log.e("PlcEngine", "Server output thread finished.");
	}

    private static PlcCommunication.DatabaseRow _DatabaseRow_Of_DB(PlcCommunication.DatabaseRow.Builder builder, int RowId, byte[] row)
    {
        long time_ms = BitUtils.Int64_Of_Bytes_BE(row, CircularRowFormat.TIME_OFFSET);
        builder.setRowId(RowId);
        builder.setVersion(BitUtils.Int32_Of_Bytes_BE(row, CircularRowFormat.VERSION_OFFSET));
        builder.setTimeMs(time_ms);
        builder.setSignalValues(ByteString.copyFrom(row, CircularRowFormat.SIGNALS_OFFSET, row.length - CircularRowFormat.SIGNALS_OFFSET));
        return builder.build();
    }

    private void _sendRows(int tail_id, int head_id, long response_id)
    {
        int batch_size = head_id - tail_id;

        // Batch buffer might be of different length.
        while (_Batch_Buffer.size() < batch_size)
        {
            _Batch_Buffer.add(new byte[_DB.RowLength]);
        }

        // Go ahead with the query.
        int n = 0;
        Exception fetch_ex = null;
        try
        {
        	n = _DB.Fetch(_Batch_Buffer, tail_id, head_id);
        }
        catch (Exception ex)
        {
        	Log.d("PlcEngine", "Error when fetching range [" + tail_id + " .. " + head_id + "): " + ex.toString());
        	fetch_ex = ex;
        }

        // Send the reply packet.
        PlcCommunication.MessageFromPlc.Builder builder = PlcCommunication.MessageFromPlc.newBuilder();
        builder.setId(response_id);
        for (int i=0; i<n; ++i)
        {
            builder.addResponseToDatabaseRows(_DatabaseRow_Of_DB(_BatchSignalsBuilder, tail_id + i, _Batch_Buffer.get(i)));
        }

        PlcCommunication.Response.Builder response_builder = PlcCommunication.Response.newBuilder();
        if (fetch_ex!=null)
        {
            response_builder.setOK(false);
            response_builder.setMessage("Exception when fetching range: [" + tail_id + " .. " + head_id + "): " + fetch_ex.toString() + ".");
        }
        else if (n>0)
        {
            response_builder.setOK(true);
        }
        else
        {
            response_builder.setOK(false);
            response_builder.setMessage("Invalid range specified: [" + tail_id + " .. " + head_id + ").");
        }
        builder.setResponse(response_builder);
        PlcCommunication.MessageFromPlc reply_packet = builder.build();
        _sendToServer(reply_packet);
    }
}
