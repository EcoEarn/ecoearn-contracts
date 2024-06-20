using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
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
}