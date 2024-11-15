using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// Simply wraps a Guid, but done so to clarify intention of the Guid.
    /// </summary>
    public readonly record struct PlayerID(Guid ID);
}
