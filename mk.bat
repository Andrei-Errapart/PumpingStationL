set TARGETDIR=LoksaControlPanel

set MSBUILD=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\msbuild.exe 
set ZIPIT=w:\DotnetZip\Tools\Zipit.exe

%MSBUILD% PumpingStationL.sln /p:Configuration=Release 

"c:\Program Files (x86)\Eziriz\.NET Reactor\dotNET_Reactor.Console.exe" -project LoksaControlPanel.nrproj

rem CONTROL PANEL
copy /Y ControlPanel\bin\Release\System.Data.SQLite.dll %TARGETDIR%
copy /Y ControlPanel\bin\Release\WPFToolkit.Extended.dll %TARGETDIR%
copy /Y ControlPanel\bin\Release\Xceed.Wpf.DataGrid.dll %TARGETDIR%
copy /Y ControlPanel\bin\Release\YY.dll %TARGETDIR%
copy /Y ControlPanel\bin\Release\Google.ProtocolBuffersLite.dll %TARGETDIR%
copy /Y PlcManager\bin\FSharp.Core.dll %TARGETDIR%
copy /Y PlcManager\bin\FSharpx.TypeProviders.Xaml.dll %TARGETDIR%
copy /Y ControlPanel\bin\Release\ControlPanel_Secure\ControlPanel.exe %TARGETDIR%
copy /Y ControlPanel\lib\*.ini %TARGETDIR%
copy /Y ControlPanel\lib\ControlPanel-Scheme.txt %TARGETDIR%

rem PLC MANAGER
rem copy /Y PlcManager\bin\Release\*.dll %TARGETDIR%
rem copy /Y PlcManager\bin\Release\PlcManager.exe %TARGETDIR%
rem copy /Y PlcManager\lib\*.ini %TARGETDIR%

del /q %TARGETDIR%.zip
%ZIPIT% %TARGETDIR%.zip -r+ %TARGETDIR%

