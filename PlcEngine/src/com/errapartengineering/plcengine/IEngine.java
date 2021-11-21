package com.errapartengineering.plcengine;

/// Interface to the PlcEngine.
public interface IEngine
{
	/// Start the engine.
	public void start(Context plc);
	/// Stop the engine.
	public void stop();
	/// Is the engine alive?
	public boolean isAlive();
	/// Is the connection to the central server active?
	public boolean isConnected();
	/// Shall the engine be restarted?
	public boolean shouldRestart();
	/// Pop row of results, should there be any.
	public byte[] popRowIfAny();
	/// Handle message in the main loop.
	public void handleMessage(PlcCommunication.MessageToPlc msg);
} // interface IPlcEngine
