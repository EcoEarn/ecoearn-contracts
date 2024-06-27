using System.Threading.Tasks;
using AElf;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests
{
    [Fact]
    public async Task AddLiquidityAndStakeTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        SetBlockTime(10);

        var claimIds = await Claim(poolId);

        var stakeInput = new StakeInput
        {
            ClaimIds = { claimIds },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = _appId,
            PoolId = poolId,
            Period = 100
        };

        var input = new AddLiquidityAndStakeInput
        {
            StakeInput = stakeInput,
            TokenAMin = 10,
            TokenBMin = 10,
            Deadline = BlockTimeProvider.GetBlockTime().AddDays(1),
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, new AddLiquidityAndStakeInput
            {
                StakeInput = stakeInput,
                TokenAMin = 10,
                TokenBMin = 10
            })
        };

        // var result = await UserEcoEarnRewardsContractStub.AddLiquidityAndStake.SendAsync(input);
        // result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
    }
    
    private ByteString GenerateSignature(byte[] privateKey, AddLiquidityAndStakeInput input)
    {
        var data = new AddLiquidityAndStakeInput
        {
            StakeInput = input.StakeInput,
            TokenAMin = input.TokenAMin,
            TokenBMin = input.TokenBMin
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
}