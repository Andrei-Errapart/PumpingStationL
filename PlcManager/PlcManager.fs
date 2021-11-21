open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Data.Linq.SqlClient
open System.Linq
open System.Windows
open System.Windows.Controls
open System.Net.Sockets

open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq
open FSharpx

type XamlMain = XAML<"WindowMain.xaml">
type XamlConfiguration = XAML<"WindowConfiguration.xaml">

//===========================================================================
let ConfigFilename = "PlcManager.ini"
let ConfigSection = "PlcManager"

//===========================================================================
let TitleOk = "Plc Manager"
let TitleError = TitleOk + " - Viga"
let TitleConfirm = TitleOk + " - Kinnitage"

//===========================================================================
type Configuration() =
    member val ServerConnectionString = "127.0.0.1:1503" with get, set
    member val ServerUsername = "" with get, set
    member val ServerPassword = "" with get, set

//===========================================================================
type WindowConfiguration (w:XamlConfiguration, cfg:Configuration, msg:string) as self =
    do
        w.textboxServer.Text <- cfg.ServerConnectionString
        w.textboxUsername.Text <- cfg.ServerUsername
        w.passwordBox.Password <- cfg.ServerPassword
        if msg.Length>0 then
            w.textblockMessage.Text <- msg
        w.buttonOK.Click.Add(self.OK)
        w.buttonCancel.Click.Add(self.Cancel)

    member this.Window = w.Root

    member this.OK (args:RoutedEventArgs) =
        this.Window.DialogResult <- new Nullable<bool>(true)
        cfg.ServerConnectionString <- w.textboxServer.Text
        cfg.ServerUsername <- w.textboxUsername.Text
        cfg.ServerPassword <- w.passwordBox.Password
        this.Window.Close()

    member this.Cancel (args:RoutedEventArgs)=
        this.Window.DialogResult <- new Nullable<bool>(false)
        this.Window.Close()

    static member ShowDialog(cfg:Configuration, msg:string) =
        let w = new WindowConfiguration(XamlConfiguration(), cfg, msg)
        let r = w.Window.ShowDialog()
        r.HasValue && r.Value

//===========================================================================
type RowPlcConfiguration() =
    inherit DependencyObject()
    static let CreateDateProperty = DependencyProperty.Register("CreateDate", typeof<DateTime>, typeof<RowPlcConfiguration>, new PropertyMetadata(DateTime.MinValue))
    static let VersionProperty = DependencyProperty.Register("Version", typeof<int>, typeof<RowPlcConfiguration>, new PropertyMetadata(-1))
    static let ConfigurationFileProperty = DependencyProperty.Register("ConfigurationFile", typeof<string>, typeof<RowPlcConfiguration>, new PropertyMetadata(""))
    static let PreferencesProperty = DependencyProperty.Register("Preferences", typeof<string>, typeof<RowPlcConfiguration>, new PropertyMetadata(""))
    // Android preferences.
    member val Id = -1 with get, set
    member val UserId = -1 with get, set
    member this.CreateDate with get() = this.GetValue(CreateDateProperty) :?> DateTime and set(v) = this.SetValue(CreateDateProperty, v)
    member this.Version with get() = this.GetValue(VersionProperty) :?> int and set(v) = this.SetValue(VersionProperty, v)
    member this.ConfigurationFile with get() = this.GetValue(ConfigurationFileProperty) :?> string and set(v) = this.SetValue(ConfigurationFileProperty, v)
    member this.Preferences with get() = this.GetValue(PreferencesProperty) :?> string and set(v) = this.SetValue(PreferencesProperty, v)
//===========================================================================
type RowUser() =
    inherit DependencyObject()
    static let CreateDateProperty = DependencyProperty.Register("CreateDate", typeof<DateTime>, typeof<RowUser>, new PropertyMetadata(DateTime.MinValue))
    static let NameProperty = DependencyProperty.Register("Name", typeof<string>, typeof<RowUser>, new PropertyMetadata(""))
    static let TypeProperty = DependencyProperty.Register("Type", typeof<string>, typeof<RowUser>, new PropertyMetadata(""))
    static let IsPublicProperty = DependencyProperty.Register("IsPublic", typeof<bool>, typeof<RowUser>, new PropertyMetadata(false))
    static let PlcConfigurationProperty = DependencyProperty.Register("PlcConfiguration", typeof<RowPlcConfiguration>, typeof<RowUser>, new PropertyMetadata(null))
    member val Id = -1 with get, set
    member this.CreateDate with get() = this.GetValue(CreateDateProperty) :?> DateTime and set(v) = this.SetValue(CreateDateProperty, v)
    member this.Name with get() = this.GetValue(NameProperty) :?> string and set(v) = this.SetValue(NameProperty, v)
    member this.Type with get() = this.GetValue(TypeProperty) :?> string and set(v) = this.SetValue(TypeProperty, v)
    member this.IsPublic with get() = this.GetValue(IsPublicProperty) :?> bool and set(v) = this.SetValue(IsPublicProperty, v)
    member this.PlcConfiguration with get() = this.GetValue(PlcConfigurationProperty) :?> RowPlcConfiguration and set(v) = this.SetValue(PlcConfigurationProperty, v)

    member val IsPlc = false with get, set
    member val IsDirty = false with get, set
