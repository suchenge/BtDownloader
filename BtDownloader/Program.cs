// See https://aka.ms/new-console-template for more information
using BtDownloader;

Console.WriteLine("Hello, World!");

var downloader = new Downloander();
await downloader.AddTorrent(@"E:\downfile2.torrent", @"E:\Movie");
await downloader.Start();
downloader.Dispose();