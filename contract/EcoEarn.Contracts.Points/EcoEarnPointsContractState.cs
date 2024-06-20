using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }
    public SingletonState<Address> Admin { get; set; }
    public SingletonState<Config> Config { get; set; }

    // <DappId, DappInfo>
    public MappedState<Hash, DappInfo> DappInfoMap { get; set; }

    // <PoolId, PoolInfo>
    public MappedState<Hash, PoolInfo> PoolInfoMap { get; set; }

    // <DappId, PointsName, PoolId>
    public MappedState<Hash, string, Hash> PointsNameMap { get; set; }

    // <PoolId, BlockNumber, PoolInfo>
    public MappedState<Hash, long, Snapshot> SnapshotMap { get; set; }

    // <ClaimId, ClaimInfo>
    public MappedState<Hash, ClaimInfo> ClaimInfoMap { get; set; }
    public MappedState<Hash, bool> SignatureMap { get; set; }
}