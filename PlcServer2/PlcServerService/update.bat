set SRCDIR= w:\PumpingStationL\PlcServer2\bin\Release
copy /Y %SRCDIR%\*.dll .
copy /Y %SRCDIR%\*.exe .
del /q PlcServer.exe
ren PlcServer2.exe PlcServer.exe
