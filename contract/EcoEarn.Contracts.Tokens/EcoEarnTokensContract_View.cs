using AElf.CSharp.Core;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
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

    public override GetPoolInfoOutput GetPoolInfo(Hash input)
    {
        if (!IsHashValid(input) || State.PoolInfoMap[input]?.PoolId == null) return new GetPoolInfoOutput();

        var info = State.PoolInfoMap[input];
        var output = new GetPoolInfoOutput
        {
            PoolInfo = info,
            Status = CheckPoolEnabled(info.Config.EndTime)
        };

        return output;
    }

    public override PoolAddressInfo GetPoolAddressInfo(Hash input)
    {
        return IsHashValid(input)
            ? new PoolAddressInfo
            {
                StakeAddress = CalculateVirtualAddress(GetStakeVirtualAddress(input)),
                RewardAddress = CalculateVirtualAddress(GetRewardVirtualAddress(input))
            }
            : new PoolAddressInfo();
    }

    public override PoolData GetPoolData(Hash input)
    {
        return IsHashValid(input) ? State.PoolDataMap[input] : new PoolData();
    }

    public override Int64Value GetPoolCount(Hash input)
    {
        return new Int64Value
        {
            Value = IsHashValid(input) ? State.PoolCountMap[input] : 0
        };
    }

    public override ClaimInfo GetClaimInfo(Hash input)
    {
        return IsHashValid(input) ? State.ClaimInfoMap[input] : new ClaimInfo();
    }

    public override GetStakeInfoOutput GetStakeInfo(Hash input)
    {
        var output = new GetStakeInfoOutput();
        if (!IsHashValid(input)) return output;

        var stakeInfo = State.StakeInfoMap[input];
        if (stakeInfo.StakeId == null) return output;

        output.StakeInfo = stakeInfo;
        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];

        output.IsInUnlockWindow = CheckPoolEnabled(poolInfo.Config.EndTime) &&
                                  IsInUnlockWindow(stakeInfo, poolInfo.Config.UnlockWindowDuration);

        return output;
    }

    public override GetRewardOutput GetReward(Hash input)
    {
        var output = new GetRewardOutput();
        if (!IsHashValid(input)) return output;

        var stakeInfo = State.StakeInfoMap[input];
        if (stakeInfo.StakeId == null) return output;

        output.Account = stakeInfo.Account;
        output.StakeId = input;

        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];
        if (poolInfo.PoolId == null) return output;

        output.Symbol = poolInfo.Config.RewardToken;

        var poolData = State.PoolDataMap[stakeInfo.PoolId];
        if (poolData.PoolId == null) return output;

        var blockTime = Context.CurrentBlockTime;

        long reward;

        if (blockTime >= poolData.LastRewardTime && poolData.TotalStakedAmount != 0)
        {
            reward = CalculateRewardAmount(poolInfo, poolData, stakeInfo);
        }
        else
        {
            output.Amount = stakeInfo.RewardAmount;
            return output;
        }

        var config = State.Config.Value;
        output.Amount = reward.Sub(CalculateCommissionFee(reward, config.CommissionRate))
            .Add(stakeInfo.RewardAmount);

        return output;
    }

    public override Int64Value GetUserStakeCount(GetUserStakeCountInput input)
    {
        return new Int64Value
        {
            Value = State.UserStakeCountMap[input.PoolId][input.Account]
        };
    }

    public override Hash GetUserStakeId(GetUserStakeIdInput input)
    {
        return State.UserStakeIdMap[input.PoolId][input.Account];
    }
}