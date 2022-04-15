using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.DoAfter;
using Content.Server.Electrocution;
using Content.Server.Power.Components;
using Content.Server.Wires;
using Content.Shared.Power;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Power;

// Generic power wire action. Use on anything
// that requires power.
//
// note that a lot of this relies on a cheap trick;
// this should be refactored either before this is
// merged
[DataDefinition]
public class PowerWireAction : BaseWireAction
{
    [DataField("color")]
    private Color _statusColor = Color.Red;

    [DataField("name")]
    private string _text = "POWR";

    [DataField("pulseTimeout")]
    private int _pulseTimeout = 30;

    private ElectrocutionSystem _electrocutionSystem = default!;

    public PowerWireAction()
    {
    }

    public override object Identifier { get; } = PowerWireActionKey.Key;

    public override object StatusKey { get; } = PowerWireActionKey.Status;

    public override StatusLightData GetStatusLightData(Wire wire)
    {
        StatusLightState lightState = StatusLightState.Off;
        if (EntityManager.TryGetComponent<ApcPowerReceiverComponent>(wire.Owner, out var power))
        {
            WiresSystem.TryGetData(wire.Owner, PowerWireActionKey.Pulsed, out var pulsed);

            if (pulsed is bool pulseCast)
            {
                power.PowerDisabled = wire.IsCut || pulseCast;
                if (pulseCast)
                {
                    lightState = StatusLightState.BlinkingSlow;
                }
                else
                {
                    lightState = (wire.IsCut == true)
                        ? StatusLightState.Off
                        : StatusLightState.On;
                }
            }
            else
            {
                power.PowerDisabled = wire.IsCut;

                lightState = (wire.IsCut == true)
                    ? StatusLightState.Off
                    : StatusLightState.On;

            }

            if (power.PowerDisabled
                && WiresSystem.TryGetData(wire.Owner, PowerWireActionKey.ElectrifiedCancel, out var electrifiedObject)
                && electrifiedObject is CancellationTokenSource electrifiedToken)
            {
                electrifiedToken.Cancel();
            }

        }

        return new StatusLightData(
            _statusColor,
            lightState,
            _text);
    }

    private void SetElectrified(EntityUid used, bool setting, ElectrifiedComponent? electrified = null)
    {
        if (electrified == null
            && !EntityManager.TryGetComponent(used, out electrified))
            return;

        electrified.Enabled = setting;
    }

    /// <returns>false if failed, true otherwise</returns>
    private bool TrySetElectrocution(EntityUid used, EntityUid user, Wire wire, bool timed = false)
    {
        if (EntityManager.TryGetComponent<ApcPowerReceiverComponent>(used, out var power)
            && EntityManager.TryGetComponent<ElectrifiedComponent>(used, out var electrified))
        {
            // always set this to true
            SetElectrified(used, true, electrified);

            // if we were electrified, then return false
            var electrifiedAttempt = _electrocutionSystem.TryDoElectrifiedAct(used, user);

            // if this is timed, we set up a doAfter so that the
            // electrocution continues - unless cancelled
            //
            // if the power is disabled however, just don't bother
            if (timed && !power.PowerDisabled)
            {
                var newToken = new CancellationTokenSource();
                var doAfter = new DoAfterEventArgs(
                    used,
                    _pulseTimeout,
                    newToken.Token)
                {
                    UserCancelledEvent = new WireDoAfterEvent(AwaitElectrifiedCancel, wire),
                    UserFinishedEvent = new WireDoAfterEvent(AwaitElectrifiedCancel, wire)
                };

                WiresSystem.SetData(used, PowerWireActionKey.ElectrifiedCancel, newToken);
                WiresSystem.StartWireAction(doAfter);
            }
            else
            {
                SetElectrified(used, false, electrified);
            }

            return !electrifiedAttempt;
        }

        return false;
    }

    public override void Initialize(EntityUid uid, Wire wire)
    {
        base.Initialize(uid, wire);

        _electrocutionSystem = EntitySystem.Get<ElectrocutionSystem>();
    }

    public override bool Cut(EntityUid used, EntityUid user, Wire wire)
    {
        if (!TrySetElectrocution(used, user, wire))
            return false;

        WiresSystem.TryCancelWireAction(used, PowerWireActionKey.PulseCancel);
        WiresSystem.TryCancelWireAction(used, PowerWireActionKey.ElectrifiedCancel);

        wire.IsCut = true;

        return true;
    }

    public override bool Mend(EntityUid used, EntityUid user, Wire wire)
    {
        if (!TrySetElectrocution(used, user, wire))
            return false;

        wire.IsCut = false;
        return true;
    }

    public override bool Pulse(EntityUid used, EntityUid user, Wire wire)
    {
        WiresSystem.TryCancelWireAction(used, PowerWireActionKey.ElectrifiedCancel);

        if (!TrySetElectrocution(used, user, wire, true))
            return false;

        // disrupted power shouldn't re-disrupt
        if (WiresSystem.TryGetData(used, PowerWireActionKey.Pulsed, out var pulsedKey)
            && (bool) pulsedKey)
        {
            return false;
        }

        WiresSystem.SetData(used, PowerWireActionKey.Pulsed, true);

        var newPulseToken = new CancellationTokenSource();
        var doAfter = new DoAfterEventArgs(
            used,
            _pulseTimeout,
            newPulseToken.Token)
        {
            UserCancelledEvent = new WireDoAfterEvent(AwaitPulseCancel, wire),
            UserFinishedEvent = new WireDoAfterEvent(AwaitPulseCancel, wire)
        };

        WiresSystem.StartWireAction(doAfter);

        // AwaitPulseCancel(used, wire, _doAfterSystem.WaitDoAfter(doAfter));

        WiresSystem.SetData(used, PowerWireActionKey.PulseCancel, newPulseToken);

        return true;
    }

    private void AwaitElectrifiedCancel(EntityUid used, Wire wire)
    {
        WiresSystem.SetData(used, PowerWireActionKey.Electrified, false);
        SetElectrified(used, false);
    }

    private void AwaitPulseCancel(EntityUid used, Wire wire)
    {
        WiresSystem.SetData(used, PowerWireActionKey.Pulsed, false);
        WiresSystem.UpdateUserInterface(used);
    }
}
