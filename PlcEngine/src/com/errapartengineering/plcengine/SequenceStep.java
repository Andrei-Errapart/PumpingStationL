package com.errapartengineering.plcengine;

public class SequenceStep {
	/// Start time, in milliseconds.
	public final long startTimeMs;
	/// Message to be processed by the PLC Engine.
	public final PlcCommunication.MessageToPlc[] commands;

	/// CTR
	public SequenceStep(long startTimeMs, PlcCommunication.MessageToPlc[] commands)
	{
		this.startTimeMs = startTimeMs;
		this.commands = commands;
	}
	
	/// Command to set signal to some value.
	public static PlcCommunication.MessageToPlc CommandSetSignal(String signalName, int signalValue)
	{
		PlcCommunication.MessageToPlc.Builder msg_builder = PlcCommunication.MessageToPlc.newBuilder();
		PlcCommunication.SignalAndValue.Builder msg_sv_builder = PlcCommunication.SignalAndValue.newBuilder();
		msg_sv_builder.setName(signalName);
		msg_sv_builder.setValue(signalValue);
		msg_builder.setId(1);
		msg_builder.addSetSignals(msg_sv_builder.build());
		PlcCommunication.MessageToPlc msg = msg_builder.build();
		return msg;
	}
}
