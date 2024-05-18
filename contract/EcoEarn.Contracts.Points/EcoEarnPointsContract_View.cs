using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContract
{
    public override Address GetAdmin(Empty input)
    {
        return State.Admin?.Value;
    }

    public override Config GetConfig(Empty input)
    {
        return State.Config?.Value;
    }

    public override DappInfo GetDappInfo(Hash input)
    {
        return IsHashValid(input) ? State.DappInfoMap[input] ?? new DappInfo() : new DappInfo();
    }

    public override GetPoolInfoOutput GetPoolInfo(Hash input)
    {
        if (!IsHashValid(input) || State.PoolInfoMap[input] == null) return new GetPoolInfoOutput();

        var info = State.PoolInfoMap[input];
        var output = new GetPoolInfoOutput
        {
            PoolInfo = info,
            Status = CheckPoolEnabled(info.Config.EndTime)
        };

        return output;
    }

    public override Address GetPoolAddress(Hash input)
    {
        return IsHashValid(input) ? CalculateVirtualAddress(input) ?? new Address() : new Address();
    }

    public override ClaimInfo GetClaimInfo(Hash input)
    {
        return IsHashValid(input) ? State.ClaimInfoMap[input] ?? new ClaimInfo() : new ClaimInfo();
    }

    public override Snapshot GetSnapshot(GetSnapshotInput input)
    {
        return input.BlockNumber > 0 && IsHashValid(input.PoolId)
            ? State.SnapshotMap[input.PoolId]?[input.BlockNumber] ?? new Snapshot()
            : new Snapshot();
    }
}