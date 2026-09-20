using System.Collections.Generic;
using System.Linq;

namespace StreamMesh.Models
{
    public class SeriesGroup : Channel
    {
        public List<Channel> Episodes { get; set; } = new List<Channel>();

        public int SeasonCount => Episodes.Where(e => e.SeasonNumber > 0).Select(e => e.SeasonNumber).Distinct().Count();
        public int EpisodeCount => Episodes.Count;

        public SeriesGroup(string name, List<Channel> episodes)
        {
            this.Name = name;
            this.Episodes = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
            this.Category = "Dizi";

            var first = episodes.FirstOrDefault();
            if (first != null)
            {
                this.LogoUrl = first.LogoUrl;
                if (this.SeasonCount > 0)
                {
                    this.GroupTitle = $"{this.SeasonCount} Sezon, {this.EpisodeCount} Bölüm";
                }
                else
                {
                    this.GroupTitle = $"{this.EpisodeCount} Bölüm / Parça";
                }
                this.BackdropUrl = first.BackdropUrl;
                this.ImdbId = first.ImdbId;
                this.PlaylistUrl = first.PlaylistUrl;
                this.M3uLineNumber = first.M3uLineNumber;
                this.RawM3uBlock = first.RawM3uBlock;
                this.Language = first.Language;
                var epUrls = episodes.Select(e => e.PrimaryUrl).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
                this.Url = epUrls.Count > 0 ? string.Join("||", epUrls) : (first.Url ?? "");
            }
        }

        public Channel? GetNextEpisode(string currentEpisodeId)
        {
            int idx = Episodes.FindIndex(e => e.Id == currentEpisodeId);
            if (idx >= 0 && idx < Episodes.Count - 1)
            {
                return Episodes[idx + 1];
            }
            return null;
        }
    }
}
