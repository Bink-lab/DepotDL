// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2.CDN;

namespace DepotDownloader
{
    /// <summary>
    /// CDNClientPool provides a pool of connections to CDN endpoints, requesting CDN tokens as needed
    /// </summary>
    class CDNClientPool
    {
        private const double PenaltyDecayFactor = 0.75;
        private const int PenaltyDecayFloor = 5;

        private readonly Steam3Session steamSession;
        private readonly uint appId;
        public Client CDNClient { get; }
        public Server ProxyServer { get; private set; }

        private readonly List<Server> servers = [];
        private int nextServer = -1;
        private readonly object _serverLock = new();

        public CDNClientPool(Steam3Session steamSession, uint appId)
        {
            this.steamSession = steamSession;
            this.appId = appId;
            CDNClient = new Client(steamSession.steamClient);
        }

        public async Task UpdateServerList()
        {
            DecayServerPenalties();

            var servers = await this.steamSession.steamContent.GetServersForSteamPipe();

            ProxyServer = servers.Where(x => x.UseAsProxy).FirstOrDefault();

            var weightedCdnServers = servers
                .Where(server =>
                {
                    var isEligibleForApp = server.AllowedAppIds.Length == 0 || server.AllowedAppIds.Contains(appId);
                    return isEligibleForApp && (server.Type == "SteamCache" || server.Type == "CDN");
                })
                .Select(server =>
                {
                    AccountSettingsStore.Instance.ContentServerPenalty.TryGetValue(server.Host, out var penalty);

                    return (server, penalty);
                })
                .OrderBy(pair => pair.penalty).ThenBy(pair => pair.server.WeightedLoad);

            foreach (var (server, weight) in weightedCdnServers)
            {
                for (var i = 0; i < server.NumEntries; i++)
                {
                    this.servers.Add(server);
                }
            }

            if (this.servers.Count == 0)
            {
                throw new Exception("Failed to retrieve any download servers.");
            }
        }

        private static void DecayServerPenalties()
        {
            if (AccountSettingsStore.Instance == null) return;

            foreach (var host in AccountSettingsStore.Instance.ContentServerPenalty.Keys.ToList())
            {
                AccountSettingsStore.Instance.ContentServerPenalty.AddOrUpdate(
                    host,
                    0,
                    (_, penalty) => (int)(penalty * PenaltyDecayFactor));
            }

            foreach (var host in AccountSettingsStore.Instance.ContentServerPenalty
                .Where(kv => kv.Value < PenaltyDecayFloor)
                .Select(kv => kv.Key)
                .ToList())
            {
                AccountSettingsStore.Instance.ContentServerPenalty.TryRemove(host, out _);
            }
        }

        public Server GetConnection()
        {
            lock (_serverLock)
            {
                if (servers.Count == 0)
                {
                    throw new InvalidOperationException("No download servers available. Call UpdateServerList() first.");
                }

                var index = Interlocked.Increment(ref nextServer);
                return servers[(int)((uint)index % servers.Count)];
            }
        }

        public void ReturnBrokenConnection(Server server)
        {
            if (server == null) return;

            lock (_serverLock)
            {
                AccountSettingsStore.Instance.ContentServerPenalty.TryGetValue(server.Host, out var penalty);
                AccountSettingsStore.Instance.ContentServerPenalty[server.Host] = penalty + 100;
                AccountSettingsStore.Save();
            }
        }
    }
}
