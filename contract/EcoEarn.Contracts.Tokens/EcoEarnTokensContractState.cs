using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }
    public SingletonState<Address> Admin { get; set; }

    public SingletonState<Config> Config { get; set; }

    // <DappId, DappInfo>
    public MappedState<Hash, DappInfo> DappInfoMap { get; set; }

    // <DappId, count>
    public MappedState<Hash, long> PoolCountMap { get; set; }

    // <PoolId, PoolInfo>
    public MappedState<Hash, PoolInfo> PoolInfoMap { get; set; }

    // <PoolId, PoolData>
    public MappedState<Hash, PoolData> PoolDataMap { get; set; }

    // <StakeId, StakeInfo>
    public MappedState<Hash, StakeInfo> StakeInfoMap { get; set; }

    // <PoolId, UserAddress, StakeId>
    public MappedState<Hash, Address, Hash> UserStakeIdMap { get; set; }

    // <PoolId, UserAddress, long>
    public MappedState<Hash, Address, long> UserStakeCountMap { get; set; }

    // <ClaimId, ClaimInfo>
    public MappedState<Hash, ClaimInfo> ClaimInfoMap { get; set; }

    // <StakeId, EarlyStakeInfo>
    public MappedState<Hash, EarlyStakeInfo> EarlyStakeInfoMap { get; set; }
}