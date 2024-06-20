using System.Linq;
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
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        Assert(input.Admin == null || !input.Admin.Value.IsNullOrEmpty(), "Invalid admin.");
        Assert(State.DappInfoMap[input.DappId] == null, "Dapp registered.");

        var dappInformationOutput = State.PointsContract.GetDappInformation.Call(new GetDappInformationInput
        {
            DappId = input.DappId
        });
        Assert(dappInformationOutput.DappInfo != null, "Dapp not exists.");
        Assert(dappInformationOutput.DappInfo!.DappAdmin == Context.Sender, "No permission to register.");

        var dappInfo = new DappInfo
        {
            DappId = input.DappId,
            Admin = input.Admin ?? Context.Sender
        };

        State.DappInfoMap[input.DappId] = dappInfo;

        Context.Fire(new Registered
        {
            DappId = dappInfo.DappId,
            Admin = dappInfo.Admin
        });

        return new Empty();
    }

    public override Empty SetDappAdmin(SetDappAdminInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        Assert(IsAddressValid(input.Admin), "Invalid admin.");

        var dappInfo = State.DappInfoMap[input.DappId];
        Assert(dappInfo != null, "Dapp not exists.");
        Assert(dappInfo!.Admin == Context.Sender, "No permission.");

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
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        CheckDAppAdminPermission(input.DappId);
        ValidatePointsPoolConfig(input);
        CheckPointExists(input.DappId, input.PointsName);

        var poolId = GeneratePoolId(input);
        Assert(State.PoolInfoMap[poolId] == null, "Pool exists.");

        Assert(State.PointsNameMap[input.DappId][input.PointsName] == null, "Points name taken.");
        State.PointsNameMap[input.DappId][input.PointsName] = poolId;

        var config = new PointsPoolConfig
        {
            RewardToken = input.RewardToken,
            RewardPerSecond = input.RewardPerSecond,
            ReleasePeriods = { input.ReleasePeriods.Distinct().OrderBy(n => n) },
            ClaimInterval = input.ClaimInterval,
            UpdateAddress = input.UpdateAddress,
            StartTime = new Timestamp
            {
                Seconds = input.StartTime
            },
            EndTime = new Timestamp
            {
                Seconds = input.EndTime
            }
        };

        State.PoolInfoMap[poolId] = new PoolInfo
        {
            DappId = input.DappId,
            PoolId = poolId,
            PointsName = input.PointsName,
            Config = config
        };

        var totalReward = CalculateTotalRewardAmount(input.StartTime, input.EndTime, input.RewardPerSecond);

        Context.Fire(new PointsPoolCreated
        {
            DappId = input.DappId,
            PoolId = poolId,
            PointsName = input.PointsName,
            Config = config,
            Amount = totalReward,
            PoolAddress = CalculateVirtualAddress(poolId)
        });

        return new Empty();
    }

    public override Empty SetPointsPoolEndTime(SetPointsPoolEndTimeInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input!.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        Assert(input.EndTime > poolInfo.Config.EndTime.Seconds, "Invalid end time.");

        var addedReward = CalculateTotalRewardAmount(poolInfo.Config.EndTime.Seconds, input.EndTime,
            poolInfo.Config.RewardPerSecond);

        poolInfo.Config.EndTime = new Timestamp
        {
            Seconds = input.EndTime
        };

        Context.Fire(new PointsPoolEndTimeSet
        {
            PoolId = input.PoolId,
            EndTime = poolInfo.Config.EndTime,
            Amount = addedReward
        });

        return new Empty();
    }

    public override Empty RestartPointsPool(RestartPointsPoolInput input)
    {
        Assert(input != null, "Invalid input.");
        ValidatePointsPoolConfig(input);

        var poolInfo = GetPool(input!.PoolId);
        Assert(!CheckPoolEnabled(poolInfo.Config.EndTime), "Can not restart yet.");
        CheckDAppAdminPermission(poolInfo.DappId);

        poolInfo.Config = new PointsPoolConfig
        {
            RewardToken = input.RewardToken,
            RewardPerSecond = input.RewardPerSecond,
            ReleasePeriods = { input.ReleasePeriods.Distinct().OrderBy(n => n) },
            ClaimInterval = input.ClaimInterval,
            UpdateAddress = input.UpdateAddress,
            StartTime = new Timestamp
            {
                Seconds = input.StartTime
            },
            EndTime = new Timestamp
            {
                Seconds = input.EndTime
            }
        };

        var totalReward = CalculateTotalRewardAmount(input.StartTime, input.EndTime, input.RewardPerSecond);

        Context.Fire(new PointsPoolRestarted
        {
            PoolId = input.PoolId,
            Amount = totalReward,
            Config = poolInfo.Config
        });

        return new Empty();
    }

    public override Empty SetPointsPoolUpdateAddress(SetPointsPoolUpdateAddressInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input!.UpdateAddress), "Invalid update address.");

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

    public override Empty SetPointsPoolRewardConfig(SetPointsPoolRewardConfigInput input)
    {
        Assert(input != null, "Invalid input.");

        var poolInfo = GetPool(input.PoolId);
        Assert(input!.ReleasePeriods != null && input.ReleasePeriods.Count > 0 && input.ReleasePeriods.All(p => p >= 0),
            "Invalid release periods.");
        Assert(input.ClaimInterval >= 0, "Invalid claim interval.");

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.ReleasePeriods.Equals(input.ReleasePeriods) &&
            poolInfo.Config.ClaimInterval == input.ClaimInterval) return new Empty();

        poolInfo.Config.ReleasePeriods.Clear();
        poolInfo.Config.ReleasePeriods.AddRange(input.ReleasePeriods!.Distinct().OrderBy(n => n));
        poolInfo.Config.ClaimInterval = input.ClaimInterval;

        Context.Fire(new PointsPoolRewardConfigSet
        {
            PoolId = input.PoolId,
            ReleasePeriods = new ReleasePeriods
            {
                Data = { poolInfo.Config.ReleasePeriods }
            },
            ClaimInterval = input.ClaimInterval
        });

        return new Empty();
    }

    public override Empty SetPointsPoolRewardPerSecond(SetPointsPoolRewardPerSecondInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(input!.RewardPerSecond >= 0, "Invalid reward per second.");

        var poolInfo = GetPool(input.PoolId);

        CheckDAppAdminPermission(poolInfo.DappId);

        if (poolInfo.Config.RewardPerSecond == input.RewardPerSecond) return new Empty();

        poolInfo.Config.RewardPerSecond = input.RewardPerSecond;

        Context.Fire(new PointsPoolRewardPerSecondSet
        {
            PoolId = input.PoolId,
            RewardPerSecond = input.RewardPerSecond
        });

        return new Empty();
    }

    #endregion

    #region private

    private void ValidatePointsPoolConfig(CreatePointsPoolInput input)
    {
        Assert(IsAddressValid(input.UpdateAddress), "Invalid update address.");
        CheckTokenExists(input.RewardToken);
        Assert(input.StartTime >= Context.CurrentBlockTime.Seconds, "Invalid start time.");
        Assert(input.EndTime > input.StartTime, "Invalid end time.");
        Assert(input.RewardPerSecond > 0, "Invalid reward per second.");
        Assert(input.ReleasePeriods != null && input.ReleasePeriods.Count > 0 && input.ReleasePeriods.All(p => p >= 0),
            "Invalid release periods.");
        Assert(input.ClaimInterval >= 0, "Invalid claim interval.");
    }

    private void ValidatePointsPoolConfig(RestartPointsPoolInput input)
    {
        Assert(input != null, "Invalid config.");
        Assert(IsAddressValid(input!.UpdateAddress), "Invalid update address.");
        CheckTokenExists(input.RewardToken);
        Assert(input.StartTime >= Context.CurrentBlockTime.Seconds, "Invalid start time.");
        Assert(input.EndTime > input.StartTime, "Invalid end time.");
        Assert(input.RewardPerSecond > 0, "Invalid reward per second.");
        Assert(input.ReleasePeriods != null && input.ReleasePeriods.Count > 0 && input.ReleasePeriods.All(p => p >= 0),
            "Invalid release periods.");
        Assert(input.ClaimInterval >= 0, "Invalid claim interval.");
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

    private PoolInfo GetPool(Hash poolId)
    {
        Assert(IsHashValid(poolId), "Invalid pool id.");

        var poolInfo = State.PoolInfoMap[poolId];
        Assert(poolInfo != null, "Pool not exists.");

        return poolInfo;
    }

    #endregion
}