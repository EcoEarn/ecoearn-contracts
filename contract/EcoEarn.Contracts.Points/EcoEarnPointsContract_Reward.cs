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
using Google.Protobuf.Collections;
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

        var height = Context.CurrentHeight;
        Assert(State.SnapshotMap[input.PoolId]?[height] == null, "Duplicate Snapshot.");

        Assert(IsHashValid(input.MerkleTreeRoot), "Invalid merkle tree root.");

        State.SnapshotMap[input.PoolId][height] = new Snapshot
        {
            PoolId = input.PoolId,
            MerkleTreeRoot = input.MerkleTreeRoot,
            BlockNumber = height
        };

        Context.Fire(new SnapshotUpdated
        {
            PoolId = input.PoolId,
            MerkleTreeRoot = input.MerkleTreeRoot,
            UpdateBlockNumber = height
        });

        return new Empty();
    }

    public override Empty Claim(ClaimInput input)
    {
        ValidateClaimInput(input);

        var poolInfo = GetPool(input.PoolId);

        Assert(RecoverAddressFromSignature(input) == poolInfo.Config.UpdateAddress, "Signature not valid.");

        State.SignatureMap[input.Seed] = true;

        var claimId = GenerateClaimId(input);
        Assert(State.ClaimInfoMap[claimId] == null, "Claim id taken.");

        var config = State.Config.Value;

        var commissionFee = CalculateCommissionFee(input.Amount, config.CommissionRate);

        if (commissionFee != 0)
        {
            Context.SendVirtualInline(input.PoolId, State.TokenContract.Value, "Transfer", new TransferInput
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

        Context.SendVirtualInline(input.PoolId, State.TokenContract.Value, "Transfer", new TransferInput
        {
            To = CalculateVirtualAddress(Context.Sender),
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

        var output = State.TokenContract.GetBalance.Call(new GetBalanceInput
        {
            Owner = CalculateVirtualAddress(input.PoolId),
            Symbol = input.Token
        });

        Assert(output.Balance > 0, "Invalid token.");

        Context.SendVirtualInline(input.PoolId, State.TokenContract.Value,"Transfer",new TransferInput
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
        Assert(poolInfo != null, "Pool not exists.");
        
        var list = ProcessEarlyStake(input.ClaimIds.Distinct().ToList(), poolInfo.Config.StakingToken, out var amount);
        
        Context.SendVirtualInline(HashHelper.ComputeFrom(Context.Sender), State.TokenContract.Value, "Transfer", new TransferInput
        {
            To = poolInfo.PoolAddress,
            Amount = amount,
            Symbol = poolInfo.Config.StakingToken,
            Memo = "early"
        });
        
        Context.SendInline(State.EcoEarnTokensContract.Value, "StakeFor", new StakeForInput
        {
            Address = Context.Sender,
            PoolId = input.PoolId,
            Amount = amount,
            Period = input.Period
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
        var hash = ComputeConfirmInputHash(input);
        var publicKey = Context.RecoverPublicKey(input.Signature.ToByteArray(), hash.ToByteArray());
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
            Seed = input.Seed
        }.ToByteArray());
    }

    private void ValidateClaimInput(ClaimInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input.Account) && input.Account == Context.Sender, "Invalid account.");
        Assert(input.Amount > 0, "Invalid amount.");
        Assert(IsHashValid(input.Seed) && !State.SignatureMap[input.Seed], "Invalid seed.");
        Assert(!input.Signature.IsNullOrEmpty(), "Invalid signature.");
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
            Assert(claimInfo.EarlyStakeTime == null, "Already early staked.");
            Assert(Context.CurrentBlockTime >= claimInfo.UnlockTime, "Not unlock yet.");

            claimInfo.WithdrawTime = Context.CurrentBlockTime;
            rewards.TryGetValue(claimInfo.ClaimedSymbol, out var value);
            rewards[claimInfo.ClaimedSymbol] = value.Add(claimInfo.ClaimedAmount);

            result.Add(claimInfo);
        }

        return result;
    }
    
    private List<ClaimInfo> ProcessEarlyStake(List<Hash> claimIds, string token, out long amount)
    {
        var list = new List<ClaimInfo>();
        amount = 0L;

        if (claimIds.Count == 0) return list;

        foreach (var id in claimIds)
        {
            Assert(IsHashValid(id), "Invalid claim id.");

            var claimInfo = State.ClaimInfoMap[id];
            Assert(claimInfo != null, "Claim info not exists.");
            Assert(claimInfo.EarlyStakeTime == null, "Already early staked.");
            Assert(claimInfo.WithdrawTime == null, "Already withdrawn.");
            Assert(claimInfo.Account == Context.Sender, "No permission.");
            Assert(claimInfo.ClaimedSymbol == token, "Token not matched.");

            amount = amount.Add(claimInfo.ClaimedAmount);
            claimInfo.EarlyStakeTime = Context.CurrentBlockTime;
            
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
            Context.SendVirtualInline(HashHelper.ComputeFrom(Context.Sender), State.TokenContract.Value, "Transfer", new TransferInput
            {
                To = Context.Sender,
                Amount = reward.Value,
                Symbol = reward.Key,
                Memo = "withdraw"
            });
        }
    }

    #endregion
}