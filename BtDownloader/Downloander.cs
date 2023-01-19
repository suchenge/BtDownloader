using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MonoTorrent;
using MonoTorrent.Client;

namespace BtDownloader
{
    internal class Downloander : IDisposable
    {
        private ClientEngine engine;
        private List<TorrentDownloader> torrentDownloaders = new List<TorrentDownloader>();

        public Downloander()
        {
            var settingBuilder = new EngineSettingsBuilder
            {
                AllowPortForwarding = true,
                AutoSaveLoadDhtCache = true,
                AutoSaveLoadFastResume = true,
                AutoSaveLoadMagnetLinkMetadata = true,
                ListenPort = 55123,
                DhtPort = 55123,
                CacheDirectory = @"E:\Movie\temp.file"
            };

            this.engine = new ClientEngine(settingBuilder.ToSettings());
        }

        public async Task AddTorrent(string torrentFilePath, string downloadFilePath)
        {
            var torrentDownloader = new TorrentDownloader(this.engine);
            await torrentDownloader.AddTask(torrentFilePath, downloadFilePath);
            torrentDownloaders.Add(torrentDownloader);
        }

        public async Task Start()
        {
            await torrentDownloaders.First().DownloadAsync();

            foreach (var manager in this.engine.Torrents)
            {
                var stoppingTask = manager.StopAsync();

                while (manager.State != TorrentState.Stopped)
                {
                    Console.WriteLine("{0} is {1}", manager.Torrent.Name, manager.State);
                    await Task.WhenAll(stoppingTask, Task.Delay(250));
                }

                await stoppingTask;

                if (this.engine.Settings.AutoSaveLoadFastResume)
                    Console.WriteLine($"FastResume data for {manager.Torrent?.Name ?? manager.InfoHash.ToHex()} has been written to disk.");
            }

            if (this.engine.Settings.AutoSaveLoadDhtCache)
                Console.WriteLine($"DHT cache has been written to disk.");

            if (this.engine.Settings.AllowPortForwarding)
                Console.WriteLine("uPnP and NAT-PMP port mappings have been removed");
        }

        public void Dispose()
        {
            this.engine.Dispose();
        }
    }
}
