namespace ZeDMDUpdater;

public record Version(byte Major, byte Minor, byte Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
