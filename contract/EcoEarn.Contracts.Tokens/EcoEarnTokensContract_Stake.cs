using System.Collections.Generic;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Stake(StakeInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);
        Assert(input.Amount >= 0, "Invalid amount.");
        Assert(input.Period >= 0, "Invalid period.");
        Assert(Context.CurrentHeight < poolInfo.Config.EndBlockNumber, "Pool closed.");

        ProcessStake(poolInfo, input.Amount, 0, input.Period, Context.Sender);

        if (input.Amount > 0)
        {
            Context.SendInline(poolInfo.Config.StakeTokenContract, "TransferFrom", new TransferFromInput
            {
                From = Context.Sender,
                To = CalculateVirtualAddress(input.PoolId),
                Amount = input.Amount,
                Memo = "stake",
                Symbol = poolInfo.Config.StakingToken
            });
        }

        return new Empty();
    }

    public override Empty Unlock(Hash input)
    {
        Assert(IsHashValid(input), "Invalid input.");

        var existId = State.UserStakeIdMap[input]?[Context.Sender];
        Assert(existId != null, "Not staked before.");

        var stakeInfo = State.StakeInfoMap[existId];
        Assert(stakeInfo != null, "Stake info not exists.");
        Assert(stakeInfo.Account == Context.Sender, "No permission.");
        Assert(stakeInfo.WithdrawTime == null, "Already withdrawn.");

        stakeInfo.WithdrawTime = Context.CurrentBlockTime;
        stakeInfo.LastOperationTime = Context.CurrentBlockTime;

        var poolInfo = GetPool(stakeInfo.PoolId);

        ProcessClaim(poolInfo, stakeInfo);

        Context.SendVirtualInline(stakeInfo.PoolId, poolInfo.Config.StakeTokenContract, "Transfer", new TransferInput
        {
            Amount = stakeInfo.StakedAmount,
            Memo = "withdraw",
            Symbol = poolInfo.Config.StakingToken,
            To = stakeInfo.Account
        });

        Context.Fire(new Unlocked
        {
            StakeId = existId,
            PoolData = State.PoolDataMap[stakeInfo.PoolId],
            Amount = stakeInfo.StakedAmount
        });

        return new Empty();
    }

    public override Empty EarlyStake(EarlyStakeInput input)
    {
        // TODO Not needed for now
        // Assert(input != null, "Invalid input.");
        // Assert(input.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");
        //
        // var poolInfo = GetPool(input.PoolId);
        // Assert(input.Period >= 0 && input.Period <= poolInfo.Config.MaximumStakeDuration, "Invalid period.");
        // Assert(Context.CurrentHeight < poolInfo.Config.EndBlockNumber, "Pool closed.");
        //
        // var amount = ProcessEarlyStake(input.ClaimIds.ToList(), poolInfo);
        // Assert(amount >= poolInfo.Config.MinimumAmount, "Invalid amount.");
        //
        // ProcessStake(poolInfo, amount, input.Period, Context.Sender);
        //
        // var stakeId = State.UserStakeIdMap[input.PoolId][Context.Sender];
        // Context.SendVirtualInline(stakeId, poolInfo.Config.RewardTokenContract, "Transfer", new TransferInput
        // {
        //     To = CalculateVirtualAddress(input.PoolId),
        //     Amount = amount,
        //     Symbol = poolInfo.Config.RewardToken
        // });

        return new Empty();
    }

    public override Empty StakeFor(StakeForInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(State.EcoEarnPointsContract.Value == Context.Sender, "No permission.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(IsAddressValid(input.Address), "Invalid address.");

        var poolInfo = GetPool(input.PoolId);
        Assert(input.Amount >= poolInfo.Config.MinimumAmount, "Invalid amount.");
        Assert(input.Period >= 0 && input.Period <= poolInfo.Config.MaximumStakeDuration, "Invalid period.");
        Assert(Context.CurrentHeight < poolInfo.Config.EndBlockNumber, "Pool closed.");

        ProcessStake(poolInfo, 0, input.Amount, input.Period, input.Address);

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
        return amount.Mul(config.FixedBoostFactor).Div(EcoEarnTokensContractConstants.Denominator).Mul(period)
            .Add(amount);
    }

    private void UpdatePool(PoolInfo info, PoolData poolData)
    {
        var blockNumber = Context.CurrentHeight;

        if (poolData == null) return;

        if (blockNumber <= poolData.LastRewardBlock) return;

        if (poolData.TotalStakedAmount == 0)
        {
            poolData.LastRewardBlock = blockNumber;
            return;
        }

        var multiplier = GetMultiplier(poolData.LastRewardBlock, blockNumber, info.Config.EndBlockNumber);
        var rewards = multiplier.Mul(info.Config.RewardPerBlock);
        poolData.AccTokenPerShare = rewards.Mul(EcoEarnTokensContractConstants.Denominator)
            .Div(poolData.TotalStakedAmount).Add(poolData.AccTokenPerShare);
        poolData.LastRewardBlock = blockNumber;
    }

    private long GetMultiplier(long from, long to, long endBlockNumber)
    {
        if (to <= endBlockNumber) return to - from;
        if (from >= endBlockNumber) return 0;
        return endBlockNumber - from;
    }

    private long CalculatePending(long amount, long accTokenPerShare, long debt)
    {
        return amount.Mul(accTokenPerShare).Div(EcoEarnTokensContractConstants.Denominator) - debt;
    }

    private long CalculateDebt(long amount, long accTokenPerShare)
    {
        return amount.Mul(accTokenPerShare).Div(EcoEarnTokensContractConstants.Denominator);
    }

    private long ProcessEarlyStake(List<Hash> claimIds, PoolInfo poolInfo)
    {
        var amount = 0L;

        if (claimIds.Count == 0) return amount;

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim info not exists.");
            Assert(claimInfo.EarlyStakeTime == null, "Already early staked.");
            Assert(claimInfo.WithdrawTime == null, "Already withdrawn.");
            Assert(claimInfo.ClaimedSymbol == poolInfo.Config.StakingToken, "Token not matched.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(claimInfo.PoolId == poolInfo.PoolId, "Pool id not matched.");

            amount = amount.Add(claimInfo.ClaimedAmount);
            claimInfo.EarlyStakeTime = Context.CurrentBlockTime;
        }

        return amount;
    }

    private void ProcessStake(PoolInfo poolInfo, long stakedAmount, long earlyStakedAmount, long period, Address address)
    {
        var existId = State.UserStakeIdMap[poolInfo.PoolId]?[address];
        var stakeInfo = existId == null ? new StakeInfo() : State.StakeInfoMap[existId];

        var amount = stakedAmount.Add(earlyStakedAmount);
        
        if (amount > 0)
        {
            Assert(amount >= poolInfo.Config.MinimumAmount, "Amount not enough.");
            stakeInfo.StakedAmount = stakeInfo.StakedAmount.Add(stakedAmount);
            stakeInfo.EarlyStakedAmount = stakeInfo.EarlyStakedAmount.Add(earlyStakedAmount);
        }

        if (period > 0)
        {
            Assert(period <= poolInfo.Config.MaximumStakeDuration, "Period too long.");
            stakeInfo.Period = stakeInfo.Period.Add(period);
        }

        // create position
        if (existId == null || Context.CurrentBlockTime >= stakeInfo.StakedTime.AddDays(stakeInfo.Period))
        {
            Assert(amount > 0 && period > 0, "New position requires both amount and period.");
            var stakeId = GenerateStakeId(poolInfo.PoolId, address);
            Assert(State.StakeInfoMap[stakeId] == null, "Stake id exists.");

            stakeInfo = new StakeInfo
            {
                StakeId = stakeId,
                PoolId = poolInfo.PoolId,
                Account = address,
                StakedBlockNumber = Context.CurrentHeight,
                StakedTime = Context.CurrentBlockTime,
                Period = period,
                StakedAmount = stakedAmount,
                EarlyStakedAmount = earlyStakedAmount,
                LastOperationTime = Context.CurrentBlockTime,
                StakingToken = poolInfo.Config.StakingToken
            };

            State.StakeInfoMap[stakeId] = stakeInfo;
            State.UserStakeIdMap[poolInfo.PoolId][address] = stakeId;
        }
        // add position
        else
        {
            Assert(stakeInfo.Account == address, "No permission.");
        }

        var boostedAmount = CalculateBoostedAmount(poolInfo.Config,
            stakeInfo.StakedAmount.Add(stakeInfo.EarlyStakedAmount), stakeInfo.Period);

        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        if (stakeInfo.BoostedAmount > 0)
        {
            var pending = CalculatePending(stakeInfo.BoostedAmount, poolData.AccTokenPerShare, stakeInfo.RewardDebt);
            var actualReward = ProcessCommissionFee(pending, poolInfo);
            if (actualReward > 0)
            {
                stakeInfo.RewardAmount = stakeInfo.RewardAmount.Add(actualReward);
                Context.SendVirtualInline(stakeInfo.PoolId, poolInfo.Config.RewardTokenContract, "Transfer",
                    new TransferInput
                    {
                        To = CalculateVirtualAddress(stakeInfo.StakeId),
                        Amount = actualReward,
                        Memo = "reward",
                        Symbol = poolInfo.Config.RewardToken
                    });
            }
        }

        poolData.TotalStakedAmount = poolData.TotalStakedAmount.Add(boostedAmount).Sub(stakeInfo.BoostedAmount);
        stakeInfo.BoostedAmount = boostedAmount;
        stakeInfo.RewardDebt = CalculateDebt(boostedAmount, poolData.AccTokenPerShare);

        Context.Fire(new Staked
        {
            StakeInfo = stakeInfo,
            PoolData = poolData
        });
    }

    private long ProcessCommissionFee(long pending, PoolInfo poolInfo)
    {
        if (pending == 0) return 0;

        var config = State.Config.Value;
        var commissionFee = CalculateCommissionFee(pending, config.CommissionRate);

        if (commissionFee != 0)
        {
            Context.SendVirtualInline(poolInfo.PoolId, State.TokenContract.Value, "Transfer", new TransferInput
            {
                To = config.Recipient,
                Amount = commissionFee,
                Symbol = poolInfo.Config.RewardToken,
                Memo = "commission"
            });
        }

        return pending - commissionFee;
    }

    #endregion
}