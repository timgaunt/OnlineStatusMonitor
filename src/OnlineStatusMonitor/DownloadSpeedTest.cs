using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace OnlineStatusMonitor
{
    public class DownloadSpeedTest
    {
        private long bytesReceivedPrev = 0;

        public long CheckBandwidthUsage()
        {
            var bytesReceived = CalculateBytesReceived();

            if (bytesReceivedPrev == 0)
                bytesReceivedPrev = bytesReceived;

            var bytesUsed = bytesReceived - bytesReceivedPrev;

            bytesReceivedPrev = bytesReceived;

            return bytesUsed;
        }

        private static long CalculateBytesReceived()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            return interfaces.Where(inf => inf.OperationalStatus == OperationalStatus.Up && inf.NetworkInterfaceType != NetworkInterfaceType.Loopback && inf.NetworkInterfaceType != NetworkInterfaceType.Tunnel && inf.NetworkInterfaceType != NetworkInterfaceType.Unknown && !inf.IsReceiveOnly)
                             .Sum(inf => inf.GetIPv4Statistics().BytesReceived);
        }

        public static double GetInternetSpeedInBytes()
        {
            double startTime = Environment.TickCount;
            var fileSize = DownloadFile();
            double endtime = Environment.TickCount;

            var secs = Math.Floor(endtime - startTime) / 1000;
            return Math.Round(fileSize / secs);
        }

        private static long DownloadFile()
        {
            var url = new Uri(GetFileDownloadUrl());
            using (var client = new WebClient())
            {
                var ms = new MemoryStream(client.DownloadData(url));
                return GetFileSize(ms);
            }
        }

        private static long GetFileSize(MemoryStream ms)
        {
            return ms.Length;
        }

        private static string GetFileDownloadUrl()
        {
            return "https://dl.google.com/tag/s/appguid%3D%7BD0AB2EBC-931B-4013-9FEB-C9C4C2225C8C%7D%26iid%3D%7B274DF1A5-43FE-DB2E-E24D-B90112BF3891%7D%26lang%3Den-GB%26browser%3D2%26usagestats%3D0%26appname%3DGoogle%2520voice%2520and%2520video%2520Chat%26needsadmin%3Dfalse/googletalk/googletalkplugin/GoogleVoiceAndVideoSetup.exe";
        }
    }
}