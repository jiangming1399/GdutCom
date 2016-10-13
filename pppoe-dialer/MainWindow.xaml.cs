using System;
using System.Linq;
using System.Windows;
using DotRas;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Configuration;

namespace pppoe_dialer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        private Socket client;
        int pppoeCount = 1;
        byte PPPOE_FLAG = 0x2a;
        Thread authThread;
        IPEndPoint hostipe;
        Configuration cfa;

        public MainWindow()
        {
            InitializeComponent();
            CreateConnect("PPPoEDialer");
            hangup.IsEnabled = false;
            initSocket();
            cfa = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            readConfig();
            if ((bool)autologin.IsChecked && dial.IsEnabled)
                dial_Click(null,null);
        }

        public void readConfig()
        {
            try
            {
                tb_username.Text = cfa.AppSettings.Settings["username"].Value;
                pb_password.Password = cfa.AppSettings.Settings["password"].Value;
                if (cfa.AppSettings.Settings["autoLogin"].Value == "Y")
                    autologin.IsChecked = true;
                if (cfa.AppSettings.Settings["remember"].Value == "Y")
                    remenber.IsChecked = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }

        public void saveConfig()
        {
            if (cfa.AppSettings.Settings.Count > 0)
            {
                try
                {
                    cfa.AppSettings.Settings["username"].Value = tb_username.Text;
                    if ((bool)remenber.IsChecked)
                        cfa.AppSettings.Settings["password"].Value = pb_password.Password;
                    else
                        cfa.AppSettings.Settings["password"].Value = "";
                    cfa.AppSettings.Settings["autoLogin"].Value = (bool)autologin.IsChecked ? "Y" : "N";
                    cfa.AppSettings.Settings["remember"].Value = (bool)remenber.IsChecked ? "Y" : "N";
                    cfa.Save();
                    ConfigurationManager.RefreshSection("appSettings");// 刷新命名节，在下次检索它时将从磁盘重新读取它。记住应用程序要刷新节点
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            else
            {
                try
                {
                    cfa.AppSettings.Settings.Add("username", tb_username.Text);
                    if ((bool)remenber.IsChecked)
                        cfa.AppSettings.Settings.Add("password", pb_password.Password);
                    else
                        cfa.AppSettings.Settings.Add("password", "");
                    cfa.AppSettings.Settings.Add("autoLogin", (bool)autologin.IsChecked ? "Y" : "N");
                    cfa.AppSettings.Settings.Add("remember", (bool)remenber.IsChecked ? "Y" : "N");
                    cfa.Save();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }


        }

        public void CreateConnect(string ConnectName)
        {
            RasDialer dialer = new RasDialer();
            RasPhoneBook book = new RasPhoneBook();
            try
            {
                book.Open(RasPhoneBook.GetPhoneBookPath(RasPhoneBookType.User));
                if (book.Entries.Contains(ConnectName))
                {
                    book.Entries[ConnectName].PhoneNumber = " ";
                    book.Entries[ConnectName].Update();
                }
                else
                {
                    System.Collections.ObjectModel.ReadOnlyCollection<RasDevice> readOnlyCollection = RasDevice.GetDevices();
                    RasDevice device = RasDevice.GetDevices().Where(o => o.DeviceType == RasDeviceType.PPPoE).First();
                    RasEntry entry = RasEntry.CreateBroadbandEntry(ConnectName, device);
                    entry.PhoneNumber = " ";
                    book.Entries.Add(entry);
                }
            }
            catch (Exception e)
            {
                lb_status.Content = "创建PPPoE连接失败";
                Console.WriteLine(e.Message);
            }
        }
        private void remenber_Click(object sender, RoutedEventArgs e)
        {
            if (!(bool)remenber.IsChecked && (bool)autologin.IsChecked)
                autologin.IsChecked = false;
        }

        private void autologin_Click(object sender, RoutedEventArgs e)
        {
            if (!(bool)remenber.IsChecked && (bool)autologin.IsChecked)
                remenber.IsChecked = true;
        }

        private void dial_Click(object sender, RoutedEventArgs e)
        {   
            //自动添加\r\n
            string username = "\r\n" + tb_username.Text.Replace("\r", "").Replace("\n", "");
            string password = pb_password.Password.ToString();
            saveConfig();

            Thread threadHand1 = new Thread(() =>
            {
                dialme(username, password);
            });
            threadHand1.Start();
        }

        private void hangup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Collections.ObjectModel.ReadOnlyCollection<RasConnection> conList = RasConnection.GetActiveConnections();
                foreach (RasConnection con in conList)
                {
                    con.HangUp();
                }
                System.Threading.Thread.Sleep(1000);
                lb_status.Content = "注销成功";
                lb_message.Content = "已注销";
                dial.IsEnabled = true;
                hangup.IsEnabled = false;
                authThread.Abort();
            }
            catch (Exception)
            {
                lb_status.Content = "注销出现异常";
            }
        }

        private void FollowMe(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            hangup_Click(null, null);
        }

        private void dialme(string username, string password)
        {
            //auth();
            try
            {
                RasDialer dialer = new RasDialer();
                dialer.EntryName = "PPPoEDialer";
                dialer.PhoneNumber = " ";
                dialer.AllowUseStoredCredentials = true;
                dialer.PhoneBookPath = RasPhoneBook.GetPhoneBookPath(RasPhoneBookType.User);
                dialer.Credentials = new System.Net.NetworkCredential(username, password);
                dialer.Timeout = 500;
                RasHandle myras = dialer.Dial();
                while (myras.IsInvalid)
                {
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_status.Content = "拨号失败";
                    }));
                }
                if (!myras.IsInvalid)
                {
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_status.Content = "PPPOE拨号成功! ";
                    }));
                    RasConnection conn = RasConnection.GetActiveConnectionByHandle(myras);
                    RasIPInfo ipaddr = (RasIPInfo)conn.GetProjectionInfo(RasProjectionType.IP);
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_message.Content = "获得IP： " + ipaddr.IPAddress.ToString();
                        dial.IsEnabled = false;
                        hangup.IsEnabled = true;
                    }));
                    /*
                    ThreadStart threadStart = new ThreadStart(auth);
                    authThread = new Thread(threadStart);
                    authThread.Start();
                    */
                    auth();
                }
            }
            catch (Exception)
            {
                this.Dispatcher.Invoke(new Action(() => {
                    lb_status.Content = "拨号出现异常";
                }));
                
            }
        }

        private void initSocket()
        {
            int port = 61440;
            string localhost = "0.0.0.0";
            string host = "10.0.3.2";

            IPEndPoint ipe = new IPEndPoint(IPAddress.Parse(localhost), port);
            hostipe = new IPEndPoint(IPAddress.Parse(host), port);

            try
            {
                client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                client.ReceiveTimeout = 3000;
                //client.Connect(ipe);
                client.Bind(ipe);
                Console.WriteLine("success");
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException:{0}", e);
                lb_status.Content = "端口绑定异常";
                lb_message.Content = "请关闭官方客户端并重试";
                dial.IsEnabled = false;
            }

            //auth();
        }

        public int auth()
        {
            while (true)
            {
                if (heartbeat() == 0)
                    return 0;
                Thread.Sleep(20000);
            }
        }

        private int heartbeat()
        {
            while (true)
            {
                byte[] packet = generateStartPacket((byte)pppoeCount);
                this.Dispatcher.Invoke(new Action(() => {
                    lb_status.Content = "发送握手包";
                }));
                
                Console.WriteLine("pppoe: send challenge request");
                //sent_packet(packet, packetSize);

                try
                {
                    client.SendTo(packet, packet.Length, SocketFlags.None, hostipe);
                }
                catch (SocketException e)
                {
                    Console.WriteLine("SocketException:{0}", e);
                }



                byte[] recvBuff = recv_packet();
                if (recvBuff == null)
                {
                    
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_status.Content = "内部错误，重新开始拨号";
                        //lb_message.Content = "";
                    }));
                    return 1;
                    //continue;
                }
                Console.WriteLine("pppoe: received challenge response");
                this.Dispatcher.Invoke(new Action(() => {
                    lb_status.Content = "收到握手包";
                }));
                
                pppoeCount++;

                byte[] seed = new byte[4], sip = new byte[4];

                for (int i = 0; i < 4; i++)
                {
                    seed[i] = recvBuff[8 + i];
                    sip[i] = recvBuff[12 + i];
                }

                byte[] hbPacket;
                if (pppoeCount > 2)
                    hbPacket = generateHeartbeatPacket((byte)pppoeCount, sip, seed, false);
                else
                    hbPacket = generateHeartbeatPacket((byte)pppoeCount, sip, seed, true);
                try
                {
                    client.SendTo(hbPacket, hbPacket.Length, SocketFlags.None, hostipe);
                }
                catch (SocketException e)
                {
                    Console.WriteLine("SocketException:{0}", e.Message);
                }
                this.Dispatcher.Invoke(new Action(() => {
                    lb_status.Content = "第" + pppoeCount + "次发送心跳包";
                }));
                
                Console.WriteLine("pppoe: send heartbeat request");
                //sent_packet(packet, packetSize);

                if (recv_packet() == null)
                {
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_message.Content = "拨号失败，重新拨号中";
                        lb_status.Content = "crc = "+ (seed[0] % 4).ToString("x");
                    }));
                    
                    Console.WriteLine("pppoe: heartbeat response failed, retry");
                    Console.WriteLine("pppoe: reset idx to 0x01\n");
                    pppoeCount = 1;
                    Thread.Sleep(1000);
                    return 0;
                    //continue;
                }
                else
                {
                    Console.WriteLine("pppoe: received heartbeat response");
                    this.Dispatcher.Invoke(new Action(() => {
                        lb_message.Content = "拨号成功";
                    }));
                    //return 1;

                    break;
                }
            }
            return 1;
        }

        int sent_packet(byte[] data)
        {
            int result = client.SendTo(data, data.Length, SocketFlags.None, hostipe);
            if (result < 0)
                Console.WriteLine("Send packet error");
            return result;
        }

        byte[] recv_packet()
        {
            byte[] recBytes = new byte[4096];
            //unsigned char realStr[1024] = { 0x00 };
            EndPoint remote = (EndPoint)hostipe;
            while (true)
            {
                //result = recvfrom(client_fd, recvBuff, 1024, 0, NULL, NULL);

                try
                {
                    int bytes = client.ReceiveFrom(recBytes, 0, recBytes.Length, SocketFlags.None, ref remote);
                }
                catch (SocketException e)
                {
                    Console.WriteLine("SocketException:{0}: {1}", e.ErrorCode, e.Message);
                    return null;
                }

                if (recBytes[0] == 0x4d)
                {
                    //cut_char(recvBuff, (unsigned char *) & realStr, 3);
                    Console.WriteLine("Message: {0}", recBytes);
                    continue;
                }
                else return recBytes;
            }
        }

        byte[] generateStartPacket(byte count)
        {
            int index = 0;
            byte[] packet = new byte[8];
            packet[index++] = 0x07;
            packet[index++] = (byte)count;
            packet[index++] = 0x08;
            packet[index++] = 0x00;
            packet[index++] = 0x01;
            packet[index++] = 0x00;
            packet[index++] = 0x00;
            packet[index++] = 0x00;

            return packet;
        }

        byte[] generateHeartbeatPacket(byte count, byte[] sip, byte[] seed, bool isFirst)
        {
            int index = 0;
            byte[] packet = new byte[100];
            packet[index++] = 0x07; //header
            packet[index++] = count; //id
            packet[index++] = 0x60; //length
            packet[index++] = 0x00;
            packet[index++] = 0x03; //type
            packet[index++] = 0x00; //uid length
            for (int i = 0; i < 6; i++)
            {
                packet[index++] = 0x00; //mac
            }
            for (int i = 0; i < 4; i++)
            {
                packet[index++] = sip[i]; //serverip
            }

            //Option
            packet[index++] = 0x00;

            if (isFirst)
                packet[index++] = 0x62;
            else
                packet[index++] = 0x63;

            packet[index++] = 0x00;
            packet[index++] = PPPOE_FLAG;

            //ChallengeSeed
            for (int i = 0; i < 4; i++)
            {
                packet[index++] = seed[i];
            }

            ///计算CRC
            byte[] crc = new byte[8];

            int encryptMode = seed[0] % 4;
            crc = genCRC(seed, encryptMode);

            ///填充CRC
            for (int i = 0; i < 8; i++)
            {
                packet[index++] = crc[i];
            }
            ///填充IP
            for (int i = 0; i < 68; i++)
            {
                //0x00 * 4 CRC
                //0x00 * 16 ip * 4
                packet[index++] = 0x00;
            }
            return packet;
        }

        /*CRC*/
        byte[] genCRC(byte[] data, int type)
        {
            int index = 0;
            byte[] result = new byte[8];
            //byte[] mdResult = new byte[20];

            switch (type)
            {
                case 0: //default
                        //20000711
                    result[index++] = 0xC7;
                    result[index++] = 0x2F;
                    result[index++] = 0x31;
                    result[index++] = 0x01;
                    //126
                    result[index++] = 0x7E;
                    result[index++] = 0x00;
                    result[index++] = 0x00;
                    result[index++] = 0x00;
                    break;
                case 1: //md5
                    MD5 md5 = new MD5CryptoServiceProvider();
                    byte[] mdResult = md5.ComputeHash(data);

                    result[index++] = mdResult[2];
                    result[index++] = mdResult[3];
                    result[index++] = mdResult[8];
                    result[index++] = mdResult[9];
                    result[index++] = mdResult[5];
                    result[index++] = mdResult[6];
                    result[index++] = mdResult[13];
                    result[index++] = mdResult[14];
                    break;
                case 2: //md4
                    MD4 md4 = new MD4();
                    byte[] md4Result = md4.ComputeHash(data);

                    result[index++] = md4Result[1];
                    result[index++] = md4Result[2];
                    result[index++] = md4Result[8];
                    result[index++] = md4Result[9];
                    result[index++] = md4Result[4];
                    result[index++] = md4Result[5];
                    result[index++] = md4Result[11];
                    result[index++] = md4Result[12];
                    break;
                case 3: //sha1
                    SHA1 SHA1 = new SHA1CryptoServiceProvider();
                    byte[] shaResult = SHA1.ComputeHash(data);

                    result[index++] = shaResult[2];
                    result[index++] = shaResult[3];
                    result[index++] = shaResult[9];
                    result[index++] = shaResult[10];
                    result[index++] = shaResult[5];
                    result[index++] = shaResult[6];
                    result[index++] = shaResult[15];
                    result[index++] = shaResult[16];
                    break;
                default:
                    //printf("Invalid CRC type %d\n", type);
                    break;
            }
            return result;
        }


    }
    public class MD4 : HashAlgorithm
    {
        private uint _a;
        private uint _b;
        private uint _c;
        private uint _d;
        private uint[] _x;
        private int _bytesProcessed;

        public MD4()
        {
            _x = new uint[16];

            Initialize();
        }

        public override void Initialize()
        {
            _a = 0x67452301;
            _b = 0xefcdab89;
            _c = 0x98badcfe;
            _d = 0x10325476;

            _bytesProcessed = 0;
        }

        protected override void HashCore(byte[] array, int offset, int length)
        {
            ProcessMessage(Bytes(array, offset, length));
        }

        protected override byte[] HashFinal()
        {
            try
            {
                ProcessMessage(Padding());

                return new[] { _a, _b, _c, _d }.SelectMany(word => Bytes(word)).ToArray();
            }
            finally
            {
                Initialize();
            }
        }

        private void ProcessMessage(IEnumerable<byte> bytes)
        {
            foreach (byte b in bytes)
            {
                int c = _bytesProcessed & 63;
                int i = c >> 2;
                int s = (c & 3) << 3;

                _x[i] = (_x[i] & ~((uint)255 << s)) | ((uint)b << s);

                if (c == 63)
                {
                    Process16WordBlock();
                }

                _bytesProcessed++;
            }
        }

        private static IEnumerable<byte> Bytes(byte[] bytes, int offset, int length)
        {
            for (int i = offset; i < length; i++)
            {
                yield return bytes[i];
            }
        }

        private IEnumerable<byte> Bytes(uint word)
        {
            yield return (byte)(word & 255);
            yield return (byte)((word >> 8) & 255);
            yield return (byte)((word >> 16) & 255);
            yield return (byte)((word >> 24) & 255);
        }

        private IEnumerable<byte> Repeat(byte value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return value;
            }
        }

        private IEnumerable<byte> Padding()
        {
            return Repeat(128, 1)
               .Concat(Repeat(0, ((_bytesProcessed + 8) & 0x7fffffc0) + 55 - _bytesProcessed))
               .Concat(Bytes((uint)_bytesProcessed << 3))
               .Concat(Repeat(0, 4));
        }

        private void Process16WordBlock()
        {
            uint aa = _a;
            uint bb = _b;
            uint cc = _c;
            uint dd = _d;

            foreach (int k in new[] { 0, 4, 8, 12 })
            {
                aa = Round1Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round1Operation(dd, aa, bb, cc, _x[k + 1], 7);
                cc = Round1Operation(cc, dd, aa, bb, _x[k + 2], 11);
                bb = Round1Operation(bb, cc, dd, aa, _x[k + 3], 19);
            }

            foreach (int k in new[] { 0, 1, 2, 3 })
            {
                aa = Round2Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round2Operation(dd, aa, bb, cc, _x[k + 4], 5);
                cc = Round2Operation(cc, dd, aa, bb, _x[k + 8], 9);
                bb = Round2Operation(bb, cc, dd, aa, _x[k + 12], 13);
            }

            foreach (int k in new[] { 0, 2, 1, 3 })
            {
                aa = Round3Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round3Operation(dd, aa, bb, cc, _x[k + 8], 9);
                cc = Round3Operation(cc, dd, aa, bb, _x[k + 4], 11);
                bb = Round3Operation(bb, cc, dd, aa, _x[k + 12], 15);
            }

            unchecked
            {
                _a += aa;
                _b += bb;
                _c += cc;
                _d += dd;
            }
        }

        private static uint ROL(uint value, int numberOfBits)
        {
            return (value << numberOfBits) | (value >> (32 - numberOfBits));
        }

        private static uint Round1Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + ((b & c) | (~b & d)) + xk, s);
            }
        }

        private static uint Round2Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + ((b & c) | (b & d) | (c & d)) + xk + 0x5a827999, s);
            }
        }

        private static uint Round3Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + (b ^ c ^ d) + xk + 0x6ed9eba1, s);
            }
        }
    }
}
