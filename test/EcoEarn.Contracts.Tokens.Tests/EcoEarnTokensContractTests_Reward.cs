using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests
{
    [Fact]
    public async Task ClaimTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.RewardAmount.ShouldBe(0);
        stakeInfo.LockedRewardAmount.ShouldBe(0);

        SetBlockTime(1);
        
        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);

        var addressInfo = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, addressInfo.RewardAddress);

        var result = await EcoEarnTokensContractUserStub.Claim.SendAsync(stakeInfo.StakeId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult);
        log.StakeId.ShouldBe(stakeInfo.StakeId);
        log.ClaimInfo.ClaimedSymbol.ShouldBe(Symbol);
        log.ClaimInfo.ClaimedAmount.ShouldBe(reward.Amount);
        log.ClaimInfo.ClaimedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        log.ClaimInfo.PoolId.ShouldBe(poolId);
        log.ClaimInfo.Account.ShouldBe(UserAddress);
        log.ClaimInfo.UnlockTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(10));
        log.ClaimInfo.WithdrawTime.ShouldBeNull();
        log.ClaimInfo.ClaimedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);
        log.ClaimInfo.EarlyStakeTime.ShouldBeNull();

        var output = await EcoEarnTokensContractStub.GetClaimInfo.CallAsync(log.ClaimInfo.ClaimId);
        output.ShouldBe(log.ClaimInfo);

        var stakeOutput = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        stakeOutput.StakeInfo.RewardAmount.ShouldBe(0);
        stakeOutput.StakeInfo.LockedRewardAmount.ShouldBe(reward.Amount);

        SetBlockTime(1);
        
        var newReward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeOutput.StakeInfo.StakeId);
        newReward.ShouldBe(reward);

        var balance2 = await GetTokenBalance(Symbol, addressInfo.RewardAddress);
        balance2.ShouldBe(balance - reward.Amount - 1_00000000);
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(HashHelper.ComputeFrom("test"));
        result.TransactionResult.Error.ShouldContain("Stake info not exists.");

        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(stakeInfo.StakeId);
        result.TransactionResult.Error.ShouldContain("No permission.");

        SetBlockTime(86400);

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId);

        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(stakeInfo.StakeId);
        result.TransactionResult.Error.ShouldContain("Already unlocked.");

        poolId = await CreateTokensPoolWithHighLimitation();
        stakeInfo = await Stake(poolId, tokenBalance);

        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(stakeInfo.StakeId);
        result.TransactionResult.Error.ShouldContain("Reward not enough.");

        poolId = await CreateTokensPoolAwayFromStart();
        stakeInfo = await Stake(poolId, tokenBalance);

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        reward.Amount.ShouldBe(0);

        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(stakeInfo.StakeId);
        result.TransactionResult.Error.ShouldContain("Reward not enough.");
    }

    [Fact]
    public async Task WithdrawTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var (stakeInfo, claimInfo) = await Claim(poolId, tokenBalance);

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        SetBlockTime(86400);

        var result = await EcoEarnTokensContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Withdrawn>(result.TransactionResult);
        log.ClaimInfos.Data.Count.ShouldBe(1);

        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(99_00000000);
    }

    [Fact]
    public async Task WithdrawTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var (stakeInfo, claimInfo) = await Claim(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput());
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await EcoEarnTokensContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { }
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await EcoEarnTokensContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");

        result = await EcoEarnTokensContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { HashHelper.ComputeFrom("test") }
        });
        result.TransactionResult.Error.ShouldContain("Claim id not exists.");

        result = await EcoEarnTokensContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnTokensContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Error.ShouldContain("Not unlock yet.");

        SetBlockTime(86400);

        var poolId2 = await CreateTokensPool();

        await EcoEarnTokensContractUserStub.EarlyStake.SendAsync(new EarlyStakeInput
        {
            PoolId = poolId2,
            ClaimIds = { claimInfo.ClaimId },
            Period = 86400
        });

        result = await EcoEarnTokensContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Error.ShouldContain("Not unlocked.");

        SetBlockTime(86400);
        await EcoEarnTokensContractUserStub.Unlock.SendAsync(poolId2);

        await EcoEarnTokensContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });

        result = await EcoEarnTokensContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        result.TransactionResult.Error.ShouldContain("Already withdrawn.");
    }

    [Fact]
    public async Task RecoverTokenTests()
    {
        var poolId = await CreateTokensPool();

        var address = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(10000000_00000000);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);
        
        SetBlockTime(86401);

        var result = await EcoEarnTokensContractStub.RecoverToken.SendAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Recipient = UserAddress,
            Token = Symbol
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokenRecovered>(result.TransactionResult);
        log.Amount.ShouldBe(10000000_00000000);
        log.PoolId.ShouldBe(poolId);
        log.Account.ShouldBe(UserAddress);
        log.Token.ShouldBe(Symbol);

        balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(0);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(10000000_00000000);
    }

    [Fact]
    public async Task RecoverTokenTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1)
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1),
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not closed.");
        
        SetBlockTime(86401);
        
        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "ELF"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        result = await EcoEarnTokensContractUserStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task<Hash> CreateTokensPoolWithHighLimitation()
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
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 10000,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1000_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }

    private async Task<Hash> CreateTokensPoolAwayFromStart()
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await Register();
            await CreateToken();
        }

        var blockTime = BlockTimeProvider.GetBlockTime().AddSeconds(1000).Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 10000,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }
}