//===========================================================================
type WindowMain (fc:CSUtils.FileConfig, w:XamlMain) as self =
    let new_message_builder (id:int64) = (new PlcCommunication.MessageToPlc.Builder()).SetId(id)
    do
        w.Root.Loaded.Add(self.Loaded)
        w.Root.Closed.Add(self.Closed)
        w.buttonRestart.Click.Add(self.OperateOnSelectedUser(self.Restart))
        w.buttonSaveChanges.Click.Add(self.OperateOnSelectedUser(self.SaveChanges))
        w.buttonCancelChanges.Click.Add(self.CancelChanges)
        w.buttonBrowsePreferencesFile.Click.Add(self.OperateOnSelectedUser(self.BrowsePreferencesFile))
        w.Root.DataContext <- self
    member val Next_OOB_Id = -1L with get, set
    member val Next_Query_Id = 1L with get, set
    member val Config = new Configuration() with get, set
    member val Client = (null :> System.Net.Sockets.TcpClient) with get, set
    member val Stream = (null  :> System.IO.Stream) with get, set
    member val Users = new ObservableCollection<RowUser>() with get

    member this.Window = w.Root
    member this.Dispatcher = w.Root.Dispatcher

    member this.Loaded (args:RoutedEventArgs) =
        // 1. Load the configuration file.
        try
            fc.Load()
            self.Config <- CSUtils.StringDictionary.ToObject<Configuration>(fc.[ConfigSection])
        with
        | ex -> ()
        let mutable keep_trying = true
        while this.Client=null && keep_trying do
            let msg = 
                try
                    let ip_address, port =
                        let v = this.Config.ServerConnectionString.Split([| ':' |])
                        v.[0], System.Int32.Parse(v.[1])
                    // 1. Connect to somewhere.
                    this.Client <- new TcpClient(ip_address, port)
                    this.Stream <- this.Client.GetStream()
                    // 2. Send auth. message
                    let auth_msg = 
                        let cfg = (new PlcCommunication.Configuration.Builder()).SetDeviceId(this.Config.ServerUsername).SetPassword(this.Config.ServerPassword).Build()
                        let msg = (new PlcCommunication.MessageFromPlc.Builder()).SetId(this.Next_OOB_Id).SetOOBConfiguration(cfg).Build()
                        msg.WriteDelimitedTo(this.Stream)
                        this.Next_OOB_Id <- this.Next_OOB_Id - 1L
                    // 3. Expect OK back.
                    let r = PlcCommunication.MessageFromPlc.ParseDelimitedFrom(this.Stream)
                    if r.HasResponse && r.Response.HasOK && r.Response.OK then
                        this.MessageFromServer(r)
                        ""
                    else
                        // bad!
                        this.Client.Close()
                        this.Client <- null
                        this.Stream <- null
                        "Viga: Tundmatu kasutajanimi või parool."
                with
                | ex ->
                    if this.Client<>null then
                        this.Client.Close()
                        this.Client <- null
                        this.Stream <- null
                    "Viga: " + ex.Message
            // Any errors?
            if this.Client=null then
                keep_trying <- WindowConfiguration.ShowDialog(this.Config, msg)
                if keep_trying then
                    fc.[ConfigSection] <- CSUtils.StringDictionary.Create(this.Config)
                    fc.Store()
        if this.Client=null || this.Stream=null then
            this.Window.Close()
        else
            // YEA!
            let th = new System.Threading.Thread(new System.Threading.ThreadStart(this.ConnectionThreadFunction))
            th.Start()

    member this.Closed (args: EventArgs) =
        let c = this.Client
        this.Client <- null
        if c<>null && c.Connected then
            c.Close()
        ()

    member this.OperateOnSelectedUser (f: RowUser -> unit) (_:RoutedEventArgs)  =
        let index =  w.listboxUsers.SelectedIndex
        if index<0 || index>=this.Users.Count then
            MessageBox.Show("Valige kõigepealt kasutaja!", TitleError) |> ignore
        else
            let user = this.Users.[index]
            f(user)

    member this.Restart (user: RowUser) =
        let msg = (new_message_builder this.Next_Query_Id).SetForwardToPlcId(user.Id).SetCommand(PlcCommunication.COMMAND.RELOAD_CONFIGURATION).Build()
        msg.WriteDelimitedTo(this.Stream)
        this.Next_Query_Id <- this.Next_Query_Id + 1L

    member this.BrowsePreferencesFile (user: RowUser) =
        let dlg = new Microsoft.Win32.OpenFileDialog( Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*")
        let r = dlg.ShowDialog()
        if r.HasValue && r.Value then
            let new_prefs = System.IO.File.ReadAllText(dlg.FileName)
            user.PlcConfiguration.Preferences <- new_prefs
            ()

    member this.SaveChanges (user: RowUser) =
        let src_cfg = user.PlcConfiguration
        // FIXME: do what?
        let cfg = (new PlcCommunication.RowPlcConfiguration.Builder()).
                        SetUserId(user.Id).
                        SetConfigurationFile(Google.ProtocolBuffers.ByteString.CopyFromUtf8(src_cfg.ConfigurationFile)).
                        SetPreferences(Google.ProtocolBuffers.ByteString.CopyFromUtf8(src_cfg.Preferences)).Build()
        let msg = (new_message_builder this.Next_Query_Id).SetNewRowPlcConfiguration(cfg).Build()
        msg.WriteDelimitedTo(this.Stream)
        this.Next_Query_Id <- this.Next_Query_Id + 1L

    member this.CancelChanges (args:RoutedEventArgs) =
        MessageBox.Show("Ei ole realiseeritud.", TitleError) |> ignore

    member this.MessageFromServer(msg: PlcCommunication.MessageFromPlc) =
        // MessageBox.Show("Teade serverilt!", TitleOk) |> ignore
        if msg.OOBRowUsersCount>0 then
            this.Users.Clear()
            for u_src in msg.OOBRowUsersList do
                let ru = new RowUser(Id = u_src.Id, CreateDate=new DateTime(u_src.CreateDate), Name=u_src.Name, Type=u_src.Type, IsPublic=u_src.IsPublic)
                this.Users.Add(ru)
                // query some stuff.
                try
                    let msg = (new_message_builder this.Next_Query_Id).SetQueryLatestRowPlcConfiguration(u_src.Id).Build()
                    msg.WriteDelimitedTo(this.Stream)
                    this.Next_Query_Id <- this.Next_Query_Id + 1L
                with
                | ex -> MessageBox.Show("Viga: " + ex.Message, TitleError) |> ignore
        if msg.HasResponseToQueryLatestRowPlcConfiguration then
            let cfg = msg.ResponseToQueryLatestRowPlcConfiguration
            let users = (seq { for u in this.Users do if u.Id = cfg.UserId then yield u }).ToArray()
            match users with
            | [| u |] ->
                let plc_cfg = new RowPlcConfiguration(Id=cfg.Id, UserId=cfg.UserId, CreateDate=new DateTime(cfg.CreateDate), Version=cfg.Version, ConfigurationFile=cfg.ConfigurationFile.ToStringUtf8(), Preferences=cfg.Preferences.ToStringUtf8())
                u.PlcConfiguration <- plc_cfg
            | _ -> ()
        ()

    member this.ServerConnectionClosed(msg:string) =
        if this.Client<>null then
            MessageBox.Show("Ühendus katkes: " + msg, TitleError) |> ignore
            this.Window.Close()

    member this.ConnectionThreadFunction () =
        let mutable read_ok = true
        let mutable exx = (null :> System.Exception)
        let mutable c = this.Client
        while c<>null && c.Connected && read_ok do
            try
                let msg = PlcCommunication.MessageFromPlc.ParseDelimitedFrom(this.Stream)
                this.Dispatcher.BeginInvoke(new Action(fun () -> this.MessageFromServer(msg)), null) |> ignore
                // forward the message!
            with
            | ex ->
                exx <- ex
                read_ok <- false
            c <- this.Client
        if c<>null && c.Connected then
            let s_msg = if exx=null then "" else exx.Message
            this.Dispatcher.BeginInvoke(new Action(fun () -> this.ServerConnectionClosed(s_msg)), null) |> ignore
            c.Close()
        ()

//===========================================================================
let unhandled_exception_handler (args: System.Windows.Threading.DispatcherUnhandledExceptionEventArgs) =
    args.Handled <- true
    MessageBox.Show(args.Exception.Message, TitleError) |> ignore

//===========================================================================
[<EntryPoint>]
[<STAThread>]
let main argv = 
    try
        let fc = new CSUtils.FileConfig(ConfigFilename)
        let app = new Application()
        app.DispatcherUnhandledException.Add(unhandled_exception_handler)
        let wm = new WindowMain (fc, XamlMain ())
        app.Run(wm.Window) |> ignore
    with
        | ex -> MessageBox.Show("Error:" + ex.Message, "Error") |> ignore
    0
