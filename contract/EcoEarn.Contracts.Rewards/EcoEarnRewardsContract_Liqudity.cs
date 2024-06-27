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
        Assert(!input.Signature.IsNullOrEmpty(), "Invalid signature");

        var signatureHash = HashHelper.ComputeFrom(input.Signature.ToByteArray());
        Assert(!State.SignatureMap[signatureHash], "Signature used.");
        State.SignatureMap[signatureHash] = true;

        var stakeInput = input!.StakeInput;

        var stakeId = GetStakeId(stakeInput.PoolId);

        var dappInfo = State.DappInfoMap[stakeInput.DappId];
        Assert(dappInfo != null, "Dapp id not exists.");

        Assert(Context.CurrentBlockTime.Seconds < stakeInput.ExpirationTime, "Signature expired.");
        Assert(
            RecoverAddressFromSignature(ComputeAddLiquidityAndStakeHash(input), input.Signature) ==
            dappInfo!.Config.UpdateAddress, "Signature not valid.");

        var claimInfos = ProcessEarlyStake(stakeInput, stakeId, dappInfo.DappId, out var rewardSymbol,
            out var longestReleaseTime);

        var poolInfo = State.EcoEarnTokensContract.GetPoolInfo.Call(stakeInput.PoolId).PoolInfo;
        Assert(stakeInput.Amount >= poolInfo.Config.MinimumAddLiquidityAmount, "Amount not enough.");

        PrepareAddLiquidity(poolInfo.Config.StakingToken, rewardSymbol, poolInfo.Config.SwapContract, stakeInput.Amount,
            CalculateUserAddressHash(dappInfo.DappId, Context.Sender), out var symbolA, out var symbolB,
            out var amountA, out var amountB);

        var lpAmount = CalculateLpAmount(symbolA, symbolB, amountA, amountB, poolInfo.Config.SwapContract);

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
            Account = Context.Sender,
            LongestReleaseTime = longestReleaseTime
        };

        State.LiquidityInfoMap[liquidityId] = liquidityInfo;

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
                To = Context.Self,
                Channel = EcoEarnRewardsContractConstants.Channel
            });

        Context.SendInline(poolInfo.Config.StakeTokenContract, nameof(State.TokenContract.Approve), new ApproveInput
        {
            Amount = lpAmount,
            Spender = State.EcoEarnTokensContract.Value,
            Symbol = poolInfo.Config.StakingToken
        });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = stakeInput.PoolId,
            Amount = lpAmount,
            FromAddress = stakeInput.Account,
            Period = stakeInput.Period,
            LongestReleaseTime = longestReleaseTime,
            IsLiquidity = true
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
            out var tokenContractAddress, out _, out var tokenSymbolA, out var tokenSymbolB);

        PrepareRemoveLiquidity(lpSymbol, amount, CalculateUserAddressHash(input.DappId, Context.Sender),
            tokenContractAddress, swapContractAddress, out var symbolA, out var symbolB, out var amountA,
            out var amountB);

        Context.SendInline(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.RemoveLiquidity),
            new Awaken.Contracts.Swap.RemoveLiquidityInput
            {
                SymbolA = symbolA,
                SymbolB = symbolB,
                AmountAMin = symbolA == tokenSymbolA ? input.TokenAMin : input.TokenBMin,
                AmountBMin = symbolA == tokenSymbolA ? input.TokenBMin : input.TokenAMin,
                Deadline = input.Deadline,
                To = Context.Self,
                LiquidityRemove = amount
            });

        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = CalculateUserAddress(input.DappId, Context.Sender),
            Symbol = rewardSymbol,
            Amount = rewardSymbol == symbolA ? amountA : amountB
        });

        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = Context.Sender,
            Symbol = rewardSymbol == symbolA ? symbolB : symbolA,
            Amount = rewardSymbol == symbolA ? amountB : amountA
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
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");

        var stakeId = GetStakeId(input.PoolId);

        var liquidityInfos = ProcessLiquidity(input.LiquidityIds!.Distinct().ToList(), stakeId, null, out var amount,
            out _, out var lpSymbol, out _, out var tokenContractAddress, out var longestReleaseTime, out _, out _);

        Context.SendVirtualInline(CalculateUserAddressHash(input.DappId, Context.Sender), tokenContractAddress,
            nameof(State.TokenContract.Transfer), new TransferInput
            {
                Amount = amount,
                Symbol = lpSymbol,
                To = Context.Self
            });

        Context.SendInline(tokenContractAddress, nameof(State.TokenContract.Approve), new ApproveInput
        {
            Spender = State.EcoEarnTokensContract.Value,
            Symbol = lpSymbol,
            Amount = amount
        });

        State.EcoEarnTokensContract.StakeFor.Send(new StakeForInput
        {
            PoolId = input.PoolId,
            Amount = amount,
            FromAddress = Context.Sender,
            Period = input.Period,
            LongestReleaseTime = longestReleaseTime
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

    private void ExtractTokenPair(string lpSymbol, out string symbolA, out string symbolB)
    {
        var symbols = lpSymbol.Split(EcoEarnRewardsContractConstants.Space)[1]
            .Split(EcoEarnRewardsContractConstants.Separator);

        symbolA = "";
        symbolB = "";

        switch (symbols.Length)
        {
            // ABC-DEF
            case 2:
                symbolA = symbols[0];
                symbolB = symbols[1];
                break;
            // ABC-1 - DEF / ABC - DEF-1
            case 3:
                symbolA = symbols[1].All(IsValidItemIdChar)
                    ? $"{symbols[0]}-{symbols[1]}"
                    : $"{symbols[1]}-{symbols[2]}";
                symbolB = symbols[1].All(IsValidItemIdChar) ? symbols[2] : symbols[0];
                break;
            // ABC-1 - DEF-1
            case 4:
                symbolA = $"{symbols[0]}-{symbols[1]}";
                symbolB = $"{symbols[2]}-{symbols[3]}";
                break;
            default:
                Assert(true, "Invalid lp symbol.");
                break;
        }
    }

    private bool IsValidItemIdChar(char character)
    {
        return character >= '0' && character <= '9';
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
        out Address tokenContractAddress, out Timestamp longestReleaseTime, out string symbolA, out string symbolB)
    {
        var liquidityInfos = new List<LiquidityInfo>();

        amount = 0L;
        swapContractAddress = null;
        lpSymbol = null;
        rewardSymbol = null;
        tokenContractAddress = null;
        longestReleaseTime = null;
        symbolA = null;
        symbolB = null;

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
            symbolA ??= liquidityInfo.TokenASymbol;
            symbolB ??= liquidityInfo.TokenBSymbol;

            longestReleaseTime ??= liquidityInfo.LongestReleaseTime;
            longestReleaseTime = longestReleaseTime == null || longestReleaseTime <= liquidityInfo.LongestReleaseTime
                ? liquidityInfo.LongestReleaseTime
                : longestReleaseTime;
        }

        return liquidityInfos;
    }

    private void PrepareAddLiquidity(string stakingToken, string rewardSymbol, Address swapContract, long amount,
        Hash userAddressHash, out string symbolA, out string symbolB, out long amountA, out long amountB)
    {
        ExtractTokenPair(stakingToken, out symbolA, out symbolB);

        var quoteSymbol = rewardSymbol == symbolA ? symbolB : symbolA;

        var quote = Context.Call<Int64Value>(swapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.Quote), new QuoteInput
            {
                SymbolA = rewardSymbol,
                SymbolB = quoteSymbol,
                AmountA = amount
            });

        var quoteAmount = quote.Value;

        Assert(quoteAmount > 0, "Quote amount is zero.");

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = Context.Self,
            Amount = quoteAmount,
            Memo = "add",
            Symbol = quoteSymbol
        });

        State.TokenContract.Transfer.VirtualSend(userAddressHash, new TransferInput
        {
            To = Context.Self,
            Symbol = rewardSymbol,
            Amount = amount
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = swapContract,
            Symbol = rewardSymbol,
            Amount = amount
        });

        State.TokenContract.Approve.Send(new ApproveInput
        {
            Spender = swapContract,
            Symbol = quoteSymbol,
            Amount = quoteAmount
        });

        symbolA = rewardSymbol;
        symbolB = quoteSymbol;
        amountA = amount;
        amountB = quoteAmount;
    }

    private long CalculateLpAmount(string symbolA, string symbolB, long amountA, long amountB, Address swapContract)
    {
        var pairSymbol = symbolA + EcoEarnRewardsContractConstants.Separator + symbolB;

        var reservePairResult = Context.Call<GetReservesOutput>(swapContract,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { pairSymbol } }).Results.First();

        var totalSupplyResult = Context.Call<GetTotalSupplyOutput>(swapContract,
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
            var reserveA = symbolA == reservePairResult.SymbolA ? amountA : amountB;
            var reserveB = symbolA == reservePairResult.SymbolA ? amountB : amountA;

            var liquidity0Str = new BigIntValue(reserveA).Mul(totalSupplyResult.TotalSupply)
                .Div(reservePairResult.ReserveA).Value;
            var liquidity1Str = new BigIntValue(reserveB).Mul(totalSupplyResult.TotalSupply)
                .Div(reservePairResult.ReserveB).Value;
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

        return liquidity;
    }

    private void PrepareRemoveLiquidity(string lpSymbol, long amount, Hash userAddressHash,
        Address tokenContractAddress, Address swapContractAddress, out string symbolA, out string symbolB,
        out long amountA, out long amountB)
    {
        ExtractTokenPair(lpSymbol, out symbolA, out symbolB);

        var pairSymbol = symbolA + EcoEarnRewardsContractConstants.Separator + symbolB;

        var getReservesOutput = Context.Call<GetReservesOutput>(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetReserves),
            new GetReservesInput { SymbolPair = { pairSymbol } });

        Assert(
            getReservesOutput?.Results != null && getReservesOutput.Results.Count > 0 &&
            getReservesOutput.Results.First().SymbolPair != null, "GetReserves failed.");

        var reservePairResult = getReservesOutput!.Results!.First();

        var getTotalSupplyOutput = Context.Call<GetTotalSupplyOutput>(swapContractAddress,
            nameof(AwakenSwapContractContainer.AwakenSwapContractReferenceState.GetTotalSupply),
            new StringList { Value = { pairSymbol } });

        Assert(
            getTotalSupplyOutput?.Results != null && getTotalSupplyOutput.Results.Count > 0 &&
            getTotalSupplyOutput.Results.First().SymbolPair != null, "GetTotalSupply failed.");

        var totalSupplyResult = getTotalSupplyOutput!.Results!.First();

        var valueA = new BigIntValue(amount).Mul(reservePairResult.ReserveA).Div(totalSupplyResult.TotalSupply).Value;
        var valueB = new BigIntValue(amount).Mul(reservePairResult.ReserveB).Div(totalSupplyResult.TotalSupply).Value;

        if (!long.TryParse(valueA, out amountA))
        {
            throw new AssertionException($"Failed to parse {valueA}");
        }

        if (!long.TryParse(valueB, out amountB))
        {
            throw new AssertionException($"Failed to parse {valueB}");
        }

        Context.SendVirtualInline(userAddressHash, tokenContractAddress, nameof(State.TokenContract.Transfer),
            new TransferInput
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
    }

    #endregion
}