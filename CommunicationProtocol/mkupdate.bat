REM Update the protocol definitions.

set COMMDIR=..\CommunicationProtocol

cd ..\PlcCommunication
%COMMDIR%\protogen.exe -namespace=PlcCommunication --proto_path=%COMMDIR%  %COMMDIR%\PlcCommunication.proto

rem cd ..\PlcMaster
rem %COMMDIR%\protoc.exe --java_out=src --proto_path=%COMMDIR% %COMMDIR%\PlcCommunication.proto

cd ..\PlcEngine
%COMMDIR%\protoc.exe --java_out=src --proto_path=%COMMDIR% %COMMDIR%\PlcCommunication.proto

