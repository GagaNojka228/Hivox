using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hivox.Models
{
    public class ServerInfo : INotifyPropertyChanged
    {
        public string Uri { get; set; } = "";

        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                NotifyOnUi();
            }
        }

        public string Protocol { get; set; } = "";
        public string Host { get; set; } = "";

        private int _ping = -1;
        public int Ping
        {
            get => _ping;
            set
            {
                if (_ping == value) return;
                _ping = value;
                NotifyOnUi();
            }
        }

        private string _countryCode = "";
        public string CountryCode
        {
            get => _countryCode;
            set
            {
                if (_countryCode == value) return;
                _countryCode = value ?? "";
                NotifyOnUi();
                NotifyOnUi(nameof(CountryBadge));
                NotifyOnUi(nameof(LocationSummary));
            }
        }

        private string _countryName = "Unknown";
        public string CountryName
        {
            get => _countryName;
            set
            {
                if (_countryName == value) return;
                _countryName = value ?? "Unknown";
                NotifyOnUi();
                NotifyOnUi(nameof(CountryBadge));
                NotifyOnUi(nameof(LocationSummary));
            }
        }

        private string _flag = "\U0001F310";
        public string Flag
        {
            get => _flag;
            set
            {
                if (_flag == value) return;
                _flag = value;
                NotifyOnUi();
            }
        }

        public int Port { get; set; } = 443;
        public string Details { get; set; } = "";
        public string Transport { get; set; } = "";
        public string Security { get; set; } = "";
        public string RawJsonConfig { get; set; } = "";
        public string SubscriptionUrl { get; set; } = "";
        public string SubscriptionName { get; set; } = "";

        public string GroupTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SubscriptionName))
                    return SubscriptionName;
                if (!string.IsNullOrWhiteSpace(SubscriptionUrl))
                    return GetSubscriptionDisplayName(SubscriptionUrl);
                return "Ручные серверы";
            }
        }

        public static string GetSubscriptionDisplayName(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "Ручные серверы";
            try
            {
                if (url.Contains('#'))
                {
                    var frag = System.Uri.UnescapeDataString(url.Split('#')[^1]).Trim();
                    if (!string.IsNullOrWhiteSpace(frag))
                        return frag;
                }
                var cleanUrl = url.Contains('#') ? url.Split('#')[0] : url;
                if (System.Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                    return uri.Host;
            }
            catch { }
            return url.Length > 36 ? url[..36] + "..." : url;
        }

        public string DedupeKey => !string.IsNullOrEmpty(RawJsonConfig)
            ? $"{Name}\0{RawJsonConfig}"
            : Uri ?? "";

        public string CountryBadge
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_countryCode) && _countryCode.Length >= 2)
                    return _countryCode.Substring(0, 2).ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(_countryName) && !_countryName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) && _countryName.Length >= 2)
                    return _countryName.Substring(0, 2).ToUpperInvariant();
                return "GL";
            }
        }

        public string LocationSummary
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_countryName) && !_countryName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(Host) ? _countryName : $"{_countryName} • {Host}";
                return string.IsNullOrWhiteSpace(Host) ? Protocol : Host;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyOnUi([CallerMemberName] string name = null)
        {
            var app = System.Windows.Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(name)));
            else
                OnPropertyChanged(name);
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
