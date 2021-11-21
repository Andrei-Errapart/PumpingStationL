package com.errapartengineering.plcmaster;

import java.io.File;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.*; // Bundle, IBinder, etc.
import android.preference.PreferenceManager;
import android.util.Log;
import android.view.Menu;
import android.view.View;
import android.widget.*;
import android.content.*; // DialogInterface, ServiceConnection, ComponentName

import com.errapartengineering.plcengine.FileUtils;
import com.errapartengineering.plcengine.IOSignal;
import com.errapartengineering.plcengine.CircularRowFormat;
import com.errapartengineering.plcengine.IOType;
import com.errapartengineering.plcengine.ComputedSignal;
import com.errapartengineering.plcengine.PlcEngine;


/**
 * @author Andrei
 *
 */
public class PlcMasterActivity extends Activity implements Runnable {
	public final int RESULT_FQI_SETUP = 1;
	
	public final static File configurationFile = FileUtils.getFileOnExternalStorage("setup.xml");
	public final static File programFile = FileUtils.getFileOnExternalStorage("setup.prg");
	public final static File versionFile = FileUtils.getFileOnExternalStorage("setup.txt");
	
	public final static int COLOUR_RED = 0xFFFF1111;
	public final static int COLOUR_GREEN = 0xFF11FF11;
	
	private PlcMasterService _service = null;
	private com.errapartengineering.plcengine.Context _plcContext = null;
	private Intent _serviceIntent = null;
	private boolean _is_first_run = true;

	private final java.text.DecimalFormat _percentage_format = new java.text.DecimalFormat("##.#");
	private final java.text.DecimalFormat _cpu_usage_format = new java.text.DecimalFormat("##");
	private final java.text.DecimalFormat _pressure_format = new java.text.DecimalFormat("#.#");

	// CPU usage.
	private final TextviewCache _tvCpuUsage = new TextviewCache(0);
	
	// Communication state.
	private final TextviewCache _tvCommunication = new TextviewCache(0);
	
	// Dwell pump.
	// P01.PLC_CONTROL, P01.AUTO_CONTROL, P01.IS_RUNNING, P01.ERROR
	private final TextviewCache _tvP01 = new TextviewCache(4);
	
	
	// Analog sensors.
	private final TextviewCache _tvWellLevelSensor = new TextviewCache(1);
	private final TextviewCache _tvPressureSensorPIA1 = new TextviewCache(1);
	private final TextviewCache _tvPressureSensorPIA2 = new TextviewCache(1);
	
	// Storage tanks 1 and 2
	// LIA2.1.VALUE LSA2.1
	private final TextviewCache _tvTankMV1 = new TextviewCache(2);	
	private final TextviewCache _tvTankMV2 = new TextviewCache(2);

	// II stage pumps.
	// HZ, PRESSURE, HOURS
	private final TextviewCache _tvStage2P1 = new TextviewCache(4);
	private final TextviewCache _tvStage2P2 = new TextviewCache(4);
	private final TextviewCache _tvStage2P3 = new TextviewCache(4);
	private final TextviewCache _tvStage2P4 = new TextviewCache(4);
	private final TextviewCache _tvStage2Pressure = new TextviewCache(4);

	// Power analyzer.
	// V, kW, kWh
	private final TextviewCache _tvPowerAnalyzerL1 = new TextviewCache(3);
	private final TextviewCache _tvPowerAnalyzerL2 = new TextviewCache(3);
	private final TextviewCache _tvPowerAnalyzerL3 = new TextviewCache(3);

	// Water filter.
	private final TextviewCache _tvIncomingWaterUsage = new TextviewCache(1);
	private final TextviewCache _tvWashWaterUsage = new TextviewCache(1);
	private final TextviewCache _tvWaterOutput = new TextviewCache(1);
	private boolean _isWashingInternal = false; // assume external washing.
	private final TextviewCache _tvIsFilterWashing = new TextviewCache(1);

	// Water tank level limits.
	private double _level_limit_min = 1.0;
	private IOSignal _iosLia2Min = null;
	private double _level_limit_max = 1.0;
	private IOSignal _iosLia2Max = null;
	
	// FQI setup.
	private List<FqiSignals> _fqiSignals = new ArrayList<FqiSignals>();
	
	private int _signal_index = -1;
	
