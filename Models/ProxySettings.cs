namespace vs2026_plugin.Models
{
    public class ProxySettings
    {
        public string Protocol { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string Authentication { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string NonProxyHosts { get; set; }
    }
}
