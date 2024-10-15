using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using EcoEarn.Contracts.Rewards;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

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
            var result = await UserEcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
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
            stakeInfo.StakingPeriod.ShouldBe(86400);
            stakeInfo.Account.ShouldBe(UserAddress);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.LastOperationTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.UnstakeTime.ShouldBeNull();

            stakeInfo.SubStakeInfos.Count.ShouldBe(1);
            stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(0);
            stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.SubStakeInfos.First().RewardDebt.ShouldBe(0);
            stakeInfo.SubStakeInfos.First().StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.SubStakeInfos.First().StakedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

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

        // add position
        {
            SetBlockTime(9);
            
            var result = await UserEcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
            {
                PoolId = poolId,
                Amount = tokenBalance,
                Period = 86400
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<Staked>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2 + tokenBalance * 3);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            
            var acc = new BigIntValue(100_00000000).Mul(10).Mul(1000000000000000000).Div(poolData.TotalStakedAmount);
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.StakingPeriod.ShouldBe(86400 * 2 - 10);
            stakeInfo.SubStakeInfos.Count.ShouldBe(2);
            stakeInfo.SubStakeInfos.First().Period.ShouldBe(172800);
            stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance * 3);
            stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.SubStakeInfos.First().RewardAmount
                .ShouldBe(100_00000000 * 10 - 100_00000000 * 10 / 100); // minus commission fee
            stakeInfo.SubStakeInfos.Last().Period.ShouldBe(172800 - 10);
            stakeInfo.SubStakeInfos.Last().BoostedAmount.ShouldBe(tokenBalance * 2);

            long.TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(3).Div(1000000000000000000)).Value, out var value);

            stakeInfo.SubStakeInfos.First().RewardDebt.ShouldBe(value);
            
            long.TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(2).Div(1000000000000000000)).Value, out value);
            
            stakeInfo.SubStakeInfos.Last().RewardDebt.ShouldBe(value);
            stakeInfo.LastOperationTime.ShouldBe(BlockTimeProvider.GetBlockTime());

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
            reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 10 - 100_00000000 * 10 * 100 / 10000);
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
            Period = 10
        });
        var stakeId = GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId;

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId.ShouldBe(stakeId);

        SetBlockTime(20);

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        result.TransactionResult.Error.ShouldContain("Cannot stake during unstake window.");

        await EcoEarnTokensContractStub.Unstake.SendAsync(poolId);

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId.ShouldNotBe(stakeId);
    }
    
    [Fact]
    public async Task StakeTests_MergePosition()
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
            Period = 10
        });
        var stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.SubStakeInfos.Count.ShouldBe(1);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(1_00000000);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(1_00000000);
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(10);

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.SubStakeInfos.Count.ShouldBe(1);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(2_00000000);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(2_00000000);
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(20);
        
        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.SubStakeInfos.Count.ShouldBe(1);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(3_00000000);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(3_00000000);
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(30);

        SetBlockTime(6);

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.SubStakeInfos.Count.ShouldBe(2);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(3_00000000);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(3_00000000);
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(40);
        stakeInfo.SubStakeInfos.Last().StakedAmount.ShouldBe(1_00000000);
        stakeInfo.SubStakeInfos.Last().BoostedAmount.ShouldBe(1_00000000);
        stakeInfo.SubStakeInfos.Last().Period.ShouldBe(34);
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
        result.TransactionResult.Error.ShouldContain("Invalid amount.");
        
        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Symbol = Symbol,
            Amount = 2_00000000,
            Spender = EcoEarnTokensContractAddress
        });
        
        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Amount not enough.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Period too short.");

        result = await EcoEarnTokensContractStub.Stake.SendWithExceptionAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 500001
        });
        result.TransactionResult.Error.ShouldContain("Period too long.");

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
        result.TransactionResult.Error.ShouldContain("Cannot stake during unstake window.");

        SetBlockTime(100000);

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
    
        SetBlockTime(500);
    
        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100);
    
        var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        stakeInfo = output.StakeInfo;
        output.IsInUnstakeWindow.ShouldBe(true);
    
        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance);
    
        stakeInfo.SubStakeInfos.First().StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(-500));
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(500);
        stakeInfo.StakingPeriod.ShouldBe(500);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance);
        stakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(0);
    
        var result = await UserEcoEarnTokensContractStub.Renew.SendAsync(new RenewInput
        {
            PoolId = poolId,
            Period = 100000
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
    
        var log = GetLogEvent<Renewed>(result.TransactionResult);
        log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);
    
        stakeInfo = EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId).Result.StakeInfo;
        log.StakeInfo.ShouldBe(stakeInfo);
    
        stakeInfo.SubStakeInfos.First().StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(-500));
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(100000);
        stakeInfo.StakingPeriod.ShouldBe(100000);
        stakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100);
        stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance * 2);
    
        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100);
    
        SetBlockTime(1);
    
        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100 + 100_00000000 - 100_00000000 / 100);
    
        SetBlockTime(1);
    
        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100 + 100_00000000 * 2 - 100_00000000 * 2 / 100);
    
        SetBlockTime(1);
    
        reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeId }
        });
        reward.RewardInfos.First().Amount
            .ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100 + 100_00000000 * 3 - 100_00000000 * 3 / 100);
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

        var result = await UserEcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 1_00000000,
            Period = 10
        });
        var stakeId = GetLogEvent<Staked>(result.TransactionResult).StakeInfo.StakeId;

        var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        output.StakeInfo.StakingPeriod.ShouldBe(10);

        SetBlockTime(50000);

        await UserEcoEarnTokensContractStub.Renew.SendAsync(new RenewInput
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

        result = await UserEcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Not in unstake window.");

        SetBlockTime(500);

        await UserEcoEarnTokensContractStub.Unstake.SendAsync(poolId);

        result = await UserEcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Already unstaked.");

        SetBlockTime(100000);

        result = await UserEcoEarnTokensContractStub.Renew.SendWithExceptionAsync(new RenewInput
        {
            Period = 1,
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Pool closed.");
    }

    [Fact]
    public async Task UnstakeTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;
    
        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
    
        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);
        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance);

        var rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewardOutput.RewardInfos.First().Amount.ShouldBe(0);
    
        SetBlockTime(500);
        
        rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewardOutput.RewardInfos.Sum(r => r.Amount).ShouldBe(500 * 100_00000000 / 10000 * 9900);

        var claimIds = Claim(poolId).Result.Select(c => c.ClaimId).ToList();

        var stakeInput = new Rewards.StakeInput
        {
            ClaimIds = { claimIds },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = _appId,
            PoolId = poolId,
            Period = 100,
            LongestReleaseTime = BlockTimeProvider.GetBlockTime().Seconds
        };

        var input = new StakeRewardsInput
        {
            StakeInput = stakeInput,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, new StakeRewardsInput
            {
                StakeInput = stakeInput
            })
        };
        
        SetBlockTime(100);
        
        await UserEcoEarnRewardsContractStub.StakeRewards.SendAsync(input);
        
        SetBlockTime(600);
        
        var result = await UserEcoEarnTokensContractStub.Unstake.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
    
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(tokenBalance);
    
        var log = GetLogEvent<Unstaked>(result.TransactionResult);
        log.StakeInfo.StakeId.ShouldBe(stakeInfo.StakeId);
        log.StakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(0);
        log.PoolData.TotalStakedAmount.ShouldBe(0);
        
        rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewardOutput.RewardInfos.First().Amount.ShouldBe(0);
        
        SetBlockTime(1);
        
        rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewardOutput.RewardInfos.First().Amount.ShouldBe(0);
        
        stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.StakingPeriod.ShouldBe(500);
        stakeInfo.SubStakeInfos.First().Period.ShouldBe(500);
    }

    [Fact]
    public async Task UnstakeTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Unstake.SendWithExceptionAsync(new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnTokensContractStub.Unstake.SendWithExceptionAsync(HashHelper.ComputeFrom("test"));
        result.TransactionResult.Error.ShouldContain("Not staked before.");

        result = await EcoEarnTokensContractStub.Unstake.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not staked before.");

        result = await UserEcoEarnTokensContractStub.Unstake.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not in unstake window.");

        SetBlockTime(500);

        await UserEcoEarnTokensContractStub.Unstake.SendAsync(poolId);

        result = await UserEcoEarnTokensContractStub.Unstake.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Already unstaked.");
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
            var output = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(HashHelper.ComputeFrom("test"));
            output.StakeInfo.ShouldBeNull();
        }
    }
    
    [Fact]
    public async Task UnstakeTests_AfterStake()
    {
        const long tokenBalance = 5_00000000;
    
        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
    
        SetBlockTime(1500);
        
        stakeInfo = await Stake(poolId, tokenBalance);
        
        var rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewardOutput.RewardInfos.First().Amount.ShouldNotBe(0);
    
        SetBlockTime(700);
        
        rewardOutput = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        
        var result = await UserEcoEarnTokensContractStub.Unstake.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
    
        var log = GetLogEvent<Unstaked>(result.TransactionResult);
        log.StakeInfo.StakeId.ShouldBe(stakeInfo.StakeId);
        log.StakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(0);
        log.PoolData.TotalStakedAmount.ShouldBe(0);
        
        var log2 = GetLogEvent<Rewards.Claimed>(result.TransactionResult);
        var res = log2.ClaimInfos.Data.Select(c => c.ClaimedAmount).Sum();
        
        rewardOutput.RewardInfos.First().Amount.ShouldBe(res);
    }

    [Fact]
    public async Task StakeOnBehalfTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        await EcoEarnTokensContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                PaymentAddress = UserAddress
            }
        });

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
            Account = DefaultAddress,
            PoolId = poolId
        });
        userStakeId.ShouldBe(new Hash());

        await EcoEarnTokensContractStub.SetStakeOnBehalfPermission.SendAsync(new StakeOnBehalfPermission
        {
            DappId = _appId,
            Status = true
        });
        
        // create position
        {
            var result = await UserEcoEarnTokensContractStub.StakeOnBehalf.SendAsync(new StakeOnBehalfInput
            {
                PoolId = poolId,
                Amount = tokenBalance,
                Period = 86400,
                Account = DefaultAddress
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<StakedOnBehalf>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);
            log.PoolData.PoolId.ShouldBe(poolId);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            log.PoolData.AccTokenPerShare.ShouldBeNull();
            log.Payer.ShouldBe(UserAddress);

            var stakeInfo = log.StakeInfo;
            stakeInfo.PoolId.ShouldBe(poolId);
            stakeInfo.StakingPeriod.ShouldBe(86400);
            stakeInfo.Account.ShouldBe(DefaultAddress);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.LastOperationTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.UnstakeTime.ShouldBeNull();

            stakeInfo.SubStakeInfos.Count.ShouldBe(1);
            stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(0);
            stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.SubStakeInfos.First().RewardDebt.ShouldBe(0);
            stakeInfo.SubStakeInfos.First().StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.SubStakeInfos.First().StakedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

            userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
            {
                Account = DefaultAddress,
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
            reward.RewardInfos.First().Account.ShouldBe(DefaultAddress);
            reward.RewardInfos.First().StakeId.ShouldBe(stakeInfo.StakeId);
            reward.RewardInfos.First().Amount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000);
        }

        // add position
        {
            SetBlockTime(9);
            
            var result = await UserEcoEarnTokensContractStub.StakeOnBehalf.SendAsync(new StakeOnBehalfInput
            {
                PoolId = poolId,
                Amount = tokenBalance,
                Period = 86400,
                Account = DefaultAddress
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<StakedOnBehalf>(result.TransactionResult);
            log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2 + tokenBalance * 3);
            log.PoolData.LastRewardTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            
            var acc = new BigIntValue(100_00000000).Mul(10).Mul(1000000000000000000).Div(poolData.TotalStakedAmount);
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.StakingPeriod.ShouldBe(86400 * 2 - 10);
            stakeInfo.SubStakeInfos.Count.ShouldBe(2);
            stakeInfo.SubStakeInfos.First().Period.ShouldBe(172800);
            stakeInfo.SubStakeInfos.First().BoostedAmount.ShouldBe(tokenBalance * 3);
            stakeInfo.SubStakeInfos.First().StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.SubStakeInfos.First().RewardAmount
                .ShouldBe(100_00000000 * 10 - 100_00000000 * 10 / 100); // minus commission fee
            stakeInfo.SubStakeInfos.Last().Period.ShouldBe(172800 - 10);
            stakeInfo.SubStakeInfos.Last().BoostedAmount.ShouldBe(tokenBalance * 2);

            long.TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(3).Div(1000000000000000000)).Value, out var value);

            stakeInfo.SubStakeInfos.First().RewardDebt.ShouldBe(value);
            
            long.TryParse(new BigIntValue(acc.Mul(tokenBalance).Mul(2).Div(1000000000000000000)).Value, out value);
            
            stakeInfo.SubStakeInfos.Last().RewardDebt.ShouldBe(value);
            stakeInfo.LastOperationTime.ShouldBe(BlockTimeProvider.GetBlockTime());

            userStakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
            {
                Account = DefaultAddress,
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
            reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 10 - 100_00000000 * 10 * 100 / 10000);
        }
    }

    [Fact]
    public async Task StakeOnBehalfTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");
        
        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Permission not granted.");

        await EcoEarnTokensContractStub.SetStakeOnBehalfPermission.SendAsync(new StakeOnBehalfPermission
        {
            DappId = _appId,
            Status = true
        });
        
        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");
        
        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Payment address not set.");

        await EcoEarnTokensContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                PaymentAddress = DefaultAddress
            }
        });
        
        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");
        
        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Symbol = Symbol,
            Amount = 2_00000000,
            Spender = EcoEarnTokensContractAddress
        });
        
        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Amount not enough.");

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Period too short.");

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 500001
        });
        result.TransactionResult.Error.ShouldContain("Period too long.");

        await EcoEarnTokensContractStub.StakeOnBehalf.SendAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 86400
        });

        SetBlockTime(80000);

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 500000 - 6400 + 1
        });
        result.TransactionResult.Error.ShouldContain("Period too long.");

        SetBlockTime(6400);

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 86400
        });
        result.TransactionResult.Error.ShouldContain("Cannot stake during unstake window.");

        SetBlockTime(100000);

        result = await EcoEarnTokensContractStub.StakeOnBehalf.SendWithExceptionAsync(new StakeOnBehalfInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 1_00000000,
            Period = 86400
        });
        result.TransactionResult.Error.ShouldContain("Pool closed.");
    }

    [Fact]
    public async Task IsInUnstakeWindowTests()
    {
        const long tokenBalance = 5_00000000;

        var output = await EcoEarnTokensContractStub.IsInUnstakeWindow.CallAsync(new IsInUnstakeWindowInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = UserAddress
        });
        output.Value.ShouldBeFalse();

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);
        
        output = await EcoEarnTokensContractStub.IsInUnstakeWindow.CallAsync(new IsInUnstakeWindowInput
        {
            PoolId = poolId,
            Account = DefaultAddress
        });
        output.Value.ShouldBeFalse();
        
        output = await EcoEarnTokensContractStub.IsInUnstakeWindow.CallAsync(new IsInUnstakeWindowInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        output.Value.ShouldBeFalse();
        
        SetBlockTime(500);
        
        output = await EcoEarnTokensContractStub.IsInUnstakeWindow.CallAsync(new IsInUnstakeWindowInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        output.Value.ShouldBeTrue();
        
        SetBlockTime(100);
        
        output = await EcoEarnTokensContractStub.IsInUnstakeWindow.CallAsync(new IsInUnstakeWindowInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        output.Value.ShouldBeFalse();
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

        var result = await UserEcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = tokenBalance,
            Period = 500
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
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnstakeWindowDuration = 300,
            ReleasePeriods = { 10, 20, 30 },
            MinimumAddLiquidityAmount = 1,
            MinimumAdditionalStakeAmount = 1
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
    
    private async Task<List<ClaimInfo>> Claim(Hash poolId)
    {
        var result = await UserEcoEarnTokensContractStub.Claim.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        return GetLogEvent<Rewards.Claimed>(result.TransactionResult, 1).ClaimInfos.Data.ToList();
    }
    
    private ByteString GenerateSignature(byte[] privateKey, StakeRewardsInput input)
    {
        var data = new StakeRewardsInput
        {
            StakeInput = input.StakeInput
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
}