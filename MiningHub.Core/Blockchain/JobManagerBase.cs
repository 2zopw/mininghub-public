using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningHub.Core.Configuration;
using MiningHub.Core.Extensions;
using MiningHub.Core.Util;
using NetMQ;
using NetMQ.Sockets;
using NLog;
using Contract = MiningHub.Core.Contracts.Contract;

namespace MiningHub.Core.Blockchain
{
    public abstract class JobManagerBase<TJob>
    {
        protected JobManagerBase(IComponentContext ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            this.ctx = ctx;
        }

        protected readonly IComponentContext ctx;
        protected ClusterConfig clusterConfig;

        protected TJob currentJob;
        private int jobId;
        protected object jobLock = new object();
        protected ILogger logger;
        protected PoolConfig poolConfig;
        protected bool hasInitialBlockTemplate = false;
        protected Subject<Unit> blockSubmissionSubject = new Subject<Unit>();
        protected TimeSpan btStreamReceiveTimeout = TimeSpan.FromSeconds(60 * 10);

        protected virtual string LogCat { get; } = "Job Manager";

        protected abstract void ConfigureDaemons();

        protected virtual async Task StartDaemonAsync(CancellationToken ct)
        {
            while(!await AreDaemonsHealthyAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }

            logger.Info(() => $"[{LogCat}] All daemons online");

            while(!await AreDaemonsConnectedAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to connect to peers ...");

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        protected string NextJobId(string format = null)
        {
            Interlocked.Increment(ref jobId);
            var value = Interlocked.CompareExchange(ref jobId, 0, Int32.MinValue);

            if (format != null)
                return value.ToString(format);

            return value.ToStringHex8();
        }

        protected IObservable<string> BtStreamSubscribe(ZmqPubSubEndpointConfig config)
        {
            return Observable.Defer(() => Observable.Create<string>(obs =>
            {
                var tcs = new CancellationTokenSource();

                Task.Factory.StartNew(() =>
                {
                    using(tcs)
                    {
                        while(!tcs.IsCancellationRequested)
                        {
                            try
                            {
                                using(var subSocket = new SubscriberSocket())
                                {
                                    //subSocket.Options.ReceiveHighWatermark = 1000;
                                    subSocket.Connect(config.Url);
                                    subSocket.Subscribe(config.Topic);

                                    logger.Debug($"Subscribed to {config.Url}/{config.Topic}");

                                    while(!tcs.IsCancellationRequested)
                                    {
                                        var msg = (NetMQMessage) null;

                                        if (!subSocket.TryReceiveMultipartMessage(btStreamReceiveTimeout, ref msg, 4))
                                        {
                                            logger.Warn(() => $"Timeout receiving message from {config.Url}. Reconnecting ...");
                                            break;
                                        }

                                        // extract frames
                                        var topic = msg.Pop().ConvertToString(Encoding.UTF8);
                                        var flags = msg.Pop().ConvertToInt32();
                                        var data = msg.Pop().ToByteArray();
                                        var timestamp = msg.Pop().ConvertToInt64();

                                        // compressed
                                        if ((flags & 1) == 1)
                                        {
                                            using(var stm = new MemoryStream(data))
                                            {
                                                using(var stmOut = new MemoryStream())
                                                {
                                                    using(var ds = new DeflateStream(stm, CompressionMode.Decompress))
                                                    {
                                                        ds.CopyTo(stmOut);
                                                    }

                                                    data = stmOut.ToArray();
                                                }
                                            }
                                        }

                                        // convert
                                        var json = Encoding.UTF8.GetString(data);

                                        obs.OnNext(json);
                                    }
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex);
                            }

                            // do not consume all CPU cycles in case of a long lasting error condition
                            Thread.Sleep(1000);
                        }
                    }
                }, tcs.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                return Disposable.Create(() =>
                {
                    tcs.Cancel();
                });
            }))
            .Publish()
            .RefCount();
        }

        protected abstract Task<bool> AreDaemonsHealthyAsync();
        protected abstract Task<bool> AreDaemonsConnectedAsync();
        protected abstract Task EnsureDaemonsSynchedAsync(CancellationToken ct);
        protected abstract Task PostStartInitAsync(CancellationToken ct);

        #region API-Surface

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<TJob>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            ConfigureDaemons();
        }

        public async Task StartAsync(CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            await StartDaemonAsync(ct);
            await EnsureDaemonsSynchedAsync(ct);
            await PostStartInitAsync(ct);

            logger.Info(() => $"[{LogCat}] Online");
        }

        #endregion // API-Surface
    }
}
