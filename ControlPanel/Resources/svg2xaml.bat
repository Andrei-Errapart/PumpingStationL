@echo off
rem for some reason, the textGeometry flag is not recognized.
rem Use the GUI version of the program to do the conversion.
..\lib\SharpVectors.exe --ui=console --textGeometry- -g- Scheme.svg
