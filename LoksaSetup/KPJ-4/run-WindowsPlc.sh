#! /bin/sh

MYDIR=/mnt/mmcblk0p1/data
PLCDIR=$MYDIR/NPE_X1000

cd $MYDIR/KPJ-4

# KPJ-4 has external modem, thus there is no mygsm service.
echo -n "hwclock before:"
hwclock
ntpdate ntp.eenet.ee ntp.elion.ee ntp.estpak.ee ntp.aso.ee ntp.uninet.ee
hwclock -w -u

java -classpath $PLCDIR/protobuf-java-2.5.0rc1.jar:$PLCDIR/WindowsPlc.jar com.errapartengineering.windowsplc.WindowsPlc

