package com.errapartengineering.plcmaster;

import android.app.Activity;
import android.widget.*;
import android.content.Intent;
import android.os.Bundle;
import android.util.Log;
import android.view.Menu;
import android.view.View;

public class FqiSetupActivity extends Activity {
	public static final int RESULTCODE_OK = 0;
	public static final int RESULTCODE_FAILURE = 1;
	
	/// GUI widgets.
	private class FqiWidgets {
		public final String name;
		public final CheckBox cbFqi;
		public final EditText etFqi;
		public boolean hasValue;
		public double value;
		
		public FqiWidgets(String name, int cbId, int etId)
		{
			this.name = name;
			cbFqi = (CheckBox)findViewById(cbId);
			etFqi = (EditText)findViewById(etId);
			cbFqi.setChecked(false);
			etFqi.setEnabled(false);
			hasValue = false;
			value = 0;
		}
		
		public void UpdateFqi() throws NumberFormatException
		{
			hasValue = false;
			value = 0;
			if (this.cbFqi.isChecked())
			{
				String s1 = etFqi.getText().toString();
				String s2 = s1.replace(',', '.');
				value = Double.parseDouble(s2);
				hasValue = true;
			}
		}
	};
	
	private FqiWidgets[] _fqi = new FqiWidgets[3];
	
	@Override
	public void onCreate(Bundle savedInstanceState)
	{
		super.onCreate(savedInstanceState);
		setContentView(R.layout.activity_fqisetup);
	}

	@Override
	public void onDestroy()
	{
		super.onDestroy();
	}
	
	@Override
	public void onResume()
	{
		super.onResume();
		_fqi[0] = new FqiWidgets("FQI1", R.id.checkboxFqi1, R.id.editTextFqi1);
		_fqi[1] = new FqiWidgets("FQI2", R.id.checkboxFqi2, R.id.editTextFqi2);
		_fqi[2] = new FqiWidgets("FQI3", R.id.checkboxFqi3, R.id.editTextFqi3);
	}
	
	@Override
	public void onPause()
	{
		super.onPause();
	}

	private void _onCheckbox(int index)
	{
		_fqi[index].etFqi.setEnabled(_fqi[index].cbFqi.isChecked());
	}
	
	public void onCheckBox_Fqi1(View view)
	{
		_onCheckbox(0);
	}

	public void onCheckBox_Fqi2(View view)
	{
		_onCheckbox(1);
	}

	public void onCheckBox_Fqi3(View view)
	{
		_onCheckbox(2);
	}

	public void onButton_Accept(View view)
	{
		try 
		{
			for (int i=0; i<_fqi.length; ++i)
			{
				_fqi[i].UpdateFqi();
			}
		}
		catch (Exception ex)
		{
			Utils.messageBox(this, "Viga näidu sisestamisel!");
			return;
		}
		
		boolean any_value = false;
		for (int i=0; i<_fqi.length; ++i)
		{
			any_value = any_value || _fqi[i].hasValue;
		}
		if (any_value)
		{
			Intent r = new Intent();
			for (int i=0; i<_fqi.length; ++i)
			{
				FqiWidgets fi = _fqi[i];
				if (fi.hasValue)
				{
					r.putExtra(fi.name, fi.value);
				}
			}
			setResult(RESULTCODE_OK, r);
			finish();
		}
		else
		{
			Utils.messageBox(this, "Valige mõni veemõõdik.");
		}
	}
	
	public void onButton_Cancel(View view)
	{
		setResult(RESULTCODE_FAILURE);
		finish();
	}
}
