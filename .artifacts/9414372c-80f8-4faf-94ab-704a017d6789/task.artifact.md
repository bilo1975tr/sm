# TODO List - Reliability & Performance Fixes (Completed)

- [x] fix(media): Implement deterministic IDs for channels in `M3uEngine.cs`
- [x] fix(ui): Standardize settings keys in `SettingsView.xaml.cs`
- [x] fix(player): Enhance `PlayerView.xaml.cs` with better logging, timeouts, and safety
- [x] fix(database): Add `CleanupDuplicates` and optimize event triggers in `DatabaseEngine.cs`
- [x] fix(vm): Break the infinite reload loop in `HomeViewModel.cs`
- [x] feat(database): Run a one-time cleanup to fix the 1.9M record bloat

# TODO List - Strict Categorization & Smart Merging (Completed)

- [x] feat(media): Update `M3uEngine.cs` to support `forceCategory`
- [x] feat(media): Update `GitHubSyncEngine.cs` to enforce categories from JSON source
- [x] feat(media): Update `SmartNormalizationEngine.cs` to respect forced categories
- [x] feat(media): Aggressive card-based merging in `ChannelAggregator.cs` (URL + EPG ID)
- [x] feat(database): Integrate aggregation into `SyncIncomingChannelsAsync`
- [x] verify: Run Cloud Sync and verify merged cards and strict categories
