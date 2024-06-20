using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using EcoEarn.Contracts.Rewards;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Stake(StakeInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);
        
        Assert(input!.Amount >= 0, "Invalid amount.");
        Assert(input.Period >= 0, "Invalid period.");
        Assert(input.Period <= poolInfo.Config.MaximumStakeDuration, "Period too long.");

        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        var existId = State.UserStakeIdMap[poolInfo.PoolId][Context.Sender];
        var stakeInfo = existId == null ? null : State.StakeInfoMap[existId];

        var remainTime = CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration);
        if (stakeInfo != null && stakeInfo.UnlockTime == null)
        {
            Assert(!IsInUnlockWindow(stakeInfo, remainTime),
                "Cannot stake during unlock window.");
        }

        ProcessStake(poolInfo, stakeInfo, input.Amount, input.Period, Context.Sender, null, remainTime);

        if (input.Amount > 0)
        {
            Context.SendInline(poolInfo.Config.StakeTokenContract, nameof(State.TokenContract.TransferFrom),
                new TransferFromInput
                {
                    From = Context.Sender,
                    To = CalculateVirtualAddress(GetStakeVirtualAddress(input.PoolId)),
                    Amount = input.Amount,
                    Memo = "stake",
                    Symbol = poolInfo.Config.StakingToken
                });
        }

        return new Empty();
    }

    public override Empty Renew(RenewInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.Period > 0, "Invalid period.");

        var poolInfo = GetPool(input.PoolId);

        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        var existId = State.UserStakeIdMap[poolInfo.PoolId][Context.Sender];
        Assert(existId != null && State.StakeInfoMap[existId] != null, "Stake info not exists.");
        var stakeInfo = State.StakeInfoMap[existId];
        Assert(stakeInfo.UnlockTime == null, "Already unlocked.");
        Assert(IsInUnlockWindow(stakeInfo, CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration)),
            "Not in unlock window.");

        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        Assert(input.Period <= poolInfo.Config.MaximumStakeDuration, "Period too long.");

        stakeInfo.StakingPeriod = input.Period;
        stakeInfo.LastOperationTime = Context.CurrentBlockTime;

        ProcessSubStakeInfos(stakeInfo, poolInfo, poolData, input.Period, true);

        Context.Fire(new Renewed
        {
            PoolData = State.PoolDataMap[poolInfo.PoolId],
            StakeInfo = stakeInfo
        });

        return new Empty();
    }

    public override Empty Unlock(Hash input)
    {
        Assert(IsHashValid(input), "Invalid input.");

        var existId = State.UserStakeIdMap[input][Context.Sender];
        Assert(existId != null, "Not staked before.");

        var stakeInfo = State.StakeInfoMap[existId];
        Assert(stakeInfo != null, "Stake info not exists.");
        Assert(stakeInfo!.UnlockTime == null, "Already unlocked.");

        var poolInfo = GetPool(stakeInfo.PoolId);

        Assert(
            !CheckPoolEnabled(poolInfo.Config.EndTime) || IsInUnlockWindow(stakeInfo,
                CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration)), "Not in unlock window.");

        stakeInfo.UnlockTime = Context.CurrentBlockTime;
        stakeInfo.LastOperationTime = Context.CurrentBlockTime;

        var rewards = UpdateRewards(poolInfo, stakeInfo);
        ProcessStakeInfo(stakeInfo, out var stakedAmount, out var earlyStakedAmount);

        if (rewards > 0)
        {
            rewards = ProcessCommissionFee(rewards, poolInfo);
        }

        if (stakedAmount > 0)
        {
            Context.SendVirtualInline(GetStakeVirtualAddress(input), poolInfo.Config.StakeTokenContract,
                nameof(State.TokenContract.Transfer), new TransferInput
                {
                    Amount = stakedAmount,
                    Memo = "unlock",
                    Symbol = poolInfo.Config.StakingToken,
                    To = stakeInfo.Account
                });
        }

        CallRewardsContractClaim(input, poolInfo, rewards, out var rewardAddress);
            
        if (earlyStakedAmount > 0)
        {
            Context.SendVirtualInline(GetStakeVirtualAddress(input), poolInfo.Config.StakeTokenContract,
                nameof(State.TokenContract.Transfer), new TransferInput
                {
                    Amount = earlyStakedAmount,
                    Memo = "unlock",
                    Symbol = poolInfo.Config.StakingToken,
                    To = rewardAddress
                });
        }

        Context.Fire(new Unlocked
        {
            PoolId = input,
            StakeInfo = stakeInfo,
            Amount = stakedAmount.Add(earlyStakedAmount),
            PoolData = State.PoolDataMap[stakeInfo.PoolId]
        });

        return new Empty();
    }

    public override Empty StakeFor(StakeForInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.Amount > 0, "Invalid amount.");
        Assert(input.Period > 0, "Invalid period.");
        Assert(IsAddressValid(input.FromAddress), "Invalid from address.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(input.LongestReleaseTime != null, "Invalid longest release time.");

        var poolInfo = GetPool(input.PoolId);

        Assert(Context.Sender == State.EcoEarnRewardsContract.Value, "No permission.");

        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        var existId = State.UserStakeIdMap[poolInfo.PoolId][input.FromAddress];
        var stakeInfo = existId == null ? null : State.StakeInfoMap[existId];

        var remainTime = CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration);
        if (stakeInfo != null && stakeInfo.UnlockTime == null)
        {
            Assert(!IsInUnlockWindow(stakeInfo, remainTime),
                "Cannot stake during unlock window.");
            Assert(Context.CurrentBlockTime.AddSeconds(remainTime.Add(input.Period)) > input.LongestReleaseTime,
                "Period not enough.");
        }

        ProcessStake(poolInfo, stakeInfo, input.Amount, input.Period, input.FromAddress, input.Seed, remainTime);

        return new Empty();
    }

    #endregion

    #region private

    private Hash GenerateStakeId(Hash poolId, Address sender)
    {
        var count = State.UserStakeCountMap[poolId][sender];
        var stakeId = HashHelper.ConcatAndCompute(
            HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(count), HashHelper.ComputeFrom(sender)), poolId);
        State.UserStakeCountMap[poolId][sender] = count.Add(1);

        return stakeId;
    }

    private Hash GenerateSubStakeId(Hash stakeId, long count)
    {
        return HashHelper.ConcatAndCompute(stakeId, HashHelper.ComputeFrom(count));
    }

    private long CalculateBoostedAmount(TokensPoolConfig config, long amount, long period)
    {
        period = period >= config.MaximumStakeDuration ? config.MaximumStakeDuration : period;
        var days = period.Div(EcoEarnTokensContractConstants.SecondsPerDay);
        return amount.Mul(days).Div(config.FixedBoostFactor).Add(amount);
    }

    private void UpdatePool(PoolInfo poolInfo, PoolData poolData)
    {
        var blockTime = Context.CurrentBlockTime;

        if (poolData == null) return;

        if (blockTime <= poolData.LastRewardTime) return;

        if (poolData.TotalStakedAmount == 0)
        {
            poolData.LastRewardTime = blockTime;
            return;
        }

        var multiplier = GetMultiplier(poolData.LastRewardTime.Seconds, blockTime.Seconds,
            poolInfo.Config.EndTime.Seconds);
        var rewards = new BigIntValue(multiplier.Mul(poolInfo.Config.RewardPerSecond));
        var accTokenPerShare = poolData.AccTokenPerShare ?? new BigIntValue(0);
        poolData.AccTokenPerShare =
            rewards.Mul(poolInfo.PrecisionFactor).Div(poolData.TotalStakedAmount).Add(accTokenPerShare);
        poolData.LastRewardTime = blockTime;
    }

    private long GetMultiplier(long from, long to, long endTime)
    {
        if (to <= endTime) return to.Sub(from);
        if (from >= endTime) return 0;
        return endTime.Sub(from);
    }

    private long CalculatePending(long amount, BigIntValue accTokenPerShare, long debt, BigIntValue precisionFactor)
    {
        if (accTokenPerShare == null) return 0;
        long.TryParse(accTokenPerShare.Mul(amount).Div(precisionFactor).Sub(debt).Value, out var result);
        return result < 0 ? 0 : result;
    }

    private long CalculateDebt(long amount, BigIntValue accTokenPerShare, BigIntValue precisionFactor)
    {
        if (accTokenPerShare == null) return 0;
        long.TryParse(accTokenPerShare.Mul(amount).Div(precisionFactor).Value, out var result);
        return result;
    }

    private void ProcessStake(PoolInfo poolInfo, StakeInfo stakeInfo, long amount, long period, Address address,
        Hash seed, long remainTime)
    {
        // create position
        if (stakeInfo == null || stakeInfo.UnlockTime != null)
        {
            Assert(amount > 0, "Invalid amount.");
            Assert(amount >= poolInfo.Config.MinimumAmount, "Amount not enough.");

            var stakeId = GenerateStakeId(poolInfo.PoolId, address);
            Assert(State.StakeInfoMap[stakeId] == null, "Stake id exists.");

            stakeInfo = new StakeInfo
            {
                StakeId = stakeId,
                PoolId = poolInfo.PoolId,
                Account = address,
                StakingToken = poolInfo.Config.StakingToken
            };

            State.StakeInfoMap[stakeId] = stakeInfo;
            State.UserStakeIdMap[poolInfo.PoolId][address] = stakeId;
        }

        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        // when remain time close to maximum stake duration, can accept 0 as Period
        if (remainTime <= poolInfo.Config.MaximumStakeDuration.Sub(poolInfo.Config.MinimumStakeDuration))
        {
            Assert(period >= poolInfo.Config.MinimumStakeDuration, "Period too short.");
        }

        var stakingPeriod = remainTime.Add(period);
        Assert(stakingPeriod <= poolInfo.Config.MaximumStakeDuration, "Period too long.");

        stakeInfo.StakingPeriod = stakingPeriod;
        stakeInfo.LastOperationTime = Context.CurrentBlockTime;

        // extends 
        ProcessSubStakeInfos(stakeInfo, poolInfo, poolData, period, false);

        if (amount > 0)
        {
            AddSubStakeInfo(stakeInfo, poolInfo, poolData, amount, seed, remainTime.Add(period));
        }

        Context.Fire(new Staked
        {
            StakeInfo = stakeInfo,
            PoolData = State.PoolDataMap[poolInfo.PoolId]
        });
    }

    private long ProcessCommissionFee(long pending, PoolInfo poolInfo)
    {
        if (pending == 0) return 0;

        var config = State.Config.Value;
        var commissionFee = CalculateCommissionFee(pending, config.CommissionRate);

        if (commissionFee != 0)
        {
            Context.SendVirtualInline(GetRewardVirtualAddress(poolInfo.PoolId), poolInfo.Config.RewardTokenContract,
                nameof(State.TokenContract.Transfer), new TransferInput
                {
                    To = config.Recipient,
                    Amount = commissionFee,
                    Symbol = poolInfo.Config.RewardToken,
                    Memo = "commission"
                });
        }

        return pending.Sub(commissionFee);
    }

    private void ProcessStakeInfo(StakeInfo stakeInfo, out long stakedAmount, out long earlyStakedAmount)
    {
        var boostedAmount = 0L;
        stakedAmount = 0L;
        earlyStakedAmount = 0L;

        foreach (var subStakeInfo in stakeInfo.SubStakeInfos)
        {
            boostedAmount = boostedAmount.Add(subStakeInfo.BoostedAmount);
            if (subStakeInfo.Seed == null)
            {
                stakedAmount = stakedAmount.Add(subStakeInfo.StakedAmount);
            }
            else earlyStakedAmount = earlyStakedAmount.Add(subStakeInfo.StakedAmount);

            subStakeInfo.StakedAmount = 0;
        }

        var poolData = State.PoolDataMap[stakeInfo.PoolId];
        poolData.TotalStakedAmount = poolData.TotalStakedAmount.Sub(boostedAmount);
    }

    private bool IsInUnlockWindow(StakeInfo stakeInfo, long remainTime)
    {
        return stakeInfo.UnlockTime == null && remainTime <= 0;
    }

    private void ProcessSubStakeInfos(StakeInfo stakeInfo, PoolInfo poolInfo, PoolData poolData, long period,
        bool isRenew)
    {
        if (stakeInfo.SubStakeInfos.Count == 0 || period == 0) return;

        foreach (var subStakeInfo in stakeInfo.SubStakeInfos)
        {
            subStakeInfo.Period = isRenew ? period : subStakeInfo.Period.Add(period);

            var pending = CalculatePending(subStakeInfo.BoostedAmount, poolData.AccTokenPerShare,
                subStakeInfo.RewardDebt, poolInfo.PrecisionFactor);
            var actualReward = ProcessCommissionFee(pending, poolInfo);
            subStakeInfo.RewardAmount = subStakeInfo.RewardAmount.Add(actualReward);

            var boostedAmount = CalculateBoostedAmount(poolInfo.Config, subStakeInfo.StakedAmount, subStakeInfo.Period);

            if (boostedAmount != subStakeInfo.BoostedAmount)
            {
                poolData.TotalStakedAmount =
                    poolData.TotalStakedAmount.Add(boostedAmount).Sub(subStakeInfo.BoostedAmount);
                subStakeInfo.BoostedAmount = boostedAmount;
            }

            subStakeInfo.RewardDebt = CalculateDebt(boostedAmount, poolData.AccTokenPerShare, poolInfo.PrecisionFactor);
        }
    }

    private void AddSubStakeInfo(StakeInfo stakeInfo, PoolInfo poolInfo, PoolData poolData, long amount, Hash seed, long period)
    {
        var boostedAmount = CalculateBoostedAmount(poolInfo.Config, amount, period);

        stakeInfo.SubStakeInfos.Add(new SubStakeInfo
        {
            SubStakeId = GenerateSubStakeId(stakeInfo.StakeId, stakeInfo.SubStakeInfos.Count),
            StakedAmount = amount,
            StakedBlockNumber = Context.CurrentHeight,
            StakedTime = Context.CurrentBlockTime,
            Period = period,
            BoostedAmount = boostedAmount,
            RewardDebt = CalculateDebt(boostedAmount, poolData.AccTokenPerShare, poolInfo.PrecisionFactor),
            Seed = seed
        });

        poolData.TotalStakedAmount = poolData.TotalStakedAmount.Add(boostedAmount);
    }

    private long CalculateRemainTime(StakeInfo stakeInfo, long unlockWindowDuration)
    {
        if (stakeInfo == null || stakeInfo.StakingPeriod == 0) return 0;
        var fullCycleSeconds = stakeInfo.StakingPeriod.Add(unlockWindowDuration);
        var timeSpan = (Context.CurrentBlockTime - stakeInfo.LastOperationTime).Seconds;

        var secondsInCurrentCycle = timeSpan % fullCycleSeconds;

        return stakeInfo.StakingPeriod.Sub(secondsInCurrentCycle);
    }

    private long CalculateWindowTerms(StakeInfo stakeInfo, long unlockWindowDuration)
    {
        if (stakeInfo.StakingPeriod == 0) return 0;
        var fullCycleSeconds = stakeInfo.StakingPeriod.Add(unlockWindowDuration);
        var timeSpan = (Context.CurrentBlockTime - stakeInfo.LastOperationTime).Seconds;

        return timeSpan.Div(fullCycleSeconds);
    }

    #endregion
}