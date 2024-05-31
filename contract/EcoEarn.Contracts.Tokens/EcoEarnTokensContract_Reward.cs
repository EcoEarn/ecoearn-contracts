using System.Collections.Generic;
using System.Linq;
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

    public override Empty Claim(Hash input)
    {
        Assert(IsHashValid(input), "Invalid input.");

        var stakeInfo = State.StakeInfoMap[input];
        Assert(stakeInfo != null, "Stake info not exists.");
        Assert(stakeInfo!.Account == Context.Sender, "No permission.");
        Assert(stakeInfo.UnlockTime == null, "Already unlocked.");

        var poolInfo = GetPool(stakeInfo.PoolId);

        var actualReward = ProcessClaim(poolInfo, stakeInfo);
        Assert(actualReward > poolInfo.Config.MinimumClaimAmount, "Reward not enough.");

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

    private Hash GenerateClaimId(Hash stakeId)
    {
        return HashHelper.ConcatAndCompute(stakeId, HashHelper.ComputeFrom(Context.CurrentHeight));
    }

    private long ProcessClaim(PoolInfo poolInfo, StakeInfo stakeInfo)
    {
        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        var pending = CalculatePending(stakeInfo.BoostedAmount, poolData.AccTokenPerShare, stakeInfo.RewardDebt,
            poolInfo.PrecisionFactor);
        var actualReward = ProcessCommissionFee(pending, poolInfo).Add(stakeInfo.RewardAmount);

        if (actualReward == 0) return 0;
        stakeInfo.RewardAmount = 0;

        var claimId = GenerateClaimId(stakeInfo.StakeId);
        Assert(State.ClaimInfoMap[claimId] == null, "Claim id exists.");

        stakeInfo.LockedRewardAmount = stakeInfo.LockedRewardAmount.Add(actualReward);

        var claimInfo = new ClaimInfo
        {
            ClaimId = claimId,
            Account = stakeInfo.Account,
            ClaimedSymbol = poolInfo.Config.RewardToken,
            ClaimedTime = Context.CurrentBlockTime,
            ClaimedBlockNumber = Context.CurrentHeight,
            ClaimedAmount = actualReward,
            UnlockTime = Context.CurrentBlockTime.AddSeconds(poolInfo.Config.ReleasePeriod),
            PoolId = stakeInfo.PoolId
        };

        State.ClaimInfoMap[claimId] = claimInfo;
        stakeInfo.ClaimedAmount = stakeInfo.ClaimedAmount.Add(actualReward);
        stakeInfo.RewardDebt = stakeInfo.RewardDebt.Add(pending);

        Context.SendVirtualInline(GetRewardVirtualAddress(poolInfo.PoolId), poolInfo.Config.RewardTokenContract,
            nameof(State.TokenContract.Transfer), new TransferInput
            {
                Amount = claimInfo.ClaimedAmount,
                Symbol = claimInfo.ClaimedSymbol,
                To = CalculateVirtualAddress(Context.Sender),
                Memo = "claim"
            });

        Context.Fire(new Claimed
        {
            StakeId = stakeInfo.StakeId,
            ClaimInfo = claimInfo
        });

        return actualReward;
    }

    private List<ClaimInfo> ProcessClaimInfos(List<Hash> claimIds)
    {
        var result = new List<ClaimInfo>();

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim id not exists.");
            Assert(claimInfo!.WithdrawTime == null, "Already withdrawn.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(Context.CurrentBlockTime >= claimInfo.UnlockTime, "Not unlock yet.");

            claimInfo.WithdrawTime = Context.CurrentBlockTime;

            if (IsHashValid(claimInfo.StakeId))
            {
                var stakeInfo = State.StakeInfoMap[claimInfo.StakeId];
                Assert(stakeInfo != null && stakeInfo.UnlockTime != null, "Not unlocked.");
                stakeInfo!.LockedRewardAmount.Sub(claimInfo.ClaimedAmount);
            }

            var poolInfo = GetPool(claimInfo.PoolId);

            Context.SendVirtualInline(GetRewardVirtualAddress(claimInfo.PoolId), poolInfo.Config.RewardTokenContract,
                nameof(State.TokenContract.Transfer), new TransferInput
                {
                    To = claimInfo.Account,
                    Symbol = claimInfo.ClaimedSymbol,
                    Amount = claimInfo.ClaimedAmount,
                    Memo = "confirm"
                });

            result.Add(claimInfo);
        }

        return result;
    }

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

        return CalculatePending(stakeInfo.BoostedAmount, adjustedTokenPerShare, stakeInfo.RewardDebt,
            poolInfo.PrecisionFactor);
    }

    #endregion
}