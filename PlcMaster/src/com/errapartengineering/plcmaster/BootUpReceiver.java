package com.errapartengineering.plcmaster;

import android.content.*;

/** Start the application at boot-up. Evil, but necessary.
 */
public class BootUpReceiver extends BroadcastReceiver{
    @Override
    public void onReceive(Context context, Intent intent) {
            Intent i = new Intent(context, PlcMasterActivity.class);  
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(i);  
    }
}