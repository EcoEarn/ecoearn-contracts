using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Types;
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
        stakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(0);

        SetBlockTime(500);

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(100_00000000 * 500 - 100_00000000 * 500 / 100);

        var addressInfo = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, addressInfo.RewardAddress);

        var result = await EcoEarnTokensContractUserStub.Claim.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Account.ShouldBe(UserAddress);
        log.Amount.ShouldBe(reward.RewardInfos.First().Amount);

        var stakeOutput = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        stakeOutput.StakeInfo.SubStakeInfos.First().RewardAmount.ShouldBe(0);

        SetBlockTime(500);

        var newReward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeOutput.StakeInfo.StakeId }
        });
        newReward.RewardInfos.First().Amount.ShouldBe(reward.RewardInfos.First().Amount);

        var balance2 = await GetTokenBalance(Symbol, addressInfo.RewardAddress);
        balance2.ShouldBe(balance - 100_00000000 * 500);
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        var result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not staked before.");

        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Not in unlock window.");

        SetBlockTime(500);
        await EcoEarnTokensContractUserStub.Claim.SendAsync(poolId);
        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Already claimed during this window.");

        SetBlockTime(-501);

        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Pool not start.");
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

        SetBlockTime(100000);

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

        SetBlockTime(100000);

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = DefaultSymbol
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
}