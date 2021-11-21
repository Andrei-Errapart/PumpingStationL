#! /bin/sh

SYSTEMD_DIR=/lib/systemd/system

echo Installing mygsm.service ...
chmod a+rx 10defaultroute
chmod a+rx run-gsmconnect.sh
cp -av 10defaultroute /etc/ppp/ip-up.d
chmod a+r mygsm.service
cp -av mygsm.service $SYSTEMD_DIR
systemctl enable mygsm.service

echo Installing myproxy.service ...
chmod a+rx mbproxy
chmod a+rx run-mbproxy.sh
chmod a+r myproxy.service
cp -av myproxy.service $SYSTEMD_DIR
systemctl enable myproxy.service

echo Installing timezone Europe/Tallinn
cp -av Tallinn /usr/share/zoneinfo/Europe
cp -av Tallinn /etc/localtime
echo Europe/Tallinn > /etc/timezone

echo Installing resolv.conf
cp -av resolv.conf /etc

echo Finished!

