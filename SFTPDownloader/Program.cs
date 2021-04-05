using Microsoft.Extensions.Configuration;
using Polly;
using Rebex.IO;
using Rebex.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SFTPDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var config = SetupConfiguration();
            Rebex.Licensing.Key = config["Key"];

            using var client = await Connect(config);
            await Download(client, config);
        }

        private static IConfigurationRoot SetupConfiguration()
        {
            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", true, true);
            var configuration = builder.Build();
            return configuration;
        }

        private async static Task<Sftp> Connect(IConfigurationRoot config)
        {
            var client = new Sftp();
            await client.ConnectAsync(config["Server"]);
            await client.LoginAsync(config["UserName"], config["Password"]);
            return client;
        }
        private async static Task Download(Sftp client, IConfigurationRoot config)
        {
            var remotePath = config["RemotePath"];
            var localPath = config["LocalPath"];
            var fileInfo = await client.GetItemsAsync($"{remotePath}/*", TraversalMode.MatchFilesShallow);
            var bulkhead = Policy.BulkheadAsync(100, int.MaxValue);
            var tasks = new List<Task>();
            foreach (var info in fileInfo)
            {
                var task = bulkhead.ExecuteAsync(async () =>
                {
                    var bytesDownloaded = await client.GetFileAsync($"{remotePath}/{info.Name}", @$"{localPath}\{info.Name}");
                    Console.WriteLine($"bytesDownloaded: {bytesDownloaded}");

                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }
    }
}
