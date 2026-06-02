// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2.CDN;

namespace DepotDownloader
{
    /// <summary>
    /// CDNClientPool provides a pool of connections to CDN endpoints, requesting CDN tokens as needed
    /// </summary>
    class CDNClientPool
    {
        private readonly Steam3Session steamSession;
        private readonly uint appId;
        public Client CDNClient { get; }
        public Server ProxyServer { get; private set; }

        private readonly List<Server> servers = [];
        private int nextServer;
        private readonly object _serverLock = new();

        public CDNClientPool(Steam3Session steamSession, uint appId)
        {
            this.steamSession = steamSession;
            this.appId = appId;
            CDNClient = new Client(steamSession.steamClient);
        }

        public async Task UpdateServerList()
        {
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

        public Server GetConnection()
        {
            lock (_serverLock)
            {
                if (servers.Count == 0) return null;
                return servers[nextServer % servers.Count];
            }
        }

        public void ReturnBrokenConnection(Server server)
        {
            if (server == null) return;

            lock (_serverLock)
            {
                if (servers.Count > 0 && servers[nextServer % servers.Count] == server)
                {
                    nextServer++;
                }

                AccountSettingsStore.Instance.ContentServerPenalty.TryGetValue(server.Host, out var penalty);
                AccountSettingsStore.Instance.ContentServerPenalty[server.Host] = penalty + 100;
                AccountSettingsStore.Save();
            }
        }
    }
}
