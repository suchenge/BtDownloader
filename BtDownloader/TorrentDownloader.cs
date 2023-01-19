using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using MonoTorrent;
using MonoTorrent.Client;

namespace BtDownloader
{
    internal class TorrentDownloader
    {
        private ClientEngine engine;
        private Top10Listener listener;
        public TorrentDownloader(ClientEngine engine)
        {
            this.engine = engine;
            this.listener = new Top10Listener(10);
        }

        public async Task AddTask(string torrentFilePath, string saveFilePath)
        {
            var settingBuilder = new TorrentSettingsBuilder()
            {
                MaximumConnections = 60
            };

            await this.engine.AddAsync(torrentFilePath, saveFilePath, settingBuilder.ToSettings());
        }

        public async Task DownloadAsync()
        {
            foreach (var manager in this.engine.Torrents)
            {
                manager.PeerConnected += (o, e) => 
                {
                    lock (this.listener)
                        this.listener.WriteLine($"Connection succeeded: {e.Peer.Uri}");
                };

                manager.ConnectionAttemptFailed += (o, e) => 
                {
                    lock (this.listener)
                        this.listener.WriteLine($"Connection failed: {e.Peer.ConnectionUri} - {e.Reason}");
                };
                // Every time a piece is hashed, this is fired.
                manager.PieceHashed += delegate (object o, PieceHashedEventArgs e) 
                {
                    lock (this.listener)
                        this.listener.WriteLine($"Piece Hashed: {e.PieceIndex} - {(e.HashPassed ? "Pass" : "Fail")}");
                };

                // Every time the state changes (Stopped -> Seeding -> Downloading -> Hashing) this is fired
                manager.TorrentStateChanged += delegate (object o, TorrentStateChangedEventArgs e)
                {
                    lock (this.listener)
                        this.listener.WriteLine($"OldState: {e.OldState} NewState: {e.NewState}");
                };

                // Every time the tracker's state changes, this is fired
                manager.TrackerManager.AnnounceComplete += (sender, e) => 
                {
                    this.listener.WriteLine($"{e.Successful}: {e.Tracker}");
                };

                await manager.StartAsync();
                await this.LinstenrState();
            }
        }

