using System;

namespace StreamMesh.Models
{
    public class EpgSourceItem
    {
        public string Url { get; set; } = "";
        public string Origin { get; set; } = "Yerel";
        public string Color { get; set; } = "#1e293b";
        public string Status { get; set; } = "Bekliyor";
        public int ChannelCount { get; set; } = 0;
        public int ProgramCount { get; set; } = 0;
        public long LastUpdatedUnix { get; set; } = 0;
        public int TimeShiftHours { get; set; } = 0;
        public string ErrorMessage { get; set; } = "";

        public string LastUpdatedFormatted
        {
            get
            {
                if (LastUpdatedUnix <= 0) return "Hiç güncellenmedi";
                try
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(LastUpdatedUnix).LocalDateTime;
                    return dt.ToString("dd.MM.yyyy HH:mm");
                }
                catch
                {
                    return "Bilinmiyor";
                }
            }
        }

        public string StatusColor
        {
            get
            {
                if (Status.Equals("Başarılı", StringComparison.OrdinalIgnoreCase)) return "#064e3b";
                if (Status.Equals("İndiriliyor", StringComparison.OrdinalIgnoreCase)) return "#0284c7";
                if (Status.Contains("Hata", StringComparison.OrdinalIgnoreCase) || Status.Contains("Ulaşılamadı", StringComparison.OrdinalIgnoreCase)) return "#7f1d1d";
                return "#334155";
            }
        }
    }
}
