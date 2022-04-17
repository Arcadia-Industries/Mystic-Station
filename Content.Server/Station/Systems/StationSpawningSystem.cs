﻿using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.CharacterAppearance.Systems;
using Content.Server.Hands.Components;
using Content.Server.Hands.Systems;
using Content.Server.PDA;
using Content.Server.Roles;
using Content.Server.Station.Components;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Species;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Station.Systems;

/// <summary>
/// Manages spawning into the game, tracking available spawn points.
/// Also provides helpers for spawning in the player's mob.
/// </summary>
[PublicAPI]
public sealed partial class StationSpawningSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearanceSystem = default!;
    [Dependency] private readonly IdCardSystem _cardSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly PDASystem _pdaSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<StationInitializedEvent>(OnStationInitialized);
        SubscribeLocalEvent<StationSpawningComponent, StationGridAddedEvent>(OnStationGridAdded);
        SubscribeLocalEvent<StationSpawningComponent, StationGridRemovedEvent>(OnStationGridRemoved);
        SubscribeLocalEvent<StationSpawnerManagerComponent, EntParentChangedMessage>(OnSpawnerParentChanged);
        SubscribeLocalEvent<StationSpawnerManagerComponent, ComponentShutdown>(OnSpawnerShutdown);
    }

    #region Spawner management

    private void OnStationInitialized(StationInitializedEvent ev)
    {
        AddComp<StationSpawningComponent>(ev.Station);
    }

    private void OnStationGridAdded(EntityUid uid, StationSpawningComponent component,  StationGridAddedEvent ev)
    {
        var spawnerEntities = new HashSet<EntityUid>(128); // Somewhat arbitrary magic number.
        foreach (var (manager, xform) in EntityQuery<StationSpawnerManagerComponent, TransformComponent>(true))
        {
            if (!(xform.GridID == ev.GridId))
                continue;
            spawnerEntities.Add(manager.Owner);
            manager.PreviousGrid = ev.GridId;
            manager.PreviousStation = uid;
        }
        component.Spawners.UnionWith(spawnerEntities);
        component.SpawnersByGrid[ev.GridId] = spawnerEntities;
    }

    private void OnStationGridRemoved(EntityUid uid, StationSpawningComponent component, StationGridRemovedEvent args)
    {
        if (component.SpawnersByGrid.ContainsKey(args.GridId))
        {
            component.Spawners.ExceptWith(component.SpawnersByGrid[args.GridId]);

            var query = EntityManager.GetEntityQuery<StationSpawnerManagerComponent>();

            foreach (var spawner in component.SpawnersByGrid[args.GridId])
            {
                if (!query.TryGetComponent(spawner, out var spawnerManager))
                    continue;

                spawnerManager.PreviousGrid = null;
                spawnerManager.PreviousStation = null;
            }
        }

        component.SpawnersByGrid.Remove(args.GridId);
    }

    private void OnSpawnerShutdown(EntityUid uid, StationSpawnerManagerComponent component, ComponentShutdown args)
    {
        if (component.PreviousGrid != null && component.PreviousStation != null && Exists(component.PreviousStation))
        {
            RemoveSpawnerFromStation(uid, component.PreviousStation.Value, component.PreviousGrid.Value);
        }
    }

    private void OnSpawnerParentChanged(EntityUid uid, StationSpawnerManagerComponent component, ref EntParentChangedMessage args)
    {
        if (component.PreviousGrid != null && component.PreviousStation != null && Exists(component.PreviousStation))
        {
            RemoveSpawnerFromStation(uid, component.PreviousStation.Value, component.PreviousGrid.Value);
        }

        var grid = args.Transform.GridID;
        if (grid == GridId.Invalid)
            return;

        if (!TryComp<StationMemberComponent>(_mapManager.GetGridEuid(grid), out var stationMember))
            return; // Not part of a station.

        AddSpawnerToStation(uid, stationMember.Station, args.Transform);
    }

    private void AddSpawnerToStation(EntityUid spawner, EntityUid station,
        TransformComponent? spawnerTransform = null, StationSpawningComponent? stationSpawning = null)
    {
        if (!Resolve(spawner, ref spawnerTransform))
            throw new ArgumentException("Given spawner does not have a transform!", nameof(spawner));
        if (!Resolve(station, ref stationSpawning))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        DebugTools.Assert(!stationSpawning.Spawners.Contains(spawner));

        var grid = spawnerTransform.GridID;

        stationSpawning.Spawners.Add(spawner);

        if (!stationSpawning.SpawnersByGrid.ContainsKey(grid))
            stationSpawning.SpawnersByGrid[grid] = new HashSet<EntityUid> {spawner};
        else
            stationSpawning.SpawnersByGrid[grid].Add(spawner);
    }

    private void RemoveSpawnerFromStation(EntityUid spawner, EntityUid station, GridId previousGrid,
        StationSpawningComponent? stationSpawning = null)
    {
        if (!Resolve(station, ref stationSpawning))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        // We may be getting destructed after the grid is already removed.
        if (stationSpawning.SpawnersByGrid.ContainsKey(previousGrid))
            return;

        stationSpawning.Spawners.Remove(spawner);

        stationSpawning.SpawnersByGrid[previousGrid].Remove(spawner);
        if (stationSpawning.SpawnersByGrid[previousGrid].Count == 0)
            stationSpawning.SpawnersByGrid.Remove(previousGrid);
    }

    #endregion

    /// <summary>
    /// Attempts to spawn a player character onto the given station.
    /// </summary>
    /// <param name="station">Station to spawn onto.</param>
    /// <param name="job">The job to assign, if any.</param>
    /// <param name="profile">The character profile to use, if any.</param>
    /// <param name="stationSpawning">Resolve pattern, the station spawning component for the station.</param>
    /// <returns>The resulting player character, if any.</returns>
    /// <exception cref="ArgumentException">Thrown when the given station is not a station.</exception>
    public EntityUid? SpawnPlayerCharacterOnStation(EntityUid station, Job? job, HumanoidCharacterProfile? profile, StationSpawningComponent? stationSpawning = null)
    {
        if (!Resolve(station, ref stationSpawning))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        // TODO: Cache this list.
        var options = stationSpawning.Spawners.ToList();
        if (options.Count == 0)
            return null;

        var startAt = _random.Next(0, options.Count);
        var curr = startAt;
        var ev = new SpawnPlayerEvent(job, profile);
        do
        {
            RaiseLocalEvent(options[curr], ref ev);
            if (ev.SpawnResult != null)
                break;

            curr = (curr + 1) % options.Count;
        } while (curr != startAt);

        return ev.SpawnResult;
    }

    //TODO: Figure out if everything in the player spawning region belongs somewhere else.
    #region Player spawning helpers

    /// <summary>
    /// Spawns in a player's mob according to their job and character information at the given coordinates.
    /// Used by systems that need to handle spawning players.
    /// </summary>
    /// <param name="coordinates">Coordinates to spawn the character at.</param>
    /// <param name="job">Job to assign to the character, if any.</param>
    /// <param name="profile">Appearance profile to use for the character.</param>
    /// <returns>The spawned entity</returns>
    public EntityUid SpawnPlayerMob(EntityCoordinates coordinates, Job? job, HumanoidCharacterProfile? profile)
    {
        var entity = EntityManager.SpawnEntity(
            _prototypeManager.Index<SpeciesPrototype>(profile?.Species ?? SpeciesManager.DefaultSpecies).Prototype,
            coordinates);

        if (job?.StartingGear != null)
        {
            var startingGear = _prototypeManager.Index<StartingGearPrototype>(job.StartingGear);
            EquipStartingGear(entity, startingGear, profile);
            if (profile != null)
                EquipIdCard(entity, profile.Name, job.Prototype);
        }

        if (profile != null)
        {
            _humanoidAppearanceSystem.UpdateFromProfile(entity, profile);
            EntityManager.GetComponent<MetaDataComponent>(entity).EntityName = profile.Name;
        }

        foreach (var jobSpecial in job?.Prototype.Special ?? Array.Empty<JobSpecial>())
        {
            jobSpecial.AfterEquip(entity);
        }

        return entity;
    }

    /// <summary>
    /// Equips starting gear onto the given entity.
    /// </summary>
    /// <param name="entity">Entity to load out.</param>
    /// <param name="startingGear">Starting gear to use.</param>
    /// <param name="profile">Character profile to use, if any.</param>
    public void EquipStartingGear(EntityUid entity, StartingGearPrototype startingGear, HumanoidCharacterProfile? profile)
    {
        if (_inventorySystem.TryGetSlots(entity, out var slotDefinitions))
        {
            foreach (var slot in slotDefinitions)
            {
                var equipmentStr = startingGear.GetGear(slot.Name, profile);
                if (!string.IsNullOrEmpty(equipmentStr))
                {
                    var equipmentEntity = EntityManager.SpawnEntity(equipmentStr, EntityManager.GetComponent<TransformComponent>(entity).Coordinates);
                    _inventorySystem.TryEquip(entity, equipmentEntity, slot.Name, true);
                }
            }
        }

        if (!TryComp(entity, out HandsComponent? handsComponent))
            return;

        var inhand = startingGear.Inhand;
        var coords = EntityManager.GetComponent<TransformComponent>(entity).Coordinates;
        foreach (var (hand, prototype) in inhand)
        {
            var inhandEntity = EntityManager.SpawnEntity(prototype, coords);
            _handsSystem.TryPickup(entity, inhandEntity, hand, checkActionBlocker: false, handsComp: handsComponent);
        }
    }

    /// <summary>
    /// Equips an ID card and PDA onto the given entity.
    /// </summary>
    /// <param name="entity">Entity to load out.</param>
    /// <param name="characterName">Character name to use for the ID.</param>
    /// <param name="jobPrototype">Job prototype to use for the PDA and ID.</param>
    public void EquipIdCard(EntityUid entity, string characterName, JobPrototype jobPrototype)
    {
        if (!_inventorySystem.TryGetSlotEntity(entity, "id", out var idUid))
            return;

        if (!EntityManager.TryGetComponent(idUid, out PDAComponent? pdaComponent) || pdaComponent.ContainedID == null)
            return;

        var card = pdaComponent.ContainedID;
        _cardSystem.TryChangeFullName(card.Owner, characterName, card);
        _cardSystem.TryChangeJobTitle(card.Owner, jobPrototype.Name, card);

        var access = EntityManager.GetComponent<AccessComponent>(card.Owner);
        var accessTags = access.Tags;
        accessTags.UnionWith(jobPrototype.Access);
        _pdaSystem.SetOwner(pdaComponent, characterName);
    }


    #endregion Player spawning helpers
}

/// <summary>
/// Event fired on any spawner eligible to attempt to spawn a player.
/// This event's success is measured by if SpawnResult is not null.
/// You should not make this event's success rely on random chance.
/// </summary>
[PublicAPI, ByRefEvent]
public sealed class SpawnPlayerEvent : EntityEventArgs
{
    /// <summary>
    /// The entity spawned, if any. You should set this if you succeed at spawning the character.
    /// </summary>
    public EntityUid? SpawnResult;
    /// <summary>
    /// The job to use, if any.
    /// </summary>
    public readonly Job? Job;
    /// <summary>
    /// The profile to use, if any.
    /// </summary>
    public readonly HumanoidCharacterProfile? HumanoidCharacterProfile;

    public SpawnPlayerEvent(Job? job, HumanoidCharacterProfile? humanoidCharacterProfile)
    {
        Job = job;
        HumanoidCharacterProfile = humanoidCharacterProfile;
    }
}
