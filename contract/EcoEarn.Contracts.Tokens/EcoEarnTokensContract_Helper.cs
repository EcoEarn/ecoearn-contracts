using AElf;
using AElf.Types;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract
{
    private void CheckAdminPermission()
    {
        Assert(Context.Sender == State.Admin.Value, "No permission.");
    }

    private void CheckEcoEarnPointsPermission()
    {
        Assert(Context.Sender == State.EcoEarnPointsContract.Value, "No permission.");
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

    private void CheckDAppAdminPermission(Hash id)
    {
        var info = State.DappInfoMap[id];
        Assert(info != null && info.Admin == Context.Sender, "No permission.");
    }

    private bool CheckPoolEnabled(long endBlockNumber)
    {
        return Context.CurrentHeight < endBlockNumber;
    }
}