using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Kernel.Blockchain;
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

    private Task<BlockExecutedSet> SimulateBlockMining()
    {
        return Task.FromResult(ContractTestService.MineAsync(new List<Transaction>()).Result);
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
}