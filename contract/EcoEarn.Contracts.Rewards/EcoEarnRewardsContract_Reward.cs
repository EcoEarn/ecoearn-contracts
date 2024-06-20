using System.Collections.Generic;
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

        Assert(
            RecoverAddressFromSignature(ComputeWithdrawInputHash(input), input.Signature) ==
            dappInfo!.Config.UpdateAddress, "Signature not valid.");

        Assert(Context.CurrentBlockTime.Seconds < input.ExpirationTime, "Signature expired.");

        State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())] = true;

        var claimInfos = ProcessClaimIds(input.ClaimIds.Distinct().ToList(), out var symbol);
            
        var maximumReward = State.TokenContract.GetBalance.Call(new GetBalanceInput
        {
            Owner = CalculateUserAddress(dappInfo.DappId, Context.Sender),
            Symbol = symbol
        }).Balance;
        Assert(input.Amount <= maximumReward, "Amount too much.");

        State.TokenContract.Transfer.VirtualSend(CalculateUserAddressHash(dappInfo.DappId, Context.Sender),
            new TransferInput
            {
                Amount = input.Amount,
                Memo = "withdraw",
                Symbol = symbol,
                To = input.Account
            });

        Context.Fire(new Withdrawn
        {
            ClaimInfos = claimInfos,
            Account = input.Account,
            Amount = input.Amount,
            Seed = input.Seed
        });

        return new Empty();
    }

    public override Empty EarlyStake(EarlyStakeInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidateEarlyStakeInput(input!.StakeInput);
        Assert(
            !input.Signature.IsNullOrEmpty() &&
            !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())], "Invalid signature.");

        var stakeInput = input!.StakeInput;

        var stakeId = GetStakeId(stakeInput.PoolId);

        var dappInfo = State.DappInfoMap[stakeInput.DappId];
        Assert(dappInfo != null, "Dapp id not exists.");

        Assert(
            RecoverAddressFromSignature(ComputeEarlyStakeInputHash(input), input.Signature) ==
            dappInfo!.Config.UpdateAddress, "Signature not valid.");

        var claimInfos = ProcessEarlyStake(stakeInput, input.Signature, stakeId, dappInfo.DappId, out var symbol,
            out var longestReleaseTime);

        var poolAddressInfo = State.EcoEarnTokensContract.GetPoolAddressInfo.Call(stakeInput.PoolId);

        State.TokenContract.Transfer.VirtualSend(CalculateUserAddressHash(dappInfo.DappId, Context.Sender),
            new TransferInput
            {
                Amount = stakeInput.Amount,
                Memo = "early",
                Symbol = symbol,
                To = poolAddressInfo.StakeAddress
            });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = stakeInput.PoolId,
            Amount = stakeInput.Amount,
            FromAddress = stakeInput.Account,
            Period = stakeInput.Period,
            LongestReleaseTime = longestReleaseTime,
            Seed = stakeInput.Seed
        });

        Context.Fire(new EarlyStaked
        {
            ClaimInfos = claimInfos,
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
        Assert(input.ExpirationTime > 0, "Invalid expiration time.");
        Assert(
            !input.Signature.IsNullOrEmpty() &&
            !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())], "Invalid signature.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
    }

    private void ValidateEarlyStakeInput(StakeInput input)
    {
        Assert(input != null, "Invalid stake input.");
        Assert(input!.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");
        Assert(IsAddressValid(input.Account), "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(input.ExpirationTime > 0, "Invalid expiration time.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(input.Period > 0, "Invalid period.");
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

    private Hash ComputeEarlyStakeInputHash(EarlyStakeInput input)
    {
        return HashHelper.ComputeFrom(new EarlyStakeInput
        {
            StakeInput = input.StakeInput
        }.ToByteArray());
    }

    private Hash ComputeAddLiquidityAndStakeHash(AddLiquidityAndStakeInput input)
    {
        return HashHelper.ComputeFrom(new AddLiquidityAndStakeInput
        {
            StakeInput = input.StakeInput,
            TokenAMin = input.TokenAMin,
            TokenBMin = input.TokenBMin
        }.ToByteArray());
    }

    private Hash ComputeStakeInputHash(StakeInput input)
    {
        return HashHelper.ComputeFrom(input.ToByteArray());
    }

    private ClaimInfos ProcessEarlyStake(StakeInput input, ByteString signature, Hash stakeId, Hash dappId,
        out string symbol, out Timestamp longestReleaseTime)
    {
        ValidateEarlyStakeInput(input);
        Assert(!signature.IsNullOrEmpty() && !State.SignatureMap[HashHelper.ComputeFrom(signature.ToByteArray())],
            "Invalid signature.");

        Assert(Context.CurrentBlockTime.Seconds < input.ExpirationTime, "Signature expired.");

        State.SignatureMap[HashHelper.ComputeFrom(signature.ToByteArray())] = true;

        var claimInfos = new ClaimInfos();
        symbol = null;
        longestReleaseTime = null;

        foreach (var claimId in input.ClaimIds.Distinct())
        {
            Assert(IsHashValid(claimId), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[claimId];
            Assert(claimInfo.WithdrawnTime == null, "Already claimed.");

            claimInfo.EarlyStakedAmount = claimInfo.ClaimedAmount;

            if (claimInfo.StakeId != null)
            {
                var getStakeInfoOutput = State.EcoEarnTokensContract.GetStakeInfo.Call(claimInfo.StakeId);
                Assert(getStakeInfoOutput.StakeInfo?.UnlockTime != null, "Cannot early stake yet.");
            }

            claimInfo.StakeId = stakeId;

            symbol ??= claimInfo.ClaimedSymbol;
            Assert(claimInfo.ClaimedSymbol == symbol, "Symbol not match.");

            longestReleaseTime = longestReleaseTime == null || longestReleaseTime < claimInfo.ReleaseTime
                ? claimInfo.ReleaseTime
                : longestReleaseTime;
            
            claimInfos.Data.Add(claimInfo);
        }

        var maximumReward = State.TokenContract.GetBalance.Call(new GetBalanceInput
        {
            Owner = CalculateUserAddress(dappId, Context.Sender),
            Symbol = symbol
        }).Balance;
        Assert(input.Amount <= maximumReward, "Amount too much.");

        return claimInfos;
    }

    private Hash GetStakeId(Hash poolId)
    {
        var stakeId = State.EcoEarnTokensContract.GetUserStakeId.Call(new GetUserStakeIdInput
        {
            PoolId = poolId,
            Account = Context.Sender
        });

        var output = State.EcoEarnTokensContract.GetStakeInfo.Call(stakeId);

        if (IsHashValid(stakeId) && output.StakeInfo.UnlockTime == null) return stakeId;

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

    private ClaimInfos ProcessClaimIds(List<Hash> claimIds, out string symbol)
    {
        var claimInfos = new ClaimInfos();
        symbol = null;

        foreach (var claimId in claimIds)
        {
            Assert(IsHashValid(claimId), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[claimId];
            Assert(claimInfo != null, "Claim id not exists.");
            Assert(claimInfo.WithdrawnTime == null, "Already claimed.");

            claimInfo.WithdrawnTime = Context.CurrentBlockTime;

            if (claimInfo.StakeId != null)
            {
                var getStakeInfoOutput = State.EcoEarnTokensContract.GetStakeInfo.Call(claimInfo.StakeId);
                Assert(getStakeInfoOutput.StakeInfo?.UnlockTime != null, "Cannot claim yet.");
            }

            symbol ??= claimInfo.ClaimedSymbol;
            Assert(claimInfo.ClaimedSymbol == symbol, "Symbol not match.");

            claimInfos.Data.Add(claimInfo);
        }

        return claimInfos;
    }

    #endregion
}