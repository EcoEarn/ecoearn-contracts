using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
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
            Status = CheckPoolEnabled(info.Config.EndBlockNumber)
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
        return IsHashValid(input) ? State.PoolDataMap[input] ?? new PoolData() : new PoolData();
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
        return IsHashValid(input) ? State.ClaimInfoMap[input] ?? new ClaimInfo() : new ClaimInfo();
    }

    public override StakeInfo GetStakeInfo(Hash input)
    {
        return IsHashValid(input) ? State.StakeInfoMap[input] ?? new StakeInfo() : new StakeInfo();
    }

    public override GetRewardOutput GetReward(Hash input)
    {
        var output = new GetRewardOutput();
        if (!IsHashValid(input)) return output;

        var stakeInfo = State.StakeInfoMap[input];
        if (stakeInfo == null) return output;

        output.Account = stakeInfo.Account;
        output.StakeId = input;

        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];
        if (poolInfo == null) return output;

        output.Symbol = poolInfo.Config.RewardToken;

        var poolData = State.PoolDataMap[stakeInfo.PoolId];
        if (poolData == null) return output;

        var blockNumber = Context.CurrentHeight;

        long reward;

        if (blockNumber > poolData.LastRewardBlock && poolData.TotalStakedAmount != 0 &&
            Context.CurrentBlockTime < CalculateUnlockTime(stakeInfo))
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
        return State.UserStakeIdMap[input.PoolId]?[input.Account];
    }

    private Timestamp CalculateUnlockTime(StakeInfo stakeInfo)
    {
        return stakeInfo.StakedTime.AddSeconds(stakeInfo.Period);
    }
}