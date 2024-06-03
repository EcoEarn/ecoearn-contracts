using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Claim(ClaimInput input)
    {
        ValidateClaimInput(input);

        var stakeInfo = State.StakeInfoMap[input.StakeId];
        Assert(stakeInfo != null, "Stake info not exists.");
        Assert(stakeInfo!.Account == Context.Sender, "No permission.");

        var poolInfo = GetPool(stakeInfo.PoolId);
        Assert(Context.CurrentBlockTime >= poolInfo.Config.StartTime, "Pool not start.");
        Assert(Context.CurrentBlockTime.Seconds < input.ExpirationTime, "Signature expired.");
        
        UpdateReward(poolInfo, stakeInfo);
        
        Assert(input.Amount <= stakeInfo.RewardAmount.Div(poolInfo.Config.ReleasePeriod), "Amount too much.");

        Assert(RecoverAddressFromSignature(input) == poolInfo.Config.UpdateAddress, "Signature not valid.");

        State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())] = true;

        var claimId = GenerateClaimId(input.StakeId);
        Assert(State.ClaimInfoMap[claimId] == null, "Claim id taken.");

        var claimedAmount = ProcessCommissionFee(input.Amount, poolInfo);

        stakeInfo.ClaimedAmount = stakeInfo.ClaimedAmount.Add(claimedAmount);

        var claimInfo = new ClaimInfo
        {
            ClaimId = claimId,
            ClaimedAmount = claimedAmount,
            ClaimedBlockNumber = Context.CurrentHeight,
            ClaimedSymbol = poolInfo.Config.RewardToken,
            ClaimedTime = Context.CurrentBlockTime,
            PoolId = poolInfo.PoolId,
            Account = input.Account,
            Seed = input.Seed
        };

        State.ClaimInfoMap[claimId] = claimInfo;

        // transfer rewards to user
        State.TokenContract.Transfer.VirtualSend(GetRewardVirtualAddress(stakeInfo.PoolId), new TransferInput
        {
            To = Context.Sender,
            Amount = claimedAmount,
            Symbol = poolInfo.Config.RewardToken,
            Memo = "claim"
        });

        Context.Fire(new Claimed
        {
            StakeId = input.StakeId,
            ClaimInfo = claimInfo
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

    private Hash GenerateClaimId(Hash stakeId)
    {
        return HashHelper.ConcatAndCompute(stakeId, HashHelper.ComputeFrom(Context.CurrentHeight));
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

    private void ValidateClaimInput(ClaimInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.StakeId), "Invalid stake id.");
        Assert(IsAddressValid(input.Account) && input.Account == Context.Sender, "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(input.ExpirationTime > 0, "Invalid expiration time.");
        Assert(
            !input.Signature.IsNullOrEmpty() &&
            !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())], "Invalid signature.");
    }

    private Address RecoverAddressFromSignature(ClaimInput input)
    {
        var computedHash = ComputeConfirmInputHash(input);
        var publicKey = Context.RecoverPublicKey(input.Signature.ToByteArray(), computedHash.ToByteArray());
        Assert(publicKey != null, "Invalid signature.");

        return Address.FromPublicKey(publicKey);
    }

    private Hash ComputeConfirmInputHash(ClaimInput input)
    {
        return HashHelper.ComputeFrom(new ClaimInput
        {
            StakeId = input.StakeId,
            Account = input.Account,
            Amount = input.Amount,
            Seed = input.Seed,
            ExpirationTime = input.ExpirationTime
        }.ToByteArray());
    }
    
    private void UpdateReward(PoolInfo poolInfo, StakeInfo stakeInfo)
    {
        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        var pending = CalculatePending(stakeInfo.BoostedAmount, poolData.AccTokenPerShare, stakeInfo.RewardDebt,
            poolInfo.PrecisionFactor);
        var actualReward = ProcessCommissionFee(pending, poolInfo).Add(stakeInfo.RewardAmount);
        stakeInfo.RewardAmount = stakeInfo.RewardAmount.Add(actualReward);
        
        stakeInfo.RewardDebt = stakeInfo.RewardDebt.Add(pending);
    }

    #endregion
}