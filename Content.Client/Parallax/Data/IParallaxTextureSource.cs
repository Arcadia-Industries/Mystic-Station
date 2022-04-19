using System.Threading.Tasks;
﻿using System.Collections.Generic;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Client.Graphics;

namespace Content.Client.Parallax.Data
{
    [ImplicitDataDefinitionForInheritors]
    public interface IParallaxTextureSource
    {
        /// <summary>
        /// Generates or loads the texture.
        /// Note that this should be cached, but not necessarily *here*.
        /// </summary>
        Task<Texture> GenerateTexture();
    }
}

