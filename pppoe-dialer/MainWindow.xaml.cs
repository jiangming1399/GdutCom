using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Deployment.Application;
using DotRas;
using System.Threading;
using System.Configuration;

using log4net;


namespace pppoe_dialer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        drcom drcom;
        Thread PPPoEThread;
        Configuration cfa;

        private NotifyIcon trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            LogHelper.SetConfig();
            LogHelper.WriteLog("初始化组件");

            CreateConnect("PPPoEDialer");
            hangup.IsEnabled = false;

            try
            {
                lb_status.Content = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }
            catch
            {

            }
            
            try
            {
                trayIcon = new NotifyIcon();
                trayIcon.Text = "GdutCom";

                trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
                trayIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(notifyIcon_MouseClick);
                trayIcon.Visible = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                LogHelper.WriteLog(e.Message, e);
            }

            drcom = new drcom(new drcom.labelCallback(changeLabel));
            drcom.initSocket();

            cfa = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            readConfig();
            if ((bool)autologin.IsChecked && dial.IsEnabled)
                dial_Click(null, null);

            drcom.test();
        }

        private void notifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && this.WindowState == WindowState.Minimized)
            {
                this.Show();
                this.Activate();
                this.WindowState = WindowState.Normal;
            }
        }

        private void MetroWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
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

            PPPoEThread = new Thread(() =>
            {
                dialme(username, password);
            });
            PPPoEThread.Start();
        }

        private void hangup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PPPoEThread.Abort();
            }
            catch(Exception exp)
            {
                LogHelper.WriteLog(exp.Message, exp);
            }
            hangupPPPoE();
        }

        private void FollowMe(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            hangup_Click(null, null);
            drcom.exit();
        }

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
        }

        /// <summary>
        /// 拨号
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        private void dialme(string username, string password)
        {
            while (true)
            {
                bool pppoeResult = dialPPPoE(username, password);
                if (!pppoeResult) break;
                drcom.auth();
                hangupPPPoE();
            }
        }

        private bool dialPPPoE(string username, string password)
        {
            try
            {
                LogHelper.WriteLog("PPPoE尝试拨号");
                this.Dispatcher.Invoke(new Action(() =>
                {
                    lb_status.Content = "PPPoE尝试拨号";
                    dial.IsEnabled = false;
                }));

                RasDialer dialer = new RasDialer();
                dialer.EntryName = "PPPoEDialer";
                dialer.PhoneNumber = " ";
                dialer.AllowUseStoredCredentials = true;
                dialer.PhoneBookPath = RasPhoneBook.GetPhoneBookPath(RasPhoneBookType.User);
                dialer.Credentials = new System.Net.NetworkCredential(username, password);
                dialer.Timeout = 500;

                dialer.StateChanged += new EventHandler<StateChangedEventArgs>(onStateChange);

                RasHandle myras = dialer.Dial();
                while (myras.IsInvalid)
                {
                    this.Dispatcher.Invoke(new Action(() =>
                    {
                        LogHelper.WriteLog("拨号失败", null);
                        lb_status.Content = "拨号失败";
                        dial.IsEnabled = true;
                    }));
                }
                if (!myras.IsInvalid)
                {
                    this.Dispatcher.Invoke(new Action(() =>
                    {
                        lb_status.Content = "PPPOE拨号成功! ";
                    }));
                    RasConnection conn = RasConnection.GetActiveConnectionByHandle(myras);
                    RasIPInfo ipaddr = (RasIPInfo)conn.GetProjectionInfo(RasProjectionType.IP);
                    this.Dispatcher.Invoke(new Action(() =>
                    {
                        lb_message.Content = "获得IP： " + ipaddr.IPAddress.ToString();
                        hangup.IsEnabled = true;
                    }));
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                this.Dispatcher.Invoke(new Action(() =>
                {
                    lb_status.Content = e.Message;
                    dial.IsEnabled = true;
                }));

                LogHelper.WriteLog(e.Message, e);
                return false;
            }
        }

        private bool hangupPPPoE()
        {
            LogHelper.WriteLog("尝试注销");
            try
            {
                System.Collections.ObjectModel.ReadOnlyCollection<RasConnection> conList = RasConnection.GetActiveConnections();
                foreach (RasConnection con in conList)
                {
                    con.HangUp();
                }
                System.Threading.Thread.Sleep(1000);
                this.Dispatcher.Invoke(new Action(() =>
                {
                    lb_status.Content = "注销成功";
                    lb_message.Content = "已注销";
                    dial.IsEnabled = true;
                    hangup.IsEnabled = false;
                }));
                return true;
            }
            catch (Exception exc)
            {
                lb_status.Content = "注销出现异常";
                LogHelper.WriteLog("注销出现异常" + exc.Message, exc);
                return false;
            }
        }

        /// <summary>
        /// 修改主界面标签
        /// </summary>
        /// <param name="status"></param>
        /// <param name="message"></param>
        private void changeLabel(string status, string message)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                if (status != null)
                    lb_status.Content = status;
                if (message != null)
                    lb_message.Content = message;
            }));
        }


        /// <summary>
        /// 读取设置
        /// </summary>
        private void readConfig()
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
                LogHelper.WriteLog(e.Message, e);
            }

        }


        /// <summary>
        /// 状态改变调用
        /// </summary>
        /// <param name="a"></param>
        /// <param name="e"></param>
        private void onStateChange(object a, StateChangedEventArgs e)
        {
            Console.WriteLine(e.State);
            LogHelper.WriteLog(e.State.ToString());
            if (e.State == RasConnectionState.Disconnected)
                changeLabel(e.State.ToString(), null);
        }


        /// <summary>
        /// 保存配置
        /// </summary>
        private void saveConfig()
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


        /// <summary>
        /// PPPOE创建新链接
        /// </summary>
        /// <param name="ConnectName"></param>
        private void CreateConnect(string ConnectName)
        {
            try
            {
                RasDialer dialer = new RasDialer();
                RasPhoneBook book = new RasPhoneBook();

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
                LogHelper.WriteLog(e.Message, e);
            }
        }

    }

    /// <summary>
    /// 使用LOG4NET记录日志的功能，在WEB.CONFIG里要配置相应的节点
    /// </summary>
    public class LogHelper
    {
        //log4net日志专用
        public static readonly log4net.ILog loginfo = log4net.LogManager.GetLogger("loginfo");
        public static readonly log4net.ILog logerror = log4net.LogManager.GetLogger("logerror");

        public static void SetConfig()
        {
            log4net.Config.XmlConfigurator.Configure();
        }

        /// <summary>
        /// 普通的文件记录日志
        /// </summary>
        /// <param name="info"></param>
        public static void WriteLog(string info)
        {
            if (loginfo.IsInfoEnabled)
            {
                try
                {
                    loginfo.Info(info);
                    Console.WriteLine(info);
                }
                catch
                {

                }

            }
        }
        /// <summary>
        /// 错误日志
        /// </summary>
        /// <param name="info"></param>
        /// <param name="se"></param>
        public static void WriteLog(string info, Exception se)
        {
            if (logerror.IsErrorEnabled)
            {
                try
                {
                    logerror.Error(info, se);
                }
                catch
                {

                }

            }
        }
    }
}
