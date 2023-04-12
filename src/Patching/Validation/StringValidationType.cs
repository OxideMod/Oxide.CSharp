namespace Oxide.CSharp.Patching.Validation
{
    public enum StringValidationType
    {
        Equals = 0x00,

        Contains = 0x01,

        StartsWith = 0x02,

        EndsWith = 0x04,

        RegularExpression = 0x08
    }
}
