@echo off

set SQLITE=..\SQLite\sqlite3.exe
set USERSQL=..\PlcDb\User.sql
set PLCSQL=..\PlcDb\Plc.sql


call :mkoperator Kasutaja
call :mkplc "Ranna VTJ" eth0:00:0D:E0:B0:27:7B
call :mkplc "Posti VTJ" eth0:00:0D:E0:B0:27:7A
call :mkplc KPJ-2 name:KPJ-2
call :mkplc KPJ-4 name:KPJ-4
call :mkplc KPJ-5 name:KPJ-5
call :mkplc KPJ-6 name:KPJ-6
call :mkplc KPJ-Nooruse name:KPJ-Nooruse
call :mkplc KPJ-Papli name:KPJ-Papli
echo Done
goto :eof

:mkplc
echo Creating %1.plc
del /q %1.plc
%SQLITE% %1.plc < %USERSQL%
%SQLITE% %1.plc < %PLCSQL%
%SQLITE% %1.plc "INSERT INTO Environment(Name,Value) VALUES('PlcId', '%2')"
%SQLITE% %1.plc "INSERT INTO Environment(Name,Value) VALUES('Created', '05/17/2013 18:37:33')"
%SQLITE% %1.plc "INSERT INTO Environment(Name,Value) VALUES('IsPublic', 'true')"
exit /B

:mkoperator
echo Creating %1.op
del /q %1.op
%SQLITE% %1.op < %USERSQL%
%SQLITE% %1.op "INSERT INTO Environment(Name,Value) VALUES('PasswordHash', '')"
%SQLITE% %1.op "INSERT INTO Environment(Name,Value) VALUES('Created', '05/17/2013 18:37:33')"
%SQLITE% %1.op "INSERT INTO Environment(Name,Value) VALUES('IsPublic', 'true')"
exit /B

