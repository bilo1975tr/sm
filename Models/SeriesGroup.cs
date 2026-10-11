using System.Collections.Generic;
using System.Linq;

namespace StreamMesh.Models
{
    public class SeriesGroup : Channel
    {
        public List<Channel> Episodes { get; set; } = new List<Channel>();

        public int SeasonCount => Episodes.Select(e => e.SeasonNumber).Distinct().Count();
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
                this.GroupTitle = $"{this.SeasonCount} Sezon, {this.EpisodeCount} Bölüm";
                this.BackdropUrl = first.BackdropUrl;
                this.ImdbId = first.ImdbId;
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
