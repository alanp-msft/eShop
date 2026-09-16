namespace eShop.Ordering.API.Application.Commands;

/// <summary>
/// Outcome of <see cref="CancelOrderCommandHandler"/>. <see cref="Unknown"/> is the first (default) member
/// so <c>default(CancelOrderResult)</c> is never a legitimate handler outcome: it is distinct from
/// <see cref="NotFound"/> and every other named result, which lets the API layer tell a genuine "not found"
/// apart from the value <see cref="IdentifiedCommandHandler{T, R}.Handle"/>'s catch-all produces when it
/// swallows an unexpected exception.
/// </summary>
public enum CancelOrderResult
{
    Unknown = 0,
    NotFound,
    Forbidden,
    AlreadyCancelled,
    IneligibleStatus,
    Success
}
