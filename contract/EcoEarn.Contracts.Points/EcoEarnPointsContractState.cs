using AElf.Sdk.CSharp.State;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

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

    public MappedState<Hash, bool> SignatureMap { get; set; }

    // <PoolId, User Address, LastClaimTime>
    public MappedState<Hash, Address, Timestamp> ClaimTimeMap { get; set; }

    // <PoolId, PoolData>
    public MappedState<Hash, PoolData> PoolDataMap { get; set; }
}