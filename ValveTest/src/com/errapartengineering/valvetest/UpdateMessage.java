package com.errapartengineering.valvetest;

import java.util.Date;

public class UpdateMessage {
	public boolean isRunning;
	public int loopNumber;
	public int valvePosition; // 0=open, 1=close
	public Date startTime;
	public int successCount;
	public int failureCount;
	public boolean[] valvesOK;
}
