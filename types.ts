
export enum ContentType {
  M3U_STREAM = 'M3U_STREAM',
  XTREAM_STREAM = 'XTREAM_STREAM',
  YOUTUBE = 'YOUTUBE',
  LOCAL_FILE = 'LOCAL_FILE',
  P2P_STREAM = 'P2P_STREAM'
}

export type ContentCategory = 'TV' | 'SERIES' | 'MOVIE' | 'OTHER';

export interface DecisionRequest {
    id: string;
    item: ContentItem;
    reason: string;
    options?: string[]; // Önerilen diller
}

export interface Bookmark {
    id: string;
    title: string;
    url: string;
    icon?: string;
}

export interface BrowserConfig {
    adBlockEnabled: boolean;
    snifferEnabled: boolean;
    bookmarks: Bookmark[];
}

export enum QualityProfile {
  P1080 = '1080p',
  P720 = '720p',
  P480 = '480p',
  P360 = '360p'
}

export interface AudioTrack {
  id: string;
  language: string;
  label: string;
}

export interface SubtitleTrack {
  id: number;
  lang: string;
  label: string;
}

export interface StreamSource {
  url: string;
  priority: number;
  label?: string;
  status?: 'ONLINE' | 'OFFLINE' | 'UNKNOWN';
  resolution?: string;
  lastChecked?: number;
  language?: string;
  isPrivate?: boolean; 
  videoCodec?: string;
  audioCodec?: string;
  season?: number;  
  episode?: number;
  origin?: 'M3U' | 'XTREAM' | 'MANUAL'; 
}

export interface EpgProgram {
  channelId: string;
  title: string;
  description: string;
  start: Date;
  end: Date;
  sourceUrl?: string; // Hangi kaynaktan geldiği
}

export interface WatchSession {
  timestamp: number;
  duration: number; // seconds
}

export interface ContentItem {
  id: string;
  title: string;
  type: ContentType;
  category: ContentCategory;
  url: string;
  sources?: StreamSource[];
  description?: string;
  tags: string[];
  availableTracks: AudioTrack[];
  duration?: number;
  thumbnail?: string;
  alternativeLogos?: string[]; 
  groupTitle?: string;
  language?: string; // BÜLENT ABİ: Country -> Language
  genre?: string;
  epgId?: string;
  epgSource?: string; // BÜLENT ABİ: EPG Kaynağı (URL) - Scoped EPG için
  epgTimeShift?: number; 
  currentProgram?: EpgProgram;
  totalWatchDuration?: number;
  watchHistory?: WatchSession[];
  lastWatched?: number;
  status?: 'ONLINE' | 'OFFLINE' | 'UNKNOWN';
  resolution?: string;
  lastChecked?: number;
  isFavorite?: boolean;
  techInfo?: {
      videoCodec?: string;
      audioCodec?: string;
      bitrate?: string;
      fps?: string;
  };
  rating?: number; 
  releaseYear?: number;
  season?: number;    
  episode?: number;   
  addedDate?: number; 
  director?: string;
  cast?: string;
  isUserEdited?: boolean; 
  sourceReference?: string; 
  lastUpdated?: number; 
  consensusScore?: number; 
  verifiedByNetwork?: boolean;
  liveViewerCount?: number;
  catchup?: boolean;       
  catchupDays?: number;    
  catchupSource?: string;  
  maxDvrDuration?: number; 
  isGuest?: boolean; 
  manualCollectionIds?: string[]; 
}

export interface LibraryIndexItem {
    id: string;
    title: string;
    updated: number;
    language?: string; // BÜLENT ABİ: Country -> Language
}

export interface PeerStats {
  connectedPeers: number;
  uploadSpeed: number;
  downloadSpeed: number;
  totalUploaded: number;
  totalDownloaded: number;
  health: number;
  efficiency: number;
  bufferHealth?: number;
}

export interface P2PChannelConfig {
  id: string;
  name: string;
  peerId: string;
}

export interface XtreamAccountMetadata {
    status: string;
    exp_date: string; 
    active_cons: string;
    max_connections: string;
    username: string;
    server_info?: {
        url: string;
        port: string;
        https_port: string;
        server_protocol: string;
    };
}

