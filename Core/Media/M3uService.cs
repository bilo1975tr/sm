using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media
{
    public class M3uService
    {
        private readonly M3uEngine _m3uEngine = new M3uEngine();

        public async Task<List<Channel>> ParseM3uAsync(string url)
        {
            try
            {
                return await _m3uEngine.ParseM3uAsync(url);
            }
            catch (Exception ex)
            {
                LogService.LogError($"M3u Parse Error: {ex.Message}");
                return new List<Channel>();
            }
        }
    }
}

