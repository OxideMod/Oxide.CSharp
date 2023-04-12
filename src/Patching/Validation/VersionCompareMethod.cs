using System;

namespace Oxide.CSharp.Patching.Validation
{
    [Flags]
    public enum VersionCompareMethod
    {
        Equality = 0x00,
        GreaterThan = 0x01,
        LessThan = 0x02,
        GreaterThanOrEqualTo = 0x04 | Equality | GreaterThan,
        LessThanOrEqualTo = 0x08 | Equality | LessThan
    }
}
