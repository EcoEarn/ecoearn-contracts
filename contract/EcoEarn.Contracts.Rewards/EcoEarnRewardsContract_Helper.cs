using AElf;
using AElf.Types;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    private void CheckAdminPermission()
    {
        Assert(Context.Sender == State.Admin.Value, "No permission.");
    }

    private void CheckInitialized()
    {
        Assert(State.Initialized.Value, "Not initialized.");
    }

    private bool IsStringValid(string input)
    {
        return !string.IsNullOrWhiteSpace(input);
    }

    private bool IsAddressValid(Address input)
    {
        return input != null && !input.Value.IsNullOrEmpty();
    }

    private bool IsHashValid(Hash input)
    {
        return input != null && !input.Value.IsNullOrEmpty();
    }

    private DappInfo GetAndCheckDAppAdminPermission(Hash id)
    {
        var dappInfo = State.DappInfoMap[id];
        Assert(dappInfo != null && dappInfo.Admin == Context.Sender, "No permission.");

        return dappInfo;
    }
}