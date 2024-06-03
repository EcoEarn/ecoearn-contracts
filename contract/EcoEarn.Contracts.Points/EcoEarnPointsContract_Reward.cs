using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
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
        Assert(poolInfo.Config.UpdateAddress == Context.Sender, "No permission.");
        Assert(CheckPoolEnabled(poolInfo.Config.EndTime), "Pool disabled.");

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

        var maximumReward =
            (Context.CurrentBlockTime - poolInfo.Config.StartTime).Seconds.Mul(poolInfo.Config.RewardPerSecond);
        Assert(input.Amount <= maximumReward, "Amount too much.");

        Assert(RecoverAddressFromSignature(input) == poolInfo.Config.UpdateAddress, "Signature not valid.");

        State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())] = true;

        var claimId = GenerateClaimId(input);
        Assert(State.ClaimInfoMap[claimId] == null, "Claim id taken.");

        var config = State.Config.Value;

        // calculate and charge commission fee
        var commissionFee = CalculateCommissionFee(input.Amount, config.CommissionRate);
        if (commissionFee > 0)
        {
            State.TokenContract.Transfer.VirtualSend(input.PoolId, new TransferInput
            {
                To = config.Recipient,
                Amount = commissionFee,
                Symbol = poolInfo.Config.RewardToken,
                Memo = "commission"
            });
        }

        var claimedAmount = input.Amount.Sub(commissionFee);

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
        State.TokenContract.Transfer.VirtualSend(input.PoolId, new TransferInput
        {
            To = Context.Sender,
            Amount = claimedAmount,
            Symbol = poolInfo.Config.RewardToken,
            Memo = "claim"
        });

        Context.Fire(new Claimed
        {
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

    private Hash GenerateClaimId(ClaimInput input)
    {
        return HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(input),
            HashHelper.ComputeFrom(Context.CurrentHeight));
    }

    #endregion
}