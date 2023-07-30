﻿using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Generation.Teg;
using Content.Shared.Atmos.Monitor;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.SensorMonitoring;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using ConsoleUIState = Content.Shared.SensorMonitoring.SensorMonitoringConsoleBoundInterfaceState;

namespace Content.Server.SensorMonitoring;

public sealed partial class SensorMonitoringConsoleSystem : EntitySystem
{
    private EntityQuery<DeviceNetworkComponent> _deviceNetworkQuery;

    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitUI();

        SubscribeLocalEvent<SensorMonitoringConsoleComponent, DeviceListUpdateEvent>(DeviceListUpdated);
        SubscribeLocalEvent<SensorMonitoringConsoleComponent, DeviceNetworkPacketEvent>(DevicePacketReceived);
        SubscribeLocalEvent<SensorMonitoringConsoleComponent, AtmosDeviceUpdateEvent>(AtmosUpdate);

        _deviceNetworkQuery = GetEntityQuery<DeviceNetworkComponent>();
    }

    public override void Update(float frameTime)
    {
        var consoles = EntityQueryEnumerator<SensorMonitoringConsoleComponent>();
        while (consoles.MoveNext(out var entityUid, out var comp))
        {
            UpdateConsole(entityUid, comp);
        }
    }

    private void UpdateConsole(EntityUid uid, SensorMonitoringConsoleComponent comp)
    {
        var minTime = _gameTiming.CurTime - comp.RetentionTime;

        foreach (var (ent, data) in comp.Sensors)
        {
            // Cull old data.
            foreach (var stream in data.Streams.Values)
            {
                while (stream.Samples.TryPeek(out var sample) && sample.Time < minTime)
                {
                    stream.Samples.Dequeue();
                }
            }
        }

        UpdateConsoleUI(uid, comp);
    }

    private void DeviceListUpdated(
        EntityUid uid,
        SensorMonitoringConsoleComponent component,
        DeviceListUpdateEvent args)
    {
        var kept = new HashSet<EntityUid>();

        foreach (var newDevice in args.Devices)
        {
            var deviceType = DetectDeviceType(newDevice);
            if (deviceType == SensorDeviceType.Unknown)
                continue;

            kept.Add(newDevice);
            var sensor = component.Sensors.GetOrNew(newDevice);
            sensor.DeviceType = deviceType;
            if (sensor.NetId == 0)
                sensor.NetId = MakeNetId(component);
        }

        foreach (var oldDevice in args.OldDevices)
        {
            if (kept.Contains(oldDevice))
                continue;

            if (component.Sensors.TryGetValue(oldDevice, out var sensorData))
            {
                component.RemovedSensors.Add(sensorData.NetId);
                component.Sensors.Remove(oldDevice);
            }
        }
    }

    private SensorDeviceType DetectDeviceType(EntityUid entity)
    {
        if (HasComp<TegGeneratorComponent>(entity))
            return SensorDeviceType.Teg;

        if (HasComp<AtmosMonitorComponent>(entity))
            return SensorDeviceType.AtmosSensor;

        return SensorDeviceType.Unknown;
    }

    private void DevicePacketReceived(EntityUid uid, SensorMonitoringConsoleComponent component,
        DeviceNetworkPacketEvent args)
    {
        if (!component.Sensors.TryGetValue(args.Sender, out var sensorData))
            return;

        if (!args.Data.TryGetValue(DeviceNetworkConstants.Command, out string? command))
            return;

        switch (sensorData.DeviceType)
        {
            case SensorDeviceType.Teg:
                if (command != TegSystem.DeviceNetworkCommandSyncData)
                    return;

                if (!args.Data.TryGetValue(TegSystem.DeviceNetworkCommandSyncData, out TegSensorData? tegData))
                    return;

                // @formatter:off
                WriteSample(component, sensorData, "teg_circa_in_pressure",     SensorUnit.Pressure,    tegData.CirculatorA.InletPressure);
                WriteSample(component, sensorData, "teg_circa_in_temperature",  SensorUnit.Temperature, tegData.CirculatorA.InletTemperature);
                WriteSample(component, sensorData, "teg_circa_out_pressure",    SensorUnit.Pressure,    tegData.CirculatorA.OutletPressure);
                WriteSample(component, sensorData, "teg_circa_out_temperature", SensorUnit.Temperature, tegData.CirculatorA.OutletTemperature);

                WriteSample(component, sensorData, "teg_circb_in_pressure",     SensorUnit.Pressure,    tegData.CirculatorB.InletPressure);
                WriteSample(component, sensorData, "teg_circb_in_temperature",  SensorUnit.Temperature, tegData.CirculatorB.InletTemperature);
                WriteSample(component, sensorData, "teg_circb_out_pressure",    SensorUnit.Pressure,    tegData.CirculatorB.OutletPressure);
                WriteSample(component, sensorData, "teg_circb_out_temperature", SensorUnit.Temperature, tegData.CirculatorB.OutletTemperature);
                // @formatter:on
                break;

            case SensorDeviceType.AtmosSensor:
                if (command != AtmosDeviceNetworkSystem.SyncData)
                    return;

                if (!args.Data.TryGetValue(AtmosDeviceNetworkSystem.SyncData, out AtmosSensorData? atmosData))
                    return;

                // @formatter:off
                WriteSample(component, sensorData, "atmo_pressure",    SensorUnit.Pressure,    atmosData.Pressure);
                WriteSample(component, sensorData, "atmo_temperature", SensorUnit.Temperature, atmosData.Temperature);
                // @formatter:on
                break;
        }
    }

    private void WriteSample(
        SensorMonitoringConsoleComponent component,
        SensorMonitoringConsoleComponent.SensorData sensorData,
        string streamName,
        SensorUnit unit,
        float value)
    {
        var stream = sensorData.Streams.GetOrNew(streamName);
        stream.Unit = unit;
        if (stream.NetId == 0)
            stream.NetId = MakeNetId(component);

        var time = _gameTiming.CurTime;
        stream.Samples.Enqueue(new SensorSample(time, value));
    }

    private static int MakeNetId(SensorMonitoringConsoleComponent component)
    {
        return ++component.IdCounter;
    }

    private void AtmosUpdate(
        EntityUid uid,
        SensorMonitoringConsoleComponent comp,
        AtmosDeviceUpdateEvent args)
    {
        foreach (var (ent, data) in comp.Sensors)
        {
            // Send network requests for new data!
            NetworkPayload payload;
            switch (data.DeviceType)
            {
                case SensorDeviceType.Teg:
                    payload = new NetworkPayload
                    {
                        [DeviceNetworkConstants.Command] = TegSystem.DeviceNetworkCommandSyncData
                    };
                    break;

                case SensorDeviceType.AtmosSensor:
                    payload = new NetworkPayload
                    {
                        [DeviceNetworkConstants.Command] = AtmosDeviceNetworkSystem.SyncData
                    };
                    break;

                default:
                    // Unknown device type, don't do anything.
                    continue;
            }

            var address = _deviceNetworkQuery.GetComponent(ent);
            _deviceNetwork.QueuePacket(uid, address.Address, payload);
        }
    }
}
