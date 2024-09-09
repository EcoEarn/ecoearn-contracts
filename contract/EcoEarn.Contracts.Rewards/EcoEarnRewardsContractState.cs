using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }
    public SingletonState<Address> Admin { get; set; }
    public SingletonState<Config> Config { get; set; }

    // <DappId, DappInfo>
    public MappedState<Hash, DappInfo> DappInfoMap { get; set; }

    // <ClaimId, ClaimInfo>
    public MappedState<Hash, ClaimInfo> ClaimInfoMap { get; set; }
    // SignatureHash
    public MappedState<Hash, bool> SignatureMap { get; set; }
    // <LiquidityId, LiquidityInfo>
    public MappedState<Hash, LiquidityInfo> LiquidityInfoMap { get; set; }
    
    public MappedState<Address, bool> JoinRecord { get; set; }
    public SingletonState<PointsContractConfig> PointsContractConfig { get; set; }
}