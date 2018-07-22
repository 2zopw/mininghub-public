using System.Linq;
using System.Reflection;
using Autofac;
using MiningHub.Core.Api;
using MiningHub.Core.Banning;
using MiningHub.Core.Blockchain.Ethereum;
using MiningHub.Core.Configuration;
using MiningHub.Core.Messaging;
using MiningHub.Core.Mining;
using MiningHub.Core.Notifications;
using MiningHub.Core.Payments;
using MiningHub.Core.Payments.PaymentSchemes;
using MiningHub.Core.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = Autofac.Module;

namespace MiningHub.Core
{
    public class AutofacModule : Module
    {
        /// <summary>
        /// Override to add registrations to the container.
        /// </summary>
        /// <remarks>
        /// Note that the ContainerBuilder parameter is unique to this module.
        /// </remarks>
        /// <param name="builder">The builder through which components can be registered.</param>
        protected override void Load(ContainerBuilder builder)
        {
            var thisAssembly = typeof(AutofacModule).GetTypeInfo().Assembly;

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            builder.RegisterType<MessageBus>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<PayoutManager>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<StandardClock>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterType<ShareRecorder>()
                .SingleInstance();

            builder.RegisterType<ShareReceiver>()
                .SingleInstance();

            builder.RegisterType<ShareRelay>()
                .SingleInstance();

            builder.RegisterType<ApiServer>()
                .SingleInstance();

            builder.RegisterType<StatsRecorder>()
                .AsSelf();

            builder.RegisterType<NotificationService>()
                .SingleInstance();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetCustomAttributes<CoinMetadataAttribute>().Any() && t.GetInterfaces()
                    .Any(i =>
                        i.IsAssignableFrom(typeof(IMiningPool)) ||
                        i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                        i.IsAssignableFrom(typeof(IPayoutScheme))))
                .WithMetadataFrom<CoinMetadataAttribute>()
                .AsImplementedInterfaces();

            //////////////////////
            // Payment Schemes

            builder.RegisterType<PPLNSPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();

            builder.RegisterType<SoloPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.Solo)
                .SingleInstance();

            builder.RegisterType<PPSPaymentScheme>()
               .Keyed<IPayoutScheme>(PayoutScheme.PPS)
               .SingleInstance();

            //////////////////////
            // Ethereum

            builder.RegisterType<EthereumJobManager>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
