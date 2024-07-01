using System;
using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Swap;
using Google.Protobuf.WellKnownTypes;
using StringList = Awaken.Contracts.Swap.StringList;

namespace EcoEarn.Contracts.TestContract;

public class TestContract : TestContractContainer.TestContractBase
{
    public override Empty AddLiquidityAndStake(AddLiquidityAndStakeInput input)
    {
        State.TokenContract.Value = Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        
        var quote = Context.Call<Int64Value>(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.Quote), new QuoteInput
            {
                SymbolA = input.TokenASymbol,
                SymbolB = input.TokenBSymbol,
                AmountA = input.TokenAAmount
            });

        var tokenBAmount = quote.Value;

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = Context.Self,
            Amount = input.TokenAAmount,
            Symbol = input.TokenASymbol
        });

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = Context.Self,
            Amount = tokenBAmount,
            Symbol = input.TokenBSymbol
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = input.SwapContract,
            Amount = input.TokenAAmount,
            Symbol = input.TokenASymbol
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = input.SwapContract,
            Amount = tokenBAmount,
            Symbol = input.TokenBSymbol
        });
        
        var reservePairResult = Context.Call<GetReservesOutput>(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { input.TokenASymbol + "-" + input.TokenBSymbol } }).Results.First();

        var totalSupplyResult = Context.Call<GetTotalSupplyOutput>(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetTotalSupply),
            new StringList { Value = { input.TokenASymbol + "-" + input.TokenBSymbol } }).Results.First();
        
        long liquidity;
        if (totalSupplyResult.TotalSupply == 0)
        {
            var liquidityStr = Sqrt(new BigIntValue(input.TokenAAmount).Mul(quote.Value).Sub(1)).Value;
            if (!long.TryParse(liquidityStr, out liquidity))
            {
                throw new AssertionException($"Failed to parse {liquidityStr}");
            }
        }
        else
        {
            var liquidity0Str = new BigIntValue(input.TokenAAmount).Mul(totalSupplyResult.TotalSupply).Div(reservePairResult.ReserveA).Value;
            var liquidity1Str = new BigIntValue(quote.Value).Mul(totalSupplyResult.TotalSupply).Div(reservePairResult.ReserveB).Value;
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

        var liquidityId = GenerateLiquidityId(HashHelper.ComputeFrom(input));
        Assert(State.LiquidityInfoMap[liquidityId] == null, "Liquidity id exists.");

        var liquidityInfo = new LiquidityInfo
        {
            LiquidityId = liquidityId,
            LpAmount = liquidity,
            TokenASymbol = input.TokenASymbol,
            TokenBSymbol = input.TokenBSymbol,
            TokenAAmount = input.TokenAAmount,
            TokenBAmount = tokenBAmount,
            AddedTime = Context.CurrentBlockTime,
            SwapAddress = input.SwapContract
        };

        State.LiquidityInfoMap[liquidityId] = liquidityInfo;

        Context.SendInline(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.AddLiquidity), new AddLiquidityInput
            {
                SymbolA = input.TokenASymbol,
                SymbolB = input.TokenBSymbol,
                AmountADesired = input.TokenAAmount,
                AmountBDesired = tokenBAmount,
                AmountAMin = input.TokenAAmount,
                AmountBMin = tokenBAmount,
                Deadline = Context.CurrentBlockTime,
                To = Context.Self,
                Channel = "13579"
            });

        Context.Fire(new LiquidityAdded
        {
            LiquidityInfo = liquidityInfo
        });

        return new Empty();
    }

    public override Empty RemoveLiquidity(RemoveLiquidityInput input)
    {
        State.TokenContract.Value = Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        
        var reservePairResult = Context.Call<GetReservesOutput>(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { input.Pair } }).Results.First();

        var totalSupplyResult = Context.Call<GetTotalSupplyOutput>(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetTotalSupply),
            new StringList { Value = { input.Pair } }).Results.First();

        var amountA = input.LpAmount.Mul(reservePairResult.ReserveA).Div(totalSupplyResult.TotalSupply);
        var amountB = input.LpAmount.Mul(reservePairResult.ReserveB).Div(totalSupplyResult.TotalSupply);

        Context.SendInline(input.AwakenTokenContract, "Approve", new ApproveInput
        {
            Spender = input.SwapContract,
            Symbol = input.LpSymbol,
            Amount = input.LpAmount
        });
        
        Context.SendInline(input.SwapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.RemoveLiquidity), new Awaken.Contracts.Swap.RemoveLiquidityInput
            {
                SymbolA = input.TokenASymbol,
                SymbolB = input.TokenBSymbol,
                AmountAMin = amountA,
                AmountBMin = amountB,
                Deadline = Context.CurrentBlockTime,
                To = Context.Self,
                LiquidityRemove = input.LpAmount
            });

        Context.Fire(new LiquidityRemoved
        {
            TokenAAmount = amountA,
            TokenBAmount = amountB
        });

        return new Empty();
    }

    public override LiquidityInfo GetLiquidityInfo(Hash input)
    {
        return State.LiquidityInfoMap[input];
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
}