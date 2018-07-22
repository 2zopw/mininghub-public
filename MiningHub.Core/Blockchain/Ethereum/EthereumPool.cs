using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningHub.Core.Blockchain.Ethereum.Configuration;
using MiningHub.Core.Configuration;
using MiningHub.Core.Extensions;
using MiningHub.Core.JsonRpc;
using MiningHub.Core.Messaging;
using MiningHub.Core.Mining;
using MiningHub.Core.Notifications;
using MiningHub.Core.Persistence;
using MiningHub.Core.Persistence.Repositories;
using MiningHub.Core.Stratum;
using MiningHub.Core.Time;
using MiningHub.Core.Util;
using Newtonsoft.Json;

namespace MiningHub.Core.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC, CoinType.EXP, CoinType.ELLA, CoinType.UBQ)]
    public class EthereumPool : PoolBase
    {
        public EthereumPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, notificationService)
        {
        }

        private object currentJobParams;
        private EthereumJobManager manager;

        #region Nicehash
        private void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            manager.PrepareWorker(client);

            var data = new object[]
                {
                    new object[]
                    {
                        EthereumStratumMethods.MiningNotify,
                        client.ConnectionId,
                        EthereumConstants.EthereumStratumVersion
                    },
                    context.ExtraNonce1
                }
                .ToArray();

            client.Respond(data, request.Id);

            // setup worker context
            context.IsSubscribed = true;
            context.IsNiceHashClient = true;
            //context.UserAgent = requestParams[0].Trim();
        }

        private void OnAuthorize(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);

            // extract worker/miner
            var workerParts = workerValue?.Split('.');
            var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
            var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : null;

            // assumes that workerName is an address
            context.IsAuthorized = !string.IsNullOrEmpty(minerName) && manager.ValidateAddress(minerName);
            context.MinerName = minerName.ToLower();
            context.WorkerName = workerName;
            context.IsNiceHashClient = true;

            // respond
            client.Respond(context.IsAuthorized, request.Id);

            // send the first job to the client
            EnsureInitialWorkSent(client);

            // log association
            if (!string.IsNullOrEmpty(context.WorkerName))
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] recieved Authorize command for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}");
            else
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] recieved Authorize command for {context.MinerName} from {client.RemoteEndpoint.Address}");
        }

        private void EnsureInitialWorkSent(StratumClient client)
        {
            var context = client.GetContextAs<EthereumWorkerContext>();

            lock (context)
            {
                if (context.IsAuthorized && context.IsAuthorized && !context.IsInitialWorkSent && context.IsNiceHashClient)
                {
                    context.IsInitialWorkSent = true;

                    // send intial update
                    client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty * EthereumConstants.NicehashStratumDiffFactor });
                    client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
                }
            }
        }
        #endregion

        #region Classic
        private void OnSubmitLogin(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            if (requestParams == null || requestParams.Length < 1 || requestParams.Any(string.IsNullOrEmpty))
            {
                client.RespondError(StratumError.MinusOne, "invalid request", request.Id);
                return;
            }

            manager.PrepareWorker(client);
            client.Respond(true, request.Id);

            // setup worker context
            context.IsSubscribed = true;
            context.IsAuthorized = true;
            context.MinerName = requestParams[0].Trim();

            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;

            // extract worker/miner
            var workerParts = workerValue?.Split('.');
            var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
            var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : null;

            if (!string.IsNullOrEmpty(minerName))
                context.MinerName = minerName.ToLower();
            if (!string.IsNullOrEmpty(workerName))
                context.WorkerName = workerName;

            // log association
            if (!string.IsNullOrEmpty(context.WorkerName))
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] recieved SubmitLogin command for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}");
            else
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] recieved SubmitLogin command for {context.MinerName} from {client.RemoteEndpoint.Address}");
        }

        private void OnGetWork(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            object[] newJobParams = (object[])currentJobParams;
            var header = newJobParams[2];
            var seed = newJobParams[1];
            var target = EthereumUtils.GetTargetHex(new BigInteger(context.Difficulty * EthereumConstants.StratumDiffFactor));

            client.Respond(new object[] { header, seed, target }, request.Id);
            context.IsInitialWorkSent = true;

            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;

            // log association
            if (!string.IsNullOrEmpty(context.WorkerName))
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] recieved GetWork command for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}");
            else
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] received GetWork command for {context.MinerName} from {client.RemoteEndpoint.Address}");
        }

        private void OnSubmitHashrate(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            // Dummy command, just predend like you did something with it and send true to keep the miner happy
            client.Respond(true, request.Id);

            var context = client.GetContextAs<EthereumWorkerContext>();
            if (!string.IsNullOrEmpty(context.WorkerName))
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] received SubmitHashrate command for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}");
            else
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] received SubmitHashrate command for {context.MinerName} from {client.RemoteEndpoint.Address}");
        }

        private async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                    return;
                }

                // validate worker
                if (!context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                // check request
                var submitRequest = request.ParamsAs<string[]>();

                if (submitRequest.Length != 3 ||
                    submitRequest.Any(string.IsNullOrEmpty))
                    throw new StratumException(StratumError.MinusOne, "malformed PoW result");

                // recognize activity
                context.LastActivity = clock.Now;

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, context.Difficulty, poolEndpoint.Difficulty);

                // success
                client.Respond(true, request.Id);
                messageBus.SendMessage(new ClientShare(client, share));

                // update pool stats
                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;

                if (!string.IsNullOrEmpty(context.WorkerName))
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}. Diff: {Math.Round(share.Difficulty / EthereumConstants.Pow2x32, 3)}. Shares: {context.Stats.ValidShares}");
                else
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted for {context.MinerName} from {client.RemoteEndpoint.Address}. Diff: {Math.Round(share.Difficulty / EthereumConstants.Pow2x32, 3)}. Shares: {context.Stats.ValidShares}");
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                // update client stats
                context.Stats.InvalidShares++;

                if (!string.IsNullOrEmpty(context.WorkerName))
                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected for {context.MinerName}.{context.WorkerName}: {ex.Code} - {ex.Message}. Invalid shares: {context.Stats.InvalidShares}");
                else
                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected for {context.MinerName}: {ex.Code} - {ex.Message}. Invalid shares: {context.Stats.InvalidShares}");

                // banning
                ConsiderBan(client, context, poolConfig.Banning);
            }
        }
        #endregion

        private void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;

            logger.Debug(() => $"[{LogCat}] Broadcasting new job to all connected nicehash compatible clients");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<EthereumWorkerContext>();
                if (context.IsSubscribed && context.IsAuthorized && context.IsInitialWorkSent && context.IsNiceHashClient)
                {
                    // check alive
                    var lastActivity = clock.Now - context.LastActivity;
                    if (poolConfig.ClientConnectionTimeout > 0 && lastActivity.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        DisconnectClient(client);

                        if (!string.IsNullOrEmpty(context.WorkerName))
                            logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booted zombie-worker (idle-timeout exceeded) for {context.MinerName}.{context.WorkerName} from {client.RemoteEndpoint.Address}");
                        else
                            logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booted zombie-worker (idle-timeout exceeded) for {context.MinerName} from {client.RemoteEndpoint.Address}");
                        return;
                    }

                    // send job
                    client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty * EthereumConstants.NicehashStratumDiffFactor });
                    client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);

                    // log association
                    if (!string.IsNullOrEmpty(context.WorkerName))
                        logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] sent a new job for {context.MinerName}.{context.WorkerName} to {client.RemoteEndpoint.Address}");
                    else
                        logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] sent a new job for {context.MinerName} to {client.RemoteEndpoint.Address}");
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<EthereumJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs.Subscribe(OnNewJob));

                // we need work before opening the gates
                await manager.Jobs.Take(1).ToTask(ct);
            }
        }

        protected override void InitStats()
        {
            base.InitStats();
            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new EthereumWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch (request.Method)
            {
                #region Nicehash
                case EthereumStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case EthereumStratumMethods.Authorize:
                    OnAuthorize(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case EthereumStratumMethods.ExtraNonceSubscribe:
                    // unsupported
                    break;
                #endregion

                #region Classic
                case EthereumStratumMethods.SubmitLogin:
                    OnSubmitLogin(client, tsRequest);
                    break;

                case EthereumStratumMethods.GetWork:
                    OnGetWork(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitHasrate:
                    OnSubmitHashrate(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitWork:
                    await OnSubmitAsync(client, tsRequest);
                    break;
                #endregion

                default:
                    logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var result = shares / interval;
            return result;
        }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            // validate mandatory extra config
            var extraConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();
            if (clusterConfig.PaymentProcessing?.Enabled == true && extraConfig?.WalletPassword == null)
                logger.ThrowLogPoolStartupException("\"paymentProcessing.coinbasePassword\" pool-configuration property missing or empty (required for unlocking wallet during payment processing)");
        }

        #endregion // Overrides
    }
}
