package com.errapartengineering.plcmaster;

import android.preference.PreferenceActivity;

public class PlcPreferencesActivity extends PreferenceActivity {
    public static final String KEY_PREF_MODBUS_PROXY = "pref_modbus_server";
    public static final String KEY_PREF_CONTROL_SERVER = "pref_control_server";
    
    @Override
    public void onCreate(android.os.Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        addPreferencesFromResource(R.xml.preferences);
    }
    
    
}
