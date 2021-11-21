package com.errapartengineering.plcmaster;

import com.errapartengineering.plcengine.IOSignal;
import com.errapartengineering.plcengine.ComputedSignal;
import android.widget.TextView;
import android.app.Activity;

/// Cache important pointers for the GUI update.
public class TextviewCache {
	public final int expectedSignalsLength;
	public IOSignal[] signals;
	public ComputedSignal[] computedSignals;
	public TextView textview;
	
	public TextviewCache(int expectedSignalsLength)
	{
		this.expectedSignalsLength = expectedSignalsLength;
	}
	
	public void setSignals(IOSignal[] signals)
	{
		this.signals = signals;
		this.computedSignals = new ComputedSignal[signals.length];
		for (int i=0; i<signals.length; ++i)
		{
			IOSignal s = signals[i];
			if (s instanceof ComputedSignal)
			{
				this.computedSignals[i] = (ComputedSignal)s;
			}
		}
	}
	
	public void updateTextview(Activity a, int id)
	{
		this.textview = (TextView)a.findViewById(id);
	}
	
	public void updateComputedSignals()
	{
		if (computedSignals!=null)
		{
			// 1. Update all the computed signals.
			for (int i=0; i<computedSignals.length; ++i)
			{
				ComputedSignal cs = computedSignals[i];
				if (cs!=null)
				{
					cs.Update();
				}
			}
		}
	}
	
	public final boolean isOk()
	{
		boolean ok = textview!=null && ((signals!=null && signals.length==expectedSignalsLength) || expectedSignalsLength==0);
		for (int i=0; ok && i<expectedSignalsLength; ++i)
		{
			ok = ok && signals[i]!=null;
		}
		return ok;
	}

	// Set the text.
	public final void setText(String newText)
	{
		if (!newText.equals(_oldText) && textview!=null)
		{
			textview.setText(newText);
			_oldText = newText;
		}
	}

	// Set the background colour.
	public final void setBackgroundColor(int colour)
	{
		if (_oldColour!=colour && textview!=null)
		{
			textview.setBackgroundColor(colour);
			_oldColour = colour;
		}
	}
	
	private String _oldText = "";
	private int _oldColour = -1;
}
