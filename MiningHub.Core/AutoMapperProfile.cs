using AutoMapper;
using MiningHub.Core.Blockchain;
using MiningHub.Core.Configuration;
using MiningHub.Core.Persistence.Model;
using MiningHub.Core.Persistence.Model.Projections;
using MinerStats = MiningHub.Core.Persistence.Model.Projections.MinerStats;

namespace MiningHub.Core
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            //////////////////////
            // outgoing mappings

            CreateMap<Blockchain.Share, Persistence.Model.Share>();

            CreateMap<Blockchain.Share, Block>()
                .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
                .ForMember(dest => dest.Hash, opt => opt.MapFrom(src => src.BlockHash))
                .ForMember(dest => dest.Status, opt => opt.Ignore());

            CreateMap<BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

            CreateMap<Mining.PoolStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            CreateMap<BlockchainStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            // API
            CreateMap<PoolConfig, Api.Responses.PoolInfo>();
            CreateMap<PoolStats, Api.Responses.PoolInfo>();
            CreateMap<PoolStats, Api.Responses.AggregatedPoolStats>();
            CreateMap<Block, Api.Responses.Block>();
            CreateMap<Payment, Api.Responses.Payment>();
            CreateMap<BalanceChange, Api.Responses.BalanceChange>();
            CreateMap<CoinConfig, Api.Responses.ApiCoinConfig>();
            CreateMap<PoolPaymentProcessingConfig, Api.Responses.ApiPoolPaymentProcessingConfig>();

            CreateMap<MinerStats, Api.Responses.MinerStats>()
                .ForMember(dest => dest.LastPayment, opt => opt.Ignore())
                .ForMember(dest => dest.LastPaymentLink, opt => opt.Ignore());

            CreateMap<WorkerPerformanceStats, Api.Responses.WorkerPerformanceStats>();
            CreateMap<WorkerPerformanceStatsContainer, Api.Responses.WorkerPerformanceStatsContainer>();

            // PostgreSQL
            CreateMap<Persistence.Model.Share, Persistence.Postgres.Entities.Share>();
            CreateMap<Block, Persistence.Postgres.Entities.Block>();
            CreateMap<Balance, Persistence.Postgres.Entities.Balance>();
            CreateMap<Payment, Persistence.Postgres.Entities.Payment>();
            CreateMap<PoolStats, Persistence.Postgres.Entities.PoolStats>();

            CreateMap<MinerWorkerPerformanceStats, Persistence.Postgres.Entities.MinerWorkerPerformanceStats>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());

            //////////////////////
            // incoming mappings

            // PostgreSQL
            CreateMap<Persistence.Postgres.Entities.Share, Persistence.Model.Share>();
            CreateMap<Persistence.Postgres.Entities.Block, Block>();
            CreateMap<Persistence.Postgres.Entities.Balance, Balance>();
            CreateMap<Persistence.Postgres.Entities.Payment, Payment>();
            CreateMap<Persistence.Postgres.Entities.BalanceChange, BalanceChange>();
            CreateMap<Persistence.Postgres.Entities.PoolStats, PoolStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, MinerWorkerPerformanceStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, Api.Responses.MinerPerformanceStats>();

            CreateMap<PoolStats, Mining.PoolStats>();
            CreateMap<BlockchainStats, Mining.PoolStats>();

            CreateMap<PoolStats, BlockchainStats>()
                .ForMember(dest => dest.RewardType, opt => opt.Ignore())
                .ForMember(dest => dest.NetworkType, opt => opt.Ignore());
        }
    }
}
