using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using static System.Int64;

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
        ValidateTokensPoolConfig(input.Config);
        Assert(IsStringValid(input.Config.StakingToken), "Invalid staking token.");
        CheckTokenExists(input.Config.StakingToken, input.Config.StakeTokenContract ?? State.TokenContract.Value,
            out var decimals);

        var poolId = GeneratePoolId(input);
        Assert(State.PoolInfoMap[poolId] == null, "Pool exists.");

        var poolInfo = new PoolInfo
        {
            DappId = input.DappId,
            PoolId = poolId,
            Config = input.Config,
            PrecisionFactor = CalculatePrecisionFactor(decimals)
        };
        poolInfo.Config.RewardTokenContract = input.Config.RewardTokenContract ?? State.TokenContract.Value;
        poolInfo.Config.StakeTokenContract = input.Config.StakeTokenContract ?? State.TokenContract.Value;

        State.PoolInfoMap[poolId] = poolInfo;

        State.PoolDataMap[poolId] = new PoolData
        {
            PoolId = poolId,
            LastRewardBlock = Context.CurrentHeight
        };

        TransferReward(input.Config, poolId, out var amount);

        Context.Fire(new TokensPoolCreated
        {
            DappId = input.DappId,
            PoolId = poolId,
            Config = input.Config,
            Amount = amount
        });

        return new Empty();
    }

    public override Empty CloseTokensPool(Hash input)
    {
        var poolInfo = GetPool(input);
        CheckDAppAdminPermission(poolInfo.DappId);
        Assert(CheckPoolEnabled(poolInfo.Config.EndBlockNumber), "Pool already closed.");

        poolInfo.Config.EndBlockNumber = Context.CurrentHeight;

        Context.Fire(new TokensPoolClosed
        {
            PoolId = input,
            Config = poolInfo.Config
        });

        return new Empty();
    }

    public override Empty SetTokensPoolEndBlockNumber(SetTokensPoolEndBlockNumberInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(input.EndBlockNumber > poolInfo.Config.StartBlockNumber, "Invalid end block number.");

        if (input.EndBlockNumber == poolInfo.Config.EndBlockNumber) return new Empty();

        var totalRewards = 0L;

        if (input.EndBlockNumber > poolInfo.Config.EndBlockNumber)
        {
            totalRewards = CalculateTotalRewardAmount(poolInfo.Config.EndBlockNumber, input.EndBlockNumber,
                poolInfo.Config.RewardPerBlock);

            Context.SendInline(poolInfo.Config.RewardTokenContract, nameof(State.TokenContract.TransferFrom),
                new TransferFromInput
                {
                    From = Context.Sender,
                    To = CalculateVirtualAddress(GetRewardVirtualAddress(input.PoolId)),
                    Symbol = poolInfo.Config.RewardToken,
                    Amount = totalRewards
                });
        }

        poolInfo.Config.EndBlockNumber = input.EndBlockNumber;

        Context.Fire(new TokensPoolEndBlockNumberSet
        {
            PoolId = input.PoolId,
            EndBlockNumber = input.EndBlockNumber,
            Amount = totalRewards
        });

        return new Empty();
    }

    public override Empty SetTokensPoolUpdateAddress(SetTokensPoolUpdateAddressInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input.UpdateAddress), "Invalid update address.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.UpdateAddress == input.UpdateAddress) return new Empty();

        poolInfo.Config.UpdateAddress = input.UpdateAddress;

        Context.Fire(new TokensPoolUpdateAddressSet
        {
            PoolId = input.PoolId,
            UpdateAddress = input.UpdateAddress
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
        Assert(input.FixedBoostFactor >= 0, "Invalid fixed boost factor.");

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

    #endregion

    #region private

    private void ValidateTokensPoolConfig(TokensPoolConfig config)
    {
        Assert(config != null, "Invalid config.");
        Assert(IsAddressValid(config.UpdateAddress), "Invalid update address.");
        Assert(config.RewardTokenContract == null || !config.RewardTokenContract.Value.IsNullOrEmpty(),
            "Invalid reward token contract.");
        Assert(config.StakeTokenContract == null || !config.StakeTokenContract.Value.IsNullOrEmpty(),
            "Invalid stake token contract.");
        Assert(IsStringValid(config.RewardToken), "Invalid reward token.");
        CheckTokenExists(config.RewardToken, config.RewardTokenContract ?? State.TokenContract.Value, out _);
        Assert(config.StartBlockNumber >= Context.CurrentHeight, "Invalid start block number.");
        Assert(config.EndBlockNumber > config.StartBlockNumber, "Invalid end block number.");
        Assert(config.RewardPerBlock > 0, "Invalid reward per block.");
        Assert(config.FixedBoostFactor >= 0, "Invalid fixed boost factor.");
        Assert(config.MinimumAmount >= 0, "Invalid minimum amount.");
        Assert(config.ReleasePeriod >= 0, "Invalid release period.");
        Assert(config.MaximumStakeDuration > 0, "Invalid maximum stake duration.");
        Assert(config.MinimumClaimAmount >= 0, "Invalid minimum claim amount.");
        Assert(config.MinimumStakeDuration > 0, "Invalid minimum stake duration.");
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

    private void TransferReward(TokensPoolConfig config, Hash poolId, out long amount)
    {
        amount = CalculateTotalRewardAmount(config.StartBlockNumber, config.EndBlockNumber, config.RewardPerBlock);

        Context.SendInline(config.RewardTokenContract, nameof(State.TokenContract.TransferFrom), new TransferFromInput
        {
            From = Context.Sender,
            To = CalculateVirtualAddress(GetRewardVirtualAddress(poolId)),
            Symbol = config.RewardToken,
            Amount = amount,
            Memo = "reward"
        });
    }

    private long CalculateTotalRewardAmount(long start, long end, long rewardPerBlock)
    {
        return end.Sub(start) * rewardPerBlock;
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

    private long CalculatePrecisionFactor(int decimals)
    {
        var tryParse = TryParse(new BigIntValue(EcoEarnTokensContractConstants.Ten).Pow(decimals).Value, out var value);
        return !tryParse || value <= 1 ? EcoEarnTokensContractConstants.Denominator : value;
    }

    #endregion
}