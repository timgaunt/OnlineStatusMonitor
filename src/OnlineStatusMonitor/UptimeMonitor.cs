using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Timers;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace OnlineStatusMonitor
{
    public partial class UptimeMonitor : Form
    {
        string _speedLogFilename = @"speed-log.txt";
        string _statusLogFilename = @"log.txt";

        private string _currentIcon = "";
        private bool _isRunning;
        private Timer _iconTimer;
        private Timer _pingTimer;
        private Timer _speedTimer;
        private int _totalOutages;
        private DateTime? _lastChanged;
        private TimeSpan _totalTimeOffline;
        private bool _currentlyOnline;
        private Dictionary<DateTime, double> _speedLogs;

        readonly Color _offlineColour = Color.FromArgb(192, 0, 0);
        readonly Font _offlineFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);
        readonly Color _okColour = Color.FromArgb(0, 192, 0);
        readonly Font _okFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);

        private delegate void ChangeOfStatusDelegate(bool online);
        private delegate void UpdateSpeedDelegate();
        private delegate void UpdateStatusDelegate();
        private delegate void ChangeIconDelegate(string icon);

        public UptimeMonitor()
        {
            InitializeComponent();
            Reset();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            StartMonitoring();
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
            // Assume true to start with as this will get reset if not
            // Can't set this on initialise as if you restart the monitor when offline it won't be set
            _currentlyOnline = true; 

            btnStart.Text = "Stop Monitoring";

            Reset();

            RunSpeedTest(true);
            StartPingTimer();
            StartSpeedTestTimer();

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

            LogStatusChangeToTextFile();

            _isRunning = false;
        }

        private void Reset()
        {
            _lastChanged = null;
            _totalTimeOffline = new TimeSpan();
            _currentlyOnline = false;
            _totalOutages = 0;
            _speedLogs = new Dictionary<DateTime, double>();
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
            var currentStateTimer = GetSinceLastChange();
            var averageSpeed = CalculateAverageSpeed();
            var message = String.Format("Good news! We can talk to {0}", txtHost.Text);

            if (currentStateTimer.TotalMinutes > 2)
                message = String.Format("{0} after {1:#,##} minutes offline. Current speed: {2:f2}mbps", message, currentStateTimer.TotalMinutes, averageSpeed);

            notifyIcon.ShowBalloonTip(2500, "Online", message, ToolTipIcon.Info);
        }

        private void ShowOfflineToolTip()
        {
            notifyIcon.ShowBalloonTip(2500, "OFFLINE", "Bad times! We can't talk to " + txtHost.Text, ToolTipIcon.Error);
        }

        private void StartPingTimer()
        {
            ChangeState();

            _pingTimer = new Timer();
            _pingTimer.Elapsed += PingTimerElapsed;
            _pingTimer.Interval = 1000;
            _pingTimer.Start();
        }

        private void StartSpeedTestTimer()
        {
            _speedTimer = new Timer();
            _speedTimer.Elapsed += SpeedTestTimerElapsed;
            _speedTimer.Interval = GetMinutesInMilliseconds(5);
            _speedTimer.Start();
        }

        private void SpeedTestTimerElapsed(object sender, ElapsedEventArgs e)
        {
            RunSpeedTest();
        }

        private void RunSpeedTest(bool ignoreStatus = false)
        {
            if (!ignoreStatus && !_currentlyOnline)
            {
                UpdateSpeed();
                return;
            }

            if (_currentlyOnline)
            {
                var speed = DownloadSpeedTest.GetInternetSpeedInBytes();
                _speedLogs.Add(DateTime.Now, speed);
                LogSpeedToTextFile();
            }

            UpdateSpeed();
        }

        private double GetMinutesInMilliseconds(int i)
        {
            return i * 60 * 1000;
        }

        private void StopTimer()
        {
            if (_pingTimer.Enabled)
                _pingTimer.Stop();
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
                RunSpeedTest();
                ShowOnlineToolTip();
                ShowOnlineIcon();
            }
            else
            {
                ShowOfflineToolTip();
                AnimateOfflineIcon();
            }
        }

        private void UpdateSpeed()
        {
            if (lblAvgSpeed.InvokeRequired)
            {
                Invoke(new UpdateSpeedDelegate(UpdateSpeed));
                return;
            }

            lblAvgSpeed.Text = CalculateAverageSpeed().ToString("f2");
            lblCurrent.Text = GetMostRecentSpeed().ToString("f2");
        }

        private static string PrettyFormatTimespan(TimeSpan timer)
        {
            var offlineTime = String.Empty;

            if (timer.TotalHours >= 1)
                offlineTime = String.Format("{0:#,##;0;0}h {1:#,##;0;0}m", timer.TotalHours, timer.Minutes);
            else if (timer.TotalMinutes >= 1)
                offlineTime = String.Format("{0:#,##;0;0}m {1:#,##;0;0}s", timer.Minutes, timer.Seconds);
            else
                offlineTime = String.Format("{0:#,##;0}s", timer.Seconds);

            return offlineTime;
        }

        private void PingTimerElapsed(object sender, ElapsedEventArgs e)
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
        }

        private void HandleJustGoneOnline()
        {
            var currentStateTimer = GetSinceLastChange();
            _totalTimeOffline = _totalTimeOffline.Add(currentStateTimer);
            LogStatusChangeToTextFile();
            ChangeOfStatus(true);
            _currentlyOnline = true;
            ChangeState();
        }

        private void LogStatusChangeToTextFile()
        {
            if (!System.IO.File.Exists(_statusLogFilename))
            {
                var headers = String.Format("Log Date/Time\tStatus\tTime At Current State\tTotal Outages\tTotal Time Offline\tMin Speed\tAvg Speed\tMax Speed");
                WriteLineToLog(_statusLogFilename, headers);
            }

            var currentStateTimer = GetSinceLastChange();
            var minSpeed = CalculateMinimumSpeed();
            var avgSpeed = CalculateAverageSpeed();
            var maxSpeed = CalculateMaximumSpeed();

            var text = String.Format("{0:yyyy-MM-dd HH:mm:ss}\t{1}\t{2:g}\t{3:#,##;0;0}\t{4:g}\t{5:f2}\t{6:f2}\t{7:f2}", DateTime.Now, _currentlyOnline ? "ONLINE" : "OFFLINE", currentStateTimer, _totalOutages, _totalTimeOffline, minSpeed, avgSpeed, maxSpeed);
            WriteLineToLog(_statusLogFilename, text);
        }

        private void LogSpeedToTextFile()
        {
            if (!System.IO.File.Exists(_speedLogFilename))
            {
                var headers = String.Format("Log Date/Time\tCurrent Speed\tMin Speed\tAvg Speed\tMax Speed");
                WriteLineToLog(_speedLogFilename, headers);
            }

            var lastSpeed = GetMostRecentSpeed();
            var minSpeed = CalculateMinimumSpeed();
            var avgSpeed = CalculateAverageSpeed();
            var maxSpeed = CalculateMaximumSpeed();

            var text = String.Format("{0:yyyy-MM-dd HH:mm:ss}\t{1:f2}\t{2:f2}\t{3:f2}\t{4:f2}", DateTime.Now, lastSpeed, minSpeed, avgSpeed, maxSpeed);
            WriteLineToLog(_speedLogFilename, text);
        }

        private double CalculateMinimumSpeed()
        {
            if (!_speedLogs.Any())
                return 0;

            return GetSpeedInMegaBytes(_speedLogs.Min(s => s.Value));
        }

        private double CalculateAverageSpeed()
        {
            if (!_speedLogs.Any())
                return 0;

            return GetSpeedInMegaBytes(_speedLogs.Average(s => s.Value));
        }

        private double CalculateMaximumSpeed()
        {
            if (!_speedLogs.Any())
                return 0;

            return GetSpeedInMegaBytes(_speedLogs.Max(s => s.Value));
        }

        private double GetMostRecentSpeed()
        {
            if (!_speedLogs.Any())
                return 0;

            var mostRecent = _speedLogs.OrderByDescending(l => l.Key).FirstOrDefault();
            return GetSpeedInMegaBytes(mostRecent.Value);
        }

        private double GetSpeedInMegaBytes(double bytes)
        {
            if (!_currentlyOnline)
                return 0;

            const long bytesInMegabytes = 128 * 1024;
            var mb = bytes / bytesInMegabytes;
            return mb;
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
            LogStatusChangeToTextFile();
            ChangeOfStatus(false);
            _currentlyOnline = false;
            _totalOutages++;
            ChangeState();
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
                LogStatusChangeToTextFile();

            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            if (_pingTimer != null)
            {
                _pingTimer.Enabled = false;
                _pingTimer.Stop();
            }

            if (_iconTimer != null)
            {
                _iconTimer.Enabled = false;
                _iconTimer.Stop();
            }
        }

        private void lblAvgSpeed_DoubleClick(object sender, EventArgs e)
        {
            RunSpeedTest(true);
            lblAvgSpeed.Text = CalculateAverageSpeed().ToString("f2");
        }

        private void lblCurrent_DoubleClick(object sender, EventArgs e)
        {
            RunSpeedTest(true);
            lblCurrent.Text = GetMostRecentSpeed().ToString("f2");
        }
    }

}
