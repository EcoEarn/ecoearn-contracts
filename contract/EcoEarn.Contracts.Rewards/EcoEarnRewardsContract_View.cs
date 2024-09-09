using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
    }

    public override Config GetConfig(Empty input)
    {
        return State.Config.Value;
    }

    public override DappInfo GetDappInfo(Hash input)
    {
        return IsHashValid(input) ? State.DappInfoMap[input] : new DappInfo();
    }

    public override ClaimInfo GetClaimInfo(Hash input)
    {
        return IsHashValid(input) ? State.ClaimInfoMap[input] : new ClaimInfo();
    }

    public override Address GetRewardAddress(GetRewardAddressInput input)
    {
        return CalculateUserAddress(input.DappId, input.Account);
    }

    public override LiquidityInfo GetLiquidityInfo(Hash input)
    {
        return IsHashValid(input) ? State.LiquidityInfoMap[input] : new LiquidityInfo();
    }
    
    // PixiePoints
    public override GetPointsContractConfigOutput GetPointsContractConfig(Empty input)
    {
        return new GetPointsContractConfigOutput
        {
            PointsContract = State.PointsContract.Value,
            Config = State.PointsContractConfig.Value
        };
    }

    public override BoolValue GetJoinRecord(Address input)
    {
        return IsAddressValid(input)
            ? new BoolValue { Value = State.JoinRecord[input] }
            : new BoolValue { Value = false };
    }
}