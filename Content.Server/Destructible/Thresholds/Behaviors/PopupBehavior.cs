using Content.Shared.Popups;

﻿namespace Content.Server.Destructible.Thresholds.Behaviors;

/// <summary>
/// Shows a popup for everyone.
/// </summary>
[DataDefinition]
public sealed partial class PopupBehavior : IThresholdBehavior
{
    /// <summary>
    /// Locale id of the popup message.
    /// </summary>
    [DataField("popup", required: true)]
    public string Popup;

    /// <summary>
    /// Type of popup to show.
    /// </summary>
    [DataField("popupType")]
    public PopupType PopupType;

    public void Execute(EntityUid uid, DestructibleSystem system, EntityUid? cause = null)
    {
        var popup = system.EntityManager.System<SharedPopupSystem>();
        popup.PopupEntity(Loc.GetString(Popup), uid, PopupType);
    }
}
