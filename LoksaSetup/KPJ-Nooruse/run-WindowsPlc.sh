#! /bin/sh

MYDIR=/mnt/mmcblk0p1/data
PLCDIR=$MYDIR/NPE_X1000

cd $MYDIR/KPJ-Nooruse

java -classpath $PLCDIR/protobuf-java-2.5.0rc1.jar:$PLCDIR/WindowsPlc.jar com.errapartengineering.windowsplc.WindowsPlc