export interface XtreamAccountConfig {
    id: string; 
    url: string;
    username: string;
    password: string;
    lastUpdated?: number;
    autoUpdate?: boolean; 
    updateAvailable?: boolean; 
    metadata?: XtreamAccountMetadata;
    stats?: {
        serverTotalChannels: number;
        localChannels: number;
        serverTotalVods: number;
        localVods: number;
    };
}

export interface M3UPlaylistConfig {
    id: string;
    url: string;
    label?: string;
    lastUpdated?: number;
    autoUpdate?: boolean;
    updateAvailable?: boolean; 
    isInternal?: boolean; 
    internalContent?: string; 
    forcedLanguage?: string; // BÜLENT ABİ: Liste bazlı dil ayarı
    stats?: {
        totalRemote: number; 
        totalLocal: number;  
    };
}

export interface EpgAnalysisResult {
    totalInEpg: number;      
    matchedInLibrary: number; 
    uniqueContribution: number; 
    redundantCount: number;   
    uniqueChannels: string[]; 
}

export interface EpgSourceConfig {
    id: string;
    url: string;
    label?: string; 
    lastUpdated?: number;
    autoUpdate?: boolean;
    updateAvailable?: boolean;
    stats?: {
        channelCount: number; 
        programCount: number; 
    };
    analysis?: EpgAnalysisResult; 
}

export interface FirebaseConfig {
  apiKey: string;
  authDomain: string;
  databaseURL: string;
  projectId: string;
  storageBucket: string;
  messagingSenderId: string;
  appId: string;
  measurementId: string;
}

export interface AdsConfig {
    enabled: boolean;
    adminChatEnabled?: boolean;
    activeAdminId?: string; // BÜLENT ABİ: Admin Chat için eklendi
    vastUrl: string;
    
    // Slot 1 (Primary)
    sidebarBannerHtml: string; 
    sidebarBannerImg?: string;
    sidebarBannerLink?: string;
    playerBannerHtml: string;  
    playerBannerImg?: string;
    playerBannerLink?: string;
    globalScriptHtml: string;  
    globalBannerImg?: string;
    globalBannerLink?: string;

    // Slot 2
    sidebarBannerHtml_2?: string;
    sidebarBannerImg_2?: string;
    sidebarBannerLink_2?: string;
    playerBannerHtml_2?: string;
    playerBannerImg_2?: string;
    playerBannerLink_2?: string;
    globalScriptHtml_2?: string;
    globalBannerImg_2?: string;
    globalBannerLink_2?: string;

    // Slot 3
    sidebarBannerHtml_3?: string;
    sidebarBannerImg_3?: string;
    sidebarBannerLink_3?: string;
    playerBannerHtml_3?: string;
    playerBannerImg_3?: string;
    playerBannerLink_3?: string;
    globalScriptHtml_3?: string;
    globalBannerImg_3?: string;
    globalBannerLink_3?: string;

    // Slot 4
    sidebarBannerHtml_4?: string;
    sidebarBannerImg_4?: string;
    sidebarBannerLink_4?: string;
    playerBannerHtml_4?: string;
    playerBannerImg_4?: string;
    playerBannerLink_4?: string;
    globalScriptHtml_4?: string;
    globalBannerImg_4?: string;
    globalBannerLink_4?: string;

    interstitialUrl: string; 
    adFrequencyMinutes: number; 
    adRotationIntervalMinutes?: number; // Rotasyon süresi
}

export interface AppSettings {
  maxUploadPeers: number;
  maxUploadBandwidth: number;
  measuredBandwidth: number;
  enableCache: boolean;
  cacheRetentionSeconds: number;
  uiLanguage: string; // BÜLENT ABİ: Arayüz Dili
  preferredLanguages: string[]; // BÜLENT ABİ: İçerik Dilleri
  setupCompleted: boolean; 
  enableHomeServer: boolean;
  homeServerHideXtream: boolean; 
  showUnknownChannels: boolean; 
  
  useStunTurn: boolean;
  epgUrls: EpgSourceConfig[]; 
  playlistUrls: M3UPlaylistConfig[]; 
  p2pChannels: P2PChannelConfig[];
  xtreamAccounts: XtreamAccountConfig[]; 
  
  deletedChannelIds: string[]; 
  deletedEpgUrls: string[]; 
  blacklistedPeerIds: string[]; 

