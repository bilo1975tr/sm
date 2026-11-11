using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public class PluginManager
    {
        private static readonly Lazy<PluginManager> _lazy = new(() => new PluginManager());
        public static PluginManager Instance => _lazy.Value;

        private readonly ObservableCollection<IPlugin> _plugins = new();
        public ObservableCollection<IPlugin> Plugins => _plugins;

        private readonly DatabaseEngine _db = new();

        private PluginManager()
        {
            RegisterDefaults();
            LoadAllSettings();
        }

        private void RegisterDefaults()
        {
            _plugins.Add(new StreamResolutionPlugin());
            _plugins.Add(new LocalNetworkServerPlugin());
            _plugins.Add(new EpgPlugin());
            _plugins.Add(new MetadataPlugin());
            _plugins.Add(new LogoPlugin());
            _plugins.Add(new XtreamPlugin());
            _plugins.Add(new M3uPlaylistPlugin());
            _plugins.Add(new GitHubSyncPlugin());
            _plugins.Add(new SsdpDiscoveryPlugin());
            _plugins.Add(new AiAssistantPlugin());
            _plugins.Add(new UpdatePlugin());
            _plugins.Add(new P2PStatsPlugin());
            _plugins.Add(new MaintenancePlugin());
            _plugins.Add(new UserAuthPlugin());
            _plugins.Add(new GlobalSearchPlugin());
        }

        public void LoadAllSettings()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.LoadSettings(_db);
                }
                catch (Exception ex)
                {
                    LogService.LogError($"[PluginManager] Failed to load settings for {plugin.Name}", ex);
                }
            }
        }

        public async Task InitializeAllAsync()
        {
            LogService.LogInfo($"[PluginManager] Initializing {_plugins.Count} plugins...");
            foreach (var plugin in _plugins)
            {
                if (!plugin.IsEnabled)
                {
                    LogService.LogInfo($"[PluginManager] Plugin skipped (disabled): {plugin.Name}");
                    continue;
                }

                try
                {
                    await plugin.InitializeAsync().ConfigureAwait(false);
                    LogService.LogInfo($"[PluginManager] Plugin loaded: {plugin.Name} (v{plugin.Version}) - {plugin.Status}");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"[PluginManager] Failed to initialize plugin {plugin.Name}", ex);
                }
            }
        }

        public T? GetPlugin<T>() where T : class, IPlugin
        {
            var p = _plugins.OfType<T>().FirstOrDefault();
            return (p != null && p.IsEnabled) ? p : null;
        }

        public IPlugin? GetPluginById(string id)
        {
            return _plugins.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IPlugin> GetAllPlugins() => _plugins;

        public void SetPluginEnabled(string idOrName, bool enabled)
        {
            var p = _plugins.FirstOrDefault(x =>
                string.Equals(x.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Name, idOrName, StringComparison.OrdinalIgnoreCase));

            if (p != null)
            {
                p.IsEnabled = enabled;
                p.SaveSettings(_db);

                if (enabled)
                {
                    _ = p.InitializeAsync();
                }
                else
                {
                    _ = p.ShutdownAsync();
                }

                LogService.LogInfo($"[PluginManager] Plugin '{p.Name}' enabled state changed to: {enabled}");
            }
        }

        public void SavePluginSettings(IPlugin plugin)
        {
            if (plugin == null) return;
            plugin.SaveSettings(_db);
        }

        public void ResetPluginSettings(IPlugin plugin)
        {
            if (plugin == null) return;
            plugin.ResetSettings(_db);
        }

        public async Task<string> ExecutePluginActionAsync(string pluginId, string actionName, Dictionary<string, object>? parameters = null)
        {
            var p = GetPluginById(pluginId);
            if (p == null) return $"❌ Eklenti bulunamadı: {pluginId}";
            return await p.ExecuteActionAsync(actionName, parameters).ConfigureAwait(false);
        }
    }
}
