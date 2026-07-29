# Strict Categorization and Smart Channel Merging Plan

The goal is to enforce strict categorization based on the `auto_update.json` source and implement a more aggressive "card-based" channel merging system that combines channels by URL or EPG ID.

## User Review Required

> [!IMPORTANT]
> **Merging Logic**: Channels will now be automatically merged into a single card if they share the same URL OR the same EPG ID. This will significantly reduce duplicates and create "multi-source" channel cards.

## Proposed Changes

### 1. Strict Categorization

#### [MODIFY] [M3uEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/M3uEngine.cs)
- Add a `bool forceCategory` parameter to `ParseM3uAsync`.
- If `forceCategory` is true, the `categoryHint` will be strictly applied and `SmartNormalizationEngine` will be prevented from overwriting it.

#### [MODIFY] [GitHubSyncEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/GitHubSyncEngine.cs)
- Pass `forceCategory: true` when calling `ParseM3uAsync` for TV, Film, Dizi, and Radyo lists.

### 2. Smart Channel Aggregator

#### [MODIFY] [ChannelAggregator.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/ChannelAggregator.cs)
- Update `AggregateChannels` to use two mapping dictionaries: `urlMap` and `epgMap`.
- If an incoming channel matches an existing one by URL or EPG ID (and EPG ID is not empty), they will be merged using `MergeWith`.
- This ensures that different sources for the same channel (sharing an EPG ID) are grouped together.

### 3. Database Synchronization

#### [MODIFY] [DatabaseEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Database/DatabaseEngine.cs)
- Update `SyncIncomingChannelsAsync` to:
    1. Fetch existing channels that might match the incoming ones.
    2. Run the `ChannelAggregator` on the combined list.
    3. Save the merged results.
- Update `CleanupDuplicatesAsync` to perform a full `AutoAggregateDatabaseAsync` call to tidy up the entire database using the new logic.

## Verification Plan

### Manual Verification
- Run a "Cloud Sync" and verify that channels from the "Film" list are strictly categorized as "Film".
- Check if multiple sources for the same channel (e.g., TRT 1 from different M3U files but same EPG ID) are now shown as a single card with multiple URLs.
- Verify that the total channel count decreases after aggregation as duplicates are merged.
