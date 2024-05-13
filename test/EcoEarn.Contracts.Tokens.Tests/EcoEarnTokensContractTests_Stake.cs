using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
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
}