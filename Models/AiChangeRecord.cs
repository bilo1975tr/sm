using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamMesh.Models
{
    public class AiChangeRecord
    {
        public string ChannelId { get; set; } = "";
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string OldCategory { get; set; } = "";
        public string NewCategory { get; set; } = "";
        public bool HasChanged => !string.Equals(OldName, NewName, System.StringComparison.OrdinalIgnoreCase) ||
                                  !string.Equals(OldCategory, NewCategory, System.StringComparison.OrdinalIgnoreCase);
        public string ChangeSummary => HasChanged ? $"[DEĞİŞTİ] '{OldName}' ({OldCategory}) ➔ '{NewName}' ({NewCategory})" : $"[DEĞİŞMEDİ] {OldName} ({OldCategory})";
    }

    public class AiBatchReportItem
    {
        public int PageNumber { get; set; }
        public string Title { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<AiChangeRecord> Records { get; set; } = new List<AiChangeRecord>();
        public int ChangedCount => Records.Count(r => r.HasChanged);
        public int TotalCount => Records.Count;
        public string DisplaySummary => $"Sayfa {PageNumber} - {Timestamp:HH:mm:ss} ({TotalCount} kanal tarandı, {ChangedCount} değişiklik yapıldı)";
    }
}
