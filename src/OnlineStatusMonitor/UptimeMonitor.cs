using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Text;
using System.Timers;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace OnlineStatusMonitor
{
    public partial class UptimeMonitor : Form
    {
        private string _currentIcon = "";
        private bool _isRunning;
        private Timer _iconTimer;
        private Timer _timer;
        private int _totalOutages;
        private DateTime? _lastChanged;
        private TimeSpan _totalTimeOffline;
        private bool _currentlyOnline;

        readonly Color _offlineColour = Color.FromArgb(192, 0, 0);
        readonly Font _offlineFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);
        readonly Color _okColour = Color.FromArgb(0, 192, 0);
        readonly Font _okFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);

        private delegate void ChangeOfStatusDelegate(bool online);
        private delegate void UpdateStatusDelegate();
        private delegate void ChangeIconDelegate(string icon);

        public UptimeMonitor()
        {
            InitializeComponent();
            Reset();
        }

        void UptimeMonitor_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        }

        void notifyIcon_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Refresh();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            RunOrCancel();
        }

        private void RunOrCancel()
        {
            if (_isRunning)
                StopMonitoring();
            else
                StartMonitoring();
        }

        private void StartMonitoring()
        {
            btnStart.Text = "Stop Monitoring";

            Reset();

            StartTimer();

            _isRunning = true;

            Hide();
        }

        private void StopMonitoring()
        {
            btnStart.Text = "Start Monitoring";

            lblStatus.Text = "";
            lblTimer.Text = "";

            StopTimer();

            ShowOnlineIcon();

            LogToTextFile();

            _isRunning = false;
        }

        private void Reset()
        {
            _lastChanged = null;
            _totalTimeOffline = new TimeSpan();
            _currentlyOnline = false;
            _totalOutages = 0;
        }

        private void ShowAsOnline()
        {
            lblStatus.Text = "ONLINE";

            lblStatus.Font = _okFont;
            lblStatus.ForeColor = _okColour;

            lblTimer.Font = _okFont;
            lblTimer.ForeColor = _okColour;

            lblTimer.Text = "0s";
        }

        private void ShowAsOffline()
        {
            lblStatus.Text = "OFFLINE";

            lblStatus.Font = _offlineFont;
            lblStatus.ForeColor = _offlineColour;

            lblTimer.Font = _offlineFont;
            lblTimer.ForeColor = _offlineColour;

            lblTimer.Text = "0s";
        }

        private void ShowOnlineToolTip()
        {
            notifyIcon.ShowBalloonTip(2500, "Online", "Good news! We can talk to " + txtHost.Text, ToolTipIcon.Info);
        }

        private void ShowOfflineToolTip()
        {
            notifyIcon.ShowBalloonTip(2500, "OFFLINE", "Bad times! We can't talk to " + txtHost.Text, ToolTipIcon.Error);
        }

        private void StartTimer()
        {
            ChangeState();

            _timer = new Timer();
            _timer.Elapsed += timer_Elapsed;
            _timer.Interval = 1000;
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer.Enabled)
                _timer.Stop();
        }

        private void UpdateStatus()
        {
            if (lblTimer.InvokeRequired)
            {
                Invoke(new UpdateStatusDelegate(UpdateStatus));
                return;
            }

            if (_currentlyOnline)
                ShowAsOnline();
            else
                ShowAsOffline();

            var timeSinceLastChange = GetSinceLastChange();
            lblTimer.Text = PrettyFormatTimespan(timeSinceLastChange);

            lblOutages.Text = _totalOutages.ToString("#,##;0;0");
            lblDowntime.Text = PrettyFormatTimespan(_totalTimeOffline);
        }

        private void ChangeIcon(string icon)
        {
            if (lblTimer.InvokeRequired)
            {
                Invoke(new ChangeIconDelegate(ChangeIcon), new object[] { icon });
                return;
            }

            SetIcon(icon);
        }

        private void ChangeOfStatus(bool online)
        {
            if (lblTimer.InvokeRequired)
            {
                Invoke(new ChangeOfStatusDelegate(ChangeOfStatus), new object[] { online });
                return;
            }

            if (online)
            {
                ShowOnlineToolTip();
                ShowOnlineIcon();
            }
            else
            {
                ShowOfflineToolTip();
                AnimateOfflineIcon();
            }
        }

        private static string PrettyFormatTimespan(TimeSpan timer)
        {
            var offlineTime = String.Empty;

            if (timer.TotalHours >= 1)
                offlineTime = String.Format("{0:#,##}h {1:#,##}m", timer.TotalHours, timer.Minutes);
            else if (timer.TotalMinutes >= 1)
                offlineTime = String.Format("{0:#,##}m {1:#,##}s", timer.Minutes, timer.Seconds);
            else
                offlineTime = String.Format("{0:#,##}s", timer.Seconds);

            return offlineTime;
        }

        private void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            int averagePing;
            int packetLoss;

            GetNetworkStats(txtHost.Text, 10, 100, out averagePing, out packetLoss);

            RefreshStatusDisplay(packetLoss);

            UpdateStatus();
        }

        private void GetNetworkStats(string host, int pingAmount, int timeout, out int averagePing, out int packetLoss)
        {
            var pingSender = new Ping();
            var options = new PingOptions();

            // string data = "Whhaaaaaaaaaaaaaaaaaaaaaaaaaaa!!";
            var data = "OI! Are we online at the moment?";

            var buffer = Encoding.ASCII.GetBytes(data);

            var failedPings = 0;
            var latencySum = 0;

            for (var i = 0; i < pingAmount; i++)
            {
                var reply = pingSender.Send(host, timeout, buffer, options);

                if (reply == null)
                    continue;

                if (reply.Status != IPStatus.Success)
                    failedPings += 1;
                else
                    latencySum += (int)reply.RoundtripTime;
            }

            averagePing = (latencySum / pingAmount);
            packetLoss = (failedPings / pingAmount) * 100;
        }

        private void RefreshStatusDisplay(int packetLoss)
        {
            var offline = packetLoss > 0;

            if (_currentlyOnline && offline)
            {
                HandleJustGoneOffline();
            }
            else if (!_currentlyOnline && !offline)
            {
                HandleJustGoneOnline();
            }
            else if (offline)
            {
                _totalTimeOffline = _totalTimeOffline.Add(new TimeSpan(0, 0, 0, 1));
            }
        }

        private void HandleJustGoneOnline()
        {
            LogToTextFile();
            _currentlyOnline = true;
            ChangeState();
            ChangeOfStatus(true);
        }

        private void LogToTextFile()
        {
            var filename = @"log.txt";

            if (!System.IO.File.Exists(filename))
            {
                var headers = String.Format("Log Date/Time\tStatus\tTime At Current State\tTotal Outages\tTotal Time Offline");
                WriteLineToLog(filename, headers);
            }

            var _currentStateTimer = GetSinceLastChange();
            var text = String.Format("{0:yyyy-MM-dd HH:mm:ss}\t{1}\t{2:g}\t{3:#,##;0;0}\t{4:g}", DateTime.Now, _currentlyOnline ? "ONLINE" : "OFFLINE", _currentStateTimer, _totalOutages, _totalTimeOffline);
            WriteLineToLog(filename, text);
        }

        private static void WriteLineToLog(string filename, string text)
        {
            System.IO.File.AppendAllText(filename, text + "\r\n");
        }

        private TimeSpan GetSinceLastChange()
        {
            if (_lastChanged == null)
            {
                _lastChanged = DateTime.Now;
                return TimeSpan.Zero;
            }

            return DateTime.Now - _lastChanged.Value;
        }

        private void HandleJustGoneOffline()
        {
            LogToTextFile();
            _currentlyOnline = false;
            _totalOutages++;
            ChangeState();
            ChangeOfStatus(false);
        }

        private void ChangeState()
        {
            _lastChanged = DateTime.Now;
        }

        private void AnimateOfflineIcon()
        {
            _iconTimer = new Timer();
            _iconTimer.Elapsed += iconTimer_Elapsed;
            _iconTimer.Interval = 250;
            _iconTimer.Start();
        }

        private void ShowOnlineIcon()
        {
            if (_iconTimer != null)
            {
                _iconTimer.Enabled = false;
                _iconTimer.Stop();
                _iconTimer = null;
            }

            SetIcon("Globe-Green.ico");
        }

        void iconTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ChangeIcon(_currentIcon.Equals("Globe-Red.ico") ? "Globe-Dark-Red.ico" : "Globe-Red.ico");
        }

        private void SetIcon(string path)
        {
            var ico = new Icon(path);

            Icon = ico;
            notifyIcon.Icon = ico;

            _currentIcon = path;
        }

        private void UptimeMonitor_Closing(object sender, FormClosingEventArgs e)
        {
            if (_isRunning)
                LogToTextFile();

            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            if (_timer != null)
            {
                _timer.Enabled = false;
                _timer.Stop();
            }

            if (_iconTimer != null)
            {
                _iconTimer.Enabled = false;
                _iconTimer.Stop();
            }
        }
    }
}
