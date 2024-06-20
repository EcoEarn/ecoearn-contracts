using System;
using System.Collections.Generic;
using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Swap;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf.WellKnownTypes;
using StringList = Awaken.Contracts.Swap.StringList;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    #region public

    public override Empty AddLiquidityAndStake(AddLiquidityAndStakeInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidateEarlyStakeInput(input!.StakeInput);
        Assert(input.TokenAMin > 0, "Invalid token A min.");
        Assert(input.TokenBMin > 0, "Invalid token B min.");
        Assert(input.Deadline != null, "Invalid deadline.");
        Assert(
            !input.Signature.IsNullOrEmpty() &&
            !State.SignatureMap[HashHelper.ComputeFrom(input.Signature.ToByteArray())], "Invalid signature.");

        var stakeInput = input!.StakeInput;

        var stakeId = GetStakeId(stakeInput.PoolId);

        var claimInfos = ProcessEarlyStake(stakeInput, input.Signature, stakeId, out var rewardSymbol,
            out var longestReleaseTime);

        var poolInfo = State.EcoEarnTokensContract.GetPoolInfo.Call(stakeInput.PoolId).PoolInfo;

        GetSymbols(poolInfo.Config.StakingToken, out var symbolA, out var symbolB);

        var quoteSymbol = rewardSymbol == symbolA ? symbolB : symbolA;

        var quote = Context.Call<Int64Value>(poolInfo.Config.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.Quote), new QuoteInput
            {
                SymbolA = rewardSymbol,
                SymbolB = quoteSymbol,
                AmountA = stakeInput.Amount
            });

        var quoteAmount = quote.Value;

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = Context.Self,
            Amount = quoteAmount,
            Memo = "add",
            Symbol = quoteSymbol
        });

        Context.SendVirtualInline(
            HashHelper.ConcatAndCompute(stakeInput.DappId, HashHelper.ComputeFrom(Context.Sender)),
            State.TokenContract.Value, nameof(State.TokenContract.Transfer), new TransferInput
            {
                To = Context.Self,
                Symbol = rewardSymbol,
                Amount = stakeInput.Amount
            });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = poolInfo.Config.SwapContract,
            Symbol = rewardSymbol,
            Amount = stakeInput.Amount
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = poolInfo.Config.SwapContract,
            Symbol = quoteSymbol,
            Amount = quoteAmount
        });

        var amountA = symbolA == rewardSymbol ? stakeInput.Amount : quoteAmount;
        var amountB = symbolA == rewardSymbol ? quoteAmount : stakeInput.Amount;

        var pairSymbol = symbolA + EcoEarnRewardsContractConstants.Separator + symbolB;
        
        var reservePairResult = Context.Call<GetReservesOutput>(poolInfo.Config.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { pairSymbol } }).Results.First();

        var totalSupplyResult = Context.Call<GetTotalSupplyOutput>(poolInfo.Config.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetTotalSupply),
            new StringList { Value = { pairSymbol } }).Results.First();
        
        long liquidity;
        if (totalSupplyResult.TotalSupply == 0)
        {
            var liquidityStr = Sqrt(new BigIntValue(amountA).Mul(amountB).Sub(1)).Value;
            if (!long.TryParse(liquidityStr, out liquidity))
            {
                throw new AssertionException($"Failed to parse {liquidityStr}");
            }
        }
        else
        {
            var liquidity0Str = new BigIntValue(amountA).Mul(totalSupplyResult.TotalSupply).Div(reservePairResult.ReserveA).Value;
            var liquidity1Str = new BigIntValue(amountB).Mul(totalSupplyResult.TotalSupply).Div(reservePairResult.ReserveB).Value;
            if (!long.TryParse(liquidity0Str, out var liquidity0))
            {
                throw new AssertionException($"Failed to parse {liquidity0Str}");
            }
            if (!long.TryParse(liquidity1Str, out var liquidity1))
            {
                throw new AssertionException($"Failed to parse {liquidity1Str}");
            }

            liquidity = Math.Min(liquidity0, liquidity1);
        }
            
        Assert(liquidity > 0, "Insufficient liquidity supply.");

        var lp = Sqrt(new BigIntValue(amountA.Mul(amountB))).Value;
        long.TryParse(lp, out var lpAmount);

        var liquidityId = GenerateLiquidityId(stakeInput.Seed);
        Assert(State.LiquidityInfoMap[liquidityId] == null, "Liquidity id exists.");

        var liquidityInfo = new LiquidityInfo
        {
            LiquidityId = liquidityId,
            StakeId = stakeId,
            Seed = stakeInput.Seed,
            LpAmount = lpAmount,
            TokenASymbol = symbolA,
            TokenBSymbol = symbolB,
            TokenAAmount = amountA,
            TokenBAmount = amountB,
            AddedTime = Context.CurrentBlockTime,
            RewardSymbol = rewardSymbol,
            SwapAddress = poolInfo.Config.SwapContract,
            TokenAddress = poolInfo.Config.StakeTokenContract,
            LpSymbol = poolInfo.Config.StakingToken,
            DappId = stakeInput.DappId,
            Account = Context.Sender
        };

        State.LiquidityInfoMap[liquidityId] = liquidityInfo;

        var poolAddressInfo = State.EcoEarnTokensContract.GetPoolAddressInfo.Call(stakeInput.PoolId);

        Context.SendInline(poolInfo.Config.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.AddLiquidity), new AddLiquidityInput
            {
                SymbolA = symbolA,
                SymbolB = symbolB,
                AmountADesired = amountA,
                AmountBDesired = amountB,
                AmountAMin = input.TokenAMin,
                AmountBMin = input.TokenBMin,
                Deadline = input.Deadline,
                To = poolAddressInfo.StakeAddress,
                Channel = EcoEarnRewardsContractConstants.Channel
            });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = stakeInput.PoolId,
            Amount = stakeInput.Amount,
            FromAddress = stakeInput.Account,
            Period = stakeInput.Period,
            LongestReleaseTime = longestReleaseTime
        });

        Context.Fire(new LiquidityAdded
        {
            ClaimInfos = claimInfos,
            Account = stakeInput.Account,
            Amount = stakeInput.Amount,
            PoolId = stakeInput.PoolId,
            Period = stakeInput.Period,
            StakeId = stakeId,
            LiquidityInfo = liquidityInfo
        });

        return new Empty();
    }

    public override Empty RemoveLiquidity(RemoveLiquidityInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.LiquidityIds != null && input.LiquidityIds.Count > 0, "Invalid liquidity ids.");
        Assert(input.TokenAMin > 0, "Invalid token A min.");
        Assert(input.TokenBMin > 0, "Invalid token B min.");
        Assert(input.Deadline != null, "Invalid deadline.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");

        var liquidityInfos = ProcessLiquidity(input.LiquidityIds!.Distinct().ToList(), null, Context.CurrentBlockTime,
            out var amount, out var swapContractAddress, out var lpSymbol, out var rewardSymbol,
            out var tokenContractAddress);

        var reservePairResult = Context.Call<GetReservesOutput>(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { lpSymbol } }).Results.First();

        var totalSupplyResult = Context.Call<GetTotalSupplyOutput>(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetTotalSupply),
            new StringList { Value = { lpSymbol } }).Results.First();

        var amountA = amount.Mul(reservePairResult.ReserveA).Div(totalSupplyResult.TotalSupply);
        var amountB = amount.Mul(reservePairResult.ReserveB).Div(totalSupplyResult.TotalSupply);

        Context.SendVirtualInline(
            HashHelper.ConcatAndCompute(input.DappId, HashHelper.ComputeFrom(Context.Sender)),
            State.TokenContract.Value, nameof(State.TokenContract.Transfer), new TransferInput
            {
                To = Context.Self,
                Symbol = lpSymbol,
                Amount = amount
            });

        Context.SendInline(tokenContractAddress, nameof(State.TokenContract.Approve), new ApproveInput
        {
            Spender = swapContractAddress,
            Symbol = lpSymbol,
            Amount = amount
        });

        Context.SendInline(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.RemoveLiquidity),
            new Awaken.Contracts.Swap.RemoveLiquidityInput
            {
                SymbolA = reservePairResult.SymbolA,
                SymbolB = reservePairResult.SymbolB,
                AmountAMin = input.TokenAMin,
                AmountBMin = input.TokenBMin,
                Deadline = input.Deadline,
                To = Context.Self,
                LiquidityRemove = amount
            });

        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = CalculateUserAddress(input.DappId, Context.Sender),
            Symbol = rewardSymbol,
            Amount = rewardSymbol == reservePairResult.SymbolA ? amountA : amountB
        });

        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = Context.Sender,
            Symbol = rewardSymbol == reservePairResult.SymbolA ? reservePairResult.SymbolB : rewardSymbol,
            Amount = rewardSymbol == reservePairResult.SymbolA ? amountB : amountA
        });

        Context.Fire(new LiquidityRemoved
        {
            LiquidityInfos = new LiquidityInfos
            {
                Data = { liquidityInfos }
            },
            LpAmount = amount,
            TokenAAmount = amountA,
            TokenBAmount = amountB
        });

        return new Empty();
    }

    public override Empty StakeLiquidity(StakeLiquidityInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.LiquidityIds != null && input.LiquidityIds.Count > 0, "Invalid liquidity ids.");
        Assert(IsHashValid(input.PoolId), "Invalid pool id.");
        Assert(input.Period > 0, "Invalid period.");

        var stakeId = GetStakeId(input.PoolId);

        var liquidityInfos = ProcessLiquidity(input.LiquidityIds!.Distinct().ToList(), stakeId, null, out var amount,
            out _, out var lpSymbol, out _, out _);

        var poolAddressInfo = State.EcoEarnTokensContract.GetPoolAddressInfo.Call(input.PoolId);

        State.TokenContract.Transfer.Send(new TransferInput
        {
            Amount = amount,
            Memo = "early",
            Symbol = lpSymbol,
            To = poolAddressInfo.StakeAddress
        });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = input.PoolId,
            Amount = amount,
            FromAddress = Context.Sender,
            Period = input.Period
        });

        Context.Fire(new LiquidityStaked
        {
            LiquidityInfos = new LiquidityInfos
            {
                Data = { liquidityInfos }
            },
            Amount = amount,
            PoolId = input.PoolId,
            Period = input.Period,
            StakeId = stakeId
        });

        return new Empty();
    }

    #endregion

    #region private

    private void GetSymbols(string lpSymbol, out string symbolA, out string symbolB)
    {
        var symbols = lpSymbol.Split(EcoEarnRewardsContractConstants.Space)[1]
            .Split(EcoEarnRewardsContractConstants.Separator);
        symbolA = symbols[0];
        symbolB = symbols[1];
    }

    private Hash GenerateLiquidityId(Hash seed)
    {
        return HashHelper.ConcatAndCompute(seed, HashHelper.ComputeFrom(Context.CurrentHeight));
    }

    private static BigIntValue Sqrt(BigIntValue n)
    {
        if (n.Value == "0")
            return n;
        var left = new BigIntValue(1);
        var right = n;
        var mid = left.Add(right).Div(2);
        while (!left.Equals(right) && !mid.Equals(left))
        {
            if (mid.Equals(n.Div(mid)))
                return mid;
            if (mid < n.Div(mid))
            {
                left = mid;
                mid = left.Add(right).Div(2);
            }
            else
            {
                right = mid;
                mid = left.Add(right).Div(2);
            }
        }

        return left;
    }

    private List<LiquidityInfo> ProcessLiquidity(List<Hash> liquidityIds, Hash stakeId, Timestamp removeTime,
        out long amount, out Address swapContractAddress, out string lpSymbol, out string rewardSymbol,
        out Address tokenContractAddress)
    {
        var liquidityInfos = new List<LiquidityInfo>();

        amount = 0L;
        swapContractAddress = null;
        lpSymbol = null;
        rewardSymbol = null;
        tokenContractAddress = null;

        foreach (var liquidityId in liquidityIds)
        {
            Assert(IsHashValid(liquidityId), "Invalid liquidity id.");

            var liquidityInfo = State.LiquidityInfoMap[liquidityId];
            Assert(liquidityInfo != null, "Liquidity info not exists.");
            Assert(liquidityInfo!.Account == Context.Sender, "No permission.");

            Assert(liquidityInfo.RemovedTime == null, "Already removed.");

            if (liquidityInfo.StakeId != null)
            {
                var getStakeInfoOutput = State.EcoEarnTokensContract.GetStakeInfo.Call(liquidityInfo.StakeId);
                Assert(getStakeInfoOutput.StakeInfo?.UnlockTime != null, "Liquidity occupied.");
            }

            liquidityInfo.RemovedTime = removeTime;
            liquidityInfo.StakeId = stakeId;

            amount = amount.Add(liquidityInfo.LpAmount);

            liquidityInfos.Add(liquidityInfo);

            swapContractAddress ??= liquidityInfo.SwapAddress;
            lpSymbol ??= liquidityInfo.LpSymbol;
            rewardSymbol ??= liquidityInfo.RewardSymbol;
            tokenContractAddress ??= liquidityInfo.TokenAddress;
        }

        return liquidityInfos;
    }

    #endregion
}