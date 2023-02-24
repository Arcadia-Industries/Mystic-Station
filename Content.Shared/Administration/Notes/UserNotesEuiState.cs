using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration.Notes;

[Serializable, NetSerializable]
public sealed class UserNotesEuiState : EuiStateBase
{
    public UserNotesEuiState(Dictionary<int, SharedAdminNote> notes)
    {
        Notes = notes;
    }
    public Dictionary<int, SharedAdminNote> Notes { get; }
}

public static class UserNotesEuiMsg
{
    [Serializable, NetSerializable]
    public sealed class Close : EuiMessageBase
    {
    }
}
