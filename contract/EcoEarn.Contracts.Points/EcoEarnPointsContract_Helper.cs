using AElf;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContract
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

    private bool CheckPoolEnabled(Timestamp endTime)
    {
        return Context.CurrentBlockTime < endTime;
    }
    
    private Address GetUpdateAddress(Hash dappId)
    {
        var dappInfo = State.DappInfoMap[dappId];
        return dappInfo.Config?.UpdateAddress == null ? State.Config.Value.DefaultUpdateAddress : dappInfo.Config.UpdateAddress;
    }
}