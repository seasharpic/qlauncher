using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using fNbt;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public interface IServerStatusService
    {
        Task<ServerStatus> GetStatusAsync(string ip);
        Task<List<ServerItem>> LoadAndPingServersAsync(string gamePath);
        void InjectServersIfEnabled(string gamePath, bool autoAddServers);
        void RemoveServer(string gamePath, string ip);
        void AddServer(string gamePath, string name, string ip);
    }

    public class ServerStatusService : IServerStatusService
    {
        public static ServerStatusService Instance { get; } = new ServerStatusService();

        public async Task<ServerStatus> GetStatusAsync(string ip)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string url = $"https://api.mcstatus.io/v2/status/java/{ip}";

                string response = await client.GetStringAsync(url);
                sw.Stop();

                using JsonDocument doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.GetProperty("online").GetBoolean())
                    return new ServerStatus { Online = false };

                return new ServerStatus
                {
                    Online = true,
                    PlayersNow = root.GetProperty("players").GetProperty("online").GetInt32(),
                    PlayersMax = root.GetProperty("players").GetProperty("max").GetInt32(),
                    Version = root.GetProperty("version").GetProperty("name_clean").GetString() ?? "",
                    Motd = root.GetProperty("motd").GetProperty("clean").GetString() ?? "",
                    PingMs = sw.ElapsedMilliseconds
                };
            }
            catch
            {
                return new ServerStatus { Online = false };
            }
        }

        public async Task<List<ServerItem>> LoadAndPingServersAsync(string gamePath)
        {
            var uiServers = new List<ServerItem>();
            string serversFile = Path.Combine(gamePath, "servers.dat");

            if (File.Exists(serversFile))
            {
                try
                {
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(serversFile);
                    var serversList = nbtFile.RootTag.Get<NbtList>("servers");

                    if (serversList != null)
                    {
                        foreach (NbtCompound serverTag in serversList)
                        {
                            string name = serverTag.Get<NbtString>("name")?.Value ?? "Minecraft Server";
                            string ip = serverTag.Get<NbtString>("ip")?.Value ?? "";

                            if (!string.IsNullOrEmpty(ip))
                            {
                                uiServers.Add(new ServerItem { Name = name, Ip = ip });
                            }
                        }
                    }
                }
                catch { }
            }

            if (uiServers.Count == 0)
            {
                return uiServers;
            }

            foreach (var srv in uiServers)
            {
                var status = await GetStatusAsync(srv.Ip);
                if (status.Online)
                {
                    srv.OnlineText = $"{status.PlayersNow}/{status.PlayersMax}";
                    srv.Version = status.Version;

                    if (status.PingMs >= 0)
                    {
                        srv.PingText = $"{status.PingMs} ms";
                        if (status.PingMs < 60)
                            srv.PingColor = new SolidColorBrush(Color.FromRgb(80, 220, 100));
                        else if (status.PingMs < 160)
                            srv.PingColor = new SolidColorBrush(Color.FromRgb(240, 200, 50));
                        else
                            srv.PingColor = new SolidColorBrush(Color.FromRgb(240, 80, 80));
                    }

                    double percentage = status.PlayersMax > 0 ? (double)status.PlayersNow / status.PlayersMax : 0;
                    srv.ProgressWidth = 200 * percentage;
                }
                else
                {
                    srv.OnlineText = "Offline";
                    srv.Version = "Нет связи";
                    srv.ProgressWidth = 0;
                    srv.PingText = "-";
                    srv.PingColor = new SolidColorBrush(Color.FromRgb(150, 150, 150));
                }
            }

            return uiServers;
        }

        public void InjectServersIfEnabled(string gamePath, bool autoAddServers)
        {
            if (!autoAddServers) return;

            try
            {
                string serversFile = Path.Combine(gamePath, "servers.dat");
                NbtFile nbtFile = new NbtFile();
                NbtList serversList;

                if (File.Exists(serversFile))
                {
                    nbtFile.LoadFromFile(serversFile);
                    serversList = nbtFile.RootTag.Get<NbtList>("servers") ?? new NbtList("servers", NbtTagType.Compound);
                    if (nbtFile.RootTag.Get("servers") == null) nbtFile.RootTag.Add(serversList);
                }
                else
                {
                    nbtFile.RootTag = new NbtCompound("");
                    serversList = new NbtList("servers", NbtTagType.Compound);
                    nbtFile.RootTag.Add(serversList);
                }

                const string targetIp = "play.scraft.ru";
                const string targetName = "SCRAFT Server";

                bool exists = false;
                foreach (NbtCompound server in serversList)
                {
                    if (server.Get<NbtString>("ip")?.Value == targetIp)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    var newServer = new NbtCompound
                    {
                        new NbtString("ip", targetIp),
                        new NbtString("name", targetName),
                        new NbtByte("acceptTextures", 1)
                    };
                    serversList.Add(newServer);

                    nbtFile.SaveToFile(serversFile, NbtCompression.None);
                }
            }
            catch { }
        }

        public void RemoveServer(string gamePath, string ip)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(ip)) return;

            try
            {
                string serversFile = Path.Combine(gamePath, "servers.dat");
                if (!File.Exists(serversFile)) return;

                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(serversFile);
                var serversList = nbtFile.RootTag.Get<NbtList>("servers");
                if (serversList != null)
                {
                    for (int i = serversList.Count - 1; i >= 0; i--)
                    {
                        if (serversList[i] is NbtCompound compound &&
                            string.Equals(compound.Get<NbtString>("ip")?.Value, ip, StringComparison.OrdinalIgnoreCase))
                        {
                            serversList.RemoveAt(i);
                        }
                    }

                    nbtFile.SaveToFile(serversFile, NbtCompression.None);
                }
            }
            catch { }
        }

        public void AddServer(string gamePath, string name, string ip)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(ip)) return;
            if (string.IsNullOrWhiteSpace(name)) name = ip;

            try
            {
                string serversFile = Path.Combine(gamePath, "servers.dat");
                NbtFile nbtFile = new NbtFile();
                NbtList serversList;

                if (File.Exists(serversFile))
                {
                    nbtFile.LoadFromFile(serversFile);
                    serversList = nbtFile.RootTag.Get<NbtList>("servers") ?? new NbtList("servers", NbtTagType.Compound);
                    if (nbtFile.RootTag.Get("servers") == null) nbtFile.RootTag.Add(serversList);
                }
                else
                {
                    nbtFile.RootTag = new NbtCompound("");
                    serversList = new NbtList("servers", NbtTagType.Compound);
                    nbtFile.RootTag.Add(serversList);
                }

                bool found = false;
                foreach (NbtCompound srv in serversList)
                {
                    if (string.Equals(srv.Get<NbtString>("ip")?.Value, ip, StringComparison.OrdinalIgnoreCase))
                    {
                        srv["name"] = new NbtString("name", name);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    var compound = new NbtCompound
                    {
                        new NbtString("ip", ip),
                        new NbtString("name", name),
                        new NbtByte("acceptTextures", 1)
                    };
                    serversList.Add(compound);
                }

                nbtFile.SaveToFile(serversFile, NbtCompression.None);
            }
            catch { }
        }
    }
}
