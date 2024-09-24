using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf;
using Shouldly;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests
{
    private T GetLogEvent<T>(TransactionResult transactionResult) where T : IEvent<T>, new()
    {
        var log = transactionResult.Logs.FirstOrDefault(l => l.Name == typeof(T).Name);
        log.ShouldNotBeNull();

        var logEvent = new T();
        logEvent.MergeFrom(log.NonIndexed);

        return logEvent;
    }
    
    private T GetLogEvent<T>(TransactionResult transactionResult, int index) where T : IEvent<T>, new()
    {
        var logs = transactionResult.Logs.Where(l => l.Name == typeof(T).Name).ToList();
        logs.Count.ShouldNotBe(0);
        
        var logEvent = new T();
        logEvent.MergeFrom(logs[index].NonIndexed);

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