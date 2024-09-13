using System.Linq;
using AElf.CSharp.Core;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

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

    public override GetStakeInfoOutput GetStakeInfo(Hash input)
    {
        var output = new GetStakeInfoOutput();
        if (!IsHashValid(input)) return output;

        var stakeInfo = State.StakeInfoMap[input];
        if (stakeInfo == null) return output;

        output.StakeInfo = stakeInfo;
        var poolInfo = State.PoolInfoMap[stakeInfo.PoolId];

        output.IsInUnstakeWindow = CheckPoolEnabled(poolInfo.Config.EndTime) && IsInUnstakeWindow(stakeInfo,
            CalculateRemainTime(stakeInfo, poolInfo.Config.UnstakeWindowDuration));

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

    public override Int64Value GetUserStakeCount(GetUserStakeCountInput input)
    {
        return new Int64Value
        {
            Value = State.UserStakeCountMap[input.PoolId][input.Account]
        };
    }

    public override BoolValue IsInUnstakeWindow(IsInUnstakeWindowInput input)
    {
        var poolInfo = State.PoolInfoMap[input.PoolId];
        if (poolInfo?.PoolId == null) return new BoolValue();

        var stakeId = State.UserStakeIdMap[input.PoolId][input.Account];
        if (stakeId == null) return new BoolValue();

        var stakeInfo = State.StakeInfoMap[stakeId];

        var remainTime = CalculateRemainTime(stakeInfo, poolInfo.Config.UnstakeWindowDuration);
        if (stakeInfo != null && stakeInfo.UnstakeTime == null && IsInUnstakeWindow(stakeInfo, remainTime))
            return new BoolValue
            {
                Value = true
            };

        return new BoolValue();
    }

    public override BoolValue GetStakeOnBehalfPermission(Hash input)
    {
        return IsHashValid(input) ? new BoolValue { Value = State.StakeOnBehalfPermissionMap[input] } : new BoolValue();
    }

    #endregion

    #region private

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

        if (stakeInfo.UnstakeTime != null)
        {
            rewardInfo.Amount = 0;
            return rewardInfo;
        }

        var blockTime = Context.CurrentBlockTime;

        if (blockTime >= poolData.LastRewardTime && poolData.TotalStakedAmount != 0)
        {
            rewardInfo.Amount = CalculateRewardAmount(poolInfo, poolData, stakeInfo);
        }
        else
        {
            foreach (var subStakeInfo in stakeInfo.SubStakeInfos)
            {
                rewardInfo.Amount = rewardInfo.Amount.Add(subStakeInfo.RewardAmount);
            }
        }

        return rewardInfo;
    }

    #endregion
}