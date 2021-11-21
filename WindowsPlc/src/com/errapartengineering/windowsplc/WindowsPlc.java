package com.errapartengineering.windowsplc;

import com.errapartengineering.plcengine.*;
import java.io.File;

// TODO:
// 1. main loop.
public class WindowsPlc {

	private static void _sleep(long ms)
	{
		try {
			java.lang.Thread.sleep(ms);
		} catch (Exception ex) {
			System.out.println("Error in sleep:" + ex.getMessage());
		}
	}

	private static void _print_signals(java.util.List<IOSignal> signals)
	{
		StringBuilder sb = new StringBuilder();
		for (IOSignal ios : signals) {
			sb.append((ios.IsVariable ? " v" : " ") + ios.getValue());
		}
		System.out.println("S: " + sb.toString());
	}
	/**
	 * @param args
	 */
	public static void main(String[] args)
	{
		System.out.println("WindowsPlc: Startup.");

		int time_limit_delta_s = -1;
		String cfg_filename = "WindowsPlc.ini";
		boolean dump_signals = false;
		try
		{
			for (int i=0; i<args.length; ++i) {
				if (args[i].equals("--time") && i+1<args.length) {
					++i;
					time_limit_delta_s = Integer.parseInt(args[i]);
				} else if (args[i].equals("--config") && i+1<args.length) {
					++i;
					cfg_filename = args[i];
				} else if (args[i].equals("--dump")) {
					dump_signals = true;
				}
			}
			
			// 1. Setup the configuration.
			System.out.println("Config file:    " + cfg_filename);
			Configuration	cfg = new Configuration(cfg_filename);
			System.out.println("Device Id:      " + cfg.DeviceId);
			System.out.println("Control server: " + cfg.ControlServer);
			System.out.println("Modbus proxy:   " + cfg.ModbusServer);
			if (time_limit_delta_s>0) {
				System.out.println("Time limit:     " + time_limit_delta_s + " sec.");
			} else {
				System.out.println("Time limit:     unlimited.");
			}
			System.out.println("Dump signals:   " + (dump_signals ? "Yes" : "No"));
			
			File			cfg_file = new File("setup.xml");
			File			prg_file = new File("setup.prg");
			File			cfg_version_file = new File("setup.txt");
			Context	ctx = new Context(cfg.DeviceId, cfg_file, prg_file, cfg_version_file, cfg.ControlServer, cfg.ModbusServer);
			System.out.println("WindowsPlc: configuration loaded.");
			IEngine			engine = new PlcEngine();

			// 2. Start the engine.
			System.out.println("WindowsPlc: Starting the engine");
			engine.start(ctx);
			System.out.println("WindowsPlc: Engine started, isAlive=" + (engine.isAlive() ? "true" : "false") + ".");
			
			// 3. Run it for some time.
			if (time_limit_delta_s > 0) {
				long time_limit_ms = time_limit_delta_s > 0 ? (System.currentTimeMillis() + time_limit_delta_s*1000) : -1;
				while (System.currentTimeMillis() < time_limit_ms)
				{
					_sleep(1000);
					if (dump_signals) {
						_print_signals(ctx.Signals);
					}
				}
			} else {
				for (;;) {
					_sleep(1000);
					if (dump_signals) {
						_print_signals(ctx.Signals);
					}
				}
			}
			
			
			// 4. Stop the engine :)
			System.out.println("WindowsPlc: Stopping the engine.");
			engine.stop();
			
			System.out.println("WindowsPlc: Finished.");
		}
		catch (Exception ex)
		{
			System.out.println("Exception: " + ex);
			System.out.println("Message: " + ex.getMessage());
			System.out.println("Stack trace: ");
			ex.printStackTrace();
		}
	}
}
