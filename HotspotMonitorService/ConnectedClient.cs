using System;

namespace HotspotMonitorService
{
    public class ConnectedClient
    {
        public string MacAddress { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddresses { get; set; } = string.Empty;
    }
}
