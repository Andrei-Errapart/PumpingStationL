package com.errapartengineering.plcengine;

import android.util.Log;
import java.util.*;

public class ComputedSignal extends IOSignal {
	public static final String ANALOG_SENSOR = "AnalogSensor";
	public static final String FLOATING_POINT_VALUE = "FloatingPointValue";
	public static final String PUMP_HOURS_OF_OPERATION = "PumpHoursOfOperation";
	public static final String JAVASCRIPT = "JavaScript";

	public final String ComputationType;
	public final String Unit;
	public final String FormatString;
	public final java.text.DecimalFormat Format;
	public final String[] SourceSignalNames;
	public final IOSignal[] SourceSignals;
	public String DisplayReading = "";
    public double FloatingPointValue;

	private final static int MAX_ADC_READING = 32768;

	public ComputedSignal(
				String Name,
            	String ComputationType,
            	String[] SourceSignalNames,
            	String Parameters,
            	String FormatString,
            	String Unit,
            	String Description,
            	boolean SkipWriteCheck,
            	Map<String, IOSignal> SignalDict)
	{
		super(Name, -1, IOType.INPUT_REGISTER, -1, Description, SkipWriteCheck, false);
		this.ComputationType = ComputationType;
		this.Unit = Unit;
		this.FormatString = FormatString;
		this.Format = new java.text.DecimalFormat(FormatString);
		this.SourceSignalNames = SourceSignalNames;
        if (ComputationType.equalsIgnoreCase(ANALOG_SENSOR))
        {
        	String[] sv = Parameters.split(";");
        	double[] v = new double[sv.length];
        	for (int i=0; i<sv.length; ++i)
        	{
        		v[i] = Double.parseDouble(sv[i].replace(',', '.'));
        	}
            _CurrentZero = v[0];
            _CurrentMax = v[1];
            _ReadingZero = v[2];
            _ReadingMax = v[3];
        }
        else if (ComputationType.equalsIgnoreCase(FLOATING_POINT_VALUE))
        {
        	// pass
        } else if (ComputationType.equalsIgnoreCase(PUMP_HOURS_OF_OPERATION))
        {
        	_PumpMinutesFormat = new java.text.DecimalFormat("##");
        } else if (ComputationType.equalsIgnoreCase(JAVASCRIPT))
        {
        	// yeah!
        }
        else
        {
        	Log.d("IOComputedSignal", "Unknown computation type: " + ComputationType);
        }

        int n = this.SourceSignalNames.length;
    	IOSignal[] source_signals = new IOSignal[n];
    	for (int i=0; i<n; ++i)
    	{
    		source_signals[i] = SignalDict.get(this.SourceSignalNames[i]);
    		if (source_signals[i] == null)
    		{
    			Log.e("ComputedSignal", "Cannot find source signal '" + this.SourceSignalNames[i] + "'!");
    		}
    	}
    	this.SourceSignals = source_signals;
    	this._Values = new int[n];
	}

    public void UpdateValueOnly()
    {
        for (int i = 0; i < SourceSignals.length; ++i)
        {
            _Values[i] = SourceSignals[i].getValue();
        }
        if (ComputationType.equalsIgnoreCase(ANALOG_SENSOR))
        {
    		int sensor_reading = _Values[0];
    		// Sign-extend 16-bit modbus registers.
    		if (SourceSignals[0].BitLength==16 && sensor_reading >= 0x8000)
    		{
    			sensor_reading = sensor_reading | 0xFFFF0000;
    		}
    		double current = sensor_reading * _CurrentMax / MAX_ADC_READING;
    		double reading = (current - _CurrentZero) / (_CurrentMax - _CurrentZero) * (_ReadingMax - _ReadingZero) + _ReadingZero;
            this.FloatingPointValue = reading;
        } else if (ComputationType.equalsIgnoreCase(FLOATING_POINT_VALUE))
        {
            int n = _Values.length / 2;
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                int ofs = 2 * i;
        		int i0 = _Values[ofs + 0];
        		int i1 = _Values[ofs + 1];
        		int bits = (i0 << 16) | i1;
        		double reading = java.lang.Float.intBitsToFloat(bits);
                sum += reading;
            }
            this.FloatingPointValue = sum;
        } else if (ComputationType.equalsIgnoreCase(PUMP_HOURS_OF_OPERATION))
        {
            _PumpHours = (_Values[0] << 16) + _Values[1];
            _PumpMinutes = _Values[2];
            this.FloatingPointValue = _PumpHours + (1.0 / 60.0 * _PumpMinutes);
        } else if (ComputationType.equalsIgnoreCase(JAVASCRIPT))
        {
        	// FIXME: what to do?
        }
        // super.IsConnected = true;
        // super.DisplayIsConnected = true;
    }

	// Reverse of ComputationType.ANALOG_SENSOR.
    public int ReverseCalculation(double analogValue)
    {
    	/*
            _CurrentZero = v[0];
            _CurrentMax = v[1];
            _ReadingZero = v[2];
            _ReadingMax = v[3];
    	 */
        if (ComputationType.equalsIgnoreCase(ANALOG_SENSOR))
        {
            double  r_current = (analogValue - _ReadingZero) / (_ReadingMax - _ReadingZero) * (_CurrentMax - _CurrentZero) + _CurrentZero;
            int r_int = (int)(Math.round(r_current / _CurrentMax * MAX_ADC_READING));
            return r_int;
        }
        else
        {
        	return 0;
        }
    }
    
    public void Update()
    {
        UpdateValueOnly();
        // TODO: convert .NET style string formats to java.text.DecimalFormat styles.
        if (ComputationType.equalsIgnoreCase(ANALOG_SENSOR))
		{
                this.DisplayReading = this.Format.format(FloatingPointValue) + (Unit.length() == 0 ? "" : " ") + Unit;
		}
        else if (ComputationType.equalsIgnoreCase(FLOATING_POINT_VALUE))
        {
                this.DisplayReading = this.Format.format(FloatingPointValue) + (Unit.length() == 0 ? "" : " ") + Unit;
        } else if (ComputationType.equalsIgnoreCase(PUMP_HOURS_OF_OPERATION))
        {
                this.DisplayReading = Integer.toString(_PumpHours) + "h " + _PumpMinutesFormat.format(_PumpMinutes) + "m";
        } else if (ComputationType.equalsIgnoreCase(JAVASCRIPT))
        {
        	// FIXME: do what?
        }
        // base._IsConnected = true;
        // base.DisplayIsConnected = true;
    }

    private int[] _Values;
    private double _CurrentZero = 0.0;
    private double _CurrentMax = 20.0;
    private double _ReadingZero = 0.0;
    private double _ReadingMax = 100.0;
    private int _PumpHours = 0;
    private int _PumpMinutes = 0;
    private java.text.DecimalFormat _PumpMinutesFormat;
} // class IOComputedSignal
