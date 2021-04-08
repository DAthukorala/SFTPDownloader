using Microsoft.Extensions.Configuration;
using Polly;
using Rebex.IO;
using Rebex.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Flurl;
using System.Diagnostics;

namespace SFTPDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            //move files after download to another folder (after all secondary server backups are completed)
            //sync the same folder structure


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
            Console.WriteLine($"Connecting to server {config["Server"]}");
            await client.ConnectAsync(config["Server"]);
            Console.WriteLine("Logging in...");
            await client.LoginAsync(config["UserName"], config["Password"]);
            Console.WriteLine("Logged In");
            return client;
        }
        private async static Task Download(Sftp client, IConfigurationRoot config)
        {
            var remotePath = config["RemotePath"];
            var localPath = config["LocalPath"];
            var fileType = config["FileType"];
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            Console.WriteLine("Fetching directory names...");
            var directoryNames = client.GetList($"{remotePath}/").Cast<SftpItem>().Where(item => item.IsDirectory).Select(item => item.Name);

            var bulkhead = Policy.BulkheadAsync(100, int.MaxValue);
            var tasks = new List<Task>();
            long downloadSize = 0;
            foreach (var name in directoryNames)
            {
                var remoteDirPath = Url.Combine(remotePath, name).ToString();
                var localDirPath = Path.Combine(localPath, name).ToString();

                Console.WriteLine($"Fetching file names for {remoteDirPath}");
                var pattern = new FileSet(remoteDirPath, $"*.{fileType}", TraversalMode.MatchFilesShallow);
                var fileInfo = await client.GetItemsAsync(pattern);
                foreach (var info in fileInfo)
                {
                    if (!info.IsDirectory)
                    {
                        var fullFileName = info.Name;
                        var remoteFilePath = Url.Decode(Url.Combine(remoteDirPath, fullFileName), false).ToString();
                        var localFilePath = Path.Combine(localDirPath, fullFileName).ToString();

                        var task = bulkhead.ExecuteAsync(async () =>
                        {
                            var bytesDownloaded = await client.GetFileAsync(remoteFilePath, localFilePath);
                            downloadSize += bytesDownloaded;
                            Console.WriteLine($"Download from {remoteFilePath} to ${localFilePath}, size: {bytesDownloaded}");

                        });
                        tasks.Add(task);
                    }
                }
            }

            await Task.WhenAll(tasks);
            stopWatch.Stop();
            var elapsedTime = stopWatch.Elapsed;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Download Time {elapsedTime.Hours}h:{elapsedTime.Minutes}m:{elapsedTime.Seconds}s:{elapsedTime.Milliseconds}ms");
            Console.WriteLine($"Download Size: {downloadSize}");
            Console.ResetColor();
            Console.Read();
        }
    }
}
