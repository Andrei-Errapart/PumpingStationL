#! /bin/sh

# Run while the GSM connection is open.

gsmconnect gsm_0

cp /mnt/mmcblk0p1/data/NPE_X1000/resolv.conf /etc
chmod a+r /etc/resolv.conf
echo -n "hwclock before:"
hwclock
ntpdate ntp.eenet.ee ntp.elion.ee ntp.estpak.ee ntp.aso.ee ntp.uninet.ee
hwclock -w -u

while true
do
	ifconfig | grep -q ppp
	if [ $? != 0 ]
	then
		exit 0
	fi

	ping -q -c 20 80.235.54.18
	if [ $? != 0 ]
	then
		exit 0
	fi

	sleep 20
done

sleep 20

# kill the remnants, if any.
killall pppd