    /** Defines callbacks for service binding, passed to bindService() */
	public ServiceConnection _connection = null;
    private ServiceConnection  _newConnection = new ServiceConnection() {
        public void onServiceConnected(ComponentName className,
                IBinder service)
        {
        	Log.d("ServiceConnection", "onServiceConnected.");
        	PlcMasterService.LocalBinder binder = (PlcMasterService.LocalBinder) service;
            _service = binder.getService();
            if (_service.plcEngine.isAlive())
            {
            	Log.d("ServiceConnection", "onServiceConnected: Found live service.");
            }
            else
            {
            	if (_reloadConfig())
            	{
            		Log.d("ServiceConnection1", "Starting new connection.");
                    _service.plcContext = _plcContext;
                    startService(_serviceIntent);
            	}
            	else
            	{
            		Log.e("PlcMasterActivity", "Unexpected situation 1!");
            		// FIXME: how to unbind the service.
            		return;
            	}
            }
            // 1. _signal_index
            _signal_index = 0;
            List<IOSignal>	signals = _service.plcContext.Signals;
            Map<String, IOSignal> signalmap = _service.plcContext.SignalMap;
            for (IOSignal ios : signals)
            {
            	if (ios.Type == IOType.COIL)
            	{
            		break;
            	}
            	++_signal_index;            	
            }
            if (_signal_index<signals.size())
            {
            	_signal_view1_update();
            }
            else
            {
            	// bad!
            	_signal_index = -1;
            }
            
            // 2. Signals to be displayed...
            try
            {
        		SharedPreferences sharedPref = PreferenceManager.getDefaultSharedPreferences(PlcMasterActivity.this);        		
        		
        		// Well operation.
        		_tvP01.setSignals(new IOSignal[] {
		        	IOSignal.findSignalByName(signals, "P01.PLC_CONTROL"),
		        	IOSignal.findSignalByName(signals, "P01.AUTO_CONTROL"),
		        	IOSignal.findSignalByName(signals, "P01.IS_RUNNING"),
		        	IOSignal.findSignalByName(signals, "P01.ERROR"),
        		});
	        	
	        	// Analog sensors.
            	String pref_well_level_sensor_LIA1 = sharedPref.getString("pref_well_level_sensor_LIA1", "4;20;0;50");
            	_tvWellLevelSensor.setSignals(new IOSignal[] {
            			new ComputedSignal("LIA1.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "LIA1" }, pref_well_level_sensor_LIA1, "#.#", "m", "Puurkaevu veetase", false, signalmap),
            	});
            	
            	String pref_pressure_sensor_PIA1 = sharedPref.getString("pref_pressure_sensor_PIA1", "4;20;0;10");
            	_tvPressureSensorPIA1.setSignals(new IOSignal[] {
            			new ComputedSignal("PIA1.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "PIA1" }, pref_pressure_sensor_PIA1, "#.##", "bar", "Siseneva vee rõhk", false, signalmap),
            	});
            	
            	String pref_pressure_sensor_PIA2 = sharedPref.getString("pref_pressure_sensor_PIA2", "4;20;0;10");
            	if (signalmap.containsKey("PIA2"))
            	{
	            	_tvPressureSensorPIA2.setSignals(new IOSignal[] {
	            			new ComputedSignal("PIA2.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "PIA2" }, pref_pressure_sensor_PIA2, "#.##", "bar", "Väljuva vee rõhk", false, signalmap),
	            	});
            	}
            	
            	// Storage tanks 1 and 2
            	String pref_level_sensor = sharedPref.getString("pref_level_sensor", "0;20;0;4");
            	_tvTankMV1.setSignals(new IOSignal[] {
            			new ComputedSignal("LIA2.1.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "LIA2.1" }, pref_level_sensor, "#.##", "m", "Mahuti 1 veetase", false, signalmap),
            			IOSignal.findSignalByName(signals, "LSA2.1"),
            	});
            	_tvTankMV2.setSignals(new IOSignal[] {
            			new ComputedSignal("LIA2.2.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "LIA2.2" }, pref_level_sensor, "#.##", "m", "Mahuti 2 veetase", false, signalmap),
            			IOSignal.findSignalByName(signals, "LSA2.2"),
            	});
            	
            	// II stage pressures.
            	ComputedSignal p1pressure = new ComputedSignal("PP1.PRESSURE", ComputedSignal.ANALOG_SENSOR, new String[] {"PP1.ACTUAL_VALUE"}, "0;32767;0;327.67", "#.##", "bar", "II astme 1. pumba väljundrõhk", false, signalmap);
            	ComputedSignal p2pressure = new ComputedSignal("PP2.PRESSURE", ComputedSignal.ANALOG_SENSOR, new String[] {"PP2.ACTUAL_VALUE"}, "0;32767;0;327.67", "#.##", "bar", "II astme 2. pumba väljundrõhk", false, signalmap);
    			ComputedSignal p3pressure = new ComputedSignal("PP3.PRESSURE", ComputedSignal.ANALOG_SENSOR, new String[] {"PP3.ACTUAL_VALUE"}, "0;32767;0;327.67", "#.##", "bar", "II astme 3. pumba väljundrõhk", false, signalmap);
				ComputedSignal p4pressure = new ComputedSignal("PP4.PRESSURE", ComputedSignal.ANALOG_SENSOR, new String[] {"PP4.ACTUAL_VALUE"}, "0;32767;0;327.67", "#.##", "bar", "II astme 4. pumba väljundrõhk", false, signalmap);
				
            	// II stage P1.
            	_tvStage2P1.setSignals(new IOSignal[] {
            			new ComputedSignal("PP1.HZ", ComputedSignal.ANALOG_SENSOR, new String[] {"PP1.FREQUENCY"}, "0;32767;0;3276.7", "##.#", "Hz", "II astme 1. pumba töösagedus", false, signalmap),
            			p1pressure,
            			new ComputedSignal("PP1.HOURS", ComputedSignal.PUMP_HOURS_OF_OPERATION, new String[] {"PP1.RUNTIME_HOURS_HIGH", "PP1.RUNTIME_HOURS_LOW", "PP1.RUNTIME_MINUTES"}, "", "#.#", "", "II astme 1. pumba töötunnid", false, signalmap),
            			IOSignal.findSignalByName(signals, "PP1.ERROR"),
            	});
            	
            	// II stage P2.
            	_tvStage2P2.setSignals(new IOSignal[] {
            			new ComputedSignal("PP2.HZ", ComputedSignal.ANALOG_SENSOR, new String[] {"PP2.FREQUENCY"}, "0;32767;0;3276.7", "##.#", "Hz", "II astme 2. pumba töösagedus", false, signalmap),
            			p2pressure,
            			new ComputedSignal("PP2.HOURS", ComputedSignal.PUMP_HOURS_OF_OPERATION, new String[] {"PP2.RUNTIME_HOURS_HIGH", "PP2.RUNTIME_HOURS_LOW", "PP2.RUNTIME_MINUTES"}, "", "#.#", "", "II astme 2. pumba töötunnid", false, signalmap),
            			IOSignal.findSignalByName(signals, "PP2.ERROR"),
            	});
            	
            	// II stage P3.
            	_tvStage2P3.setSignals(new IOSignal[] {
	            	new ComputedSignal("PP3.HZ", ComputedSignal.ANALOG_SENSOR, new String[] {"PP3.FREQUENCY"}, "0;32767;0;3276.7", "##.#", "Hz", "II astme 3. pumba töösagedus", false, signalmap),
        			p3pressure,
	            	new ComputedSignal("PP3.HOURS", ComputedSignal.PUMP_HOURS_OF_OPERATION, new String[] {"PP3.RUNTIME_HOURS_HIGH", "PP3.RUNTIME_HOURS_LOW", "PP3.RUNTIME_MINUTES"}, "", "#.#", "", "II astme 3. pumba töötunnid", false, signalmap),
        			IOSignal.findSignalByName(signals, "PP3.ERROR"),
            	});
            	
            	// II stage P4.
            	_tvStage2P4.setSignals(new IOSignal[] {
	            	new ComputedSignal("PP4.HZ", ComputedSignal.ANALOG_SENSOR, new String[] {"PP4.FREQUENCY"}, "0;32767;0;3276.7", "##.#", "Hz", "II astme 4. pumba töösagedus", false, signalmap),
        			p4pressure,
	            	new ComputedSignal("PP4.HOURS", ComputedSignal.PUMP_HOURS_OF_OPERATION, new String[] {"PP4.RUNTIME_HOURS_HIGH", "PP4.RUNTIME_HOURS_LOW", "PP4.RUNTIME_MINUTES"}, "", "#.#", "", "II astme 4. pumba töötunnid", false, signalmap),
        			IOSignal.findSignalByName(signals, "PP4.ERROR"),
            	});
            	
            	_tvStage2Pressure.setSignals(new IOSignal[] {
            			p1pressure,
            			p2pressure,
            			p3pressure,
            			p4pressure,
            	});
            	
            	// Power analyzer: L1
            	_tvPowerAnalyzerL1.setSignals(new IOSignal[] {
	            	new ComputedSignal("PA.A.V", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "V_a_low", "V_a_high" }, "", "#", "V", "L1 pinge", false, signalmap),
	            	new ComputedSignal("PA.A.kW.", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kW_a_low", "kW_a_high" }, "", "##.##", "kW", "L1 võimsus", false, signalmap),
	            	new ComputedSignal("PA.A.kWh", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kWh_a_low", "kWh_a_high" }, "", "#", "kWh", "L1 energiatarve", false, signalmap),
            	});
            	
            	// Power analyzer: L2
            	_tvPowerAnalyzerL2.setSignals(new IOSignal[] {
	            	new ComputedSignal("PA.B.V", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "V_b_low", "V_b_high" }, "", "#", "V", "L2 pinge", false, signalmap),
	            	new ComputedSignal("PA.B.kW", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kW_b_low", "kW_b_high" }, "", "##.##", "kW", "L2 võimsus", false, signalmap),
	            	new ComputedSignal("PA.B.kWh", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kWh_b_low", "kWh_b_high" }, "", "#", "kWh", "L2 energiatarve", false, signalmap),
            	});
            	
            	// Power analyzer: L3
            	_tvPowerAnalyzerL3.setSignals(new IOSignal[] {
	            	new ComputedSignal("PA.C.V", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "V_c_low", "V_c_high" }, "", "#", "V", "L3 pinge", false, signalmap),
	            	new ComputedSignal("PA.C.kW", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kW_c_low", "kW_c_high" }, "", "##.##", "kW", "L3 võimsus", false, signalmap),
	            	new ComputedSignal("PA.C.kWh", ComputedSignal.FLOATING_POINT_VALUE, new String[] { "kWh_c_low", "kWh_c_high" }, "", "#", "kWh", "L3 energiatarve", false, signalmap),
            	});

            	
            	// Water Filter stuff.
            	ComputedSignal fqi1_value = new ComputedSignal("FQI1.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI1.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Puurkaevuvee kulu", false, signalmap);
            	_tvIncomingWaterUsage.setSignals(new IOSignal[] { fqi1_value });

            	ComputedSignal fqi2_value = new ComputedSignal("FQI2.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI2.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Pesuvee kulu", false, signalmap);
            	_tvWashWaterUsage.setSignals(new IOSignal[] { fqi2_value });

            	ComputedSignal fqi3_value = new ComputedSignal("FQI3.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI3.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Väljastatud vee kogus", false, signalmap);
            	_tvWaterOutput.setSignals(new IOSignal[] { fqi3_value });

            	_isWashingInternal = false;
            	for (IOSignal s : signals)
            	{
            		if (s.Name.equalsIgnoreCase(PlcEngine.WASH_SIGNAL)) {
            			_isWashingInternal = true;
            			break;
            		}
            	}
            	_tvIsFilterWashing.setSignals(new IOSignal[] {
            			IOSignal.findSignalByName(signals, _isWashingInternal ? PlcEngine.WASH_SIGNAL : "FJP.P01.STOP"),
            	});
            	
            	// some hackery of the limits.
            	_level_limit_min = Double.parseDouble(sharedPref.getString("pref_reservoir_level_min", "1.0"));
            	int level_limit_min_adc = _tvTankMV1.computedSignals[0].ReverseCalculation(_level_limit_min);
            	Log.d("PlcMasterActivity", "Min: " + Double.toString(_level_limit_min) +" ADC:" + Integer.toString(level_limit_min_adc));
            	_iosLia2Min = IOSignal.findSignalByName(signals, "LIA2.MIN");
            	_iosLia2Min.setValue(level_limit_min_adc);
            	
            	
            	_level_limit_max = Double.parseDouble(sharedPref.getString("pref_reservoir_level_max", "2.0"));
            	int level_limit_max_adc = _tvTankMV1.computedSignals[0].ReverseCalculation(_level_limit_max);
            	Log.d("PlcMasterActivity", "Max: " + Double.toString(_level_limit_max) +" ADC:" + Integer.toString(level_limit_max_adc));
            	_iosLia2Max = IOSignal.findSignalByName(signals, "LIA2.MAX");
            	_iosLia2Max.setValue(level_limit_max_adc);
            	
            	// FQI signals.
            	_fqiSignals.clear();
            	_fqiSignals.add(new FqiSignals("FQI1", signals, fqi1_value));
            	_fqiSignals.add(new FqiSignals("FQI2", signals, fqi2_value));
            	_fqiSignals.add(new FqiSignals("FQI3", signals, fqi3_value));

/*
            	_tvIncomingWaterUsage.setSignals(new IOSignal[] {
            			new ComputedSignal("FQI1.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI1.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Puurkaevuvee kulu", false, signalmap),
            	});
            	_tvWashWaterUsage.setSignals(new IOSignal[] {
            			new ComputedSignal("FQI2.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI2.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Pesuvee kulu", false, signalmap),
            	});
            	_tvWaterOutput.setSignals(new IOSignal[] {
            			new ComputedSignal("FQI3.VALUE", ComputedSignal.ANALOG_SENSOR, new String[] { "FQI3.EXTENDED" }, "0;32767;0;3276.7", "#.#", "m3", "Väljastatud vee kogus", false, signalmap),
            	});
*/
            }
            catch (Exception ex)
            {
            	Log.d("PlcMasterActivity", "Cannot obtain signals: " + ex.getMessage());
            }
            _connection = this;
        }
        
        // called when the service crashes...
        public void onServiceDisconnected(ComponentName arg0)
        {
        	Log.e("ServiceConnection", "onServiceDisconnected. Crash?");
        	_service = null;
        	_connection = null;
        	_fqiSignals.clear();
        }
    };
   
	@Override
	public void onCreate(Bundle savedInstanceState) {
		Log.d("PlcMasterActivity", "onCreate, _is_first_run="+_is_first_run+".");
		super.onCreate(savedInstanceState);
		setContentView(R.layout.activity_main);

		_serviceIntent = new Intent(this, PlcMasterService.class);
		_poll_handler.postDelayed(this, _POLL_PERIOD_MS);
	}

	@Override
	public void onDestroy()
	{
		Log.d("PlcMasterActivity", "onDestroy.");
		if (_connection!=null)
		{
			unbindService(_connection);
			_connection = null;
		}
		super.onDestroy();
	}
	
	@Override
	public void onResume()
	{
		Log.d("PlcMasterActivity", "onResume, is_first_run=" + _is_first_run + ".");		
		super.onResume();
		
		// UI caches.

		_tvCpuUsage.updateTextview(this, R.id.textviewCpuUsage);
		_tvCommunication.updateTextview(this, R.id.textviewCommunication);
		_tvP01.updateTextview(this, R.id.textViewP01Mode);
		
		_tvWellLevelSensor.updateTextview(this, R.id.textViewLIA1);
		_tvPressureSensorPIA1.updateTextview(this, R.id.textViewPIA1);
		_tvPressureSensorPIA2.updateTextview(this, R.id.textViewPIA2);

		_tvTankMV1.updateTextview(this, R.id.textViewMV1Level);
		_tvTankMV2.updateTextview(this, R.id.textViewMV2Level);
		
		_tvStage2P1.updateTextview(this, R.id.textViewPP1);
		_tvStage2P2.updateTextview(this, R.id.textViewPP2);
		_tvStage2P3.updateTextview(this, R.id.textViewPP3);
		_tvStage2P4.updateTextview(this, R.id.textViewPP4);
		
		_tvStage2Pressure.updateTextview(this,  R.id.textViewPIA2);
		
		_tvPowerAnalyzerL1.updateTextview(this, R.id.textViewPowerAnalyzerP1);
		_tvPowerAnalyzerL2.updateTextview(this, R.id.textViewPowerAnalyzerP2);
		_tvPowerAnalyzerL3.updateTextview(this, R.id.textViewPowerAnalyzerP3);
		
		_tvIncomingWaterUsage.updateTextview(this, R.id.textViewWellWaterUsage);
		_tvWashWaterUsage.updateTextview(this, R.id.textViewWashWaterUsage);
		_tvWaterOutput.updateTextview(this, R.id.textViewWaterOutput);
		_tvIsFilterWashing.updateTextview(this, R.id.textViewWash);
	}
	
	@Override
	public void onPause()
	{
		Log.d("PlcMasterActivity", "onPause, is_first_run=" + _is_first_run + ".");		
		super.onPause();
	}
	
	@Override
	public boolean onCreateOptionsMenu(Menu menu) {
		getMenuInflater().inflate(R.menu.activity_main, menu);
		return true;
	}
	
	@Override
	public boolean onOptionsItemSelected(android.view.MenuItem item) {
		switch (item.getItemId())
		{
		case R.id.menu_StartPLC:
			_PLC_Start();
			return true;
		case R.id.menu_StopPLC:
			_PLC_Stop();
			return true;
		case R.id.menu_fqisetup:
			if (_fqiSignals.size()>0 && _connection!=null)
			{
				startActivityForResult(new Intent(this, FqiSetupActivity.class), RESULT_FQI_SETUP);
			}
			else
			{
				Utils.messageBox(this, "Enne veemõõdikute algseadistamist käivita PLC!");
			}
			return true;
		case R.id.menu_preferences:
            startActivity(new Intent(this, PlcPreferencesActivity.class));
			return true;
		default:
			return super.onOptionsItemSelected(item);
		}
	}

	protected void onActivityResult(int requestCode, int resultCode, Intent data)
	{
		if (requestCode == RESULT_FQI_SETUP)
		{
			Bundle extras = data!=null ? data.getExtras() : null;
			Log.d("onActivityResult", "resultCode=" + resultCode + ", data=" + data + ", extra=" + extras);
			PlcMasterService service = _service;
			if (resultCode==FqiSetupActivity.RESULTCODE_OK && extras!=null && service!=null) {
				for (FqiSignals fqi : _fqiSignals)
				{
					if (extras.containsKey(fqi.name)) {
						double new_reading = extras.getDouble(fqi.name);
						int fqi16 = fqi.fqi16.getValue() & 0xFFFF;
						Log.d(fqi.name, "new_reading=" + new_reading + ", fqi16=" + fqi16 + ", old_fqi32=" + fqi.fqi32.getValue() + ", old_ffset=" + fqi.offset.getValue() + ".");
						
						// Have to set offset such that FQI32 = FQI16 + NEW_OFFSET
						int new_fqi32 = fqi.signalToReading.ReverseCalculation(new_reading);
						int new_offset = new_fqi32 - fqi16;
						Log.d(fqi.name, "new_fqi32=" + new_fqi32 + ", new_offset=" + new_offset);
						service.plcSetSignal(fqi.fqi32.Name, new_fqi32);
						service.plcSetSignal(fqi.offset.Name, new_offset);
					}
				}
			}
		}
	}
	
	private void _signal_view1_update()
	{
		PlcMasterService service = _service;
		int index = _signal_index;
		if (service!=null && index>=0)
		{
	    	IOSignal s = service.plcContext.Signals.get(index);
			((TextView)findViewById(R.id.textViewIO1)).setText(s.Name);
			((TextView)findViewById(R.id.textViewIO2)).setText(s.Description);
		}
	}
	
	private void _changeIO(int step)
	{
		PlcMasterService service = _service;
		int index = _signal_index;
		if (service!=null && index>=0)
		{
			List<IOSignal> signals = service.plcContext.Signals;
			
			for (int i=0; i<signals.size(); ++i)
			{
				index += step;
				if (index<0)
				{
					index = signals.size()-1;
				}
				else if (index>=signals.size())
				{
					index = 0;
				}
				IOSignal ios = signals.get(index);
            	if (ios.Type == IOType.COIL)
            	{
            		_signal_index = index;
                	_signal_view1_update();
                	break;
            	}
			}
		}
	}
	
	private void _setIO(int newValue)
	{
		PlcMasterService service = _service;
		int index = _signal_index;
		if (service!=null && index>=0)
		{
			service.plcSetSignal(service.plcContext.Signals.get(index).Name, newValue);
		}
	}
	
	public void onButton_Next1(View view)
	{
		_changeIO(+1);
	}
	
	public void onButton_Prev1(View view)
	{
		_changeIO(-1);
	}
	
	public void onButton_On1(View view)
	{
		_setIO(1);
	}
	
	public void onButton_Off1(View view)
	{
		_setIO(0);
	}

	public void onButton_Wash(View view)
	{
		PlcMasterService service = _service;
		if (service!=null)
		{
			service.plcSetSignal(com.errapartengineering.plcengine.PlcEngine.WASH_SIGNAL, 1);
		}
	}
	
	// Most important: ability to reload configuration!
	private boolean _reloadConfig()
	{
		try
		{
			String device_id = Utils.getDeviceId(this);
			SharedPreferences sharedPref = PreferenceManager.getDefaultSharedPreferences(this);
			String central_server = sharedPref.getString(PlcPreferencesActivity.KEY_PREF_CONTROL_SERVER, "213.35.156.141:1503");
			String modbus_server = sharedPref.getString(PlcPreferencesActivity.KEY_PREF_MODBUS_PROXY, "127.0.0.1:1502");
			
			_plcContext = new com.errapartengineering.plcengine.Context(device_id, configurationFile, programFile, versionFile, central_server, modbus_server);
			return true;
		}
		catch (Exception ex)
		{
			String msg = "Cannot load configuration:" + ex.getMessage();
			Log.e("PlcMasterActivity", msg);
			Utils.messageBox(this, msg);
			return false;
		}
	}
	
	private void _PLC_Start()
	{
		if (_connection == null)
		{
			bindService(_serviceIntent,  _newConnection, Context.BIND_AUTO_CREATE);
			TextView tv = (TextView)findViewById(R.id.textviewPlcStatus);
			tv.setText("Töötab");
			tv.setBackgroundColor(COLOUR_GREEN);
		}
		else
		{
			Utils.messageBox(this, "Peata PLC enne restarti.");
		}
	}

	private void _PLC_Stop()
	{
		if (_connection==null)
		{
			Utils.messageBox(this, "Käivita PLC enne peatamist.");
		}
		else
		{
			Log.d("PlcMasterActivity", "_PLC_Stop: Stopping service.");
			_service.plcStopService();
			if (_connection!=null)
			{
				Log.d("PlcMasterActivity", "_PLC_Stop: Unbinding service.");
				unbindService(_connection);
				_connection = null;
			}
			TextView tv = (TextView)findViewById(R.id.textviewPlcStatus);
			tv.setText("Peatatud.");
			tv.setBackgroundColor(COLOUR_RED);
		}
	}

	private Handler _poll_handler = new Handler();
	private int _POLL_PERIOD_MS = 100;
	private boolean[] _signal_is_connected = null;
	private int[] _signal_values = null;

	// Update the display of the 2nd stage pump.
	private void _Update2ndStageDisplay(TextviewCache tv)
	{
		if (tv.isOk())
		{
			tv.updateComputedSignals();
			int errno = tv.signals[3].getValue();
			boolean is_ok = errno==0;
			tv.setText(tv.computedSignals[0].DisplayReading
					+ " " + tv.computedSignals[1].DisplayReading
					+ " " + tv.computedSignals[2].DisplayReading
					+ " " + (is_ok ?  "Korras" : ("Viga:0x" + Integer.toHexString(errno))));
			tv.setBackgroundColor(is_ok ? COLOUR_GREEN : COLOUR_RED);
		}
	}

	private void _UpdatePowerAnalyzerDisplay(TextviewCache tv)
	{
		if (tv.isOk())
		{
			tv.updateComputedSignals();
			tv.setText(tv.computedSignals[0].DisplayReading + " " + tv.computedSignals[1].DisplayReading + " " + tv.computedSignals[2].DisplayReading);
		}
	}
	
	// Update the display according to _signal_is_connected, _signal_values and signals.
	private void _UpdateDisplay(List<IOSignal> signals)
	{
		// CPU usage.
		if (_tvCpuUsage.isOk())
		{
			double cpu_usage = Utils.getCpuUsage();
			double total_usage = Utils.getTotalCpuUsage();
			double limit = 50.0;
			_tvCpuUsage.setText("PLC:" + _cpu_usage_format.format(cpu_usage) + "%  Kokku:" + _cpu_usage_format.format(total_usage) + "%");
			_tvCpuUsage.setBackgroundColor(cpu_usage>=limit || total_usage>=limit ? COLOUR_RED : COLOUR_GREEN);
		}
		
		// Connection status.
		PlcMasterService service = _service;
		boolean is_connected = false;
		if (service!=null)
		{
			com.errapartengineering.plcengine.IEngine engine = service.plcEngine;
			if (engine!=null)
			{
				try
				{
					is_connected = engine.isConnected();
				}
				catch (Exception ex)
				{
					Log.d("PlcMasterActivity", "Didn't really expect that: " + ex.getMessage());
				}
			}
		}
		_tvCommunication.setText(is_connected ? "Olemas" : "Puudub");
		_tvCommunication.setBackgroundColor(is_connected ? COLOUR_GREEN : COLOUR_RED);
		
		// P01.PLC_CONTROL, P01.AUTO_CONTROL, P01.IS_RUNNING, P01.ERROR		
		if (_tvP01.isOk())
		{
			String controlMode = "";
			if (_tvP01.signals[0].getValue()==0)
			{
				controlMode = _tvP01.signals[1].getValue()==0 ? "Manual" : "Auto";
			}
			else
			{
				controlMode = "PLC";
			}
			_tvP01.setText(
					controlMode
				+ (_tvP01.signals[2].getValue()==0 ? "; Seisab" : "; Töötab")
				+ (_tvP01.signals[3].getValue()==0 ? "; Korras" : "; Häire")
					);
		}
		/*
		if (_fqi1!=null && _fjp_p01_stop!=null && _fjp_p5_start!=null && _fjp_enable_wash!=null && _fjp_p5_is_running!=null)
		{
			((TextView)this.findViewById(R.id.textViewFJP)).setText(""
					+ "P01:" + (_fjp_p01_stop.Get()==0 ? "Start" : "Stop") + " "
					+ "P5:" + (_fjp_p5_start.Get()==0 ? "Stop" : "Start") + ", " + (_fjp_p5_is_running.Get()==0 ? "Stopped" : "Running") + " "
					+ "Wash:" + (_fjp_enable_wash.Get()==0 ? "Disabled" : "Enabled") + " "
					+ "FQI1:" + _fqi1.Get() + " "
					);
		}
		*
		*/
		if (_tvWellLevelSensor.isOk())
		{
			_tvWellLevelSensor.updateComputedSignals();
			_tvWellLevelSensor.setText(_tvWellLevelSensor.computedSignals[0].DisplayReading);
		}
		if (_tvPressureSensorPIA1.isOk())
		{
			_tvPressureSensorPIA1.updateComputedSignals();
			_tvPressureSensorPIA1.setText(_tvPressureSensorPIA1.computedSignals[0].DisplayReading);
		}
		
		boolean isPia2Ok = _tvPressureSensorPIA2.isOk();
		if (isPia2Ok)
		{
			_tvPressureSensorPIA2.updateComputedSignals();
			_tvPressureSensorPIA2.setText(_tvPressureSensorPIA2.computedSignals[0].DisplayReading);
		}
		
		if (_tvTankMV1.isOk())
		{
			_tvTankMV1.updateComputedSignals();
			double percentage = _tvTankMV1.computedSignals[0].FloatingPointValue / _level_limit_max * 100;
			_tvTankMV1.setText(_tvTankMV1.computedSignals[0].DisplayReading
					+ " [" + _percentage_format.format(percentage)  + "%]" + (_tvTankMV1.signals[1].getValue()==0 ? "" : " Täis"));
		}
		if (_tvTankMV2.isOk())
		{
			_tvTankMV2.updateComputedSignals();
			double percentage = _tvTankMV2.computedSignals[0].FloatingPointValue / _level_limit_max * 100;
			_tvTankMV2.setText(_tvTankMV2.computedSignals[0].DisplayReading
					+ " [" + _percentage_format.format(percentage)  + "%]" + (_tvTankMV2.signals[1].getValue()==0 ? "" : " Täis"));
		}
		
		// II stage pumps.
		_Update2ndStageDisplay(_tvStage2P1);
		_Update2ndStageDisplay(_tvStage2P2);
		_Update2ndStageDisplay(_tvStage2P3);
		_Update2ndStageDisplay(_tvStage2P4);
		if (!isPia2Ok)
		{
			double sum = 0.0;
			double count = 0;
			for (int i=0; i<_tvStage2Pressure.computedSignals.length; ++i)
			{
				com.errapartengineering.plcengine.ComputedSignal cs = _tvStage2Pressure.computedSignals[i];
				if (cs!=null)
				{
					double x = cs.FloatingPointValue;
					if (x>0.0 && x<100.0)
					{
						sum += x;
						++count;
					}
				}
			}
			if (count == 0)
			{
				_tvStage2Pressure.setText("Viga!");
				// _tvStage2Pressure.setBackgroundColor(COLOUR_RED);
			}
			else
			{
				double pia2_reading = sum / count;
				_tvStage2Pressure.setText(_pressure_format.format(pia2_reading) + " bar");
				// _tvStage2Pressure.setBackgroundColor(COLOUR_GREEN);
			}
		}
		
		// Power analyzer: L1
		_UpdatePowerAnalyzerDisplay(_tvPowerAnalyzerL1);
		_UpdatePowerAnalyzerDisplay(_tvPowerAnalyzerL2);
		_UpdatePowerAnalyzerDisplay(_tvPowerAnalyzerL3);
		
		// Veefiltri asjad.
		if (_tvIncomingWaterUsage.isOk())
		{
			_tvIncomingWaterUsage.updateComputedSignals();
			_tvIncomingWaterUsage.setText(_tvIncomingWaterUsage.computedSignals[0].DisplayReading);
		}
		if (_tvWashWaterUsage.isOk())
		{
			_tvWashWaterUsage.updateComputedSignals();
			_tvWashWaterUsage.setText(_tvWashWaterUsage.computedSignals[0].DisplayReading);
		}
		if (_tvWaterOutput.isOk())
		{
			_tvWaterOutput.updateComputedSignals();
			_tvWaterOutput.setText(_tvWaterOutput.computedSignals[0].DisplayReading);
		}
		if (_tvIsFilterWashing.isOk())
		{
			boolean is_washing = _tvIsFilterWashing.signals[0].getValue()!=0;
			_tvIsFilterWashing.setText(is_washing ? "Peseb" : "Töötab");
		}
	}
	
	volatile int _ticks_to_start = -1;
	// @Override
	// timer ticks.
	public void run()
	{
		PlcMasterService service = _service;
		if (service == null)
		{
			if (_is_first_run)
			{
				_is_first_run = false;
				// 1. Create the PlcContext and connection to the database.
				_PLC_Start();
			}
		}
		else
		{
			byte[] row = service.plcPopRowIfAny();
			if (row!=null)
			{
				// 1. decode the signals
				List<IOSignal>	signals = service.plcContext.Signals;
				int signals_length = signals.size();
				if (_signal_is_connected==null || _signal_is_connected.length!=signals_length)
				{
					_signal_is_connected = new boolean[signals_length];
					_signal_values = new int[signals_length];
				}
				CircularRowFormat.decode(_signal_is_connected, _signal_values, row, signals);				
				
				// 3. Display logic.
				_UpdateDisplay(signals);
			}
			if (_connection!=null && service.plcShouldRestartService())
			{
				int nseconds = 30;
				_PLC_Stop();
				_ticks_to_start = nseconds * (1000 / _POLL_PERIOD_MS);
				TextView tv = (TextView)findViewById(R.id.textviewPlcStatus);
				tv.setText("Restart in " + nseconds + " seconds.");
				tv.setBackgroundColor(0xFF1111FF);
			}
			else
			{
				if (_ticks_to_start>=0)
				{
					--_ticks_to_start;
					if (_ticks_to_start<0)
					{
						_PLC_Start();
					}
				}
			}
		}
		_poll_handler.postDelayed(this, _POLL_PERIOD_MS);
	};
}
