#! /bin/sh

SYSTEMD_DIR=/lib/systemd/system

echo Installing myplc.service ...
chmod a+rx run-WindowsPlc.sh
chmod a+rx myplc.service
cp myplc.service $SYSTEMD_DIR
systemctl enable myplc.service

echo Finished!

