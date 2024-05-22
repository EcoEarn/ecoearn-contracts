using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    #region public

    public override Empty Register(RegisterInput input)
    {
        CheckInitialized();

        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
        Assert(input.Admin == null || !input.Admin.Value.IsNullOrEmpty(), "Invalid admin.");
        Assert(State.DappInfoMap[input.DappId] == null, "Dapp registered.");

        var dappInfo = State.EcoEarnPointsContract.GetDappInfo.Call(input.DappId);
        if (State.Config.Value.IsRegisterRestricted) Assert(dappInfo.DappId != null, "Dapp id not exists.");
        if (dappInfo.DappId != null) Assert(dappInfo.Admin == Context.Sender, "No permission to register.");

        var info = new DappInfo
        {
            DappId = input.DappId,
            Admin = input.Admin ?? Context.Sender
        };

        State.DappInfoMap[input.DappId] = info;

        Context.Fire(new Registered
        {
            DappId = info.DappId,
            Admin = info.Admin
        });

        return new Empty();
    }

    public override Empty SetDappAdmin(SetDappAdminInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");

        var dappInfo = State.DappInfoMap[input.DappId];
        Assert(dappInfo != null, "Dapp not exists.");
        Assert(dappInfo.Admin == Context.Sender, "No permission.");

        Assert(IsAddressValid(input.Admin), "Invalid admin.");

        if (input.Admin == dappInfo.Admin) return new Empty();

        dappInfo.Admin = input.Admin;

        Context.Fire(new DappAdminSet
        {
            DappId = input.DappId,
            Admin = input.Admin
        });

        return new Empty();
    }

    public override Empty CreateTokensPool(CreateTokensPoolInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
        CheckDAppAdminPermission(input.DappId);
        ValidateTokensPoolConfig(input);
        Assert(IsStringValid(input.StakingToken), "Invalid staking token.");
        CheckTokenExists(input.StakingToken, input.StakeTokenContract ?? State.TokenContract.Value,
            out var decimals);

        var poolId = GeneratePoolId(input);
        Assert(State.PoolInfoMap[poolId] == null, "Pool exists.");

        var config = new TokensPoolConfig
        {
            StakingToken = input.StakingToken,
            FixedBoostFactor = input.FixedBoostFactor,
            MinimumAmount = input.MinimumAmount,
            ReleasePeriod = input.ReleasePeriod,
            MaximumStakeDuration = input.MaximumStakeDuration,
            RewardTokenContract = input.RewardTokenContract ?? State.TokenContract.Value,
            StakeTokenContract = input.StakeTokenContract ?? State.TokenContract.Value,
            MinimumClaimAmount = input.MinimumClaimAmount,
            MinimumStakeDuration = input.MinimumStakeDuration,
            RewardToken = input.RewardToken,
            StartTime = new Timestamp
            {
                Seconds = input.StartTime
            },
            EndTime = new Timestamp
            {
                Seconds = input.EndTime
            },
            RewardPerSecond = input.RewardPerSecond,
            UnlockWindowDuration = input.UnlockWindowDuration
        };

        var poolInfo = new PoolInfo
        {
            DappId = input.DappId,
            PoolId = poolId,
            Config = config,
            PrecisionFactor = CalculatePrecisionFactor(decimals)
        };

        State.PoolInfoMap[poolId] = poolInfo;

        State.PoolDataMap[poolId] = new PoolData
        {
            PoolId = poolId,
            LastRewardTime = config.StartTime
        };

        var totalReward = CalculateTotalRewardAmount(input.StartTime, input.EndTime, input.RewardPerSecond);

        Context.Fire(new TokensPoolCreated
        {
            DappId = input.DappId,
            PoolId = poolId,
            Config = config,
            Amount = totalReward,
            AddressInfo = new PoolAddressInfo
            {
                StakeAddress = CalculateVirtualAddress(GetStakeVirtualAddress(poolId)),
                RewardAddress = CalculateVirtualAddress(GetRewardVirtualAddress(poolId))
            }
        });

        return new Empty();
    }

    public override Empty SetTokensPoolEndTime(SetTokensPoolEndTimeInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(input.EndTime > poolInfo.Config.EndTime.Seconds, "Invalid end time.");

        var addedAmount = CalculateTotalRewardAmount(poolInfo.Config.EndTime.Seconds, input.EndTime,
            poolInfo.Config.RewardPerSecond);

        poolInfo.Config.EndTime = new Timestamp
        {
            Seconds = input.EndTime
        };

        Context.Fire(new TokensPoolEndTimeSet
        {
            PoolId = input.PoolId,
            EndTime = poolInfo.Config.EndTime,
            Amount = addedAmount
        });

        return new Empty();
    }

    public override Empty SetTokensPoolRewardReleasePeriod(SetTokensPoolRewardReleasePeriodInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.ReleasePeriod >= 0, "Invalid release period.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.ReleasePeriod == input.ReleasePeriod) return new Empty();

        poolInfo.Config.ReleasePeriod = input.ReleasePeriod;

        Context.Fire(new TokensPoolRewardReleasePeriodSet
        {
            PoolId = input.PoolId,
            ReleasePeriod = input.ReleasePeriod
        });

        return new Empty();
    }

    public override Empty SetTokensPoolStakeConfig(SetTokensPoolStakeConfigInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(input.MinimumAmount >= 0, "Invalid minimum amount.");
        Assert(input.MaximumStakeDuration > 0, "Invalid maximum stake duration.");
        Assert(input.MinimumClaimAmount >= 0, "Invalid minimum claim amount.");
        Assert(input.MinimumStakeDuration > 0, "Invalid minimum stake duration.");

        if (poolInfo.Config.MinimumAmount == input.MinimumAmount &&
            poolInfo.Config.MaximumStakeDuration == input.MaximumStakeDuration &&
            poolInfo.Config.MinimumClaimAmount == input.MinimumClaimAmount &&
            poolInfo.Config.MinimumStakeDuration == input.MinimumStakeDuration)
        {
            return new Empty();
        }

        poolInfo.Config.MinimumAmount = input.MinimumAmount;
        poolInfo.Config.MaximumStakeDuration = input.MaximumStakeDuration;
        poolInfo.Config.MinimumClaimAmount = input.MinimumClaimAmount;
        poolInfo.Config.MinimumStakeDuration = input.MinimumStakeDuration;

        Context.Fire(new TokensPoolStakeConfigSet
        {
            PoolId = input.PoolId,
            MinimumClaimAmount = input.MinimumClaimAmount,
            MaximumStakeDuration = input.MaximumStakeDuration,
            MinimumAmount = input.MinimumAmount,
            MinimumStakeDuration = input.MinimumStakeDuration
        });

        return new Empty();
    }

    public override Empty SetTokensPoolFixedBoostFactor(SetTokensPoolFixedBoostFactorInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.FixedBoostFactor > 0, "Invalid fixed boost factor.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.FixedBoostFactor == input.FixedBoostFactor) return new Empty();

        poolInfo.Config.FixedBoostFactor = input.FixedBoostFactor;

        Context.Fire(new TokensPoolFixedBoostFactorSet
        {
            PoolId = input.PoolId,
            FixedBoostFactor = input.FixedBoostFactor
        });

        return new Empty();
    }

    public override Empty SetTokensPoolRewardPerSecond(SetTokensPoolRewardPerSecondInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.RewardPerSecond > 0, "Invalid reward per second.");

        var poolInfo = GetPool(input.PoolId);
        var poolData = State.PoolDataMap[poolInfo.PoolId];
        UpdatePool(poolInfo, poolData);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.RewardPerSecond == input.RewardPerSecond) return new Empty();

        poolInfo.Config.RewardPerSecond = input.RewardPerSecond;

        Context.Fire(new TokensPoolRewardPerSecondSet
        {
            PoolId = input.PoolId,
            RewardPerSecond = input.RewardPerSecond,
            PoolData = poolData
        });

        return new Empty();
    }

    public override Empty SetTokensPoolUnlockWindowDuration(SetTokensPoolUnlockWindowDurationInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.UnlockWindowDuration > 0, "Invalid unlock window duration.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.UnlockWindowDuration == input.UnlockWindowDuration) return new Empty();

        poolInfo.Config.UnlockWindowDuration = input.UnlockWindowDuration;

        Context.Fire(new TokensPoolUnlockWindowDurationSet
        {
            PoolId = input.PoolId,
            UnlockWindowDuration = input.UnlockWindowDuration
        });

        return new Empty();
    }

    #endregion

    #region private

    private void ValidateTokensPoolConfig(CreateTokensPoolInput input)
    {
        Assert(input.RewardTokenContract == null || !input.RewardTokenContract.Value.IsNullOrEmpty(),
            "Invalid reward token contract.");
        Assert(input.StakeTokenContract == null || !input.StakeTokenContract.Value.IsNullOrEmpty(),
            "Invalid stake token contract.");
        Assert(IsStringValid(input.RewardToken), "Invalid reward token.");
        CheckTokenExists(input.RewardToken, input.RewardTokenContract ?? State.TokenContract.Value, out _);
        Assert(input.StartTime >= Context.CurrentBlockTime.Seconds, "Invalid start time.");
        Assert(input.EndTime > input.StartTime, "Invalid end time.");
        Assert(input.RewardPerSecond > 0, "Invalid reward per second.");
        Assert(input.FixedBoostFactor > 0, "Invalid fixed boost factor.");
        Assert(input.MinimumAmount >= 0, "Invalid minimum amount.");
        Assert(input.ReleasePeriod >= 0, "Invalid release period.");
        Assert(input.MaximumStakeDuration > 0, "Invalid maximum stake duration.");
        Assert(input.MinimumClaimAmount >= 0, "Invalid minimum claim amount.");
        Assert(input.MinimumStakeDuration > 0, "Invalid minimum stake duration.");
        Assert(input.UnlockWindowDuration > 0, "Invalid unlock window duration.");
    }

    private void CheckTokenExists(string symbol, Address tokenContract, out int decimals)
    {
        var info = Context.Call<TokenInfo>(tokenContract, nameof(State.TokenContract.GetTokenInfo),
            new GetTokenInfoInput { Symbol = symbol });
        Assert(IsStringValid(info.Symbol), $"{symbol} not exists.");
        decimals = info.Decimals;
    }

    private Hash GeneratePoolId(CreateTokensPoolInput input)
    {
        var count = State.PoolCountMap[input.DappId];
        var poolId = HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(count), HashHelper.ComputeFrom(input));
        State.PoolCountMap[input.DappId] = count.Add(1);

        return poolId;
    }

    private long CalculateTotalRewardAmount(long start, long end, long rewardPerSecond)
    {
        return end.Sub(start).Mul(rewardPerSecond);
    }

    private Address CalculateVirtualAddress(Hash id)
    {
        return Context.ConvertVirtualAddressToContractAddress(id);
    }

    private Address CalculateVirtualAddress(Address account)
    {
        return Context.ConvertVirtualAddressToContractAddress(HashHelper.ComputeFrom(account));
    }

    private Hash GetStakeVirtualAddress(Hash id)
    {
        return HashHelper.ConcatAndCompute(id, HashHelper.ComputeFrom(EcoEarnTokensContractConstants.StakeAddress));
    }

    private Hash GetRewardVirtualAddress(Hash id)
    {
        return HashHelper.ConcatAndCompute(id, HashHelper.ComputeFrom(EcoEarnTokensContractConstants.RewardAddress));
    }

    private PoolInfo GetPool(Hash poolId)
    {
        Assert(IsHashValid(poolId), "Invalid pool id.");

        var poolInfo = State.PoolInfoMap[poolId];
        Assert(poolInfo != null, "Pool not exists.");

        return poolInfo;
    }

    private BigIntValue CalculatePrecisionFactor(int decimals)
    {
        return new BigIntValue(EcoEarnTokensContractConstants.Ten).Pow(
            EcoEarnTokensContractConstants.MaxDecimals.Sub(decimals));
    }

    #endregion
}