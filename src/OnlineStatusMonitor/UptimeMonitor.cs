using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;

namespace OnlineStatusMonitor
{
    public partial class UptimeMonitor : Form
    {
        private int _totalOutages;
        private DateTime? _lastChanged;
        private TimeSpan _totalTimeOffline;
        private TimeSpan _currentStateTimer;
        private bool _currentlyOnline;

        readonly Color _offlineColour = Color.FromArgb(192, 0, 0);
        readonly Font _offlineFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);
        readonly Color _okColour = Color.FromArgb(0, 192, 0);
        readonly Font _okFont = new Font("Verdana", 20F, FontStyle.Bold, GraphicsUnit.Point, 0);

        public UptimeMonitor()
        {
            InitializeComponent();

            Reset();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button1.Text = "Cancel";

            Reset();

            ShowAsOnline();
            StartTimer();
        }

        private void Reset()
        {
            _lastChanged = null;
            _totalTimeOffline = new TimeSpan();
            _currentStateTimer = new TimeSpan();
            _currentlyOnline = false;
            _totalOutages = 0;

            lblDowntime.Text = "N/A";
            lblOutages.Text = "0";
            lblStatus.Text = "ONLINE";
            lblTimer.Text = "";
        }

        private void ShowAsOnline()
        {
            lblStatus.Text = "ONLINE";

            lblStatus.Font = _okFont;
            lblStatus.ForeColor = _okColour;

            lblTimer.Font = _okFont;
            lblTimer.ForeColor = _okColour;
        }

        private void ShowAsOffline()
        {
            lblStatus.Text = "OFFLINE";

            lblStatus.Font = _offlineFont;
            lblStatus.ForeColor = _offlineColour;

            lblTimer.Font = _offlineFont;
            lblTimer.ForeColor = _offlineColour;
        }

        private void StartTimer()
        {
            ChangeState();

            var aTimer = new System.Timers.Timer();
            aTimer.Elapsed += OnTimedEvent;
            aTimer.Interval = 1000;
            aTimer.Start();
        }

        private delegate void UpdateStatusDelegate();

        private void UpdateStatus()
        {
            if (lblTimer.InvokeRequired)
            {
                Invoke(new UpdateStatusDelegate(UpdateStatus));
                return;
            }

            lblTimer.Text = PrettyFormatTimespan(_currentStateTimer);

            if (_currentlyOnline)
                ShowAsOnline();
            else
                ShowAsOffline();

            lblOutages.Text = _totalOutages.ToString("#,##;N/A;N/A");
            lblDowntime.Text = PrettyFormatTimespan(_totalTimeOffline);
        }

        private static string PrettyFormatTimespan(TimeSpan timer)
        {
            var offlineTime = String.Empty;

            if (timer.TotalHours > 1)
                offlineTime = String.Format("{0}h {1}m", timer.TotalHours, timer.Minutes);
            else if (timer.TotalMinutes > 1)
                offlineTime = String.Format("{0}m {1}s", timer.Minutes, timer.Seconds);
            else
                offlineTime = String.Format("{0}s", timer.Seconds);

            return offlineTime;
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            int averagePing;
            int packetLoss;

            UpdateClock();

            GetNetworkStats(txtHost.Text, 10, 100, out averagePing, out packetLoss);

            RefreshStatusDisplay(packetLoss);

            UpdateStatus();
        }

        private void UpdateClock()
        {
            if (!_lastChanged.HasValue)
                return;

            _currentStateTimer = (DateTime.Now - _lastChanged.Value);
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
        }

        private void LogToTextFile()
        {
            var text = String.Format("{0:yyyy-MM-dd HH:mm:ss}\t{1}\t{2:g}\t{3:#,##;0;0}\t{4:g}\r\n", DateTime.Now, _currentlyOnline ? "ONLINE" : "OFFLINE", _currentStateTimer, _totalOutages, _totalTimeOffline);
            System.IO.File.AppendAllText(@"log.txt", text);
        }

        private void HandleJustGoneOffline()
        {
            LogToTextFile();
            _currentlyOnline = false;
            _totalOutages++;
            ChangeState();
        }

        private void ChangeState()
        {
            _lastChanged = DateTime.Now;
        }
    }
}
