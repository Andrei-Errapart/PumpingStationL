set PLCDIR=W:\PumpingStationL
java -classpath %PLCDIR%\PlcEngine\libs\protobuf-java-2.5.0rc1.jar;%PLCDIR%\PlcEngine\bin;%PLCDIR%\WindowsAndroid\bin;%PLCDIR%\JSON\bin;%PLCDIR%\WindowsPlc\bin com.errapartengineering.windowsplc.WindowsPlc --dump --config WindowsPlc-emu.ini %*

