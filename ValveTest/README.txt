Valve testing.


ALGORITHM
---------
	Loop until button pressed through all 8 valves and 2 positions:
		1. Log test start time, valve and position.
		2. Set given valve position
		3. Monitor valve feedback inputs until there is no change for the QUIET_TIME_MS 
		4. Log test result:
			IsOk = time<DEVIATION_FEEDBACK_TIME_MS
			delta_time = start time until the last change in feedback input.
			total_time = from start time til now.

PARAMETERS
----------
	QUIET_TIME_MS = 60_000 ms // Time period while the feedback inputs should stay stable.
	DEVIATION_FEEDBACK_TIME_MS = 30_000 ms // Settling times exceeding this period should be reported.
	MONITOR_TIME_MS = 300_000 ms // Time for monitoring the valve...

OUTPUT
------
	/sdcard/logfile.txt:
	[datetime] [#loop] START: valve=, position=
	[datetime] [#loop] TIME EXCEEDED: over DEVIATION_FEEDBACK_TIME_MS
	[datetime] [#loop] FINISH: Success/Failure, delta_time=%d ms, total_time=%d ms.

GUI
---
	TextView "Status: " Running/Stopped
	TextView "Loop #"
	TextView "Valve #"
	TextView "Position: " Open/Close
	TextView "Test Start: " Date/Time
	TextView "Success count: " #
	TextView "Failure count: " #

	Button "STOP"

