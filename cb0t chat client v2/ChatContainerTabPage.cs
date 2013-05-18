using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using Ares.PacketHandlers;

namespace cb0t_chat_client_v2
{
    class ChatContainerTabPage : Panel
    {
        private TabControl tab_control;
        private PMTabPage[] pm_chat;
        private FilesTabPage[] file_chat;
        private ChatTabPage main;
        private Socket socket;
        private Thread dyndns_thread;
        private bool connected = false;
        private int spawn_time;
        private bool sleeping = false;
        private int sleep_time = 0;
        private int reconnect_attempts = 0;
        private int failed_receives = 0;
        private String my_username = String.Empty;
        private int last_update_sent = 0;
        private bool room_closing = false;
        public bool tab_selected = false;
        private bool got_topic = false;
        private Random rnd = new Random();
        private bool dyndns_resolved = false;
        private bool already_logged_in = false;
        private int last_latency_check;
        public bool supports_high_quality = false;
        public bool supports_ips = true;

        private int last_keypress_check = 0;
        private int last_nudge = 0;

        private List<byte> bytes_in = new List<byte>();
        private Queue<byte[]> bytes_out = new Queue<byte[]>();
        private List<UserObject> users = new List<UserObject>();
        private List<byte> scribble_chunks = new List<byte>();
        private Queue<byte[]> voice_bytes_out = new Queue<byte[]>();

        public ChannelObject cObj;
        public int tab_ident;

        public delegate void TopicUpdatedDelegate(ChannelObject cObj);
        public event TopicUpdatedDelegate OnTopicUpdated;
        public event TopicUpdatedDelegate OnHashlinkClicked;
        public event Packets.SendPacketDelegate OnSendToAllRooms;

        public delegate void DCRequestDelegate(UserObject userobj);
        public event DCRequestDelegate OnStartDCSession;

        internal delegate void NotifyDelegate(String text, int tab_ident);
        public event NotifyDelegate OnTriggerWordReceived;
        public event NotifyDelegate OnRedirected;

        public delegate void MakeIconUnreadDelegate(int tab_ident);
        public event MakeIconUnreadDelegate MakeIconUnread;

        public event ChatTabPage.ShowPlayListDelegate OnPlaylistRequested;

        public delegate void VoicePlayerInboundHandler(VoiceClipReceived vcr, bool queue_if_busy);
        public event VoicePlayerInboundHandler OnVoiceClipPlayRequest;

        public delegate void PauseVCHandler(bool paused);
        public event PauseVCHandler OnVCPause;

        private bool supports_custom_emotes = false;

        public ChatContainerTabPage(ChannelObject cObj, int tab_ident, int spawn_time)
        {
            this.cObj = cObj;
            this.tab_ident = tab_ident;
            this.spawn_time = spawn_time;
            this.last_latency_check = spawn_time;
            this.last_nudge = spawn_time;

            this.pm_chat = new PMTabPage[50];
            this.file_chat = new FilesTabPage[25];
            
            this.Location = new Point(4, 25); // 4, 25
            this.Name = cObj.name;
            this.Padding = new Padding(3);
            this.Size = new Size(873, 461); // 873, 461
            this.TabIndex = 1;
            this.Text = cObj.name;
            this.Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom);

