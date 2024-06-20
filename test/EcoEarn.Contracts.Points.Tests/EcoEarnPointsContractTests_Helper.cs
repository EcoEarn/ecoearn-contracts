using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf;
using Shouldly;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests
{
    private T GetLogEvent<T>(TransactionResult transactionResult) where T : IEvent<T>, new()
    {
        var log = transactionResult.Logs.FirstOrDefault(l => l.Name == typeof(T).Name);
        log.ShouldNotBeNull();

        var logEvent = new T();
        logEvent.MergeFrom(log.NonIndexed);

        foreach (var indexed in log.Indexed)
        {
            logEvent.MergeFrom(indexed);
        }

        return logEvent;
    }

    private async Task<long> GetTokenBalance(string token, Address address)
    {
        var output = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Symbol = token,
            Owner = address
        });

        return output.Balance;
    }

    private void SetBlockTime(long seconds)
    {
        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(seconds));
    }
}