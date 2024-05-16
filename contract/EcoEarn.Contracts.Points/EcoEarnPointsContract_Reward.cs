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

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContract
{
    #region public

    public override Empty UpdateSnapshot(UpdateSnapshotInput input)
    {
        Assert(input != null, "Invalid input.");
        var poolInfo = GetPool(input.PoolId);
        Assert(poolInfo.Config.UpdateAddress == Context.Sender, "No permission.");
        Assert(CheckPoolEnabled(poolInfo.Config.EndBlockNumber), "Pool disabled.");

        var currentHeight = Context.CurrentHeight;
        Assert(State.SnapshotMap[input.PoolId]?[currentHeight] == null, "Duplicate Snapshot.");

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
        Assert(Context.CurrentHeight >= poolInfo.Config.StartBlockNumber, "Pool not start.");
        Assert(Context.CurrentBlockTime < input.ExpirationTime, "Signature expired.");

        Assert(RecoverAddressFromSignature(input) == poolInfo.Config.UpdateAddress, "Signature not valid.");
        
        State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToBase64())] = true;

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
            UnlockTime = Context.CurrentBlockTime.AddSeconds(poolInfo.Config.ReleasePeriod),
            PoolId = poolInfo.PoolId,
            Account = input.Account
        };

        State.ClaimInfoMap[claimId] = claimInfo;

        // transfer rewards to user's virtual address
        State.TokenContract.Transfer.VirtualSend(input.PoolId, new TransferInput
        {
            To = CalculateVirtualAddress(Context.Sender),
            Amount = claimedAmount,
            Symbol = poolInfo.Config.RewardToken,
            Memo = "claim"
        });

        Context.Fire(new Claimed
        {
            ClaimInfo = claimInfo,
            Seed = input.Seed
        });

        return new Empty();
    }

    public override Empty Withdraw(WithdrawInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");

        var claimInfos = ProcessClaimInfos(input.ClaimIds.Distinct().ToList(), out var rewards);

        ProcessTransfer(rewards);

        Context.Fire(new Withdrawn
        {
            ClaimInfos = new ClaimInfos
            {
                Data = { claimInfos }
            }
        });

        return new Empty();
    }

    public override Empty RecoverToken(RecoverTokenInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(IsStringValid(input.Token), "Invalid token.");
        Assert(input.Recipient == null || !input.Recipient.Value.IsNullOrEmpty(), "Invalid recipient.");

        var poolInfo = GetPool(input.PoolId);
        CheckDAppAdminPermission(poolInfo.DappId);
        
        Assert(!CheckPoolEnabled(poolInfo.Config.EndBlockNumber), "Pool not closed.");

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

    public override Empty EarlyStake(EarlyStakeInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(input.ClaimIds != null && input.ClaimIds.Count > 0, "Invalid claim ids.");
        Assert(input.Period >= 0, "Invalid period.");

        var poolInfo = State.EcoEarnTokensContract.GetPoolInfo.Call(input.PoolId).PoolInfo;
        Assert(poolInfo?.PoolId == input.PoolId, "Pool not exists.");

        var stakeId = GetStakeId(input.PoolId);

        var list = ProcessEarlyStake(input.ClaimIds.Distinct().ToList(), poolInfo.Config.StakingToken, stakeId,
            out var amount);

        // approve staked amount to EcoEarnTokensContract
        State.TokenContract.Approve.VirtualSend(HashHelper.ComputeFrom(Context.Sender), new ApproveInput
        {
            Spender = State.EcoEarnTokensContract.Value,
            Amount = amount,
            Symbol = poolInfo.Config.StakingToken
        });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            Address = Context.Sender,
            PoolId = input.PoolId,
            Amount = amount,
            Period = input.Period,
            FromAddress = CalculateVirtualAddress(Context.Sender)
        });

        Context.Fire(new EarlyStaked
        {
            ClaimInfos = new ClaimInfos
            {
                Data = { list }
            },
            PoolId = input.PoolId,
            Period = input.Period,
            Amount = amount
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
        Assert(IsAddressValid(input.Account) && input.Account == Context.Sender, "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(!input.Signature.IsNullOrEmpty(), "Invalid signature.");
        Assert(input.ExpirationTime != null, "Invalid expiration time.");
        Assert(IsHashValid(input.Seed) && !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToBase64())],
            "Invalid seed.");
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

    private List<ClaimInfo> ProcessClaimInfos(List<Hash> claimIds, out Dictionary<string, long> rewards)
    {
        var result = new List<ClaimInfo>();
        rewards = new Dictionary<string, long>();

        if (claimIds.Count == 0) return result;

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim id not exists.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(claimInfo.WithdrawTime == null, "Already withdrawn.");
            Assert(Context.CurrentBlockTime >= claimInfo.UnlockTime, "Not unlock yet.");

            if (IsHashValid(claimInfo.StakeId))
            {
                var stakeInfo = State.EcoEarnTokensContract.GetStakeInfo.Call(claimInfo.StakeId);
                Assert(stakeInfo != null && stakeInfo.WithdrawTime != null, "Not unlocked.");
            }

            claimInfo.WithdrawTime = Context.CurrentBlockTime;
            rewards.TryGetValue(claimInfo.ClaimedSymbol, out var value);
            rewards[claimInfo.ClaimedSymbol] = value.Add(claimInfo.ClaimedAmount);

            result.Add(claimInfo);
        }

        return result;
    }

    private List<ClaimInfo> ProcessEarlyStake(List<Hash> claimIds, string token, Hash stakeId, out long amount)
    {
        var list = new List<ClaimInfo>();
        amount = 0L;

        if (claimIds.Count == 0) return list;

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim info not exists.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(claimInfo.WithdrawTime == null, "Already withdrawn.");
            Assert(claimInfo.ClaimedSymbol == token, "Token not matched.");

            if (IsHashValid(claimInfo.StakeId))
            {
                var stakeInfo = State.EcoEarnTokensContract.GetStakeInfo.Call(claimInfo.StakeId);
                Assert(stakeInfo != null && stakeInfo.WithdrawTime != null, "Not unlocked.");
            }

            amount = amount.Add(claimInfo.ClaimedAmount);
            claimInfo.EarlyStakeTime = Context.CurrentBlockTime;
            claimInfo.StakeId = stakeId;
            list.Add(claimInfo);
        }

        return list;
    }

    private void ProcessTransfer(Dictionary<string, long> rewards)
    {
        if (rewards.Count == 0) return;
        foreach (var reward in rewards)
        {
            if (reward.Value == 0) continue;
            State.TokenContract.Transfer.VirtualSend(HashHelper.ComputeFrom(Context.Sender), new TransferInput
            {
                To = Context.Sender,
                Amount = reward.Value,
                Symbol = reward.Key,
                Memo = "withdraw"
            });
        }
    }

    private Hash GenerateStakeId(Hash poolId, Address sender, long count)
    {
        return HashHelper.ConcatAndCompute(
            HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(count), HashHelper.ComputeFrom(sender)), poolId);
    }

    private Hash GetStakeId(Hash poolId)
    {
        var stakeId = State.EcoEarnTokensContract.GetUserStakeId.Call(new GetUserStakeIdInput
        {
            PoolId = poolId,
            Account = Context.Sender
        });

        var stakeInfo = State.EcoEarnTokensContract.GetStakeInfo.Call(stakeId);

        if (IsHashValid(stakeId) && stakeInfo.WithdrawTime == null) return stakeId;

        var count = State.EcoEarnTokensContract.GetUserStakeCount.Call(new GetUserStakeCountInput
        {
            Account = Context.Sender,
            PoolId = poolId
        }).Value;

        stakeId = GenerateStakeId(poolId, Context.Sender, count);

        return stakeId;
    }

    #endregion
}