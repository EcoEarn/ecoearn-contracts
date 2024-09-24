using System.Threading.Tasks;
using AElf;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests
{
    [Fact]
    public async Task UpdateSnapshotTests()
    {
        var root = HashHelper.ComputeFrom(1);

        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.UpdateSnapshot.SendAsync(new UpdateSnapshotInput
        {
            PoolId = poolId,
            MerkleTreeRoot = root
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<SnapshotUpdated>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.MerkleTreeRoot.ShouldBe(root);
        log.UpdateBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

        var output = await EcoEarnPointsContractStub.GetSnapshot.CallAsync(new GetSnapshotInput
        {
            PoolId = poolId,
            BlockNumber = log.UpdateBlockNumber
        });
        output.PoolId.ShouldBe(poolId);
        output.BlockNumber.ShouldBe(log.UpdateBlockNumber);
        output.MerkleTreeRoot.ShouldBe(root);

        output = await EcoEarnPointsContractStub.GetSnapshot.CallAsync(new GetSnapshotInput
        {
            PoolId = poolId,
            BlockNumber = -1
        });
        output.PoolId.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSnapshotTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid merkle tree root.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid merkle tree root.");

        result = await UserEcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("No permission.");

        SetBlockTime(100);

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool disabled.");
    }

    [Fact]
    public async Task ClaimTests()
    {
        var seed = HashHelper.ComputeFrom(1);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        SetBlockTime(1);

        var input = new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 10,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 10, UserAddress, seed,
                expirationTime)
        };

        var result = await UserEcoEarnPointsContractStub.Claim.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Account.ShouldBe(UserAddress);
        log.Amount.ShouldBe(9);
        log.Seed.ShouldBe(seed);
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        var seed = HashHelper.ComputeFrom(1);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput());
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed
        });
        result.TransactionResult.Error.ShouldContain("Invalid expiration time.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds,
            Signature = ByteString.Empty
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = new Hash(),
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = HashHelper.ComputeFrom(1),
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = poolId,
            ExpirationTime = BlockTimeProvider.GetBlockTime().Seconds
        });
        result.TransactionResult.Error.ShouldContain("Signature expired.");

        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;
        SetBlockTime(1);

        result = await UserEcoEarnPointsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 100,
            Seed = seed,
            PoolId = poolId,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed,
                expirationTime)
        });
        result.TransactionResult.Error.ShouldContain("Amount too much.");
    }

    [Fact]
    public async Task RecoverTokenTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var address = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, address);
        balance.ShouldBe(1000);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        SetBlockTime(100);

        var result = await EcoEarnPointsContractStub.RecoverToken.SendAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Recipient = UserAddress,
            Token = Symbol
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokenRecovered>(result.TransactionResult);
        log.Amount.ShouldBe(1000);
        log.PoolId.ShouldBe(poolId);
        log.Account.ShouldBe(UserAddress);
        log.Token.ShouldBe(Symbol);

        balance = await GetTokenBalance(Symbol, address);
        balance.ShouldBe(0);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(1000);
    }

    [Fact]
    public async Task RecoverTokenTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1)
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1),
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not closed.");

        SetBlockTime(100);

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = DefaultSymbol
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        result = await UserEcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        await EcoEarnPointsContractStub.RecoverToken.SendAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol
        });

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");
    }

    private ByteString GenerateSignature(byte[] privateKey, Hash poolId, long amount, Address account, Hash seed,
        long expirationTime)
    {
        var data = new ClaimInput
        {
            PoolId = poolId,
            Account = account,
            Amount = amount,
            Seed = seed,
            ExpirationTime = expirationTime
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
}