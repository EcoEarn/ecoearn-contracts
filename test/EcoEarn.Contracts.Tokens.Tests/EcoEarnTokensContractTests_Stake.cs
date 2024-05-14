using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Types;
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
        var stakeCount = await EcoEarnTokensContractStub.GetUserStakeCount.CallAsync(new GetUserStakeCountInput
        {
            Account = UserAddress,
            PoolId = poolId
        });
        stakeCount.Value.ShouldBe(0);
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
            log.PoolData.LastRewardBlock.ShouldBe(result.TransactionResult.BlockNumber);
            log.PoolData.AccTokenPerShare.ShouldBeNull();

            var stakeInfo = log.StakeInfo;
            stakeInfo.PoolId.ShouldBe(poolId);
            stakeInfo.Period.ShouldBe(86400);
            stakeInfo.Account.ShouldBe(UserAddress);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.ClaimedAmount.ShouldBe(0);
            stakeInfo.RewardAmount.ShouldBe(0);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance);
            stakeInfo.EarlyStakedAmount.ShouldBe(0);
            stakeInfo.RewardDebt.ShouldBe(0);
            stakeInfo.LockedRewardAmount.ShouldBe(0);
            stakeInfo.StakedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
            stakeInfo.StakedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime);
            stakeInfo.WithdrawTime.ShouldBeNull();
            stakeInfo.StakeId.ShouldBe(HashHelper.ConcatAndCompute(
                HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(stakeCount.Value),
                    HashHelper.ComputeFrom(UserAddress)), poolId));

            stakeCount = await EcoEarnTokensContractStub.GetUserStakeCount.CallAsync(new GetUserStakeCountInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            stakeCount.Value.ShouldBe(1);
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
            
            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
            reward.Symbol.ShouldBe(Symbol);
            reward.Account.ShouldBe(UserAddress);
            reward.StakeId.ShouldBe(stakeInfo.StakeId);
            reward.Amount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000);
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
            log.PoolData.LastRewardBlock.ShouldBe(result.TransactionResult.BlockNumber);

            var acc = 100_00000000 * 10000 / (poolData.TotalStakedAmount);
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.Period.ShouldBe(86400);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 4);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.RewardAmount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000); // minus commission fee
            stakeInfo.RewardDebt.ShouldBe(acc * tokenBalance * 4 / 10000);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime);

            stakeCount = await EcoEarnTokensContractStub.GetUserStakeCount.CallAsync(new GetUserStakeCountInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            stakeCount.Value.ShouldBe(1);
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

            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
            reward.Amount.ShouldBe(100_00000000 * 2 - 100_00000000 * 100 / 10000 * 2);
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
            log.PoolData.LastRewardBlock.ShouldBe(result.TransactionResult.BlockNumber);
            
            var acc = 100_00000000 * 10000 / (tokenBalance * 4) + 100_00000000 * 10000 / (tokenBalance * 2);
            log.PoolData.AccTokenPerShare.ShouldBe(acc);

            var stakeInfo = log.StakeInfo;
            stakeInfo.Period.ShouldBe(86400 + 172800);
            stakeInfo.StakingToken.ShouldBe(Symbol);
            stakeInfo.BoostedAmount.ShouldBe(tokenBalance * 8);
            stakeInfo.StakedAmount.ShouldBe(tokenBalance * 2);
            stakeInfo.RewardAmount.ShouldBe((100_00000000 - 100_00000000 * 100 / 10000) * 2); // minus commission fee
            stakeInfo.RewardDebt.ShouldBe(acc * tokenBalance * 8 / 10000);
            stakeInfo.LastOperationTime.ShouldBe(stakeInfo.StakedTime);

            stakeCount = await EcoEarnTokensContractStub.GetUserStakeCount.CallAsync(new GetUserStakeCountInput
            {
                Account = UserAddress,
                PoolId = poolId
            });
            stakeCount.Value.ShouldBe(1);
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

            var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
            reward.Amount.ShouldBe((100_00000000 - 100_00000000 * 100 / 10000) * 3);
        }
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
        result.TransactionResult.Error.ShouldContain("Amount not enough.");
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

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(86400));
        
        var result = await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(tokenBalance);

        var log = GetLogEvent<Unlocked>(result.TransactionResult);
        log.StakeId.ShouldBe(stakeInfo.StakeId);
        log.StakedAmount.ShouldBe(0);
        log.PoolData.ShouldBe(poolData);
        
        stakeInfo = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        stakeInfo.StakedAmount.ShouldBe(0);
        stakeInfo.ClaimedAmount.ShouldBe(100_00000000 - 100_00000000 * 100 / 10000);
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
        result.TransactionResult.Error.ShouldContain("No unlock yet.");
        
        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(86400));

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);
        
        result = await EcoEarnTokensContractUserStub.Unlock.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Already withdrawn.");
    }

    [Fact]
    public async Task EarlyStakeTests()
    {
        const long tokenBalance = 5_00000000;
        
        var poolId = await CreateTokensPool();
        var (stakeInfo, claimInfo) = await Claim(poolId, tokenBalance);
        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);
        stakeInfo.StakedAmount.ShouldBe(tokenBalance);
        stakeInfo.EarlyStakedAmount.ShouldBe(0);
        claimInfo.EarlyStakeTime.ShouldBeNull();

        var result = await EcoEarnTokensContractUserStub.EarlyStake.SendAsync(new EarlyStakeInput
        {
            PoolId = poolId,
            Period = 0,
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<EarlyStaked>(result.TransactionResult);
        log.StakeInfo.StakedAmount.ShouldBe(tokenBalance);
        log.StakeInfo.EarlyStakedAmount.ShouldBe(claimInfo.ClaimedAmount);
        log.ClaimInfos.Data.First().EarlyStakeTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        log.PoolData.TotalStakedAmount.ShouldBe(tokenBalance * 2 + claimInfo.ClaimedAmount * 2);
    }

    [Fact]
    public async Task EarlyStakeTests_Fail()
    {
        const long tokenBalance = 5_00000000;
        
        var poolId = await CreateTokensPool();
        var (_, claimInfo) = await Claim(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput());
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");
        
        result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");
        
        result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { new Hash() },
            PoolId = poolId,
            Period = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid period.");
        
        var closedPoolId = await CreateTokensPool();
        await EcoEarnTokensContractStub.CloseTokensPool.SendAsync(closedPoolId);
        
        result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { new Hash() },
            PoolId = closedPoolId,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Pool closed.");
        
        var poolIdWithLowReward = await CreateTokensPoolWithLowRewardPerBlock();
        var (_, lessClaimInfo) = await Claim(poolIdWithLowReward, tokenBalance);
        
        result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { new Hash() },
            PoolId = poolIdWithLowReward,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");
        
        result = await EcoEarnTokensContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { lessClaimInfo.ClaimId },
            PoolId = poolIdWithLowReward,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        result = await EcoEarnTokensContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { lessClaimInfo.ClaimId },
            PoolId = poolIdWithLowReward,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Amount not enough.");
        
        var poolIdWithDifferentStakingSymbol = await CreateTokensPool(DefaultSymbol);
        result = await EcoEarnTokensContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            ClaimIds = { claimInfo.ClaimId },
            PoolId = poolIdWithDifferentStakingSymbol,
            Period = 0
        });
        result.TransactionResult.Error.ShouldContain("Token not matched.");
        
        poolId = await CreateTokensPool();
        await EcoEarnTokensContractUserStub.EarlyStake.SendAsync(new EarlyStakeInput
        {
            PoolId = poolId,
            ClaimIds = { claimInfo.ClaimId },
            Period = 86400
        });
        
        result = await EcoEarnTokensContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = poolId,
            ClaimIds = { claimInfo.ClaimId },
        });
        result.TransactionResult.Error.ShouldContain("Not unlocked.");
        
        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(86400));

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);
        await EcoEarnTokensContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        
        result = await EcoEarnTokensContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = poolId,
            ClaimIds = { claimInfo.ClaimId },
        });
        result.TransactionResult.Error.ShouldContain("Already withdrawn.");
    }
    
    [Fact]
    public async Task UpdateStakeInfoTests()
    {
        const long tokenBalance = 5_00000000;
        
        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        
        var poolId2 = await CreateTokensPool();
        var stakeInfo2 = await Stake(poolId2, tokenBalance);

        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId);
        var poolData2 = await EcoEarnTokensContractStub.GetPoolData.CallAsync(poolId2);
        poolData.TotalStakedAmount.ShouldBe(tokenBalance * 2);
        poolData2.TotalStakedAmount.ShouldBe(tokenBalance * 2);

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        var reward2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        
        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(86400));

        var input = new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId, stakeInfo2.StakeId }
        };
        var result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<StakeInfoUpdated>(result.TransactionResult);
        log.StakeIds.Data.ShouldBe(input.StakeIds);
        log.PoolDatas.Data.First().TotalStakedAmount.ShouldBe(0);
        log.PoolDatas.Data.Last().TotalStakedAmount.ShouldBe(0);
        
        var newReward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        var newReward2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        reward.ShouldBe(newReward);
        reward2.ShouldBe(newReward2);
    }

    [Fact]
    public async Task UpdateStakeInfoTests_Fail()
    {
        const long tokenBalance = 5_00000000;
        
        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput());
        result.TransactionResult.Error.ShouldContain("Invalid stake ids.");
        
        result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput
        {
            StakeIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Invalid stake id.");
        
        result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput
        {
            StakeIds = { HashHelper.ComputeFrom("test") }
        });
        result.TransactionResult.Error.ShouldContain("Stake id not exists.");
        
        result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        result.TransactionResult.Error.ShouldContain("Not unlock yet.");
        
        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(86400));
        
        result = await EcoEarnTokensContractUserStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        await EcoEarnTokensContractStub.UpdateStakeInfo.SendAsync(new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        
        result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendWithExceptionAsync(new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        result.TransactionResult.Error.ShouldContain("Already updated.");
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

    private async Task<(StakeInfo, ClaimInfo)> Claim(Hash poolId, long tokenBalance)
    {
        var stakeInfo = await Stake(poolId, tokenBalance);
        var result = await EcoEarnTokensContractUserStub.Claim.SendAsync(stakeInfo.StakeId);
        return (stakeInfo, GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo);
    }
}