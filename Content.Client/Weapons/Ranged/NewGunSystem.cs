using Content.Shared.Weapons.Ranged;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client.Weapons.Ranged;

public sealed class NewGunSystem : SharedNewGunSystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;

    // TODO: Move to ballistic partial
    public override void ManualCycle(BallisticAmmoProviderComponent component, MapCoordinates coordinates)
    {
        return;
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
    }

    public override void Update(float frameTime)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalPlayer?.ControlledEntity;

        if (entityNull == null)
        {
            return;
        }

        var entity = entityNull.Value;
        var gun = GetGun(entity);

        if (gun == null)
        {
            return;
        }

        if (_inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) != BoundKeyState.Down)
        {
            if (gun.ShotCounter != 0)
                EntityManager.RaisePredictiveEvent(new RequestStopShootEvent() { Gun = gun.Owner });
            return;
        }

        if (gun.NextFire > Timing.CurTime)
            return;

        var mousePos = _eyeManager.ScreenToMap(_inputManager.MouseScreenPosition);
        EntityCoordinates coordinates;

        if (MapManager.TryFindGridAt(mousePos, out var grid))
        {
            coordinates = EntityCoordinates.FromMap(grid.GridEntityId, mousePos, EntityManager);
        }
        else
        {
            coordinates = EntityCoordinates.FromMap(MapManager.GetMapEntityId(mousePos.MapId), mousePos, EntityManager);
        }

        Sawmill.Debug($"Sending shoot request tick {Timing.CurTick} / {Timing.CurTime}");

        EntityManager.RaisePredictiveEvent(new RequestShootEvent()
        {
            Coordinates = coordinates,
            Gun = gun.Owner,
        });
    }

    public override void Shoot(List<IShootable> ammo, EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, EntityUid? user = null)
    {
        // Rather than splitting client / server for every ammo provider it's easier
        // to just delete the spawned entities. This is for programmer sanity despite the wasted perf.
        // This also means any ammo specific stuff can be grabbed as necessary.
        foreach (var ent in ammo)
        {
            if (ent is NewAmmoComponent entAmmo)
                Del(entAmmo.Owner);

        }
    }

    protected override void PlaySound(NewGunComponent gun, string? sound, EntityUid? user = null)
    {
        if (sound == null || user == null || !Timing.IsFirstTimePredicted) return;
        SoundSystem.Play(Filter.Local(), sound, gun.Owner);
    }

    protected override void Popup(string message, NewGunComponent gun, EntityUid? user)
    {
        if (user == null) return;
        PopupSystem.PopupEntity(message, gun.Owner, Filter.Entities(user.Value));
    }
}