        private async Task LinstenrState()
        {
            StringBuilder sb = new StringBuilder(1024);
            while (this.engine.IsRunning)
            {
                sb.Remove(0, sb.Length);

                AppendFormat(sb, $"Transfer Rate:      {this.engine.TotalDownloadSpeed / 1024.0:0.00}kB/sec ↓ / {this.engine.TotalUploadSpeed / 1024.0:0.00}kB/sec ↑");
                AppendFormat(sb, $"Memory Cache:       {this.engine.DiskManager.CacheBytesUsed / 1024.0:0.00}/{this.engine.Settings.DiskCacheBytes / 1024.0:0.00} kB");
                AppendFormat(sb, $"Disk IO Rate:       {this.engine.DiskManager.ReadRate / 1024.0:0.00} kB/s read / {this.engine.DiskManager.WriteRate / 1024.0:0.00} kB/s write");
                AppendFormat(sb, $"Disk IO Total:      {this.engine.DiskManager.TotalBytesRead / 1024.0:0.00} kB read / {this.engine.DiskManager.TotalBytesWritten / 1024.0:0.00} kB written");
                AppendFormat(sb, $"Open Files:         {this.engine.DiskManager.PendingReadBytes} / {this.engine.DiskManager.TotalBytesRead}");
                AppendFormat(sb, $"Open Connections:   {this.engine.ConnectionManager.OpenConnections}");

                // Print out the port mappings
                foreach (var mapping in this.engine.PortMappings.Created)
                    AppendFormat(sb, $"Successful Mapping    {mapping.PublicPort}:{mapping.PrivatePort} ({mapping.Protocol})");
                foreach (var mapping in this.engine.PortMappings.Failed)
                    AppendFormat(sb, $"Failed mapping:       {mapping.PublicPort}:{mapping.PrivatePort} ({mapping.Protocol})");
                foreach (var mapping in this.engine.PortMappings.Pending)
                    AppendFormat(sb, $"Pending mapping:      {mapping.PublicPort}:{mapping.PrivatePort} ({mapping.Protocol})");

                foreach (TorrentManager manager in this.engine.Torrents)
                {
                    AppendSeparator(sb);
                    AppendFormat(sb, $"State:              {manager.State}");
                    AppendFormat(sb, $"Name:               {(manager.Torrent == null ? "MetaDataMode" : manager.Torrent.Name)}");
                    AppendFormat(sb, $"Progress:           {manager.Progress:0.00}");
                    AppendFormat(sb, $"Transferred:        {manager.Monitor.DataBytesDownloaded / 1024.0 / 1024.0:0.00} MB ↓ / {manager.Monitor.DataBytesUploaded / 1024.0 / 1024.0:0.00} MB ↑");
                    AppendFormat(sb, $"Tracker Status");
                    foreach (var tier in manager.TrackerManager.Tiers)
                        AppendFormat(sb, $"\t{tier.ActiveTracker} : Announce Succeeded: {tier.LastAnnounceSucceeded}. Scrape Succeeded: {tier.LastScrapeSucceeded}.");

                    if (manager.PieceManager != null)
                        AppendFormat(sb, "Current Requests:   {0}", await manager.PieceManager.CurrentRequestCountAsync());

                    var peers = await manager.GetPeersAsync();
                    AppendFormat(sb, "Outgoing:");
                    foreach (PeerId p in peers.Where(t => t.ConnectionDirection == Direction.Outgoing))
                    {
                        AppendFormat(sb, "\t{2} - {1:0.00}/{3:0.00}kB/sec - {0} - {4} ({5})", p.Uri,
                                                                                    p.Monitor.DownloadSpeed / 1024.0,
                                                                                    p.AmRequestingPiecesCount,
                                                                                    p.Monitor.UploadSpeed / 1024.0,
                                                                                    p.EncryptionType,
                                                                                    string.Join("|", p.SupportedEncryptionTypes.Select(t => t.ToString()).ToArray()));
                    }
                    AppendFormat(sb, "");
                    AppendFormat(sb, "Incoming:");
                    foreach (PeerId p in peers.Where(t => t.ConnectionDirection == Direction.Incoming))
                    {
                        AppendFormat(sb, "\t{2} - {1:0.00}/{3:0.00}kB/sec - {0} - {4} ({5})", p.Uri,
                                                                                    p.Monitor.DownloadSpeed / 1024.0,
                                                                                    p.AmRequestingPiecesCount,
                                                                                    p.Monitor.DownloadSpeed / 1024.0,
                                                                                    p.EncryptionType,
                                                                                    string.Join("|", p.SupportedEncryptionTypes.Select(t => t.ToString()).ToArray()));
                    }

                    AppendFormat(sb, "", null);
                    if (manager.Torrent != null)
                        foreach (var file in manager.Files)
                            AppendFormat(sb, "{1:0.00}% - {0}", file.Path, file.BitField.PercentComplete);
                }
                Console.Clear();
                Console.WriteLine(sb.ToString());
                this.listener.ExportTo(Console.Out);

                await Task.Delay(5000);
            }
        }

        private void AppendSeparator(StringBuilder sb)
        {
            AppendFormat(sb, "");
            AppendFormat(sb, "- - - - - - - - - - - - - - - - - - - - - - - - - - - - - -");
            AppendFormat(sb, "");
        }

        private void AppendFormat(StringBuilder sb, string str, params object[] formatting)
        {
            if (formatting != null && formatting.Length > 0)
                sb.AppendFormat(str, formatting);
            else
                sb.Append(str);
            sb.AppendLine();
        }
    }
}
