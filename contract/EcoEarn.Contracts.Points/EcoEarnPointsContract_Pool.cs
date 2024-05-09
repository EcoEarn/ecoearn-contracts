using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Points.Contracts.PointsContract;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContract
{
    #region public

    public override Empty Register(RegisterInput input)
    {
        CheckInitialized();

        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
        Assert(input.Admin == null || !input.Admin.Value.IsNullOrEmpty(), "Invalid admin.");
        Assert(State.DappInfoMap[input.DappId] == null, "Dapp registered.");

        var dappInformationOutput = State.PointsContract.GetDappInformation.Call(new GetDappInformationInput
        {
            DappId = input.DappId
        });
        Assert(dappInformationOutput?.DappInfo != null, "Dapp not exists.");
        Assert(dappInformationOutput.DappInfo.DappAdmin == Context.Sender, "No permission to register.");

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
        Assert(IsAddressValid(input.Admin), "Invalid admin.");

        var dappInfo = State.DappInfoMap[input.DappId];
        Assert(dappInfo != null, "Dapp not exists.");
        Assert(dappInfo.Admin == Context.Sender, "No permission.");

        if (input.Admin == dappInfo.Admin) return new Empty();

        dappInfo.Admin = input.Admin;

        Context.Fire(new DappAdminSet
        {
            DappId = input.DappId,
            Admin = input.Admin
        });

        return new Empty();
    }

    public override Empty CreatePointsPool(CreatePointsPoolInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.DappId), "Invalid dapp id.");
        CheckDAppAdminPermission(input.DappId);
        ValidatePointsPoolConfig(input.Config);
        CheckPointExists(input.DappId, input.PointsName);

        var poolId = GeneratePoolId(input);
        Assert(State.PoolInfoMap[poolId] == null, "Pool exists.");

        Assert(State.PointsNameMap[input.DappId][input.PointsName] == null, "Points name taken.");
        State.PointsNameMap[input.DappId][input.PointsName] = poolId;

        State.PoolInfoMap[poolId] = new PoolInfo
        {
            DappId = input.DappId,
            PoolId = poolId,
            PointsName = input.PointsName,
            Config = input.Config
        };

        // charge rewards to pool address
        TransferReward(input.Config, CalculateVirtualAddress(poolId), out var amount);

        Context.Fire(new PointsPoolCreated
        {
            DappId = input.DappId,
            PoolId = poolId,
            PointsName = input.PointsName,
            Config = input.Config,
            Amount = amount
        });

        return new Empty();
    }

    public override Empty ClosePointsPool(Hash input)
    {
        var poolInfo = GetPool(input);
        CheckDAppAdminPermission(poolInfo.DappId);
        Assert(CheckPoolEnabled(poolInfo.Config.EndBlockNumber), "Pool already closed.");

        poolInfo.Config.EndBlockNumber = Context.CurrentHeight;

        Context.Fire(new PointsPoolClosed
        {
            PoolId = input,
            Config = poolInfo.Config
        });

        return new Empty();
    }

    public override Empty SetPointsPoolEndBlockNumber(SetPointsPoolEndBlockNumberInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(input.EndBlockNumber > poolInfo.Config.StartBlockNumber, "Invalid end block number.");

        if (input.EndBlockNumber == poolInfo.Config.EndBlockNumber) return new Empty();

        var amount = 0L;

        // charge rewards if extends end block number
        if (input.EndBlockNumber > poolInfo.Config.EndBlockNumber)
        {
            amount = CalculateTotalRewardAmount(poolInfo.Config.EndBlockNumber, input.EndBlockNumber,
                poolInfo.Config.RewardPerBlock);

            State.TokenContract.TransferFrom.Send(new TransferFromInput
            {
                From = Context.Sender,
                To = CalculateVirtualAddress(input.PoolId),
                Symbol = poolInfo.Config.RewardToken,
                Amount = amount
            });
        }

        poolInfo.Config.EndBlockNumber = input.EndBlockNumber;

        Context.Fire(new PointsPoolEndBlockNumberSet
        {
            PoolId = input.PoolId,
            EndBlockNumber = input.EndBlockNumber,
            Amount = amount
        });

        return new Empty();
    }

    public override Empty RestartPointsPool(RestartPointsPoolInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidatePointsPoolConfig(input.Config);

        var poolInfo = GetPool(input.PoolId);
        Assert(!CheckPoolEnabled(poolInfo.Config.EndBlockNumber), "Can not restart yet.");
        CheckDAppAdminPermission(poolInfo.DappId);

        poolInfo.Config = input.Config;

        // charge rewards to pool address
        TransferReward(input.Config, CalculateVirtualAddress(input.PoolId), out var amount);

        Context.Fire(new PointsPoolRestarted
        {
            PoolId = input.PoolId,
            Amount = amount,
            Config = input.Config
        });

        return new Empty();
    }

    public override Empty SetPointsPoolUpdateAddress(SetPointsPoolUpdateAddressInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input.UpdateAddress), "Invalid update address.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.UpdateAddress == input.UpdateAddress) return new Empty();

        poolInfo.Config.UpdateAddress = input.UpdateAddress;

        Context.Fire(new PointsPoolUpdateAddressSet
        {
            PoolId = input.PoolId,
            UpdateAddress = input.UpdateAddress
        });

        return new Empty();
    }

    public override Empty SetPointsPoolRewardReleasePeriod(SetPointsPoolRewardReleasePeriodInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input.ReleasePeriod >= 0, "Invalid release period.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.ReleasePeriod == input.ReleasePeriod) return new Empty();

        poolInfo.Config.ReleasePeriod = input.ReleasePeriod;

        Context.Fire(new PointsPoolRewardReleasePeriodSet
        {
            PoolId = input.PoolId,
            ReleasePeriod = input.ReleasePeriod
        });

        return new Empty();
    }

    #endregion

    #region private

    private void ValidatePointsPoolConfig(PointsPoolConfig config)
    {
        Assert(config != null, "Invalid config.");
        Assert(IsAddressValid(config.UpdateAddress), "Invalid update address.");
        CheckTokenExists(config.RewardToken);
        Assert(config.StartBlockNumber >= Context.CurrentHeight, "Invalid start block number.");
        Assert(config.EndBlockNumber > config.StartBlockNumber, "Invalid end block number.");
        Assert(config.RewardPerBlock > 0, "Invalid reward per block.");
        Assert(config.ReleasePeriod >= 0, "Invalid release period.");
    }

    private void CheckTokenExists(string symbol)
    {
        Assert(IsStringValid(symbol), "Invalid reward token.");
        var info = State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
        {
            Symbol = symbol
        });
        Assert(IsStringValid(info.Symbol), $"{symbol} not exists.");
    }

    private void CheckPointExists(Hash dappId, string pointsName)
    {
        Assert(IsStringValid(pointsName), "Invalid points name.");

        var pointInfo = State.PointsContract.GetPoint.Call(new GetPointInput
        {
            PointsName = pointsName,
            DappId = dappId
        });

        Assert(IsStringValid(pointInfo.TokenName), "Point not exists.");
    }

    private Hash GeneratePoolId(CreatePointsPoolInput input)
    {
        return HashHelper.ComputeFrom(input);
    }

    private void TransferReward(PointsPoolConfig config, Address poolAddress, out long amount)
    {
        amount = CalculateTotalRewardAmount(config.StartBlockNumber, config.EndBlockNumber, config.RewardPerBlock);

        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = poolAddress,
            Symbol = config.RewardToken,
            Amount = amount,
            Memo = "pool"
        });
    }

    private long CalculateTotalRewardAmount(long start, long end, long rewardPerBlock)
    {
        return end.Sub(start).Mul(rewardPerBlock);
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