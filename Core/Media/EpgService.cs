using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Media
{
    public class EpgService
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public async Task<Dictionary<string, EpgProgram>> GetCurrentEpgsAsync(List<Channel> channels)
        {
            var dict = new Dictionary<string, EpgProgram>();
            if (channels == null || channels.Count == 0) return dict;

            try
            {
                var channelNames = channels.Select(c => c.Name).Distinct().ToList();
                var programs = await _db.GetEpgForChannelsAsync(channelNames);
                var now = DateTime.Now;

                foreach (var ch in channels)
                {
                    var current = programs.FirstOrDefault(p => p.ChannelName == ch.Name && now >= p.StartTime && now <= p.EndTime);
                    if (current != null) dict[ch.Id] = current;
                }
            }
            catch { }
            return dict;
        }

        public async Task<EpgProgram?> GetNextEpgAsync(Channel channel)
        {
            try
            {
                var programs = await _db.GetEpgForChannelsAsync(new List<string> { channel.Name });
                var now = DateTime.Now;
                return programs.Where(p => p.ChannelName == channel.Name && p.StartTime > now)
                               .OrderBy(p => p.StartTime)
                               .FirstOrDefault();
            }
            catch { return null; }
        }
    }
}
