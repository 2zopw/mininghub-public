using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningHub.Core.Banning;
using MiningHub.Core.Blockchain;
using MiningHub.Core.Configuration;
using MiningHub.Core.Extensions;
using MiningHub.Core.Messaging;
using MiningHub.Core.Notifications;
using MiningHub.Core.Persistence;
using MiningHub.Core.Persistence.Repositories;
using MiningHub.Core.Stratum;
using MiningHub.Core.Time;
using MiningHub.Core.Util;
using Newtonsoft.Json;
using NLog;
using Contract = MiningHub.Core.Contracts.Contract;

namespace MiningHub.Core.Mining
{
    public abstract class PoolBase : StratumServer,
        IMiningPool
    {
        protected PoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            NotificationService notificationService) : base(ctx, clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.notificationService = notificationService;
        }

        protected PoolStats poolStats = new PoolStats();
        protected readonly JsonSerializerSettings serializerSettings;
        protected readonly NotificationService notificationService;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        protected readonly IMapper mapper;
        protected readonly IMessageBus messageBus;
        protected readonly CompositeDisposable disposables = new CompositeDisposable();
        protected BlockchainStats blockchainStats;
        protected PoolConfig poolConfig;
        protected const int VarDiffSampleCount = 32;
        protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
        protected static readonly Regex regexStaticDiff = new Regex(@"d=(\d*(\.\d+)?)", RegexOptions.Compiled);
        protected const string PasswordControlVarsSeparator = ";";

        protected override string LogCat => "Pool";

        protected abstract Task SetupJobManager(CancellationToken ct);
        protected abstract WorkerContextBase CreateClientContext();

        protected double? GetStaticDiffFromPassparts(string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return null;

            foreach(var part in parts)
            {
                var m = regexStaticDiff.Match(part);

                if (m.Success)
                {
                    var str = m.Groups[1].Value.Trim();
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var diff))
                        return diff;
                }
            }

            return null;
        }

        protected override void OnConnect(StratumClient client)
        {
            // client setup
            var context = CreateClientContext();

            var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, poolConfig.EnableInternalStratum == true ? poolEndpoint.VarDiff : null, clock);
            client.SetContext(context);

            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(client);
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            Observable.Timer(clock.Now.AddSeconds(10))
                .Take(1)
                .Subscribe(_ =>
                {
                    if (!client.LastReceive.HasValue)
                    {
                        //logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        protected void SetupBanning(ClusterConfig clusterConfig)
        {
            if (poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual void InitStats()
        {
            if(clusterConfig.ShareRelay == null)
                LoadStats();
        }

        private void LoadStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Loading pool stats");

                var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, poolConfig.Id));

                if (stats != null)
                {
                    poolStats = mapper.Map<PoolStats>(stats);
                    blockchainStats = mapper.Map<BlockchainStats>(stats);
                }
            }

            catch (Exception ex)
            {
                logger.Warn(ex, () => $"[{LogCat}] Unable to load pool stats");
            }
        }

        protected void ConsiderBan(StratumClient client, WorkerContextBase context, PoolShareBasedBanningConfig config)
        {
            var totalShares = context.Stats.ValidShares + context.Stats.InvalidShares;

            if (totalShares > config.CheckThreshold)
            {
                var ratioBad = (double) context.Stats.InvalidShares / totalShares;

                if (ratioBad < config.InvalidPercent / 100.0)
                {
                    // reset stats
                    context.Stats.ValidShares = 0;
                    context.Stats.InvalidShares = 0;
                }

                else
                {
                    if (poolConfig.Banning?.Enabled == true &&
                        (clusterConfig.Banning?.BanOnInvalidShares.HasValue == false ||
                         clusterConfig.Banning?.BanOnInvalidShares == true))
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                        banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                        DisconnectClient(client);
                    }
                }
            }
        }

        private (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if (!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return (new IPEndPoint(listenAddress, port), pep.TcpProxyProtocol);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Id}
Coin Type:              {poolConfig.Coin.Type}
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashrate(blockchainStats.NetworkHashrate)}
Stratum Port(s):        {(poolConfig.Ports?.Any() == true ? string.Join(", ", poolConfig.Ports.Keys) : string.Empty )}
Pool Fee:               {(poolConfig.RewardRecipients?.Any() == true ? poolConfig.RewardRecipients.Sum(x => x.Percentage) : 0)}%
";

            logger.Info(() => msg);
        }

        #region API-Surface

        public PoolConfig Config => poolConfig;
        public PoolStats PoolStats => poolStats;
        public BlockchainStats NetworkStats => blockchainStats;

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
        }

        public abstract double HashrateFromShares(double shares, double interval);

        public virtual async Task StartAsync(CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            try
            {
	            SetupBanning(clusterConfig);
	            await SetupJobManager(ct);
                InitStats();

                if (poolConfig.EnableInternalStratum == true)
	            {
		            var ipEndpoints = poolConfig.Ports.Keys
			            .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
			            .ToArray();

		            StartListeners(poolConfig.Id, ipEndpoints);
	            }

                logger.Info(() => $"[{LogCat}] Online");
                OutputPoolInfo();
            }

            catch(PoolStartupAbortException)
            {
                // just forward these
                throw;
            }

            catch (TaskCanceledException)
            {
                // just forward these
                throw;
            }

            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

	    #endregion // API-Surface
    }
}
