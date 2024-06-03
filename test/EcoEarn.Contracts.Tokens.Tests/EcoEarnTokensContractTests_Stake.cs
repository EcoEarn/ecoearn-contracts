using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;
using static System.Int64;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests
{
    [Fact]
    public async Task StakeTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = UserAddress,
            Amount = tokenBalance * 2,
            Symbol = Symbol
        });
        await TokenContractUserStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = tokenBalance * 2
        });

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(tokenBalance * 2);

        var addressInfo = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);

        balance = await GetTokenBalance(Symbol, addressInfo.StakeAddress);
        balance.ShouldBe(0);

        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(0);
        poolData.AccTokenPerShare.ShouldBeNull();
        var userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
        {
            Account = UserAddress,
            PoolId = poolId
        });
        userStakeId.ShouldBe(new Hash());

        // create position
        {
            var result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
            {
                PoolId = poolId,
                Amount = tokenBalance,
                Period = 86400
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<Staked>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);
            log.PoolData.PoolId.ShouldBe(poolId);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            log.PoolData.AccTokenPerShare.ShouldBeNull();

            var stakeInfo = log.StakeInfo;
            stakeInfo.PoolId.ShouldBe(poolId);
            stakeInfo.Period.ShouldBe(86400);
            stakeInfo.StakingPeriod.ShouldBe(86400);
            stakeInfo.Account.ShouldBe(UserAddress);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.ClaimedAmount.ShouldBe(0);
            stakeInfo.RewardAmount.ShouldBe(0);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.RewardDebt.ShouldBe(0);
            stakeInfo.StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.StakedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime);
            stakeInfo.UnlockTime.ShouldBeNull();
            
            userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            userStakeId.ShouldBe(log.StakeInfo.StakeId);

            poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
            poolData.ShouldBe(log.PoolData);

            balance = await GetTokenBalance(Symbol, UserAddress);
            balance.ShouldBe(tokenBalance);

            balance = await GetTokenBalance(Symbol, addressInfo.StakeAddress);
            balance.ShouldBe(tokenBalance);

            SetBlockTime(1);

            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
            {
                StakeIds = { stakeInfo.StakeId }
            });
            reward.RewardInfos.First().Symbol.ShouldBe(Symbol);
            reward.RewardInfos.First().Account.ShouldBe(UserAddress);
            reward.RewardInfos.First().StakeId.ShouldBe(stakeInfo.StakeId);
            reward.RewardInfos.First().Amount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000);
        }

        // add position with more amount
        {
            var result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
            {
                PoolId = poolId,
                Amount = tokenBalance,
                Period = 0
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<Staked>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 4);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());

            SetBlockTime(1);

            var acc = new BigIntValue(100_00000000).Mul(1000000000000000000).Div(poolData.TotalStakedAmount);
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.Period.ShouldBe(86400);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 4);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.RewardAmount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000); // minus commission fee

            TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(4).Div(1000000000000000000)).Value, out var value);

            stakeInfo.RewardDebt.ShouldBe(value);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime);
            
            userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            userStakeId.ShouldBe(log.StakeInfo.StakeId);

            poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
            poolData.ShouldBe(log.PoolData);

            balance = await GetTokenBalance(Symbol, UserAddress);
            balance.ShouldBe(0);

            balance = await GetTokenBalance(Symbol, addressInfo.StakeAddress);
            balance.ShouldBe(tokenBalance * 2);

            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
            {
                StakeIds = { stakeInfo.StakeId }
            });
            reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 2 - 100_00000000 * 100 / 10000 * 2);
        }

        // add position with more period
        {
            var result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
            {
                PoolId = poolId,
                Amount = 0,
                Period = 172800
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<Staked>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 8);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());

            SetBlockTime(1);

            var acc = new BigIntValue(100_00000000).Mul(1000000000000000000).Div(tokenBalance.Mul(4))
                .Add((new BigIntValue(100_00000000).Mul(1000000000000000000).Div(tokenBalance.Mul(2))));
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.Period.ShouldBe(86400 + 172800);
            stakeInfo.StakingPeriod.ShouldBe(86400 - 2 + 172800);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 8);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.RewardAmount.ShouldBe((100_00000000 - 100_00000000 * 100 / 10000) * 2); // minus commission fee

            TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(8).Div(1000000000000000000)).Value, out var value);

            stakeInfo.RewardDebt.ShouldBe(value);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime.AddSeconds(2));
            
            userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            userStakeId.ShouldBe(log.StakeInfo.StakeId);

            poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
            poolData.ShouldBe(log.PoolData);

            balance = await GetTokenBalance(Symbol, UserAddress);
            balance.ShouldBe(0);

            balance = await GetTokenBalance(Symbol, addressInfo.StakeAddress);
            balance.ShouldBe(tokenBalance * 2);

            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
            {
                StakeIds = { stakeInfo.StakeId }
            });
            reward.RewardInfos.First().Amount.ShouldBe((100_00000000 - 100_00000000 * 100 / 10000) * 3);
        }
    }

    [Fact]
    public async Task StakeTests_UpdatePosition()
    {
        var poolId = await CreateTokensPool();

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = 10_00000000
        });

        var result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 86400
        });
        var stakeId = GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId;

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000
        });
        GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId.ShouldBe(stakeId);

        SetBlockTime(86400);

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000
        });
        result.TransactionResult.Error.ShouldContain("Cannot stake during unlock window.");

        await EcoEarnTokensContractStub.Unlock.SendAsync(poolId);

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 86400
        });
        GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId.ShouldNotBe(stakeId);
    }

    [Fact]
    public async Task StakeTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("New position requires both amount and period.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 0,
            Period = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid period.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 0,
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Period too short.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 0,
            Period = 500001
        });
        result.TransactionResult.Error.ShouldContain("Period too long.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1,
            Period = 86400
        });
        result.TransactionResult.Error.ShouldContain("Amount not enough");

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Symbol = Symbol,
            Amount = 2_00000000,
            Spender = EcoEarnTokensContractAddress
        });

        await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 86400
        });

        SetBlockTime(80000);

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 500000 - 6400 + 1
        });
        result.TransactionResult.Error.ShouldContain("Period too long.");

        SetBlockTime(6400);

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 86400
        });
        result.TransactionResult.Error.ShouldContain("Cannot stake during unlock window.");

        SetBlockTime(1);

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 86400
        });
        result.TransactionResult.Error.ShouldContain("Pool closed.");
    }

    [Fact]
    public async Task RenewTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        var stakeId = stakeInfo.StakeId;

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(0);

        SetBlockTime(86400);

        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100);

        var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        stakeInfo = output.StakeInfo;
        output.IsInUnlockWindow.ShouldBe(true);

        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);

        stakeInfo.StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(-86400));
        stakeInfo.Period.ShouldBe(86400);
        stakeInfo.StakingPeriod.ShouldBe(86400);
        stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 2);
        stakeInfo.RewardAmount.ShouldBe(0);

        var result = await EcoEarnTokensContractUserStub.Renew.SendAsync(new RenewInput
        {
            PoolId = poolId,
            Period = 100000
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Renewed>(result.TransactionResult);
        log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);

        stakeInfo = EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId).Result.StakeInfo;
        log.StakeInfo.ShouldBe(stakeInfo);

        stakeInfo.StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(-86400));
        stakeInfo.Period.ShouldBe(86400);
        stakeInfo.StakingPeriod.ShouldBe(100000);
        stakeInfo.RewardAmount.ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100);
        stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 2);

        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100);

        SetBlockTime(1);

        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100 + 100_00000000 - 100_00000000 / 100);

        SetBlockTime(1);

        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100 + 100_00000000 - 100_00000000 / 100);

        SetBlockTime(1);

        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 86400 - 100_00000000 * 86400 / 100 + 100_00000000 - 100_00000000 / 100);
    }

    [Fact]
    public async Task RenewTests_LongPeriod()
    {
        var poolId = await CreateTokensPoolForRenew();

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = UserAddress,
            Symbol = Symbol,
            Amount = 100_00000000
        });
        await TokenContractUserStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = 100_00000000
        });

        var result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        var stakeId = GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId;

        var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        output.StakeInfo.StakingPeriod.ShouldBe(10);

        SetBlockTime(50000);

        await EcoEarnTokensContractUserStub.Renew.SendAsync(new RenewInput
        {
            PoolId = poolId,
            Period = 10
        });

        output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        output.StakeInfo.StakingPeriod.ShouldBe(10);
    }

    [Fact]
    public async Task RenewTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput());
        result.TransactionResult.Error.ShouldContain("Invalid period.");

        result = await EcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Stake info not exists.");

        result = await EcoEarnTokensContractUserStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Not in unlock window.");

        SetBlockTime(86400);

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);

        result = await EcoEarnTokensContractUserStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Already unlocked.");

        SetBlockTime(2);

        result = await EcoEarnTokensContractUserStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Pool closed.");
    }

    [Fact]
    public async Task UnlockTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.StakedAmount.ShouldBe(tokenBalance);
        stakeInfo.ClaimedAmount.ShouldBe(0);

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);
        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);

        SetBlockTime(86400);

        var result = await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(tokenBalance);

        var log = GetLogEvent<Unlocked>(result.TransactionResult);
        log.StakeId.ShouldBe(stakeInfo.StakeId);
        log.StakedAmount.ShouldBe(0);
        log.PoolData.TotalStakedAmount.ShouldBe(0);
    }

    [Fact]
    public async Task UnlockTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Unlock.SendWithExceptionAsync(new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnTokensContractStub.Unlock.SendWithExceptionAsync(HashHelper.ComputeFrom("test"));
        result.TransactionResult.Error.ShouldContain("Not staked before.");

        result = await EcoEarnTokensContractStub.Unlock.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not staked before.");

        result = await EcoEarnTokensContractUserStub.Unlock.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not in unlock window.");

        SetBlockTime(90000);

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);

        result = await EcoEarnTokensContractUserStub.Unlock.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Already unlocked.");
    }

    [Fact]
    public async Task ViewTests()
    {
        {
            var output = await EcoEarnTokensContractStub.GetDappInfo.CallAsync(new Hash());
            output.DappId.ShouldBeNull();
        }
        {
            var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(new Hash());
            output.PoolInfo.ShouldBeNull();
        }
        {
            var output = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(new Hash());
            output.StakeAddress.ShouldBeNull();
        }
        {
            var output = await EcoEarnTokensContractStub.GetPoolData.CallAsync(new Hash());
            output.PoolId.ShouldBeNull();
        }
        {
            var output = await EcoEarnTokensContractStub.GetPoolCount.CallAsync(new Hash());
            output.Value.ShouldBe(0);
        }
        {
            var output = await EcoEarnTokensContractStub.GetClaimInfo.CallAsync(new Hash());
            output.PoolId.ShouldBeNull();
        }
        {
            var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(HashHelper.ComputeFrom("test"));
            output.StakeInfo.ShouldBeNull();
        }
    }

    private async Task<StakeInfo> Stake(Hash poolId, long tokenBalance)
    {
        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = UserAddress,
            Symbol = Symbol,
            Amount = tokenBalance
        });
        await TokenContractUserStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = tokenBalance
        });

        var result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = tokenBalance,
            Period = 86400
        });
        return GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
    }

    private async Task<Hash> CreateTokensPoolForRenew()
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await Register();
            await CreateToken();
        }

        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 100000000,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 1,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnlockWindowDuration = 300,
            UpdateAddress = DefaultAddress
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 10000000_00000000,
            To = log.AddressInfo.RewardAddress,
            Symbol = Symbol
        });

        return log.PoolId;
    }
}