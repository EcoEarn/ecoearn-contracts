using System.Linq;
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
        if (stakeInfo == null) return output;

        output.StakeInfo = stakeInfo;
        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];

        output.IsInUnlockWindow = CheckPoolEnabled(poolInfo.Config.EndTime) &&
                                  IsInUnlockWindow(stakeInfo, poolInfo.Config.UnlockWindowDuration);

        return output;
    }

    public override GetRewardOutput GetReward(GetRewardInput input)
    {
        var output = new GetRewardOutput();
        if (input.StakeIds == null || input.StakeIds.Count == 0) return output;

        foreach (var id in input.StakeIds.Distinct())
        {
            var rewardInfo = ProcessGetReward(id);
            if (rewardInfo != null) output.RewardInfos.Add(rewardInfo);
        }

        return output;
    }

    public override Hash GetUserStakeId(GetUserStakeIdInput input)
    {
        return State.UserStakeIdMap[input.PoolId][input.Account];
    }

    private RewardInfo ProcessGetReward(Hash stakeId)
    {
        var rewardInfo = new RewardInfo();
        if (!IsHashValid(stakeId)) return null;
        
        var stakeInfo = State.StakeInfoMap[stakeId];
        if (stakeInfo == null) return null;

        rewardInfo.Account = stakeInfo.Account;
        rewardInfo.StakeId = stakeId;
        rewardInfo.PoolId = stakeInfo.PoolId;

        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];
        if (poolInfo == null) return null;
        
        var poolData = State.PoolDataMap[stakeInfo.PoolId];

        rewardInfo.Symbol = poolInfo.Config.RewardToken;

        var blockTime = Context.CurrentBlockTime;

        long reward;

        if (blockTime >= poolData.LastRewardTime && poolData.TotalStakedAmount != 0)
        {
            reward = CalculateRewardAmount(poolInfo, poolData, stakeInfo);
        }
        else
        {
            rewardInfo.Amount = stakeInfo.RewardAmount;
            return rewardInfo;
        }

        var config = State.Config.Value;
        rewardInfo.Amount = reward.Sub(CalculateCommissionFee(reward, config.CommissionRate))
            .Add(stakeInfo.RewardAmount);

        return rewardInfo;
    }
}