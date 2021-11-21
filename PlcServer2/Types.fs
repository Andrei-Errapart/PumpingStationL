namespace PlcServer2

module Types =
    // ======================================================================
    /// 'scope resolution' separator
    let SRS = PlcCommunication.IOSignal.SRS // 'scope resolution' separator mark (SRS), to separate device name and signal name

    // ======================================================================
    /// Notification is sent when signal value is less than LowerBound or greater than UpperBound
    /// i.e. signal value is out of normal range (defined by lower and upper bounds),
    /// or if MaxConnectionFails > 0 then also if signal connection is lost for specified n times
    /// MaxFails tells how many times a signal value may be out of bounds before notified.
    type NotificationCondition =
        {
            SignalName : string
            LowerBound : float
            UpperBound : float
            MaxFails : int
            MaxConnectionFails : int
        }

    type ServerConfiguration() =
        // General
        member val DataDirectory = "C:\\PlcServerService" with get, set

        // Notifications
        member val SmsEnable = true with get, set
        member val SmsLogFilename : string = [| System.IO.Path.GetTempPath(); "C:\\PlcServerService\\Sms-log.txt" |] 
                                                |> System.IO.Path.Combine with get, set
        member val SmsReceiverPhoneNrs : string array = Array.empty with get, set
        member val SmsModemPort : string = "COM3" with get, set
        member val SmsSize : int = 160 with get, set
        /// To separate notification messages when sending multiple in one SMS. {0} message number in queue.
        /// Allowed chars: A-z 0-9 !@#$%&*()_+-="/'.,<>:; and space
        member val SmsMessageSeparator : string = "(Teade {0})" with get, set
            
        /// {0} PLC name, {1} signal description, {2} signal value
        member val SmsSignalAlarmFormat : string = "Alarm! {0} {1} ({2})" with get, set
        /// {0} PLC name, {1} signal description, {2} signal value
        member val SmsSignalOKFormat : string = "OK! {0} {1} korras ({2})" with get, set
        /// {0} PLC name, {1} signal description
        member val SmsSignalDisconnectFormat : string = "Alarm! {0}: signaali {1} ühendus kadund." with get, set
        /// {0} PLC name, {1} signal description
        member val SmsSignalConnectedFormat : string = "OK! {0}: signaali {1} ühendus taastund." with get, set
        /// {0} PLC name
        member val SmsPlcOnlineFormat : string = "{0} ühendatud." with get, set
        /// {0} PLC name
        member val SmsPlcOfflineFormat : string = "Alarm! {0} ühendus kadund." with get, set

        member val SmsSignalValuePrecision : int = 2 with get, set

        member val SmsAlarmNotificationConditions : NotificationCondition array = Array.empty with get, set

    // ======================================================================
    type AlarmCounter =
        {
            SignalName : string
            IsComputed : bool
            mutable Count : int
            Limit : int
            mutable Notified : bool
        }
