package com.errapartengineering.plcmaster;

import java.util.List;

import com.errapartengineering.plcengine.*;;

public class FqiSignals {
	public final String name;
	public final IOSignal fqi32;
	public final IOSignal fqi16;
	public final IOSignal offset;
	public final ComputedSignal signalToReading;
	
	public FqiSignals(String name, List<IOSignal> signals, ComputedSignal signalToReading) throws Exception
	{
		this.name = name;
		this.fqi32 = IOSignal.findSignalByName(signals, name + ".EXTENDED");
		this.fqi16 = IOSignal.findSignalByName(signals, name);
		this.offset = IOSignal.findSignalByName(signals, name + ".OFFSET");
		this.signalToReading = signalToReading;
	}
}