  enableAudioNormalization: boolean;
  dvrRamLimitMb: number; 
  
  preferredSourceType: 'XTREAM' | 'M3U' | 'NONE'; 
  streamTimeoutSec: number; 
  autoFixNoAudio: boolean; 

  openWeatherApiKey?: string; 
  geminiApiKey?: string; // BÜLENT ABİ: Gemini API Anahtarı eklendi
  weatherCity?: string; 
  weatherLat?: number; 
  weatherLon?: number; 

  adsConfig: AdsConfig;

  browserConfig?: BrowserConfig;

  autoAcceptNewChannels: boolean; 
  autoAcceptProposals: boolean;   

  lastSortMode?: string;
  lastEpgUpdate?: number; 
  firebaseConfig?: FirebaseConfig;
  startupPage?: 'LIBRARY' | 'TV' | 'TOP5'; 
}

export interface ChatMessage {
    id: string;
    senderId: string;
    senderName: string;
    text: string;
    timestamp: number;
    isAdmin?: boolean;
}

export type P2PMessageType = 
  'HANDSHAKE' | 
  'REQUEST_CHUNK' | 
  'DATA_CHUNK' | 
  'ANNOUNCE_SEGMENT' | 
  'REQUEST_SEGMENT' | 
  'REQUEST_RELAY' |     
  'SEGMENT_DATA' | 
  'METADATA' | 
  'LIBRARY_INDEX' |    
  'REQUEST_ITEMS' |    
  'ITEMS_DATA' |       
  'CHANNEL_REPORT' | 
  'WATCH_HEARTBEAT' |
  'REQUEST_LIBRARY' |
  'SYNC_STATS' |
  'NEW_PROPOSAL' | 
  'CAST_VOTE';     

export interface P2PMessage {
  type: P2PMessageType;
  payload: any;
}

export interface ChannelReport {
  channelId: string;
  status: 'ONLINE' | 'OFFLINE';
  url: string;
  timestamp: number;
  
  suggestedTitle?: string;
  suggestedLogo?: string;
  suggestedEpgId?: string;
  suggestedLanguage?: string; // BÜLENT ABİ: Country -> Language
}

export interface ChunkRequest {
  fileId: string;
  chunkIndex: number;
}

export interface ChunkData {
  fileId: string;
  chunkIndex: number;
  data: ArrayBuffer;
  dataBase64?: string;
  totalChunks: number;
  mimeType: string;
}

export interface SegmentAnnouncement {
  streamUrl: string;
  segmentSn: number;
}

export interface SegmentData {
  streamUrl: string;
  segmentSn: number;
  data: ArrayBuffer;
}

export interface BroadcastMetadata {
  title: string;
  description: string;
  category: ContentCategory;
  sourceType: 'FILE' | 'SCREEN' | 'CAMERA';
  mimeType?: string;
}

export interface BroadcastState {
  isBroadcasting: boolean;
  mode: 'HOST' | 'WATCH';
  peerId: string;
  sourceType: 'FILE' | 'SCREEN' | 'CAMERA';
  title: string;
  description: string;
  category: ContentCategory;
  activePeers: number;
  targetId: string;
  remoteMeta: BroadcastMetadata | null;
}

export type ProposalType = 
    'NEW_CHANNEL' |      
    'LOGO_CHANGE' |      
    'TITLE_CHANGE' |     
    'CATEGORY_CHANGE' |  
    'LANGUAGE_FIX' | // BÜLENT ABİ: COUNTRY_FIX -> LANGUAGE_FIX
    'SOURCE_UPDATE' |    
    'EPG_UPDATE' |       
    'GENRE_UPDATE';      

export interface CommunityProposal {
    id: string; 
    targetChannelId: string;
    targetChannelTitle: string; 
    targetChannelUrl: string; 
    targetChannelLanguage?: string; // BÜLENT ABİ: Country -> Language
    proposerPeerId: string;
    type: ProposalType;
    oldValue: string;
    newValue: string;
    timestamp: number;
    expiresAt: number; 
    fullItem?: ContentItem;
    oldChannelId?: string; 
}

export type VoteType = 'APPROVE' | 'REJECT' | 'REPORT';

export interface CommunityVote {
    proposalId: string;
    voterPeerId: string;
    vote: VoteType;
    timestamp: number;
}
