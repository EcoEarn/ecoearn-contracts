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
        if (dappInfo.DappId != null) Assert(dappInfo.Admin == Context.Sender, "No permission.");

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

        var poolId = GeneratePoolId(input);
        Assert(State.PoolInfoMap[poolId] == null, "Pool exists.");

        var poolInfo = new PoolInfo
        {
            DappId = input.DappId,
            PoolId = poolId,
            Config = input.Config
        };
        poolInfo.Config.RewardTokenContract = State.TokenContract.Value;
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
            PoolAddress = CalculateVirtualAddress(poolId),
            Amount = amount
        });

        return new Empty();
    }

    public override Empty CloseTokensPool(Hash input)
    {
        var poolInfo = GetPool(input);
        CheckDAppAdminPermission(poolInfo.DappId);
        Assert(Context.CurrentHeight < poolInfo.Config.EndBlockNumber, "Pool already closed.");

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

        var amount = 0L;

        if (input.EndBlockNumber > poolInfo.Config.EndBlockNumber)
        {
            amount = CalculateTotalRewardAmount(poolInfo.Config.EndBlockNumber, input.EndBlockNumber,
                poolInfo.Config.RewardPerBlock);

            Context.SendInline(poolInfo.Config.StakeTokenContract, "TransferFrom", new TransferFromInput
            {
                From = Context.Sender,
                To = CalculateVirtualAddress(input.PoolId),
                Symbol = poolInfo.Config.RewardToken,
                Amount = amount
            });
        }

        poolInfo.Config.EndBlockNumber = input.EndBlockNumber;

        Context.Fire(new TokensPoolEndBlockNumberSet
        {
            PoolId = input.PoolId,
            EndBlockNumber = input.EndBlockNumber,
            Amount = amount
        });

        return new Empty();
    }

    public override Empty RestartTokensPool(RestartTokensPoolInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidateTokensPoolConfig(input.Config);

        var poolInfo = GetPool(input.PoolId);
        Assert(Context.CurrentHeight >= poolInfo.Config.EndBlockNumber, "Can not restart yet.");
        CheckDAppAdminPermission(poolInfo.DappId);

        poolInfo.Config = input.Config;
        poolInfo.Config.RewardTokenContract = input.Config.RewardTokenContract ?? State.TokenContract.Value;
        poolInfo.Config.StakeTokenContract = input.Config.StakeTokenContract ?? State.TokenContract.Value;

        TransferReward(input.Config, input.PoolId, out var amount);

        Context.Fire(new TokensPoolRestarted
        {
            PoolId = input.PoolId,
            Amount = amount,
            Config = input.Config
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
        Assert(input.MinimalAmount >= 0, "Invalid minimal amount.");
        Assert(input.MaximumStakeDuration > 0, "Invalid maximum stake duration.");
        Assert(input.MinimalClaimAmount >= 0, "Invalid minimal claim amount.");
        
        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.MinimalAmount == input.MinimalAmount &&
            poolInfo.Config.MaximumStakeDuration == input.MaximumStakeDuration &&
            poolInfo.Config.MinimalClaimAmount == input.MinimalClaimAmount)
        {
            return new Empty();
        }

        poolInfo.Config.MinimalAmount = input.MinimalAmount;
        poolInfo.Config.MaximumStakeDuration = input.MaximumStakeDuration;
        poolInfo.Config.MinimalClaimAmount = input.MinimalClaimAmount;
        
        Context.Fire(new TokensPoolStakeConfigSet
        {
            PoolId = input.PoolId,
            MinimalClaimAmount = input.MinimalClaimAmount,
            MaximumStakeDuration = input.MaximumStakeDuration,
            MinimalAmount = input.MinimalAmount
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
        CheckTokenExists(config.RewardToken, config.RewardTokenContract);
        Assert(config.StartBlockNumber >= Context.CurrentHeight, "Invalid start block number.");
        Assert(config.EndBlockNumber > config.StartBlockNumber, "Invalid end block number.");
        Assert(config.RewardPerBlock > 0, "Invalid reward per block.");
        CheckTokenExists(config.StakingToken, config.StakeTokenContract);
        Assert(config.FixedBoostFactor >= 0, "Invalid fixed boost factor.");
        Assert(config.MinimalAmount >= 0, "Invalid minimal amount.");
        Assert(config.ReleasePeriod >= 0, "Invalid release period.");
        Assert(config.MaximumStakeDuration > 0, "Invalid maximum stake duration.");
        Assert(config.MinimalClaimAmount >= 0, "Invalid minimal claim amount.");
    }

    private void CheckTokenExists(string symbol, Address tokenContract)
    {
        Assert(IsStringValid(symbol), "Invalid reward token.");
        var info = Context.Call<TokenInfo>(tokenContract, "GetTokenInfo", new GetTokenInfoInput
        {
            Symbol = symbol
        });
        Assert(IsStringValid(info.Symbol), $"{symbol} not exists.");
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

        Context.SendInline(config.RewardTokenContract, "TransferFrom", new TransferFromInput
        {
            From = Context.Sender,
            To = CalculateVirtualAddress(poolId),
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

    private PoolInfo GetPool(Hash poolId)
    {
        Assert(IsHashValid(poolId), "Invalid pool id.");

        var poolInfo = State.PoolInfoMap[poolId];
        Assert(poolInfo != null, "Pool not exists.");

        return poolInfo;
    }

    #endregion
}