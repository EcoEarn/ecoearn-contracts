using System.Collections.Generic;
using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using static System.Int64;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Stake(StakeInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.Amount >= 0, "Invalid amount.");
        Assert(input.Period >= 0, "Invalid period.");

        var poolInfo = GetPool(input.PoolId);

        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        ProcessStake(poolInfo, input.Amount, 0, input.Period, Context.Sender);

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
        Assert(IsInUnlockWindow(stakeInfo, poolInfo.Config.UnlockWindowDuration), "Not in unlock window.");

        ProcessStakeInfo(stakeInfo, poolInfo, input.Period);

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
        Assert(stakeInfo!.Account == Context.Sender, "No permission.");
        Assert(stakeInfo.UnlockTime == null, "Already unlocked.");

        var poolInfo = GetPool(stakeInfo.PoolId);
        Assert(
            !CheckPoolEnabled(poolInfo.Config.EndTime) ||
            IsInUnlockWindow(stakeInfo, poolInfo.Config.UnlockWindowDuration), "Not in unlock window.");

        stakeInfo.UnlockTime = Context.CurrentBlockTime;
        stakeInfo.LastOperationTime = Context.CurrentBlockTime;

        ProcessClaim(poolInfo, stakeInfo);
        UpdateTotalStakedAmount(stakeInfo);

        if (stakeInfo.StakedAmount > 0)
        {
            Context.SendVirtualInline(GetStakeVirtualAddress(input), poolInfo.Config.StakeTokenContract,
                nameof(State.TokenContract.Transfer), new TransferInput
                {
                    Amount = stakeInfo.StakedAmount,
                    Memo = "withdraw",
                    Symbol = poolInfo.Config.StakingToken,
                    To = stakeInfo.Account
                });
        }

        if (stakeInfo.EarlyStakedAmount > 0)
        {
            var dict = State.EarlyStakeInfoMap[stakeInfo.StakeId];
            foreach (var info in dict.Data)
            {
                Context.SendVirtualInline(GetStakeVirtualAddress(input), poolInfo.Config.StakeTokenContract,
                    nameof(State.TokenContract.Transfer), new TransferInput
                    {
                        Amount = info.Value,
                        Memo = "early",
                        Symbol = poolInfo.Config.StakingToken,
                        To = Address.FromBase58(info.Key)
                    });
            }
        }

        stakeInfo.StakedAmount = 0;
        stakeInfo.EarlyStakedAmount = 0;

        Context.Fire(new Unlocked
        {
            StakeId = existId,
            PoolData = State.PoolDataMap[stakeInfo.PoolId],
            StakedAmount = stakeInfo.StakedAmount,
            EarlyStakedAmount = stakeInfo.EarlyStakedAmount
        });

        return new Empty();
    }

    public override Empty EarlyStake(EarlyStakeInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");

        var poolInfo = GetPool(input.PoolId);
        Assert(input.Period >= 0 && input.Period <= poolInfo.Config.MaximumStakeDuration, "Invalid period.");
        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        var stakeId = GetStakeId(input.PoolId);
        var list = ProcessEarlyStake(input.ClaimIds!.ToList(), poolInfo, stakeId, out var stakedAmount);
        Assert(stakedAmount >= poolInfo.Config.MinimumAmount, "Amount not enough.");

        ProcessStake(poolInfo, 0, stakedAmount, input.Period, Context.Sender);

        RecordEarlyStakeInfo(stakeId, stakedAmount, CalculateVirtualAddress(Context.Sender).ToBase58());

        Context.SendVirtualInline(HashHelper.ComputeFrom(Context.Sender), poolInfo.Config.RewardTokenContract,
            nameof(State.TokenContract.Transfer), new TransferInput
            {
                To = CalculateVirtualAddress(GetStakeVirtualAddress(input.PoolId)),
                Amount = stakedAmount,
                Symbol = poolInfo.Config.RewardToken
            });

        Context.Fire(new EarlyStaked
        {
            StakeInfo = State.StakeInfoMap[stakeId],
            ClaimInfos = new ClaimInfos
            {
                Data = { list }
            },
            PoolData = State.PoolDataMap[input.PoolId],
            PoolId = input.PoolId
        });

        return new Empty();
    }

    public override Empty StakeFor(StakeForInput input)
    {
        Assert(input != null, "Invalid input.");
        CheckEcoEarnPointsPermission();
        Assert(IsHashValid(input!.PoolId), "Invalid pool id.");
        Assert(IsAddressValid(input.Address), "Invalid address.");
        Assert(IsAddressValid(input.FromAddress), "Invalid from address.");

        var poolInfo = GetPool(input.PoolId);
        Assert(input.Amount >= poolInfo.Config.MinimumAmount, "Invalid amount.");
        Assert(input.Period >= 0 && input.Period <= poolInfo.Config.MaximumStakeDuration, "Invalid period.");
        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool closed.");

        Context.SendInline(poolInfo.Config.StakeTokenContract, nameof(State.TokenContract.TransferFrom),
            new TransferFromInput
            {
                From = input.FromAddress,
                To = CalculateVirtualAddress(GetStakeVirtualAddress(input.PoolId)),
                Amount = input.Amount,
                Symbol = poolInfo.Config.StakingToken
            });

        var stakeId = ProcessStake(poolInfo, 0, input.Amount, input.Period, input.Address);

        RecordEarlyStakeInfo(stakeId, input.Amount, input.FromAddress.ToBase58());

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
        TryParse(accTokenPerShare.Mul(amount).Div(precisionFactor).Sub(debt).Value, out var result);
        return result < 0 ? 0 : result;
    }

    private long CalculateDebt(long amount, BigIntValue accTokenPerShare, BigIntValue precisionFactor)
    {
        if (accTokenPerShare == null) return 0;
        TryParse(accTokenPerShare.Mul(amount).Div(precisionFactor).Value, out var result);
        return result;
    }

    private List<ClaimInfo> ProcessEarlyStake(List<Hash> claimIds, PoolInfo poolInfo, Hash stakeId, out long amount)
    {
        amount = 0L;
        var list = new List<ClaimInfo>();

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim info not exists.");
            Assert(claimInfo!.WithdrawTime == null, "Already withdrawn.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(claimInfo.ClaimedSymbol == poolInfo.Config.StakingToken, "Token not matched.");

            if (claimInfo.StakeId != null)
            {
                var stakeInfo = State.StakeInfoMap[claimInfo.StakeId];
                Assert(stakeInfo != null && stakeInfo.UnlockTime != null, "Not unlocked.");
            }

            amount = amount.Add(claimInfo.ClaimedAmount);
            claimInfo.EarlyStakeTime = Context.CurrentBlockTime;
            claimInfo.StakeId = stakeId;
            list.Add(claimInfo);
        }

        return list;
    }

    private Hash ProcessStake(PoolInfo poolInfo, long stakedAmount, long earlyStakedAmount, long period,
        Address address)
    {
        var existId = State.UserStakeIdMap[poolInfo.PoolId][address];
        var stakeInfo = existId == null ? new StakeInfo() : State.StakeInfoMap[existId];

        var amount = stakedAmount.Add(earlyStakedAmount);
        if (amount > 0)
        {
            stakeInfo.StakedAmount = stakeInfo.StakedAmount.Add(stakedAmount);
            stakeInfo.EarlyStakedAmount = stakeInfo.EarlyStakedAmount.Add(earlyStakedAmount);
        }

        if (period > 0)
        {
            Assert(period >= poolInfo.Config.MinimumStakeDuration, "Period too short.");
            Assert(period <= poolInfo.Config.MaximumStakeDuration, "Period too long.");
        }

        // create position
        if (existId == null || stakeInfo.UnlockTime != null)
        {
            Assert(amount > 0 && period > 0, "New position requires both amount and period.");
            Assert(amount >= poolInfo.Config.MinimumAmount, "Amount not enough.");
            var stakeId = GenerateStakeId(poolInfo.PoolId, address);
            Assert(State.StakeInfoMap[stakeId] == null, "Stake id exists.");

            stakeInfo = new StakeInfo
            {
                StakeId = stakeId,
                PoolId = poolInfo.PoolId,
                Account = address,
                StakedBlockNumber = Context.CurrentHeight,
                StakedTime = Context.CurrentBlockTime,
                StakedAmount = stakedAmount,
                EarlyStakedAmount = earlyStakedAmount,
                StakingToken = poolInfo.Config.StakingToken,
                LastOperationTime = Context.CurrentBlockTime
            };

            State.StakeInfoMap[stakeId] = stakeInfo;
            State.UserStakeIdMap[poolInfo.PoolId][address] = stakeId;
        }
        else
        {
            Assert(!IsInUnlockWindow(stakeInfo, poolInfo.Config.UnlockWindowDuration),
                "Cannot stake during unlock window.");
        }

        ProcessStakeInfo(stakeInfo, poolInfo, period);

        Context.Fire(new Staked
        {
            StakeInfo = stakeInfo,
            PoolData = State.PoolDataMap[poolInfo.PoolId]
        });

        return stakeInfo.StakeId;
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

    private void RecordEarlyStakeInfo(Hash stakeId, long stakedAmount, string address)
    {
        var earlyStakeInfo = State.EarlyStakeInfoMap[stakeId] ?? new EarlyStakeInfo();
        var dict = earlyStakeInfo.Data ?? new MapField<string, long>();
        dict.TryGetValue(address, out var amount);
        dict[address] = amount.Add(stakedAmount);
        State.EarlyStakeInfoMap[stakeId] = new EarlyStakeInfo
        {
            Data = { dict }
        };
    }

    private Hash GetStakeId(Hash poolId)
    {
        var stakeId = State.UserStakeIdMap[poolId][Context.Sender];

        if (IsHashValid(stakeId) && State.StakeInfoMap[stakeId].UnlockTime == null) return stakeId;

        var count = State.UserStakeCountMap[poolId][Context.Sender];
        stakeId = HashHelper.ConcatAndCompute(
            HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(count), HashHelper.ComputeFrom(Context.Sender)), poolId);

        return stakeId;
    }

    private void UpdateTotalStakedAmount(StakeInfo stakeInfo)
    {
        var poolData = State.PoolDataMap[stakeInfo.PoolId];
        poolData.TotalStakedAmount = poolData.TotalStakedAmount.Sub(stakeInfo.BoostedAmount);
    }

    private bool IsInUnlockWindow(StakeInfo stakeInfo, long unlockWindowDuration)
    {
        var remainTime = CalculateRemainTime(stakeInfo, unlockWindowDuration);

        return stakeInfo.UnlockTime == null && remainTime <= 0;
    }

    private long CalculateRemainTime(StakeInfo stakeInfo, long unlockWindowDuration)
    {
        if (stakeInfo.StakingPeriod == 0) return 0;
        var fullCycleSeconds = stakeInfo.StakingPeriod.Add(unlockWindowDuration);
        var timeSpan = (Context.CurrentBlockTime - stakeInfo.LastOperationTime).Seconds;

        var secondsInCurrentCycle = timeSpan % fullCycleSeconds;

        return stakeInfo.StakingPeriod.Sub(secondsInCurrentCycle);
    }

    private void ProcessStakeInfo(StakeInfo stakeInfo, PoolInfo poolInfo, long period)
    {
        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        if (period > 0)
        {
            var remainTime = CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration);

            var stakingPeriod = remainTime < 0 ? period : remainTime.Add(period);

            Assert(stakingPeriod <= poolInfo.Config.MaximumStakeDuration, "Period too long.");
            stakeInfo.StakingPeriod = stakingPeriod;
            stakeInfo.Period = stakeInfo.Period.Add(period);

            stakeInfo.LastOperationTime = Context.CurrentBlockTime;
        }

        var pending = CalculatePending(stakeInfo.BoostedAmount, poolData.AccTokenPerShare, stakeInfo.RewardDebt,
            poolInfo.PrecisionFactor);
        var actualReward = ProcessCommissionFee(pending, poolInfo);
        stakeInfo.RewardAmount = stakeInfo.RewardAmount.Add(actualReward);

        var boostedAmount = CalculateBoostedAmount(poolInfo.Config,
            stakeInfo.StakedAmount.Add(stakeInfo.EarlyStakedAmount), stakeInfo.Period);

        if (boostedAmount != stakeInfo.BoostedAmount)
        {
            poolData.TotalStakedAmount = poolData.TotalStakedAmount.Add(boostedAmount).Sub(stakeInfo.BoostedAmount);
            stakeInfo.BoostedAmount = boostedAmount;
        }

        stakeInfo.RewardDebt = CalculateDebt(boostedAmount, poolData.AccTokenPerShare, poolInfo.PrecisionFactor);
    }

    #endregion
}