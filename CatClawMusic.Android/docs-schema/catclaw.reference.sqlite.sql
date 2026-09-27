CREATE TABLE "Albums" (
"Id" integer primary key autoincrement not null ,
"Title" varchar not null ,
"Name" varchar ,
"Artist" varchar ,
"CoverArtPath" varchar ,
"SongCount" integer ,
"Year" integer ,
"ArtistId" integer ,
"Cover" varchar ,
"ReleaseYear" integer );
CREATE TABLE "Artists" (
"Id" integer primary key autoincrement not null ,
"Name" varchar ,
"Cover" varchar ,
"Gender" varchar ,
"Birthday" varchar ,
"Region" varchar ,
"Description" varchar );
CREATE TABLE "CachedSongs" (
"Id" integer primary key autoincrement not null ,
"SongId" integer ,
"LocalPath" varchar ,
"CachedAt" integer ,
"FileSize" integer );
CREATE TABLE "ChatMessageRecord" (
"Id" integer primary key autoincrement not null ,
"Role" varchar ,
"Content" varchar ,
"Timestamp" bigint );
CREATE TABLE "ConnectionProfiles" (
"Id" integer primary key autoincrement not null ,
"Name" varchar ,
"Protocol" integer ,
"Host" varchar ,
"Port" integer ,
"UserName" varchar ,
"Password" varchar ,
"BasePath" varchar ,
"IsEnabled" integer ,
"ApiVersion" varchar ,
"ClientName" varchar ,
"UseHttps" integer ,
"DomainName" varchar ,
"ShareName" varchar ,
"ServerType" integer );
CREATE TABLE "DailyPlayStat" (
"Date" varchar primary key not null ,
"PlayCount" integer ,
"TotalDurationMs" integer ,
"NightPlayCount" integer );
CREATE TABLE "DailySongStat" (
"DateSong" varchar primary key not null ,
"Date" varchar ,
"SongId" integer ,
"PlayCount" integer ,
"TotalDurationMs" integer );
CREATE TABLE "Favorites" (
"SongId" integer primary key not null ,
"AddedAt" integer );
CREATE TABLE "HourlyPlayStat" (
"DateHour" varchar primary key not null ,
"Date" varchar ,
"Hour" integer ,
"PlayCount" integer ,
"TotalDurationMs" integer );
CREATE TABLE "Lyrics" (
"SongId" integer primary key not null ,
"LrcPath" varchar ,
"Content" varchar );
CREATE TABLE "MigrationFlag" (
"Name" varchar primary key not null ,
"Value" varchar );
CREATE TABLE "PlayHistory" (
"Id" integer primary key autoincrement not null ,
"SongId" integer ,
"PlayedAt" integer ,
"PlayCount" integer );
CREATE TABLE "PlaySession" (
"Id" integer primary key autoincrement not null ,
"SongId" integer ,
"PlayedAt" integer ,
"DurationMs" integer );
CREATE TABLE "PlaylistSongs" (
"Id" integer primary key autoincrement not null ,
"PlaylistId" integer ,
"SongId" integer ,
"Position" integer );
CREATE TABLE "Playlists" (
"Id" integer primary key autoincrement not null ,
"Name" varchar ,
"CreatedAt" integer ,
"UpdatedAt" integer ,
"SongCount" integer ,
"IsSystem" integer , "SortOrder" integer);
CREATE TABLE "SongArtists" (
"Id" integer primary key autoincrement not null ,
"SongId" integer not null ,
"ArtistId" integer not null );
CREATE TABLE "Songs" (
"Id" integer primary key autoincrement not null ,
"Title" varchar not null ,
"ArtistId" integer ,
"AlbumId" integer ,
"Duration" integer ,
"FilePath" varchar not null ,
"FileSize" integer ,
"Bitrate" integer ,
"TrackNumber" integer ,
"Year" integer ,
"Genre" varchar ,
"DateAdded" integer ,
"DateModified" integer ,
"CoverArtPath" varchar ,
"LyricsPath" varchar ,
"Source" integer ,
"Protocol" integer ,
"RemoteId" varchar ,
"MediaStoreId" integer , "RemoteCoverPath" varchar);
CREATE TABLE sqlite_sequence(name,seq);
CREATE INDEX "Albums_ArtistId" on "Albums"("ArtistId");
CREATE INDEX "DailySongStat_Date" on "DailySongStat"("Date");
CREATE INDEX "HourlyPlayStat_Date" on "HourlyPlayStat"("Date");
CREATE INDEX "PlayHistory_SongId" on "PlayHistory"("SongId");
CREATE INDEX "PlaySession_PlayedAt" on "PlaySession"("PlayedAt");
CREATE INDEX "PlaySession_SongId" on "PlaySession"("SongId");
CREATE INDEX "PlaylistSongs_PlaylistId" on "PlaylistSongs"("PlaylistId");
CREATE INDEX "PlaylistSongs_SongId" on "PlaylistSongs"("SongId");
CREATE INDEX "SongArtists_ArtistId" on "SongArtists"("ArtistId");
CREATE INDEX "SongArtists_SongId" on "SongArtists"("SongId");
CREATE INDEX "Songs_AlbumId" on "Songs"("AlbumId");
CREATE INDEX "Songs_ArtistId" on "Songs"("ArtistId");
CREATE UNIQUE INDEX "Songs_FilePath" on "Songs"("FilePath");
CREATE INDEX idx_albums_artist ON Albums(ArtistId);
CREATE INDEX idx_chat_messages_id ON ChatMessageRecord(Id DESC);
CREATE INDEX idx_chat_messages_timestamp ON ChatMessageRecord(Timestamp);
CREATE INDEX idx_play_history_time ON PlayHistory(PlayedAt DESC);
CREATE INDEX idx_playlist_songs_playlist_position ON PlaylistSongs(PlaylistId, Position);
CREATE INDEX idx_song_artists_artist ON SongArtists(ArtistId);
CREATE INDEX idx_song_artists_song ON SongArtists(SongId);
CREATE INDEX idx_songs_album ON Songs(AlbumId);
CREATE INDEX idx_songs_artist ON Songs(ArtistId);
CREATE INDEX idx_songs_filepath ON Songs(FilePath);
CREATE INDEX idx_songs_protocol ON Songs(Protocol);
CREATE INDEX idx_songs_remoteid ON Songs(RemoteId);
CREATE INDEX idx_songs_source ON Songs(Source);
CREATE INDEX idx_songs_source_protocol ON Songs(Source, Protocol);
CREATE INDEX idx_songs_title ON Songs(Title);
CREATE UNIQUE INDEX ux_playlist_songs_playlist_song ON PlaylistSongs(PlaylistId, SongId);
