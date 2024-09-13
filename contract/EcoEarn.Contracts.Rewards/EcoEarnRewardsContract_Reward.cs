using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    #region public

    public override Empty Claim(ClaimInput input)
    {
        CheckInitialized();
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.PoolId), "Invalid pool id.");
        Assert(IsAddressValid(input.Account), "Invalid account.");
        Assert(IsStringValid(input.Symbol), "Invalid symbol.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(input.ReleasePeriods != null && input.ReleasePeriods.Count > 0 && input.ReleasePeriods.All(p => p >= 0),
            "Invalid release periods.");
        Assert(input.Seed == null || !input.Seed.Value.IsNullOrEmpty(), "Invalid seed.");

        Assert(
            Context.Sender == State.EcoEarnPointsContract.Value || Context.Sender == State.EcoEarnTokensContract.Value,
            "No permission.");

        var claimInfos = GenerateClaimInfos(input);

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            Symbol = input.Symbol,
            To = CalculateUserAddress(input.DappId, input.Account),
            Amount = input.Amount
        });

        Context.Fire(new Claimed
        {
            ClaimInfos = claimInfos
        });

        return new Empty();
    }

    public override Empty Withdraw(WithdrawInput input)
    {
        ValidateWithdrawInput(input);
        
        var dappInfo = State.DappInfoMap[input.DappId];
        Assert(dappInfo != null, "Dapp id not exists.");

        ValidateSignature(input.Signature, input.ExpirationTime);
        Assert(
            RecoverAddressFromSignature(ComputeWithdrawInputHash(input), input.Signature) ==
            GetUpdateAddress(dappInfo), "Signature not valid.");

        var claimInfo = GetClaimInfoFromClaimId(input.ClaimIds.FirstOrDefault());
        CheckMaximumAmount(State.TokenContract.Value, input.DappId, claimInfo.ClaimedSymbol, input.Amount);

        State.TokenContract.Transfer.VirtualSend(CalculateUserAddressHash(dappInfo.DappId, Context.Sender),
            new TransferInput
            {
                Amount = input.Amount,
                Memo = "withdraw",
                Symbol = claimInfo.ClaimedSymbol,
                To = input.Account
            });

        Context.Fire(new Withdrawn
        {
            ClaimIds = new HashList
            {
                Data = { input.ClaimIds }
            },
            Account = input.Account,
            Amount = input.Amount,
            Seed = input.Seed
        });

        return new Empty();
    }

    public override Empty StakeRewards(StakeRewardsInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidateStakeInput(input!.StakeInput);

        var stakeInput = input!.StakeInput;
        
        var stakeId = GetStakeId(stakeInput.PoolId);

        var dappInfo = State.DappInfoMap[stakeInput.DappId];
        Assert(dappInfo != null, "Dapp id not exists.");

        ValidateSignature(input.Signature, stakeInput.ExpirationTime);
        Assert(
            RecoverAddressFromSignature(ComputeStakeRewardsInputHash(input), input.Signature) ==
            GetUpdateAddress(dappInfo), "Signature not valid.");

        var claimInfo = GetClaimInfoFromClaimId(stakeInput.ClaimIds.FirstOrDefault());
        CheckMaximumAmount(State.TokenContract.Value, stakeInput.DappId, claimInfo.ClaimedSymbol,
            stakeInput.Amount);

        State.TokenContract.Transfer.VirtualSend(CalculateUserAddressHash(dappInfo.DappId, Context.Sender),
            new TransferInput
            {
                Amount = stakeInput.Amount,
                Symbol = claimInfo.ClaimedSymbol,
                To = Context.Self,
                Memo = "early"
            });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = State.EcoEarnTokensContract.Value,
            Amount = stakeInput.Amount,
            Symbol = claimInfo.ClaimedSymbol
        });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = stakeInput.PoolId,
            Amount = stakeInput.Amount,
            FromAddress = stakeInput.Account,
            Period = stakeInput.Period,
            LongestReleaseTime = new Timestamp
            {
                Seconds = stakeInput.LongestReleaseTime
            }
        });

        Context.Fire(new RewardsStaked
        {
            ClaimIds = new HashList
            {
                Data = { stakeInput.ClaimIds }
            },
            Account = stakeInput.Account,
            Amount = stakeInput.Amount,
            Seed = stakeInput.Seed,
            PoolId = stakeInput.PoolId,
            Period = stakeInput.Period,
            StakeId = stakeId
        });

        return new Empty();
    }

    #endregion

    #region private

    private ClaimInfos GenerateClaimInfos(ClaimInput input)
    {
        var claimInfos = new ClaimInfos();

        var maximumPeriod = input.ReleasePeriods!.Last();

        if (maximumPeriod == 0)
        {
            var claimInfo = GenerateClaimInfo(input, input.Amount, 0, 0);

            claimInfos.Data.Add(claimInfo);
        }
        else
        {
            long releasedAmount = 0;

            for (var i = 0; i < input.ReleasePeriods.Count; i++)
            {
                var period = input.ReleasePeriods[i];

                var amount = input.Amount.Mul(period).Div(maximumPeriod);
                amount = amount.Sub(releasedAmount);

                var claimInfo = GenerateClaimInfo(input, amount, period, i);

                claimInfos.Data.Add(claimInfo);

                releasedAmount = releasedAmount.Add(amount);
            }
        }

        return claimInfos;
    }

    private ClaimInfo GenerateClaimInfo(ClaimInput input, long amount, long period, long count)
    {
        var claimId = GenerateClaimId(input, count);
        Assert(State.ClaimInfoMap[claimId] == null, "Claim id exists.");

        var claimInfo = new ClaimInfo
        {
            ClaimId = claimId,
            PoolId = input.PoolId,
            ClaimedAmount = amount,
            ClaimedSymbol = input.Symbol,
            ClaimedBlockNumber = Context.CurrentHeight,
            ClaimedTime = Context.CurrentBlockTime,
            Account = input.Account,
            ReleaseTime = Context.CurrentBlockTime.AddSeconds(period),
            Seed = input.Seed,
            ContractAddress = Context.Sender
        };

        State.ClaimInfoMap[claimId] = claimInfo;

        return claimInfo;
    }

    private Hash GenerateClaimId(ClaimInput input, long count)
    {
        return HashHelper.ConcatAndCompute(
            HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(input), HashHelper.ComputeFrom(count)),
            HashHelper.ComputeFrom(Context.CurrentHeight));
    }

    private void ValidateWithdrawInput(WithdrawInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");
        Assert(IsAddressValid(input.Account), "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
    }

    private void ValidateStakeInput(StakeInput input)
    {
        Assert(input != null, "Invalid stake input.");
        Assert(input!.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");
        Assert(IsAddressValid(input.Account), "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(input.Period > 0, "Invalid period.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
        Assert(input.LongestReleaseTime > 0, "Invalid longest release time.");
        Assert(Context.Sender == input.Account, "No permission.");
    }

    private Address RecoverAddressFromSignature(Hash input, ByteString signature)
    {
        var publicKey = Context.RecoverPublicKey(signature.ToByteArray(), input.ToByteArray());
        Assert(publicKey != null, "Invalid signature.");

        return Address.FromPublicKey(publicKey);
    }

    private Hash ComputeWithdrawInputHash(WithdrawInput input)
    {
        return HashHelper.ComputeFrom(new WithdrawInput
        {
            ClaimIds = { input.ClaimIds },
            Account = input.Account,
            Amount = input.Amount,
            Seed = input.Seed,
            ExpirationTime = input.ExpirationTime,
            DappId = input.DappId
        }.ToByteArray());
    }

    private Hash ComputeStakeRewardsInputHash(StakeRewardsInput input)
    {
        return HashHelper.ComputeFrom(new StakeRewardsInput
        {
            StakeInput = input.StakeInput
        }.ToByteArray());
    }

    private Hash GetStakeId(Hash poolId)
    {
        var stakeId = State.EcoEarnTokensContract.GetUserStakeId.Call(new GetUserStakeIdInput
        {
            PoolId = poolId,
            Account = Context.Sender
        });

        if (IsHashValid(stakeId))
        {
            var output = State.EcoEarnTokensContract.GetStakeInfo.Call(stakeId);
            if (output.StakeInfo.UnstakeTime == null) return stakeId;
        }

        var count = State.EcoEarnTokensContract.GetUserStakeCount.Call(new GetUserStakeCountInput
        {
            Account = Context.Sender,
            PoolId = poolId
        }).Value;

        stakeId = GenerateStakeId(poolId, Context.Sender, count);

        return stakeId;
    }

    private Hash GenerateStakeId(Hash poolId, Address sender, long count)
    {
        return HashHelper.ConcatAndCompute(
            HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(count), HashHelper.ComputeFrom(sender)), poolId);
    }

    private ClaimInfo GetClaimInfoFromClaimId(Hash claimId)
    {
        Assert(IsHashValid(claimId), "Invalid claim id.");
        var claimInfo = State.ClaimInfoMap[claimId];
        Assert(claimInfo != null && IsHashValid(claimInfo.ClaimId), "Claim id not exists.");

        return claimInfo;
    }

    private void CheckMaximumAmount(Address tokenContract, Hash dappId, string symbol, long amount)
    {
        var maximumAmount = Context.Call<GetBalanceOutput>(tokenContract, nameof(State.TokenContract.GetBalance),
            new GetBalanceInput
            {
                Owner = CalculateUserAddress(dappId, Context.Sender),
                Symbol = symbol
            }).Balance;

        Assert(amount <= maximumAmount, "Amount too much.");
    }

    private void ValidateSignature(ByteString signature, long expirationTime)
    {
        Assert(expirationTime > 0, "Invalid expiration time.");
        Assert(!signature.IsNullOrEmpty(), "Invalid signature.");
        Assert(Context.CurrentBlockTime.Seconds < expirationTime, "Signature expired.");

        var signatureHash = HashHelper.ComputeFrom(signature.ToByteArray());
        Assert(!State.SignatureMap[signatureHash], "Signature used.");
        State.SignatureMap[signatureHash] = true;
    }

    #endregion
}