package com.errapartengineering.valvetest;

import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.app.Activity;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.ServiceConnection;
import android.util.Log;
import android.view.Menu;
import android.view.View;
import android.widget.TextView;

import java.io.File;
import java.util.Date;

// TODO:
// Load PlcContext
public class MainActivity extends Activity implements Runnable {
	private ValveTestService _service = null;
	private Intent _serviceIntent = null;
	private Handler _poll_handler = new Handler();
	private int _POLL_PERIOD_MS = 500;
	
	private TextView _tvTestStatus = null;
	private TextView _tvLoopNumber = null;
	private TextView[] _tvValves = new TextView[8];
	private TextView _tvValveTargetPosition = null;
	private TextView _tvStartTime = null;
	private TextView _tvSuccessCount = null;
	private TextView _tvFailureCount = null;

    /** Defines callbacks for service binding, passed to bindService() */
	public ServiceConnection _connection = null;
    private ServiceConnection  _newConnection = new ServiceConnection() {
        public void onServiceConnected(ComponentName className,
                IBinder service)
        {
        	Log.d("ServiceConnection", "onServiceConnected.");
        	ValveTestService.LocalBinder binder = (ValveTestService.LocalBinder) service;
            _service = binder.getService();
        	Log.d("ServiceConnection", "onServiceConnected: Found service.");
            _connection = this;
    		Log.d("ServiceConnection", "Starting the service...");
            startService(_serviceIntent);
            
            // Report the starting time..
            TextView tv = (TextView)MainActivity.this.findViewById(R.id.textviewTotalStartTime);
            tv.setText((new Date()).toString());
        }
        
        // called when the service crashes...
        public void onServiceDisconnected(ComponentName arg0)
        {
        	Log.e("ServiceConnection", "onServiceDisconnected. Crash?");
        	_service = null;
        	_connection = null;
        }
    };

	@Override
	protected void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);
		setContentView(R.layout.activity_main);
		
		// Member initialization.
		_serviceIntent = new Intent(this, ValveTestService.class);
		_poll_handler.postDelayed(this, _POLL_PERIOD_MS);
		
		_tvTestStatus = (TextView)findViewById(R.id.textviewTestStatus);
		_tvLoopNumber = (TextView)findViewById(R.id.textviewLoopNumber);
		_tvValves[0] = (TextView)findViewById(R.id.textviewM1);
		_tvValves[1] = (TextView)findViewById(R.id.textviewM2);
		_tvValves[2] = (TextView)findViewById(R.id.textviewM3);
		_tvValves[3] = (TextView)findViewById(R.id.textviewM4);
		_tvValves[4] = (TextView)findViewById(R.id.textviewM5);
		_tvValves[5] = (TextView)findViewById(R.id.textviewM6);
		_tvValves[6] = (TextView)findViewById(R.id.textviewM7);
		_tvValves[7] = (TextView)findViewById(R.id.textviewM8);
		_tvValveTargetPosition = (TextView)findViewById(R.id.textviewValveTargetPosition);
		_tvStartTime = (TextView)findViewById(R.id.textviewStartTime);
		_tvSuccessCount = (TextView)findViewById(R.id.textviewSuccessCount);
		_tvFailureCount = (TextView)findViewById(R.id.textviewFailureCount);
	}

	@Override
	public boolean onCreateOptionsMenu(Menu menu) {
		// Inflate the menu; this adds items to the action bar if it is present.
		getMenuInflater().inflate(R.menu.main, menu);
		return true;
	}
	
	@Override
	public void onResume()
	{
		super.onResume();
		
		Log.d("MainActivity", "onResume, starting the activity for now....");
		bindService(_serviceIntent,  _newConnection, Context.BIND_AUTO_CREATE);		
	}
	
	public void onButtonStop(View view)
	{
		if (_service!=null)
		{
			_service.stop();
		}
	}
	// @Override
	// timer ticks.
	public void run()
	{
		ValveTestService svc = _service;
		if (svc!=null)
		{
			UpdateMessage msg = svc.popUpdateMessage();
			if (msg!=null)
			{
				_tvTestStatus.setText(msg.isRunning ? "Running" : "Stopped");
				_tvLoopNumber.setText(Integer.toString(msg.loopNumber));
				for (int i=0; i<msg.valvesOK.length; ++i)
				{
					boolean ok = msg.valvesOK[i];
					_tvValves[i].setBackgroundColor(ok ? 0xFF11FF11 : 0xFFFF1111);
				}
				_tvValveTargetPosition.setText(msg.valvePosition == ValveTestService.VALVEPOSITION_INACTIVE ? "Passive" : "Active");
				_tvStartTime.setText(msg.startTime.toString());
				_tvSuccessCount.setText(Integer.toString(msg.successCount));
				_tvFailureCount.setText(Integer.toString(msg.failureCount));
			}
		}
		_poll_handler.postDelayed(this, _POLL_PERIOD_MS);
	}
}
