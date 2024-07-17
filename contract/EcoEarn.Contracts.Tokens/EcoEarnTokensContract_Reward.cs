using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using EcoEarn.Contracts.Rewards;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Claim(Hash input)
    {
        Assert(IsHashValid(input), "Invalid input.");

        var existId = State.UserStakeIdMap[input][Context.Sender];
        Assert(existId != null, "Not staked before.");

        var poolInfo = GetPool(input);
        Assert(Context.CurrentBlockTime >= poolInfo.Config.StartTime, "Pool not start.");

        State.ClaimTimeMap[input][Context.Sender] = Context.CurrentBlockTime;

        var stakeInfo = State.StakeInfoMap[existId];
        Assert(stakeInfo != null, "Stake info not exists.");

        Assert(IsInUnlockWindow(stakeInfo, CalculateRemainTime(stakeInfo, poolInfo.Config.UnlockWindowDuration)),
            "Not in unlock window.");

        var term = CalculateWindowTerms(stakeInfo, poolInfo.Config.UnlockWindowDuration);
        Assert(!State.WindowTermMap[stakeInfo!.StakeId][stakeInfo.LastOperationTime][term],
            "Already claimed during this window.");

        State.WindowTermMap[stakeInfo.StakeId][stakeInfo.LastOperationTime][term] = true;

        var rewards = 0L;

        if (stakeInfo!.UnlockTime == null)
        {
            rewards = ProcessRewards(poolInfo, stakeInfo);
            Assert(rewards > 0, "Nothing to claim.");
        }

        CallRewardsContractClaim(poolInfo, rewards);

        Context.Fire(new Claimed
        {
            PoolId = input,
            Account = Context.Sender,
            Amount = rewards
        });

        return new Empty();
    }

    public override Empty RecoverToken(RecoverTokenInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.PoolId), "Invalid pool id.");
        Assert(IsStringValid(input.Token), "Invalid token.");
        Assert(input.Recipient == null || !input.Recipient.Value.IsNullOrEmpty(), "Invalid recipient.");

        var poolInfo = GetPool(input.PoolId);
        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(!CheckPoolEnabled(poolInfo.Config.EndTime), "Pool not closed.");

        var output = Context.Call<GetBalanceOutput>(poolInfo.Config.RewardTokenContract, "GetBalance",
            new GetBalanceInput
            {
                Owner = CalculateVirtualAddress(GetRewardVirtualAddress(input.PoolId)),
                Symbol = input.Token
            });

        Assert(output.Balance > 0, "Invalid token.");

        Context.SendVirtualInline(GetRewardVirtualAddress(input.PoolId), poolInfo.Config.RewardTokenContract,
            "Transfer", new TransferInput
            {
                Amount = output.Balance,
                Symbol = input.Token,
                To = input.Recipient ?? Context.Sender,
                Memo = "recover"
            });

        Context.Fire(new TokenRecovered
        {
            PoolId = input.PoolId,
            Account = input.Recipient ?? Context.Sender,
            Amount = output.Balance,
            Token = input.Token
        });

        return new Empty();
    }

    #endregion

    #region private

    private long CalculateCommissionFee(long amount, long commissionRate)
    {
        return amount.Mul(commissionRate).Div(EcoEarnTokensContractConstants.Denominator);
    }

    private long CalculateRewardAmount(PoolInfo poolInfo, PoolData poolData, StakeInfo stakeInfo)
    {
        var accTokenPerShare = poolData.AccTokenPerShare ?? new BigIntValue(0);
        var multiplier = GetMultiplier(poolData.LastRewardTime.Seconds, Context.CurrentBlockTime.Seconds,
            poolInfo.Config.EndTime.Seconds);
        var rewards = new BigIntValue(multiplier.Mul(poolInfo.Config.RewardPerSecond));
        var adjustedTokenPerShare = poolData.TotalStakedAmount > 0
            ? accTokenPerShare.Add(rewards.Mul(poolInfo.PrecisionFactor).Div(poolData.TotalStakedAmount))
            : accTokenPerShare;

        var amount = 0L;
        var config = State.Config.Value;

        foreach (var subStakeInfo in stakeInfo.SubStakeInfos)
        {
            var pending = CalculatePending(subStakeInfo.BoostedAmount, adjustedTokenPerShare, subStakeInfo.RewardDebt,
                poolInfo.PrecisionFactor);
            amount = amount.Add(pending.Sub(CalculateCommissionFee(pending, config.CommissionRate))
                .Add(subStakeInfo.RewardAmount));
        }

        return amount;
    }

    private long ProcessRewards(PoolInfo poolInfo, StakeInfo stakeInfo)
    {
        var rewards = 0L;
        var pendingAmount = 0L;

        var config = State.Config.Value;

        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        foreach (var subStakeInfo in stakeInfo.SubStakeInfos)
        {
            var pending = CalculatePending(subStakeInfo.BoostedAmount, poolData.AccTokenPerShare,
                subStakeInfo.RewardDebt, poolInfo.PrecisionFactor);

            pendingAmount = pendingAmount.Add(pending);

            var actualReward = pending.Sub(CalculateCommissionFee(pending, config.CommissionRate))
                .Add(subStakeInfo.RewardAmount);

            if (actualReward <= 0) continue;
            subStakeInfo.RewardAmount = 0;

            subStakeInfo.RewardDebt = subStakeInfo.RewardDebt.Add(pending);

            rewards = rewards.Add(actualReward);
        }

        ProcessCommissionFee(pendingAmount, poolInfo);

        return rewards;
    }

    private void CallRewardsContractClaim(PoolInfo poolInfo, long rewards)
    {
        State.TokenContract.Transfer.VirtualSend(GetRewardVirtualAddress(poolInfo.PoolId), new TransferInput
        {
            To = Context.Self,
            Amount = rewards,
            Symbol = poolInfo.Config.RewardToken
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Amount = rewards,
            Spender = State.EcoEarnRewardsContract.Value,
            Symbol = poolInfo.Config.RewardToken
        });

        State.EcoEarnRewardsContract.Claim.Send(new ClaimInput
        {
            PoolId = poolInfo.PoolId,
            Symbol = poolInfo.Config.RewardToken,
            Account = Context.Sender,
            Amount = rewards,
            ReleasePeriods = { poolInfo.Config.ReleasePeriods },
            DappId = poolInfo.DappId
        });
    }

    #endregion
}