            this.tab_control = new TabControl();
            this.tab_control.Multiline = true;
            this.tab_control.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) | AnchorStyles.Left) | AnchorStyles.Right)));
            this.tab_control.Location = new Point(0, 0);
            this.tab_control.Name = "tab_control";
            this.tab_control.SelectedIndex = 0;
            this.tab_control.Size = new Size(874, 461);
            this.tab_control.TabIndex = 0;
            this.tab_control.ImageList = new ImageList();
            this.tab_control.ImageList.TransparentColor = Color.Magenta;
            this.tab_control.ImageList.Images.Add(AresImages.Chat);
            this.tab_control.ImageList.Images.Add(AresImages.PM_Read);
            this.tab_control.ImageList.Images.Add(AresImages.PM_Unread);
            this.tab_control.ImageList.Images.Add(AresImages.Files);
            this.tab_control.SelectedIndexChanged += new EventHandler(this.OnTabChanged);
            this.tab_control.ContextMenuStrip = new ContextMenuStrip();
            this.tab_control.ContextMenuStrip.Items.Add("Close");
            this.tab_control.ContextMenuStrip.Items[0].Click += new EventHandler(this.OnSubTabRightClicked);
            this.Controls.Add(this.tab_control);
            this.main = new ChatTabPage(this.my_username);
            this.main.OnPacketSending += new Packets.SendPacketDelegate(this.OnPacketDispatching);
            this.main.OnPMRequesting += new Userlist.PMRequestDelegate(this.OnPMRequested);
            this.main.HashlinkClicked += new ChannelList.ChannelClickedDelegate(this.OnHashlinkClicking);
            this.main.OnFileBrowseRequesting += new Userlist.FileBrowseRequest(this.OnFileBrowseRequested);
            this.main.OnWhoisRequested += new Userlist.PMRequestDelegate(this.OnWhoisRequested);
            this.main.OnIgnoreRequested += new Userlist.PMRequestDelegate(this.OnIgnoreRequested);
            this.main.OnVCIgnoreRequested += new Userlist.PMRequestDelegate(this.main_OnVCIgnoreRequested);
            this.main.OnNudgeRequested += new Userlist.PMRequestDelegate(this.OnNudgeRequested);
            this.main.OnSendToAll += new Packets.SendPacketDelegate(this.OnSendToAll);
            this.main.OnDCReq += new Userlist.PMRequestDelegate(this.OnDCReq);
            this.main.OnBeginLagTest += new InputTextBox.LagTestDelegate(this.OnBeginLagTest);
            this.main.OnWritingProceed += new InputTextBox.WritingDelegate(this.OnWritingProceed);
            this.main.OnMiddleClicked += new Userlist.PMRequestDelegate(this.OnMiddleClicked);
            this.main.OnPlaylistRequesting += new ChatTabPage.ShowPlayListDelegate(this.OnPlaylistRequesting);
            this.main.OnCATReceived += new OutputTextBox.CATDelegate(this.OnCATReceived);
            this.main.UploadVoicePackets += new ChatTabPage.UploadVoicePacketsHandler(this.UploadVoicePackets);
            this.main.OnClickedVC += new OutputTextBox.VCHandler(this.OnClickedVC);
            this.main.OnDeleteVC += new OutputTextBox.VCDelHandler(this.OnDeleteVC);
            this.main.OnSaveVC += new OutputTextBox.VCHandler(this.OnSaveVC);
            this.main.PauseVCNow += new PauseVCHandler(this.PausingVC);
            this.main.RadioHashlink += new OutputTextBox.RadioHashlinkClickedHandler(this.RadioHashlinkClicked);
            this.main.CloseTabs += new EventHandler(this.CloseTabs);
            this.tab_control.TabPages.Add(this.main);
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.socket.Blocking = false;

            if (this.cObj.dyndns != String.Empty)
            {
                this.dyndns_thread = new Thread(new ThreadStart(this.ResolveDns));
                this.dyndns_thread.Start();
            }
            else
            {
                this.main.DisplayServerText("Connecting to host, please wait...");
                this.dyndns_resolved = true;

                try
                {
                    this.socket.Connect(new IPEndPoint(this.cObj.ip, this.cObj.port));
                }
                catch { }
            }
        }

        public event EventHandler CloseTabsCmd;
        private void CloseTabs(object sender, EventArgs e)
        {
            this.CloseTabsCmd(sender, e);
        }

        public void CloseTabsNow()
        {
            this.OnCATReceived();
        }

        public event OutputTextBox.RadioHashlinkClickedHandler RadioHashlink;

        private void RadioHashlinkClicked(String url)
        {
            this.RadioHashlink(url);
        }

        public void SendCustomEmoticonPacket(byte[] packet)
        {
            if (this.supports_custom_emotes)
                this.bytes_out.Enqueue(packet);
        }

        public void HotKeyClick(bool down)
        {
            if (!this.vc_compatible)
                return;

            if (((String)this.tab_control.SelectedTab.Tag) == "chat")
            {
                this.main.HotKeyPressed(down);
                return;
            }
            else if (((String)this.tab_control.SelectedTab.Tag) == "pm")
            {
                int i = ((PMTabPage)this.tab_control.SelectedTab).tab_ident;
                this.pm_chat[i].HotKeyPressed(down);
                return;
            }
        }

        public void VCEscapeCancel()
        {
            if (!this.vc_compatible)
                return;

            if (((String)this.tab_control.SelectedTab.Tag) == "chat")
            {
                this.main.VCEscapeCancel();
                return;
            }
            else if (((String)this.tab_control.SelectedTab.Tag) == "pm")
            {
                int i = ((PMTabPage)this.tab_control.SelectedTab).tab_ident;
                this.pm_chat[i].VCEscapeCancel();
                return;
            }
        }

        private void PausingVC(bool pause)
        {
            this.OnVCPause(pause);
        }

        private void OnSaveVC(String id, bool is_pm)
        {
            if (is_pm)
            {
                VoiceClipReceived vcr = this.pm_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == id; });

                if (vcr != null)
                {
                    using (SaveFileDialog sd = new SaveFileDialog())
                    {
                        String path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) + "\\";
                        sd.Filter = "wav files (*.wav)|*.wav";
                        sd.InitialDirectory = path;

                        if (sd.ShowDialog() == DialogResult.OK)
                        {
                            try
                            {
                                File.WriteAllBytes(sd.FileName, vcr.VoiceClip);
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show(e.Message, "Problem saving voice clip...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
            else
            {
                VoiceClipReceived vcr = this.room_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == id; });

                if (vcr != null)
                {
                    using (SaveFileDialog sd = new SaveFileDialog())
                    {
                        String path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) + "\\";
                        sd.Filter = "wav files (*.wav)|*.wav";
                        sd.InitialDirectory = path;

                        if (sd.ShowDialog() == DialogResult.OK)
                        {
                            try
                            {
                                File.WriteAllBytes(sd.FileName, vcr.VoiceClip);
                            }
                            catch (Exception e)
                            {
                                MessageBox.Show(e.Message, "Problem saving voice clip...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
        }

        private void OnDeleteVC(String id, bool is_pm)
        {
            if (is_pm)
            {
                this.pm_vc_received.RemoveAll(delegate(VoiceClipReceived x)
                {
                    return x.hash == id;
                });
            }
            else
            {
                this.room_vc_received.RemoveAll(delegate(VoiceClipReceived x)
                {
                    return x.hash == id;
                });
            }
        }

        private void OnClickedVC(String id, bool is_pm)
        {
            if (is_pm)
            {
                VoiceClipReceived vcr = this.pm_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == id; });

                if (vcr != null)
                    this.OnVoiceClipPlayRequest(vcr, false);
            }
            else
            {
                VoiceClipReceived vcr = this.room_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == id; });

                if (vcr != null)
                    this.OnVoiceClipPlayRequest(vcr, false);
            }
        }

        public void DisplayVCNow(VoiceClipReceived vcr)
        {
            if (vcr.pm)
            {
                VoiceClipReceived check = this.pm_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == vcr.hash && x.from == vcr.from; });

                if (check == null)
                    return;

                this.OnVoicePMReceived(vcr.from, vcr);
            }
            else
            {
                VoiceClipReceived check = this.room_vc_received.Find(delegate(VoiceClipReceived x) { return x.hash == vcr.hash && x.from == vcr.from; });

                if (check == null)
                    return;

                this.main.AddClip(vcr);
            }
        }

        public void FreeResources()
        {
            this.Controls.Clear();
            this.OnCATReceived();
            this.main.FreeResources();
            this.tab_control.Dispose();
            this.Dispose();
        }

        #region VOICE_CHAT
        public bool supports_voice_chat = false;
        private bool allow_voice_send = true;
        private int flood_timer = 0;

        private List<VoiceClipReceived> room_vc_received = new List<VoiceClipReceived>();
        private List<VoiceClipReceived> pm_vc_received = new List<VoiceClipReceived>();

        public void GUITick()
        {
            if (this.flood_timer++ > 4) // 5 seconds between outbound voice clips
            {
                this.flood_timer = 0;
                this.allow_voice_send = true;
            }

            foreach (TabPage t in this.tab_control.TabPages)
            {
                if (((String)t.Tag) == "pm")
                {
                    PMTabPage p = (PMTabPage)t;
                    p.OnVCTimerTick();
                }
                else if (((String)t.Tag) == "chat")
                {
                    ChatTabPage p = (ChatTabPage)t;
                    p.OnVCTimerTick();
                }
            }

            try
            {
                if (this.voice_bytes_out.Count > 0)
                    this.bytes_out.Enqueue(this.voice_bytes_out.Dequeue());
            }
            catch { }
        }

        private bool UploadVoicePackets(byte[][] packets)
        {
            if (!this.allow_voice_send)
                return false;

            this.allow_voice_send = false;

            try
            {
                foreach (byte[] p in packets)
                    this.voice_bytes_out.Enqueue(p);

                return true;
            }
            catch { }

            return false;
        }
        #endregion


        private int flood_check_index = 0;
        private List<String> flood_check = new List<String>(new String[] { String.Empty, String.Empty, String.Empty });

        private bool FloodCheck(String text)
        {
            this.flood_check[this.flood_check_index] = text;
            this.flood_check_index++;

            if (this.flood_check_index > 2)
                this.flood_check_index = 0;

            return this.flood_check.FindAll(delegate(String s) { return s == text; }).Count == 3 && Settings.allow_events_flood_check;
        }

        private void ResolveDns()
        {
            this.main.DisplayServerText("Resolving hostname " + this.cObj.dyndns + "...");

            try
            {
                IPAddress[] list = Dns.GetHostAddresses(this.cObj.dyndns);

                foreach (IPAddress ip in list)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        this.cObj.ip = ip;
                        this.main.DisplayServerText("Connecting to host, please wait...");
                        this.dyndns_resolved = true;

                        try
                        {
                            this.socket.Connect(new IPEndPoint(this.cObj.ip, this.cObj.port));
                        }
                        catch { }

                        return;
                    }
                }
            }
            catch { }
        }

        private void OnCATReceived()
        {
            try
            {
                while (this.tab_control.TabCount > 0)
                {
                    String _str = (String)this.tab_control.TabPages[this.tab_control.TabCount - 1].Tag;
                    int i;

                    switch (_str)
                    {
                        case "chat":
                            return;

                        case "pm":
                            i = ((PMTabPage)this.tab_control.TabPages[this.tab_control.TabCount - 1]).tab_ident;
                            this.tab_control.TabPages.Remove(this.tab_control.TabPages[this.tab_control.TabCount - 1]);
                            this.pm_chat[i].FreeResources();
                            this.pm_chat[i] = null;
                            break;

                        case "file":
                            i = ((FilesTabPage)this.tab_control.TabPages[this.tab_control.TabCount - 1]).tab_ident;
                            this.tab_control.TabPages.Remove(this.tab_control.TabPages[this.tab_control.TabCount - 1]);
                            this.file_chat[i].FreeResources();
                            this.file_chat[i] = null;
                            break;

                        default:
                            return;
                    }
                }
            }
            catch { }
        }

        private void OnPlaylistRequesting()
        {
            this.OnPlaylistRequested();
        }

        private void OnMiddleClicked(String name)
        {
            foreach (UserObject u in this.users)
            {
                if (u.name == name)
                {
                    this.main.AddToInputBox(u.name);
                    return;
                }
            }

            foreach (UserObject u in this.users)
            {
                if (u.name.StartsWith(name))
                {
                    this.main.AddToInputBox(u.name);
                    return;
                }
            }
        }

        private void OnWritingProceed(bool is_writing)
        {
            if (!Settings.send_custom_data)
                return;

            this.bytes_out.Enqueue(Packets.WritingPacket(is_writing));
            UserObject u = this.FindUserByName(this.my_username);

            if (u != null)
            {
                u.writing = is_writing;
                this.main.UserListUpdateUser(u, false, false, false);
                this.UpdateWhoIsWriting();
            }
        }

        private void UpdateWhoIsWriting()
        {
            List<String> writing_names = new List<String>();

            foreach (UserObject u in this.users)
                if (u.writing)
                    writing_names.Add(u.name);

            if (writing_names.Count == 0)
                this.main.WhoisWritingUpdate(null);
            else
                this.main.WhoisWritingUpdate(String.Join(", ", writing_names.ToArray()));
        }

        public void FixScreen()
        {
            if (((String)this.tab_control.SelectedTab.Tag) == "chat")
                this.main.FixScreen();

            if (((String)this.tab_control.SelectedTab.Tag) == "pm")
                ((PMTabPage)this.tab_control.SelectedTab).FixScreen();
        }

        public void RedrawTopic()
        {
            if (this.main != null)
                this.main.RedrawTopic();
        }

        private void OnBeginLagTest()
        {
            TimeSpan ts = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
            ulong u = (ulong)ts.TotalMilliseconds;
            this.bytes_out.Enqueue(Packets.LagCheckPacket(this.my_username, u));
        }

        private void OnDCReq(String name)
        {
            UserObject u = this.FindUserByName(name);

            if (u != null)
            {
                this.OnStartDCSession(u);
            }
        }

        private void OnSendToAll(byte[] packet)
        {
            this.OnSendToAllRooms(packet);
        }

        public void UpdateMyStatus(byte[] data)
        {
            if (this.connected)
            {
                UserObject u = this.FindUserByName(this.my_username);

                if (u != null)
                {
                    u.status = Settings.my_status;
                    this.main.UserListUpdateUser(u, false, false, false);
                    this.bytes_out.Enqueue(data);
                }
            }
        }

        public void AddGlobalPacket(byte[] packet)
        {
            if (this.connected)
                this.bytes_out.Enqueue(packet);
        }

        public void AddGlobalAvatarPacket(byte[] packet)
        {
            if (this.connected)
                this.bytes_out.Enqueue(packet);
        }

        private void OnNudgeRequested(String name)
        {
            UserObject _obj = this.FindUserByName(name);

            if (_obj != null)
            {
                this.bytes_out.Enqueue(Packets.NudgePacket(this.my_username, name));
                this.main.DisplayAnnounceText("\x000314--- You have nudged " + name);
            }
        }

        private void OnIgnoreRequested(String name)
        {
            UserObject _obj = this.FindUserByName(name);

            if (_obj != null)
            {
                _obj.ignored = !_obj.ignored;
                this.bytes_out.Enqueue(Packets.IgnoreUserPacket(_obj.name, _obj.ignored));
            }
        }

        private void main_OnVCIgnoreRequested(String name)
        {
            this.bytes_out.Enqueue(Packets.VCIgnore(name));
        }

        private void OnWhoisRequested(String name)
        {
            UserObject _obj = this.FindUserByName(name);

            if (_obj != null)
            {
                this.main.DisplayAnnounceText("\x000314--- Whois: " + _obj.name);

                if (this.supports_ips)
                {
                    this.main.DisplayAnnounceText("\x000314--- External IP: " + _obj.externalIp.ToString());
                    this.main.DisplayAnnounceText("\x000314--- Local IP: " + _obj.localIp.ToString());
                    this.main.DisplayAnnounceText("\x000314--- DC Port: " + _obj.dcPort);
                }
                else this.main.DisplayAnnounceText("\x000314--- IP Addresses are not supported in this chatroom");

                this.main.DisplayAnnounceText("\x000314--- ASL: " + _obj.ToASLString());
                this.main.DisplayAnnounceText("\x000314--- Message: " + _obj.personal_message);
            }
        }

        private void OnFileBrowseRequested(String name, byte type)
        {
            int i = this.NextFreeFilesTab();

            if (i > -1)
            {
                int r = this.rnd.Next(0, 65535);
                this.file_chat[i] = new FilesTabPage(name, type, r, type);
                this.tab_control.TabPages.Add(this.file_chat[i]);
                this.tab_control.SelectedTab = this.tab_control.TabPages[this.tab_control.TabPages.Count - 1];
                this.bytes_out.Enqueue(Packets.BrowseRequestPacket(name, (ushort)r, type));
            }
        }

        private void OnHashlinkClicking(ChannelObject cObj)
        {
            this.OnHashlinkClicked(cObj);
        }

        private void OnSubTabRightClicked(object sender, EventArgs e)
        {
            if (this.tab_control.SelectedTab != null)
            {
                if (((String)this.tab_control.SelectedTab.Tag) == "chat")
                    return;

                if (((String)this.tab_control.SelectedTab.Tag) == "pm")
                {
                    int i = ((PMTabPage)this.tab_control.SelectedTab).tab_ident;
                    this.tab_control.TabPages.Remove(this.tab_control.SelectedTab);
                    this.pm_chat[i].FreeResources();
                    this.pm_chat[i] = null;
                    return;
                }

                if (((String)this.tab_control.SelectedTab.Tag) == "file")
                {
                    int i = ((FilesTabPage)this.tab_control.SelectedTab).tab_ident;
                    this.tab_control.TabPages.Remove(this.tab_control.SelectedTab);
                    this.file_chat[i].FreeResources();
                    this.file_chat[i] = null;
                }
            }
        }

        private bool IsPMAutoPlayVC(String name)
        {
            foreach (TabPage t in this.tab_control.TabPages)
            {
                if (((String)t.Tag) == "pm")
                {
                    PMTabPage p = ((PMTabPage)t);

                    if (p.username == name)
                        return p.can_auto_play;
                }
            }

            return false;
        }

        private void OnPMRequested(String name)
        {
            foreach (TabPage t in this.tab_control.TabPages)
            {
                if (((String)t.Tag) == "pm")
                {
                    PMTabPage p = ((PMTabPage)t);

                    if (p.username == name)
                    {
                        this.tab_control.SelectedTab = t;
                        return;
                    }
                }
            }

            int i = this.NextFreePMTab();

            if (i > -1)
            {
                UserObject userobj = this.FindUserByName(name);

                if (userobj != null)
                    this.pm_chat[i] = new PMTabPage(name, this.my_username, i, userobj.externalIp.ToString(), this.supports_voice_chat, this.supports_high_quality);
                else
                    this.pm_chat[i] = new PMTabPage(name, this.my_username, i, "0.0.0.0", this.supports_voice_chat, this.supports_high_quality);

                this.pm_chat[i].OnPacketSending += new Packets.SendPacketDelegate(this.OnPMPacketDisp);
                this.pm_chat[i].OnHashlinkClicked += new ChannelList.ChannelClickedDelegate(this.OnHashlinkClicked);
                this.pm_chat[i].UploadVoicePackets += new PMTabPage.UploadVoicePacketsHandler(this.UploadVoicePackets);
                this.pm_chat[i].OnClickedVC += new OutputTextBox.VCHandler(this.OnClickedVC);
                this.pm_chat[i].OnDeleteVC += new OutputTextBox.VCDelHandler(this.OnDeleteVC);
                this.pm_chat[i].OnSaveVC += new OutputTextBox.VCHandler(this.OnSaveVC);
                this.pm_chat[i].PauseVCNow += new PauseVCHandler(this.PausingVC);
                this.pm_chat[i].RadioHashlink += new OutputTextBox.RadioHashlinkClickedHandler(this.RadioHashlinkClicked);
                this.tab_control.TabPages.Add(this.pm_chat[i]);
                this.tab_control.SelectedTab = this.tab_control.TabPages[this.tab_control.TabPages.Count - 1];

                if (userobj == null)
                {
                    if (this.is_ninja)
                    {
                        this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                        this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                        this.pm_chat[i].SetAnnounceText(" ");
                    }
                }
                else
                {
                    if (this.is_ninja && !userobj.pm_enc)
                    {
                        this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                        this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                        this.pm_chat[i].SetAnnounceText(" ");
                    }
                }
            }
        }

        private bool is_ninja = false;

        private delegate void PMReceiveDelegate(String name, String text);
        private void OnPMReceived(String name, String text)
        {
            if (this.tab_control.InvokeRequired)
            {
                this.tab_control.BeginInvoke(new PMReceiveDelegate(this.OnPMReceived), name, text);
            }
            else
            {
                bool pmnotifysent = false;
                UserObject tmp = this.FindUserByName(name);

                foreach (TabPage t in this.tab_control.TabPages)
                {
                    if (((String)t.Tag) == "pm")
                    {
                        PMTabPage p = (PMTabPage)t;

                        if (p.username == name)
                        {
                            if (tmp != null)
                                p.OnPMReceived(name, text, tmp.font, tmp.custom_emotes.ToArray(), this.supports_custom_emotes);
                            else
                                p.OnPMReceived(name, text, null, null, this.supports_custom_emotes);

                            if (!this.tab_control.SelectedTab.Equals(t))
                            {
                                p.ImageIndex = 2;
                                this.CheckPMNotify(name);
                                pmnotifysent = true;
                            }

                            if ((!this.tab_selected || !Settings.cbot_visible) && !pmnotifysent)
                                this.CheckPMNotify(name);

                            return;
                        }
                    }
                }

                int i = this.NextFreePMTab();

                if (i > -1)
                {
                    if (tmp != null)
                        this.pm_chat[i] = new PMTabPage(name, this.my_username, i, tmp.externalIp.ToString(), this.supports_voice_chat, this.supports_high_quality);
                    else
                        this.pm_chat[i] = new PMTabPage(name, this.my_username, i, "0.0.0.0", this.supports_voice_chat, this.supports_high_quality);

                    this.pm_chat[i].OnPacketSending += new Packets.SendPacketDelegate(this.OnPMPacketDisp);
                    this.pm_chat[i].OnHashlinkClicked += new ChannelList.ChannelClickedDelegate(this.OnHashlinkClicked);
                    this.pm_chat[i].UploadVoicePackets += new PMTabPage.UploadVoicePacketsHandler(this.UploadVoicePackets);
                    this.pm_chat[i].OnClickedVC += new OutputTextBox.VCHandler(this.OnClickedVC);
                    this.pm_chat[i].OnDeleteVC += new OutputTextBox.VCDelHandler(this.OnDeleteVC);
                    this.pm_chat[i].OnSaveVC += new OutputTextBox.VCHandler(this.OnSaveVC);
                    this.pm_chat[i].PauseVCNow += new PauseVCHandler(this.PausingVC);
                    this.pm_chat[i].RadioHashlink += new OutputTextBox.RadioHashlinkClickedHandler(this.RadioHashlinkClicked);
                    this.tab_control.TabPages.Add(this.pm_chat[i]);

                    if (tmp != null)
                    {
                        if (this.is_ninja && !tmp.pm_enc)
                        {
                            this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                            this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                            this.pm_chat[i].SetAnnounceText(" ");
                        }

                        this.pm_chat[i].OnPMReceived(name, text, tmp.font, tmp.custom_emotes.ToArray(), this.supports_custom_emotes);
                    }
                    else
                    {
                        if (this.is_ninja)
                        {
                            this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                            this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                            this.pm_chat[i].SetAnnounceText(" ");
                        }

                        this.pm_chat[i].OnPMReceived(name, text, null, null, this.supports_custom_emotes);
                    }

                    this.pm_chat[i].ImageIndex = 2;
                    this.CheckPMNotify(name);
                    pmnotifysent = true;
                }

                if ((!this.tab_selected || !Settings.cbot_visible) && !pmnotifysent)
                    this.CheckPMNotify(name);
            }
        }

        private void CheckPMNotify(String name)
        {
            if (Settings.pm_notify)
                foreach (String str in Settings.pm_notify_msg)
                    if (str == name)
                        this.OnTriggerWordReceived("[pm_notify]\0" + this.cObj.name + "\0" + name, this.tab_ident);
        }

        private delegate void PMVoiceReceiveDelegate(String name, VoiceClipReceived vcr);
        private void OnVoicePMReceived(String name, VoiceClipReceived vcr)
        {
            if (this.tab_control.InvokeRequired)
            {
                this.tab_control.BeginInvoke(new PMVoiceReceiveDelegate(this.OnVoicePMReceived), name, vcr);
            }
            else
            {
                bool pmnotifysend = false;

                foreach (TabPage t in this.tab_control.TabPages)
                {
                    if (((String)t.Tag) == "pm")
                    {
                        PMTabPage p = (PMTabPage)t;

                        if (p.username == name)
                        {
                            p.AddClip(vcr);

                            if (!this.tab_control.SelectedTab.Equals(t))
                            {
                                p.ImageIndex = 2;
                                this.CheckPMNotify(name);
                                pmnotifysend = true;
                            }

                            if ((!this.tab_selected || !Settings.cbot_visible) && !pmnotifysend)
                                this.CheckPMNotify(name);

                            return;
                        }
                    }
                }

                int i = this.NextFreePMTab();

                if (i > -1)
                {
                    UserObject userobj = this.FindUserByName(name);

                    if (userobj != null)
                        this.pm_chat[i] = new PMTabPage(name, this.my_username, i, userobj.externalIp.ToString(), this.supports_voice_chat, this.supports_high_quality);
                    else
                        this.pm_chat[i] = new PMTabPage(name, this.my_username, i, "0.0.0.0", this.supports_voice_chat, this.supports_high_quality);

                    this.pm_chat[i].OnPacketSending += new Packets.SendPacketDelegate(this.OnPMPacketDisp);
                    this.pm_chat[i].OnHashlinkClicked += new ChannelList.ChannelClickedDelegate(this.OnHashlinkClicked);
                    this.pm_chat[i].UploadVoicePackets += new PMTabPage.UploadVoicePacketsHandler(this.UploadVoicePackets);
                    this.pm_chat[i].OnClickedVC += new OutputTextBox.VCHandler(this.OnClickedVC);
                    this.pm_chat[i].OnDeleteVC += new OutputTextBox.VCDelHandler(this.OnDeleteVC);
                    this.pm_chat[i].OnSaveVC += new OutputTextBox.VCHandler(this.OnSaveVC);
                    this.pm_chat[i].PauseVCNow += new PauseVCHandler(this.PausingVC);
                    this.pm_chat[i].RadioHashlink += new OutputTextBox.RadioHashlinkClickedHandler(this.RadioHashlinkClicked);
                    this.tab_control.TabPages.Add(this.pm_chat[i]);

                    if (userobj == null)
                    {
                        if (this.is_ninja)
                        {
                            this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                            this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                            this.pm_chat[i].SetAnnounceText(" ");
                        }
                    }
                    else
                    {
                        if (this.is_ninja && !userobj.pm_enc)
                        {
                            this.pm_chat[i].SetAnnounceText("WARNING: sbot ninja is spying on this private conversation without your permission");
                            this.pm_chat[i].SetAnnounceText("WARNING: the admins in this room are able to eves drop on this private conversation");
                            this.pm_chat[i].SetAnnounceText(" ");
                        }
                    }

                    this.pm_chat[i].AddClip(vcr);
                    this.pm_chat[i].ImageIndex = 2;
                    this.CheckPMNotify(name);
                    pmnotifysend = true;
                }

                if ((!this.tab_selected || !Settings.cbot_visible) && !pmnotifysend)
                    this.CheckPMNotify(name);
            }
        }

        private void OnPMPacketDisp(byte[] packet)
        {
            this.FloodCheck(String.Empty);

            if (this.connected)
            {
                AresDataPacket p = new AresDataPacket(packet);
                p.SkipBytes(3);
                String name = p.ReadString();
                String text = p.ReadString();
                UserObject user = this.FindUserByName(name);

                if (user == null)
                {
                    foreach (PMTabPage _pm_off_page in this.pm_chat)
                        if (_pm_off_page != null)
                            if (_pm_off_page.username == name)
                                _pm_off_page.SetAnnounceText("User is offline");
                }
                else
                {
                    if (user.pm_enc)
                        this.bytes_out.Enqueue(Packets.CustomPM(name, text));
                    else
                        this.bytes_out.Enqueue(packet);
                }
            }
        }

        private void OnPacketDispatching(byte[] packet)
        {
            this.FloodCheck(String.Empty);

            if (this.connected)
                this.bytes_out.Enqueue(packet);
        }

        private void OnTabChanged(object sender, EventArgs e)
        {
            if (this.tab_control.SelectedTab != null)
            {
                switch ((String)this.tab_control.SelectedTab.Tag)
                {
                    case "chat":
                        ((ChatTabPage)this.tab_control.SelectedTab).OnFocusReceived();
                        break;

                    case "pm":
                        ((PMTabPage)this.tab_control.SelectedTab).OnFocusReceived();
                        this.tab_control.SelectedTab.ImageIndex = 1;
                        break;
                }
            }
        }

        public void MakeSelected(bool yes)
        {
            this.tab_selected = yes;

            if (yes)
            {
                this.FixScreen();
                this.Focus();

                switch ((String)this.tab_control.SelectedTab.Tag)
                {
                    case "chat":
                        ((ChatTabPage)this.tab_control.SelectedTab).OnFocusReceived();
                        break;

                    case "pm":
                        ((PMTabPage)this.tab_control.SelectedTab).OnFocusReceived();
                        break;
                }
            }
        }

        public void CloseRoom()
        {
            this.room_closing = true;

            try
            {
                this.socket.Disconnect(false);
            }
            catch { }

            this.socket = null;
        }

        public void ServiceChatroom(int time)
        {
            if (!this.dyndns_resolved)
                return;

            if (this.room_closing)
                return;

            if (this.sleeping)
            {
                if (time > (this.sleep_time + 60))
                {
                    this.failed_receives = 0;
                    this.main.DisplayServerText("Connecting to host, please wait..." + (this.reconnect_attempts > 0 ? (" #" + this.reconnect_attempts) : ""));
                    this.sleep_time = 0;
                    this.sleeping = false;
                    this.spawn_time = time;
                    this.bytes_out.Clear();
                    this.bytes_in.Clear();

                    this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    this.socket.Blocking = false;

                    try
                    {
                        this.socket.Connect(new IPEndPoint(this.cObj.ip, this.cObj.port));
                    }
                    catch { }
                }

                return;
            }

            if (!this.connected)
            {
                if (time > (this.spawn_time + 10))
                {
                    this.main.DisplayAnnounceText("Unable to connect");
                    this.sleeping = true;
                    this.sleep_time = time;
                    this.reconnect_attempts++;
                    this.connected = false;
                }

                if (this.socket.Poll(0, SelectMode.SelectWrite))
                {
                    this.last_update_sent = time;
                    this.last_latency_check = time;
                    this.connected = true;
                    this.bytes_out.Enqueue(Packets.LoginPacket());

                    if (!Settings.enable_custom_names)
                        this.bytes_out.Enqueue(Packets.DisableCustomNames(true));

                    this.main.DisplayServerText("Connected, handshaking...");
                }
            }
            else
            {
                if (time > (this.last_update_sent + 120))
                {
                    this.last_update_sent = time;
                    this.bytes_out.Enqueue(Packets.UpdatePacket());

                    if (Settings.my_status != Settings.OnlineStatus.online)
                    {
                        this.bytes_out.Enqueue(Packets.OnlineStatusPacket());
                        UserObject _u = this.FindUserByName(this.my_username);

                        if (_u != null)
                        {
                            _u.status = Settings.my_status;
                            this.main.UserListUpdateUser(_u, false, false, false);
                        }
                    }
                }

                if (time > (this.last_latency_check + 20))
                {
                    this.last_latency_check = time;

                    if (Settings.send_custom_data)
                        this.bytes_out.Enqueue(Packets.LatencyCheckPacket(this.my_username));
                }

                if (time > (this.last_keypress_check + 3))
                {
                    this.last_keypress_check = time;
                    this.main.TimeoutCheck(time);
                }

                byte[] buf1;

                while (this.bytes_out.Count > 0) // send outbound
                {
                    buf1 = this.bytes_out.Peek();

                    try
                    {
                        this.socket.Send(buf1);
                        this.bytes_out.Dequeue();
                    }
                    catch { break; }
                }

                // receive inbound

                buf1 = new byte[8192];
                SocketError e = SocketError.Success;
                int received = 0;

                try
                {
                    int avail = this.socket.Available;

                    if (avail > 8192)
                        avail = 8192;

                    received = this.socket.Receive(buf1, 0, avail, SocketFlags.None, out e);
                }
                catch { }

                if (received == 0)
                {
                    if (e == SocketError.WouldBlock)
                    {
                        this.failed_receives = 0;
                    }
                    else
                    {
                        if (this.failed_receives++ > 3) // connection lost
                        {
                            this.main.DisplayAnnounceText("Disconnected (10053)");
                            this.sleeping = true;
                            this.sleep_time = time;
                            this.reconnect_attempts++;
                            this.connected = false;
                            this.already_logged_in = false;
                        }
                    }
                }
                else
                {
                    this.failed_receives = 0;
                    byte[] buf2 = new byte[received];
                    Array.Copy(buf1, 0, buf2, 0, buf2.Length);
                    this.bytes_in.AddRange(buf2);
                }

                while (this.bytes_in.Count >= 3)
                {
                    buf1 = this.bytes_in.ToArray();

                    ushort packet_size = BitConverter.ToUInt16(buf1, 0);
                    byte packet_id = buf1[2];

                    if (buf1.Length >= (packet_size + 3))
                    {
                        byte[] buf2 = new byte[packet_size];
                        Array.Copy(buf1, 3, buf2, 0, buf2.Length);
                        this.bytes_in.RemoveRange(0, (packet_size + 3));

                        if (packet_id == 80) // zlib compressed data packet received
                        {
                            byte[] unzipped = null;

                            try
                            {
                                unzipped = ZLib.Zlib.Decompress(buf2, false);
                            }
                            catch { return; }

                            if (unzipped != null)
                            {
                                AresDataPacket zlib_dump = new AresDataPacket(unzipped);

                                while (zlib_dump.Remaining() > 2) // need at least a header
                                {
                                    packet_size = zlib_dump.ReadInt16();
                                    packet_id = zlib_dump.ReadByte();

                                    if (zlib_dump.Remaining() >= packet_size)
                                    {
                                        buf2 = zlib_dump.ReadBytes(packet_size);

                                        try
                                        {
                                            this.EvalPacket(packet_id, new AresDataPacket(buf2), time, true);
                                        }
                                        catch { return; }
                                    }
                                }
                            }
                        }
                        else // regular packet received
                        {
                            try
                            {
                                this.EvalPacket(packet_id, new AresDataPacket(buf2), time, false);
                            }
                            catch { return; }
                        }
                    }
                    else { return; }
                }
            }
        }

        private void EvalAdvancedFeaturesPacket(byte id, AresDataPacket packet)
        {
            String name;
            UserObject user;
            VoiceClipReceived vcr;
            uint tmp_ident;

            switch (id)
            {
                case 204: // callisto font
                    name = packet.ReadString();

                    if (name.Length > 0)
                    {
                        user = this.FindUserByName(name);

                        if (user != null)
                        {
                            if (packet.Remaining() > 2)
                            {
                                int _fsize = (int)packet.ReadByte();
                                String _fname = packet.ReadString();
                                int _fnc = -1;
                                int _ftc = -1;

                                if (packet.Remaining() >= 2)
                                {
                                    _fnc = packet.ReadByte();
                                    _ftc = packet.ReadByte();

                                    if (_fnc == 255)
                                        _fnc = -1;

                                    if (_ftc == 255)
                                        _ftc = -1;
                                }

                                user.font = new UserFont(_fname, _fsize, _fnc, _ftc);
                            }
                            else user.font = null;
                        }
                    }

                    break;

                case 205: // server supports voice chat?
                    this.supports_voice_chat = packet.ReadByte() == 1;
                    this.supports_high_quality = packet.ReadByte() == 1;

                    if (this.supports_voice_chat)
                    {
                        this.main.DisplayServerText("Voice Chat: Supported");
                        this.bytes_out.Enqueue(Packets.EnableClips(Settings.enable_clips, Settings.receive_private_clips));
                        this.SetVCEnabled(true, this.supports_high_quality);
                    }
                    else this.SetVCEnabled(false, false);

                    break;

                case 206: // new room voice clip received
                    vcr = new VoiceClipReceived(packet, false);

                    if (vcr.Received)
                    {
                        vcr.Unpack();
                        this.room_vc_received.Add(vcr);

                        if (!this.main.can_auto_play)
                            this.main.AddClip(vcr);
                        else
                            this.OnVoiceClipPlayRequest(vcr, true);
                    }
                    else this.room_vc_received.Add(vcr);
                    break;

                case 207: // new private voice clip received
                    vcr = new VoiceClipReceived(packet, true);

                    if (vcr.Received)
                    {
                        vcr.Unpack();
                        this.pm_vc_received.Add(vcr);

                        if (this.IsPMAutoPlayVC(vcr.from))
                            this.OnVoiceClipPlayRequest(vcr, true);
                        else
                            this.OnVoicePMReceived(vcr.from, vcr);
                    }
                    else this.pm_vc_received.Add(vcr);
                    break;

                case 208: // room voice clip chunk received
                    name = packet.ReadString();
                    tmp_ident = packet.ReadInt32();
                    vcr = this.room_vc_received.Find(delegate(VoiceClipReceived x) { return x.ident == tmp_ident && x.from == name; });

                    if (vcr != null)
                    {
                        vcr.AddChunk(packet.ReadBytes());

                        if (vcr.Received)
                        {
                            vcr.Unpack();

                            if (!this.main.can_auto_play)
                                this.main.AddClip(vcr);
                            else
                                this.OnVoiceClipPlayRequest(vcr, true);
                        }
                    }
                    break;

                case 209: // private voice clip chunk received
                    name = packet.ReadString();
                    tmp_ident = packet.ReadInt32();
                    vcr = this.pm_vc_received.Find(delegate(VoiceClipReceived x) { return x.ident == tmp_ident && x.from == name; });

                    if (vcr != null)
                    {
                        vcr.AddChunk(packet.ReadBytes());

                        if (vcr.Received)
                        {
                            vcr.Unpack();

                            if (this.IsPMAutoPlayVC(vcr.from))
                                this.OnVoiceClipPlayRequest(vcr, true);
                            else
                                this.OnVoicePMReceived(vcr.from, vcr);
                        }
                    }
                    break;

                case 210: // user is ignoring voice clips from you
                    name = packet.ReadString();

                    foreach (PMTabPage _pm_ig_page in this.pm_chat)
                        if (_pm_ig_page != null)
                            if (_pm_ig_page.username == name)
                                _pm_ig_page.SetAnnounceText(name + " is ignoring your voice clips");

                    break;

                case 211: // user doesn't support private voice clips
                    name = packet.ReadString();

                    foreach (PMTabPage _pm_ig_page in this.pm_chat)
                        if (_pm_ig_page != null)
                            if (_pm_ig_page.username == name)
                                _pm_ig_page.SetAnnounceText(name + " does not accept private voice clips");

                    break;

                case 212: // user's voice chat options
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        user.can_vc_public = packet.ReadByte() == 1;
                        user.can_vc_private = packet.ReadByte() == 1;
                        this.main.UserListUpdateUser(user, false, false, false);
                    }
                    break;

                case 215: // no ip
                    this.supports_ips = false;
                    this.main.DisableIPS();
                    break;

                case 220: // supports custom emotes
                    if (Settings.enable_custom_emotes)
                    {
                        this.supports_custom_emotes = true;
                        bool sent_a_custom_emote = false;

                        foreach (CEmoteItem c in CustomEmotes.Emotes)
                        {
                            if (c.Image != null)
                            {
                                sent_a_custom_emote = true;
                                this.bytes_out.Enqueue(Packets.CustomEmoteItem(c));
                            }
                        }

                        if (!sent_a_custom_emote)
                            this.bytes_out.Enqueue(Packets.CustomEmoteFlag());
                    }

                    break;

                case 221: // custom emoticon item
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        CEmoteItem c_new = new CEmoteItem();
                        c_new.Shortcut = packet.ReadString();
                        c_new.Size = packet.ReadByte();
                        c_new.Image = packet.ReadBytes();
                        user.custom_emotes.Add(c_new);
                    }

                    break;

                case 222: // delete custom emoticon
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        name = packet.ReadString();
                        user.custom_emotes.RemoveAll(delegate(CEmoteItem xx) { return xx.Shortcut == name; });
                    }

                    break;
            }
        }

        private void EvalPacket(byte id, AresDataPacket packet, int time, bool compressed_packet)
        {
            String name, text;
            UserObject user;
            byte[] buffer;

            switch (id)
            {
                case 0: // failed to login
                    this.main.DisplayAnnounceText("Disconnected: " + packet.ReadString());
                    this.sleeping = true;
                    this.sleep_time = time;
                    this.reconnect_attempts++;
                    this.connected = false;
                    this.already_logged_in = false;
                    break;

                case 3: // ack
                    foreach (UserObject uu in this.users)
                        if (uu.avatar != null)
                            uu.avatar.Dispose();

                    this.users.Clear();
                    this.supports_custom_emotes = false;
                    this.my_username = packet.ReadString();
                    this.main.my_username = this.my_username;
                    this.reconnect_attempts = 0;
                    this.main.UserListClear();
                    this.main.UserListUpdateMode(true);
                    this.main.DisplayServerText("Logged in, retrieving user's list...");

                    // send additional items now

                    String[] files = Settings.share_file_msg.Split(new String[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (String f_str in files)
                        this.bytes_out.Enqueue(Packets.FakeFilePacket(f_str));

                    if (Settings.enable_my_custom_font)
                        this.bytes_out.Enqueue(Packets.FontPacket());

                    break;

                case 5: // update
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        packet.SkipBytes(9);
                        user.externalIp = packet.ReadIP();
                        byte _test_admin_changed = packet.ReadByte();

                        if (_test_admin_changed != user.level)
                        {
                            user.level = _test_admin_changed;
                            this.main.UserListUpdateUser(user, false, false, false);
                        }
                    }

                    break;

                case 6: // redirect
                    IPAddress redirect_ip = packet.ReadIP();
                    ushort redirect_port = packet.ReadInt16();
                    packet.SkipBytes(4);
                    String redirect_name = packet.ReadString();

                    try
                    {
                        this.socket.Disconnect(false);
                    }
                    catch { }

                    this.main.DisplayAnnounceText("Disconnected (10053)");
                    this.already_logged_in = false;
                    this.connected = false;
                    this.spawn_time = time;
                    this.sleeping = false;
                    this.sleep_time = 0;
                    this.reconnect_attempts = 0;
                    this.failed_receives = 0;
                    this.last_update_sent = 0;
                    this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    this.socket.Blocking = false;
                    this.main.DisplayServerText("Redirecting, please wait...");
                    this.cObj = new ChannelObject();
                    this.cObj.ip = redirect_ip;
                    this.cObj.port = redirect_port;
                    this.cObj.name = redirect_name;
                    this.OnRedirected(redirect_name, this.tab_ident);

                    try
                    {
                        this.socket.Connect(new IPEndPoint(this.cObj.ip, this.cObj.port));
                    }
                    catch { }

                    break;

                case 9: // avatar
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        bool currently_has_avatar = user.avatar_data != null;

                        if (packet.Remaining() > 10)
                        {
                            user.UpdateAvatar(packet.ReadBytes());
                            this.main.UserListUpdateUser(user, true, true, currently_has_avatar);
                        }
                        else
                        {
                            user.avatar = null;
                            user.avatar_data = null;
                            this.main.UserListUpdateUser(user, true, false, currently_has_avatar);
                        }
                    }

                    break;

                case 10: // public
                    name = packet.ReadString();
                    text = packet.ReadString();

                    //       this.main.DisplayAnnounceText("-> " + BitConverter.ToString(Encoding.UTF8.GetBytes(text)));

                    if (text.Length > 250)
                        text = text.Substring(0, 250);

                    user = this.FindUserByName(name);

                    if (user != null)
                        if (Ignores.IsIgnored(user))
                            return;

                    if (user == null || !Settings.receive_custom_fonts)
                        this.main.DisplayPublicText(name, text);
                    else
                        this.main.DisplayPublicText(name, text, user.font, this.supports_custom_emotes ? user.custom_emotes.ToArray() : null);

                    if (name != this.my_username)
                    {
                        if (user != null)
                        {
                            String[] event_results = ChatEvents.GetEvents(this.cObj, user, "OnText", text);

                            foreach (String str in event_results)
                            {
                                if (this.FloodCheck(str))
                                    continue;

                                if (str.StartsWith("/me "))
                                    this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                else if (str.StartsWith("/"))
                                    this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                else
                                    this.bytes_out.Enqueue(Packets.TextPacket(str));
                            }
                        }
                    }

                    if (!this.tab_selected)
                        this.MakeIconUnread(this.tab_ident);

                    if (Settings.notify_on)
                    {
                        foreach (String _isn in Settings.notify_msg)
                        {
                            if (text.ToUpper().Contains(_isn.ToUpper()))
                            {
                                this.OnTriggerWordReceived(name + "\0" + this.cObj.name + "\0" + _isn, this.tab_ident);
                                return;
                            }
                        }
                    }

                    break;

                case 11: // emote
                    name = packet.ReadString();
                    text = packet.ReadString();

                    if (text.Length > 250)
                        text = text.Substring(0, 250);

                    user = this.FindUserByName(name);

                    if (user != null)
                        if (Ignores.IsIgnored(user))
                            return;

                    if (user == null || !Settings.receive_custom_fonts)
                        this.main.DisplayEmoteText(name, text);
                    else
                        this.main.DisplayEmoteText(name, text, user.font, this.supports_custom_emotes ? user.custom_emotes.ToArray() : null);

                    if (name != this.my_username)
                    {
                        if (user != null)
                        {
                            String[] event_results = ChatEvents.GetEvents(this.cObj, user, "OnEmote", text);

                            foreach (String str in event_results)
                            {
                                if (this.FloodCheck(str))
                                    continue;

                                if (str.StartsWith("/me "))
                                    this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                else if (str.StartsWith("/"))
                                    this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                else
                                    this.bytes_out.Enqueue(Packets.TextPacket(str));
                            }
                        }
                    }

                    if (!this.tab_selected)
                        this.MakeIconUnread(this.tab_ident);

                    if (Settings.notify_on)
                    {
                        foreach (String _isn in Settings.notify_msg)
                        {
                            if (text.ToUpper().Contains(_isn.ToUpper()))
                            {
                                this.OnTriggerWordReceived(name + "\0" + this.cObj.name + "\0" + _isn, this.tab_ident);
                                return;
                            }
                        }
                    }

                    break;

                case 13: // personal message
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        if (packet.Remaining() > 1)
                        {
                            if (packet.PeekByte() == 7) // song
                            {
                                packet.SkipByte();
                                text = packet.ReadString();

                                if (text.Length > 150)
                                    text = text.Substring(0, 150);

                                user.is_song = true;
                                user.personal_message = text;
                            }
                            else
                            {
                                text = Helpers.FormatAresColorCodes(packet.ReadString());
                                text = text.Replace("\x0006", "");
                                text = text.Replace("\x0007", "");
                                text = text.Replace("\x0009", "");
                                text = text.Trim();

                                if (text.Length > 150)
                                    text = text.Substring(0, 150);

                                user.is_song = false;
                                user.personal_message = text;
                            }
                        }
                        else user.personal_message = "";

                        this.main.UserListUpdateUser(user, false, false, false);
                    }

                    break;

                case 20: // join
                    user = new UserObject();
                    user.files = packet.ReadInt16();
                    user.speed = packet.ReadInt32();
                    user.externalIp = packet.ReadIP();
                    user.dcPort = packet.ReadInt16();
                    user.nodeIp = packet.ReadIP();
                    user.nodePort = packet.ReadInt16();
                    user.pm_enc = user.nodePort == 65535;
                    packet.SkipByte();
                    user.name = packet.ReadString();
                    user.me = (user.name == this.my_username);
                    user.localIp = packet.ReadIP();
                    user.browse = packet.ReadByte() == 1;
                    user.level = packet.ReadByte();

                    if (packet.Remaining() > 3)
                    {
                        user.age = packet.ReadByte();
                        user.sex = packet.ReadByte();
                        user.country = packet.ReadByte();
                        user.countryname = Helpers.CountryCodeToString(user.country);
                        user.region = packet.ReadString();
                    }

                    if (this.FindUserByName(user.name) == null)
                    {
                        this.users.Add(user);
                        this.main.DisplayJoinText(user);
                        this.main.UserListAdd(user);
                        this.main.UserListUpdateUserCount();

                        if (user.name != this.my_username)
                        {
                            String[] event_results = ChatEvents.GetEvents(this.cObj, user, "OnJoin", String.Empty);

                            foreach (String str in event_results)
                            {
                                if (this.FloodCheck(str))
                                    continue;

                                if (str.StartsWith("/me "))
                                    this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                else if (str.StartsWith("/"))
                                    this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                else
                                    this.bytes_out.Enqueue(Packets.TextPacket(str));
                            }
                        }
                    }

                    break;

                case 22: // part
                    name = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (user != null)
                    {
                        this.main.DisplayPartText(user);
                        this.main.UserListRemove(user);
                        this.main.UserListUpdateUserCount();
                        this.RemoveUser(user.name);
                        this.UpdateWhoIsWriting();

                        if (user.name != this.my_username)
                        {
                            String[] event_results = ChatEvents.GetEvents(this.cObj, user, "OnPart", String.Empty);

                            foreach (String str in event_results)
                            {
                                if (this.FloodCheck(str))
                                    continue;

                                if (str.StartsWith("/me "))
                                    this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                else if (str.StartsWith("/"))
                                    this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                else
                                    this.bytes_out.Enqueue(Packets.TextPacket(str));
                            }
                        }

                        if (user.writing)
                            this.UpdateWhoIsWriting();
                    }

                    break;

                case 25: // pm
                    name = packet.ReadString();
                    text = packet.ReadString();
                    user = this.FindUserByName(name);

                    if (Settings.receive_pm)
                    {
                        if (user != null)
                        {
                            if (Ignores.IsIgnored(user))
                            {
                                if (!user.told_that_they_are_ignored)
                                {
                                    user.told_that_they_are_ignored = true;
                                    this.bytes_out.Enqueue(Packets.PMPacket(name, " you have been permanently ignored by " + this.my_username));
                                }

                                return;
                            }
                        }

                        this.OnPMReceived(name, text);

                        if (name != this.my_username)
                        {
                            user = this.FindUserByName(name);

                            if (user != null)
                            {
                                String[] event_results = ChatEvents.GetEvents(this.cObj, user, "OnPM", text);

                                foreach (String str in event_results)
                                {
                                    if (this.FloodCheck(str))
                                        continue;

                                    if (str.StartsWith("/me "))
                                        this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                    else if (str.StartsWith("/"))
                                        this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                    else
                                        this.bytes_out.Enqueue(Packets.TextPacket(str));
                                }
                            }
                        }

                        if (!this.tab_selected)
                            this.MakeIconUnread(this.tab_ident);
                    }
                    else
                    {
                        if (user != null)
                        {
                            if (!user.told_that_pm_is_disabled)
                            {
                                user.told_that_pm_is_disabled = true;
                                this.bytes_out.Enqueue(Packets.PMPacket(name, "private messages are disabled for " + this.my_username));
                            }
                        }
                    }

                    break;

                case 26: // pm ignored
                    name = packet.ReadString();

                    foreach (PMTabPage _pm_ig_page in this.pm_chat)
                    {
                        if (_pm_ig_page != null)
                        {
                            if (_pm_ig_page.username == name)
                            {
                                _pm_ig_page.SetAnnounceText("You are being ignored");
                            }
                        }
                    }

                    break;

                case 27: // pm offline
                    name = packet.ReadString();

                    foreach (PMTabPage _pm_off_page in this.pm_chat)
                    {
                        if (_pm_off_page != null)
                        {
                            if (_pm_off_page.username == name)
                            {
                                _pm_off_page.SetAnnounceText("User is offline");
                            }
                        }
                    }

                    break;

                case 30: // userlist item
                    user = new UserObject();
                    user.files = packet.ReadInt16();
                    user.speed = packet.ReadInt32();
                    user.externalIp = packet.ReadIP();
                    user.dcPort = packet.ReadInt16();
                    user.nodeIp = packet.ReadIP();
                    user.nodePort = packet.ReadInt16();
                    user.pm_enc = user.nodePort == 65535;
                    packet.SkipByte();
                    user.name = packet.ReadString();
                    user.me = (user.name == this.my_username);
                    user.localIp = packet.ReadIP();
                    user.browse = packet.ReadByte() == 1;
                    user.level = packet.ReadByte();

                    if (packet.Remaining() > 3)
                    {
                        user.age = packet.ReadByte();
                        user.sex = packet.ReadByte();
                        user.country = packet.ReadByte();
                        user.countryname = Helpers.CountryCodeToString(user.country);
                        user.region = packet.ReadString();
                    }

                    if (this.FindUserByName(user.name) == null)
                    {
                        this.users.Add(user);
                        this.main.UserListAdd(user);
                    }

                    break;

                case 31: // topic update
                    text = packet.ReadString();
                    this.main.SetTopic(text);
                    break;

                case 32: // topic first
                    text = packet.ReadString();
                    this.main.SetTopicFirst(text);

                    if (!this.got_topic)
                    {
                        this.got_topic = true;
                        this.cObj.topic = text;
                        this.OnTopicUpdated(cObj);
                    }

                    break;

                case 35: // userlist end
                    this.main.UserListUpdateMode(false);
                    this.main.UserListUpdateUserCount();

                    if (Settings.my_status != Settings.OnlineStatus.online)
                    {
                        this.bytes_out.Enqueue(Packets.OnlineStatusPacket());
                        user = this.FindUserByName(this.my_username);

                        if (user != null)
                        {
                            user.status = Settings.my_status;
                            this.main.UserListUpdateUser(user, false, false, false);
                        }
                    }

                    if (!this.already_logged_in)
                    {
                        this.already_logged_in = true;
                        user = this.FindUserByName(this.my_username);

                        if (user != null)
                        {
                            String[] event_results = ChatEvents.GetEvents(this.cObj, null, "OnConnect", String.Empty);

                            foreach (String str in event_results)
                            {
                                if (this.FloodCheck(str))
                                    continue;

                                if (str.StartsWith("/me "))
                                    this.bytes_out.Enqueue(Packets.EmotePacket(str.Substring(4)));
                                else if (str.StartsWith("/"))
                                    this.bytes_out.Enqueue(Packets.CommandPacket(str.Substring(1)));
                                else
                                    this.bytes_out.Enqueue(Packets.TextPacket(str));
                            }
                        }
                    }

                    break;

                case 44: // announce
                    text = packet.ReadString();

                    if (!compressed_packet) // motd may send full lengthed packets
                        if (text.Length > 512)
                            text = text.Substring(0, 512);

                    this.main.DisplayAnnounceText(text);

                    if (Settings.notify_on && !compressed_packet)
                    {
                        foreach (String _isn in Settings.notify_msg)
                        {
                            if (text.ToUpper().Contains(_isn.ToUpper()))
                            {
                                this.OnTriggerWordReceived("\0" + this.cObj.name + "\0" + _isn, this.tab_ident);
                                return;
                            }
                        }
                    }

                    break;

                case 53: // file browse end
                    ushort _f_end = packet.ReadInt16();

                    foreach (FilesTabPage _f in this.file_chat)
                    {
                        if (_f != null)
                        {
                            if (_f.session_ident == _f_end)
                            {
                                _f.OnFileBrowseComplete();
                                return;
                            }
                        }
                    }

                    break;

                case 54: // file browse error
                    ushort _f_fail = packet.ReadInt16();

                    foreach (FilesTabPage _f in this.file_chat)
                    {
                        if (_f != null)
                        {
                            if (_f.session_ident == _f_fail)
                            {
                                _f.OnFileBrowseFailed();
                                return;
                            }
                        }
                    }

                    break;

                case 55: // file item
                    ushort _f_session_id = packet.ReadInt16();
                    byte _f_type = packet.ReadByte();
                    ReceivedBrowseItem _f_name = Helpers.ParseFileData(packet.ReadBytes(), _f_type);

                    if (_f_name == null)
                        return;

                    foreach (FilesTabPage _f in this.file_chat)
                    {
                        if (_f != null)
                        {
                            if (_f.session_ident == _f_session_id)
                            {
                                switch (_f_type)
                                {
                                    case 1:
                                        _f.audio.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Audio", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 0));
                                        break;

                                    case 3:
                                        _f.software.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Software", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 1));
                                        break;

                                    case 5:
                                        _f.video.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Video", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 2));
                                        break;

                                    case 6:
                                        _f.document.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Document", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 3));
                                        break;

                                    case 7:
                                        _f.image.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Image", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 4));
                                        break;

                                    default:
                                        _f.other.Add(new ListViewItem(new String[] { _f_name.Title, _f_name.Artist, "Other", _f_name.Category, _f_name.FileSizeString, _f_name.FileName }, 5));
                                        break;
                                }

                                _f.UpdateFilesSoFar();
                                return;
                            }
                        }
                    }

                    break;

                case 56: // file browse start
                    ushort _f_start_id = packet.ReadInt16();
                    ushort _f_start_count = packet.ReadInt16();

                    foreach (FilesTabPage _f in this.file_chat)
                    {
                        if (_f != null)
                        {
                            if (_f.session_ident == _f_start_id)
                            {
                                _f.predicted_total = _f_start_count;
                                _f.ShowExpectedFileCount(_f_start_count);
                                return;
                            }
                        }
                    }

                    break;

                case 73: // urltag
                    name = packet.ReadString();
                    text = packet.ReadString();
                    this.main.UpdateUrl(name, text);
                    break;

                case 92: // version
                    this.SetVCEnabled(false, false);
                    this.supports_voice_chat = false;
                    text = packet.ReadString();
                    this.is_ninja = Helpers.StripColors(text).ToUpper().Contains("ZZL");
                    this.main.DisplayServerText("Server " + text);
                    packet.SkipBytes(2);

                    if (packet.Remaining() > 0)
                    {
                        this.main.DisplayServerText("Language: " + Helpers.LanguageCodeToString(packet.ReadByte()));
                    }
                    else return;

                    if (packet.Remaining() > 3)
                    {
                        this.cObj.cookie = packet.ReadInt32();

                        if (this.cObj.password.Length > 0)
                            this.bytes_out.Enqueue(Packets.AutoLoginPasswordPacket(this.cObj));
                    }
                    else return;

                    if (packet.Remaining() > 0)
                    {
                        if (Settings.personal_message.Length > 0)
                            this.bytes_out.Enqueue(Packets.PersonalMessagePacket());

                        if (Avatar.avatar_small != null)
                            this.bytes_out.Enqueue(Packets.AvatarPacket());
                    }

                    break;

                case 200: // custom data
                    switch (packet.ReadString())
                    {
                        case "cb0t_pm_msg":
                            name = packet.ReadString();
                            text = Encoding.UTF8.GetString(Helpers.SoftDecrypt(this.my_username, packet.ReadBytes()));
                            List<byte> list = new List<byte>();
                            list.AddRange(Encoding.UTF8.GetBytes(name));
                            list.Add(0);
                            list.AddRange(Encoding.UTF8.GetBytes(text));
                            this.EvalPacket(25, new AresDataPacket(list.ToArray()), time, false);
                            break;

                        case "cb0t_nudge": // nudge packet
                            name = packet.ReadString();

                            try
                            {
                                text = Encoding.Default.GetString(packet.ReadBytes());
                                buffer = Convert.FromBase64String(text);
                                buffer = AresCryptography.d67(buffer, 1488);
                                text = Encoding.UTF8.GetString(buffer);
                            }
                            catch { return; } // badly formed nudge packet

                            if (text.Length == 0) return; // badly formed nudge packet

                            int nudge_packet_type;

                            if (!int.TryParse(text.Substring(0, 1), out nudge_packet_type)) // badly formed budge packet
                            {
                                return;
                            }
                            else
                            {
                                if (nudge_packet_type == 0) // user trying to nudge us
                                {
                                    text = text.Substring(1);

                                    if (text == name) // let user nudge us
                                    {
                                        if (Settings.receive_nudges)
                                        {
                                            if (time > (this.last_nudge + 3))
                                            {
                                                this.last_nudge = time;
                                                this.main.NudgeScreen(name, this.rnd);
                                                this.OnTriggerWordReceived(name + "\0" + this.cObj.name + "\0\x0001", this.tab_ident);
                                            }
                                        }
                                        else
                                        {
                                            this.bytes_out.Enqueue(Packets.NudgeRejectPacket(name));
                                        }
                                    }

                                    return;
                                }
                                else // user rejecting a nudge
                                {
                                    this.main.DisplayAnnounceText("\x000314--- " + name + " has nudge disabled!");
                                    return;
                                }
                            }

                        case "cb0t_scribble_reject":
                            name = packet.ReadString();
                            this.main.DisplayAnnounceText("\x000314--- " + name + " has scribble disabled!");
                            break;

                        case "cb0t_scribble_once":
                            name = packet.ReadString();
                            byte[] scribble_once = packet.ReadBytes();

                            if (Settings.scribble_enabled)
                            {
                                this.main.DisplayScribble(name, scribble_once);

                                if (!this.tab_selected)
                                    this.MakeIconUnread(this.tab_ident);
                            }
                            else
                            {
                                this.bytes_out.Enqueue(Packets.ScribbleRejectedPacket(name));
                            }

                            break;

                        case "cb0t_scribble_first":
                            packet.ReadString();
                            this.scribble_chunks.Clear();
                            this.scribble_chunks.AddRange(packet.ReadBytes());
                            break;

                        case "cb0t_scribble_chunk":
                            packet.ReadString();
                            this.scribble_chunks.AddRange(packet.ReadBytes());
                            break;

                        case "cb0t_scribble_last":
                            name = packet.ReadString();
                            this.scribble_chunks.AddRange(packet.ReadBytes());

                            if (Settings.scribble_enabled)
                            {
                                this.main.DisplayScribble(name, this.scribble_chunks.ToArray());

                                if (!this.tab_selected)
                                    this.MakeIconUnread(this.tab_ident);
                            }
                            else
                            {
                                this.bytes_out.Enqueue(Packets.ScribbleRejectedPacket(name));
                            }

                            break;

                        case "cb0t_lag_check":
                            name = packet.ReadString();
                            TimeSpan ts = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
                            ulong lag_check_start = packet.ReadInt64();
                            ulong lag_check_stop = (ulong)ts.TotalMilliseconds;
                            this.bytes_out.Enqueue(Packets.TextPacket("Lag test: " + (lag_check_stop - lag_check_start) + " milliseconds"));
                            break;

                        case "cb0t_latency_check":
                            name = packet.ReadString();
                            TimeSpan ts1 = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0));
                            ulong lag_check_start1 = packet.ReadInt64();
                            ulong lag_check_stop1 = (ulong)ts1.TotalMilliseconds;
                            this.main.UpdateLatency(lag_check_stop1 - lag_check_start1);
                            break;

                        case "cb0t_writing":
                            name = packet.ReadString();
                            user = this.FindUserByName(name);

                            if (user != null)
                            {
                                user.writing = packet.ReadByte() == 2;
                                this.main.UserListUpdateUser(user, false, false, false);
                                this.UpdateWhoIsWriting();
                            }

                            break;

                        case "cb0t_online_status":
                            name = packet.ReadString();
                            user = this.FindUserByName(name);

                            if (user != null)
                            {
                                user.status = (Settings.OnlineStatus)packet.ReadByte();
                                this.main.UserListUpdateUser(user, false, false, false);
                            }

                            break;
                    }

                    break;

                case 250:
                    ushort adf_len = packet.ReadInt16();
                    byte adf_id = packet.ReadByte();
                    byte[] adf_data = packet.ReadBytes();

                    if (adf_data.Length == adf_len)
                        this.EvalAdvancedFeaturesPacket(adf_id, new AresDataPacket(adf_data));

                    break;
            }
        }

        private bool vc_compatible = false;

        private void SetVCEnabled(bool yes, bool high_quality)
        {
            this.vc_compatible = yes;

            foreach (TabPage t in this.tab_control.TabPages)
            {
                if (((String)t.Tag) == "pm")
                {
                    PMTabPage p = (PMTabPage)t;
                    p.SetVCEnabledButton(yes, high_quality);
                }
                else if (((String)t.Tag) == "chat")
                {
                    ChatTabPage p = (ChatTabPage)t;
                    p.SetVCEnabledButton(yes, high_quality);
                }
            }
        }

        private UserObject FindUserByName(String name)
        {
            try
            {
                for (int i = 0; i < this.users.Count; i++)
                    if (this.users[i].name == name)
                        return this.users[i];
            }
            catch { }

            return null;
        }

        private void RemoveUser(String name)
        {
            for (int i = 0; i < this.users.Count; i++)
            {
                if (this.users[i].name == name)
                {
                    if (this.users[i].avatar != null)
                        this.users[i].avatar.Dispose();

                    this.users.RemoveAt(i);
                    return;
                }
            }
        }

        public bool SameAs(ChannelObject cObj)
        {
            return this.cObj.Equals(cObj);
        }

        private int NextFreePMTab()
        {
            for (int i = 0; i < this.pm_chat.Length; i++)
                if (this.pm_chat[i] == null)
                    return i;

            return -1;
        }

        private int NextFreeFilesTab()
        {
            for (int i = 0; i < this.file_chat.Length; i++)
                if (this.file_chat[i] == null)
                    return i;

            return -1;
        }




    }
}
