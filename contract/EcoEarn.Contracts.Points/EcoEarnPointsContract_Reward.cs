using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContract
{
    #region public

    public override Empty UpdateSnapshot(UpdateSnapshotInput input)
    {
        Assert(input != null, "Invalid input.");
        var poolInfo = GetPool(input!.PoolId);
        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool disabled.");
        
        Assert(GetUpdateAddress(poolInfo.DappId) == Context.Sender, "No permission.");

        var currentHeight = Context.CurrentHeight;
        Assert(State.SnapshotMap[input.PoolId][currentHeight] == null, "Duplicate Snapshot.");

        Assert(IsHashValid(input.MerkleTreeRoot), "Invalid merkle tree root.");

        State.SnapshotMap[input.PoolId][currentHeight] = new Snapshot
        {
            PoolId = input.PoolId,
            MerkleTreeRoot = input.MerkleTreeRoot,
            BlockNumber = currentHeight
        };

        Context.Fire(new SnapshotUpdated
        {
            PoolId = input.PoolId,
            MerkleTreeRoot = input.MerkleTreeRoot,
            UpdateBlockNumber = currentHeight
        });

        return new Empty();
    }

    public override Empty Claim(ClaimInput input)
    {
        ValidateClaimInput(input);

        var poolInfo = GetPool(input.PoolId);
        Assert(Context.CurrentBlockTime >= poolInfo.Config.StartTime, "Pool not start.");
        Assert(Context.CurrentBlockTime.Seconds < input.ExpirationTime, "Signature expired.");

        CheckClaimLimitation(poolInfo, input.Amount);
        UpdatePoolData(input.PoolId, input.Amount);
        
        ValidateSignature(input, GetUpdateAddress(poolInfo.DappId));

        ChargeCommissionFee(poolInfo, input.Amount, out var claimedAmount);

        CallRewardsContractClaim(poolInfo, claimedAmount, input.Seed);
        
        Join(Context.Sender);

        Context.Fire(new Claimed
        {
            PoolId = input.PoolId,
            Account = Context.Sender,
            Amount = claimedAmount,
            Seed = input.Seed
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
        GetAndCheckDAppAdminPermission(poolInfo.DappId);

        Assert(!CheckPoolEnabled(poolInfo.Config.EndTime), "Pool not closed.");

        var output = State.TokenContract.GetBalance.Call(new GetBalanceInput
        {
            Owner = CalculateVirtualAddress(input.PoolId),
            Symbol = input.Token
        });

        Assert(output.Balance > 0, "Invalid token.");

        State.TokenContract.Transfer.VirtualSend(input.PoolId, new TransferInput
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
            PoolId = input.PoolId,
            Account = input.Account,
            Amount = input.Amount,
            Seed = input.Seed,
            ExpirationTime = input.ExpirationTime
        }.ToByteArray());
    }

    private void ValidateClaimInput(ClaimInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input!.Account) && input.Account == Context.Sender, "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed), "Invalid seed.");
        Assert(input.ExpirationTime > 0, "Invalid expiration time.");
        Assert(
            !input.Signature.IsNullOrEmpty() &&
            !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())], "Invalid signature.");
    }

    private long CalculateCommissionFee(long amount, long commissionRate)
    {
        return amount.Mul(commissionRate).Div(EcoEarnPointsContractConstants.Denominator);
    }

    private void CheckClaimLimitation(PoolInfo poolInfo, long amount)
    {
        var poolId = poolInfo.PoolId;

        Assert(
            State.ClaimTimeMap[poolId][Context.Sender] == null || Context.CurrentBlockTime >=
            State.ClaimTimeMap[poolId][Context.Sender].AddSeconds(poolInfo.Config.ClaimInterval),
            "Cannot claim yet.");
        State.ClaimTimeMap[poolId][Context.Sender] = Context.CurrentBlockTime;

        var poolData = State.PoolDataMap[poolId];
        var maximumReward = (Context.CurrentBlockTime - poolData.LastRewardsUpdateTime).Seconds
            .Mul(poolInfo.Config.RewardPerSecond)
            .Add(poolData.CalculatedRewards).Sub(poolData.ClaimedRewards);
        Assert(amount <= maximumReward, "Amount too much.");
    }

    private void ValidateSignature(ClaimInput input, Address updateAddress)
    {
        Assert(RecoverAddressFromSignature(input) == updateAddress, "Signature not valid.");
        State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())] = true;
    }

    private void ChargeCommissionFee(PoolInfo poolInfo, long amount, out long claimedAmount)
    {
        var config = State.Config.Value;

        // calculate and charge commission fee
        var commissionFee = CalculateCommissionFee(amount, config.CommissionRate);
        if (commissionFee > 0)
        {
            State.TokenContract.Transfer.VirtualSend(poolInfo.PoolId, new TransferInput
            {
                To = config.Recipient,
                Amount = commissionFee,
                Symbol = poolInfo.Config.RewardToken,
                Memo = "commission"
            });
        }

        claimedAmount = amount.Sub(commissionFee);
    }

    private void CallRewardsContractClaim(PoolInfo poolInfo, long claimedAmount, Hash seed)
    {
        State.TokenContract.Transfer.VirtualSend(poolInfo.PoolId, new TransferInput
        {
            To = Context.Self,
            Amount = claimedAmount,
            Symbol = poolInfo.Config.RewardToken,
            Memo = "claim"
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Amount = claimedAmount,
            Spender = State.EcoEarnRewardsContract.Value,
            Symbol = poolInfo.Config.RewardToken
        });

        State.EcoEarnRewardsContract.Claim.Send(new Rewards.ClaimInput
        {
            PoolId = poolInfo.PoolId,
            Symbol = poolInfo.Config.RewardToken,
            Account = Context.Sender,
            Amount = claimedAmount,
            ReleasePeriods = { poolInfo.Config.ReleasePeriods },
            DappId = poolInfo.DappId,
            Seed = seed
        });
    }

    private void UpdatePoolData(Hash poolId, long amount)
    {
        var poolData = State.PoolDataMap[poolId];
        poolData.ClaimedRewards = poolData.ClaimedRewards.Add(amount);
    }

    #endregion
}