package com.errapartengineering.windowsplc;

import org.json.*;


public class Configuration {
	/// Device Id.
	public final String DeviceId;
	/// IpAddr:Port of the modbus server.
	public final String ModbusServer;
	/// IpAddr:Port of the control server.
	public final String ControlServer;
    
	/// Load configuration from the given filename.
	public Configuration(String filename) throws java.io.IOException
	{
		String txt = com.errapartengineering.plcengine.FileUtils.ReadFileAsString(new java.io.File(filename));
		JSONObject json = new JSONObject(txt);
		this.DeviceId = json.getString("DeviceId");
		this.ControlServer = json.getString("ControlServer");
		this.ModbusServer = json.getString("ModbusServer");
	}
}
