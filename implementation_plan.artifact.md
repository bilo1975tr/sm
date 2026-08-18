# Smart EPG Matching & Locking System

Implement an intelligent EPG matching system that automatically finds channel IDs while respecting manual user overrides and preventing re-matching of previously rejected suggestions.

## User Review Required

> [!IMPORTANT]
> The system will now automatically modify the `EpgId` field of channels in your database if a high-confidence match is found. However, once you manually change or delete an EPG ID, the channel will be "Locked" for EPG matching to prevent future automatic changes.

## Proposed Changes

### Core Models & Database

#### [MODIFY] [Channel.cs](file:///C:/Users/Administrator/Downloads/streammesh/Models/Channel.cs)
- Add `IsEpgLocked` property (bool) to track if the system should skip automatic EPG matching for this channel.

#### [MODIFY] [DatabaseEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Database/DatabaseEngine.cs)
- Add `IsEpgLocked` column to `Channels` table.
- Update `SaveChannelAsync` and `SaveChannelsBatchAsync` to persist the lock state.
- Update `MapReaderToChannel` to load the lock state.

### Epg Logic

#### [MODIFY] [EpgService.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/EpgService.cs)
- Implement `PerformSmartEpgMatchAsync` to search `EpgChannels` for a 100% name match.
- Update `EnrichBatchEpgAsync` to:
  1. Check if `EpgId` is empty and `IsEpgLocked` is false.
  2. Perform smart matching.
  3. Save the found `EpgId` back to the database to "cement" the match.

## Verification Plan

### Manual Verification
- Verify that a channel without an EPG ID automatically finds its EPG if a clear name match exists in the EPG source.
- Verify that deleting an EPG ID in the "Kanal Yönetimi" view prevents the system from re-assigning it automatically.
- Verify that manually assigned EPG IDs are never overwritten by the smart matcher.
