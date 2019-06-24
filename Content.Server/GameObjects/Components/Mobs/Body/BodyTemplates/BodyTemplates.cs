﻿using System;
using Robust.Shared.Maths;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Robust.Shared.Interfaces.GameObjects;

namespace Content.Server.GameObjects.Components.Mobs.Body
{
    /// <summary>
    ///    Core of the mobcode. It glues all the shitcode with limbs, organs 
    ///    and body functions together with DAMAGE, making frankensteins that we call Mobs
    /// </summary>
    public class BodyTemplate
    {
        public string Name;
        public List<Limb> bodyMap;//it's for damage calculation
        public List<Organ> allOrgans;//it's for life calls
        public IEntity Owner;

        public Blood Blood; //blood should wait for reagents to get truly implemented

        private Random _randomLimb;

        public virtual void ExposeData(ObjectSerializer obj)
        {
            //obj.DataField(ref bodyMap, "limbs", null); TODO: soon.
            //obj.DataField(ref neededFunctions, "bodyFunctions", null);
        }

        public virtual void Initialize(IEntity owner)
        {
            Owner = owner;
            _randomLimb = new Random(owner.Uid.GetHashCode() ^ DateTime.Now.GetHashCode());
        }

        public void Life() //this is main Life() proc!
        {
            foreach(var organ in allOrgans)
            {
                organ.Life();
                Blood = organ.CirculateBlood(Blood);
            }
            foreach(var limb in bodyMap)
            {
                Blood = limb.CirculateBlood(Blood);
            }
        }
        public void HandleDamage(DamageableComponent damage)
        {   
            //TODO: Targetting.
            _randomLimb.Pick(bodyMap).HandleDamage(damage.LastTakenDamage, new Random(Owner.Uid.GetHashCode() ^ DateTime.Now.GetHashCode()));
        }
    }
}
